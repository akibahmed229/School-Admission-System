using Npgsql;

namespace SchoolAdmission.Api;

/// <summary>
/// Creates the schema idempotently on startup. A transaction-scoped advisory lock makes this
/// safe when several backend processes start at the same time against one database.
/// </summary>
public sealed class SchemaInitializer
{
    private const string Ddl = @"
CREATE TABLE IF NOT EXISTS classes (
    id           text PRIMARY KEY,
    name         text NOT NULL,
    total_seats  integer NOT NULL CHECK (total_seats > 0),
    created_at   timestamptz NOT NULL DEFAULT clock_timestamp()
);

CREATE TABLE IF NOT EXISTS holds (
    id            text PRIMARY KEY,
    class_id      text NOT NULL REFERENCES classes(id),
    student_id    text NOT NULL,
    status        text NOT NULL CHECK (status IN ('HELD','CONFIRMED','EXPIRED','CANCELLED')),
    created_at    timestamptz NOT NULL DEFAULT clock_timestamp(),
    expires_at    timestamptz NOT NULL,
    confirmed_at  timestamptz NULL
);

-- DB-level backstop: one active (HELD/CONFIRMED) application per student per class.
CREATE UNIQUE INDEX IF NOT EXISTS uq_holds_one_active_per_student_class
    ON holds (class_id, student_id) WHERE status IN ('HELD','CONFIRMED');

CREATE INDEX IF NOT EXISTS ix_holds_student ON holds (student_id, created_at DESC);
CREATE INDEX IF NOT EXISTS ix_holds_class_status ON holds (class_id, status);
";

    private readonly NpgsqlDataSource _ds;
    private readonly ILogger<SchemaInitializer> _log;

    public SchemaInitializer(NpgsqlDataSource ds, ILogger<SchemaInitializer> log)
    {
        _ds = ds;
        _log = log;
    }

    public async Task InitializeAsync(CancellationToken ct = default)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                await RunAsync(ct);
                _log.LogInformation("Database schema is ready.");
                return;
            }
            catch (Exception ex) when (attempt < 30 && IsTransient(ex))
            {
                _log.LogWarning("Database not ready (attempt {Attempt}/30): {Message}", attempt, ex.Message);
                await Task.Delay(TimeSpan.FromSeconds(2), ct);
            }
        }
    }

    private async Task RunAsync(CancellationToken ct)
    {
        await using var conn = await _ds.OpenConnectionAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);

        await using (var lockCmd = new NpgsqlCommand("SELECT pg_advisory_xact_lock(727274)", conn, tx))
        {
            await lockCmd.ExecuteNonQueryAsync(ct);
        }

        await using (var ddl = new NpgsqlCommand(Ddl, conn, tx))
        {
            await ddl.ExecuteNonQueryAsync(ct);
        }

        await tx.CommitAsync(ct);
    }

    private static bool IsTransient(Exception ex) => ex switch
    {
        PostgresException pe => pe.SqlState == "57P03", // "database system is starting up"
        NpgsqlException => true,
        System.Net.Sockets.SocketException => true,
        TimeoutException => true,
        _ => false
    };
}
