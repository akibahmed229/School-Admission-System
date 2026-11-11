using System.Globalization;
using Npgsql;

namespace SchoolAdmission.Api;

/// <summary>
/// All seat-hold business rules.
///
/// Concurrency model (see ASSUMPTIONS.md): every operation that can change how many seats of a
/// class are taken runs in a transaction that first takes a row lock on that class
/// (SELECT ... FOR UPDATE). Requests for the same class are therefore serialized *by PostgreSQL*,
/// not by the application, so the invariant "held + confirmed &lt;= totalSeats" also holds when
/// several backend processes share one database.
///
/// Expiry never needs a background job: a HELD row only counts while expires_at is in the future,
/// and all comparisons use the database clock (clock_timestamp()).
/// </summary>
public sealed class AdmissionService
{
    private const string ClassSelect = @"
SELECT c.id, c.name, c.total_seats,
       (COUNT(h.id) FILTER (WHERE h.status = 'HELD' AND h.expires_at > clock_timestamp()))::int AS held,
       (COUNT(h.id) FILTER (WHERE h.status = 'CONFIRMED'))::int AS confirmed
FROM classes c
LEFT JOIN holds h ON h.class_id = c.id";

    private readonly NpgsqlDataSource _ds;
    private readonly AdmissionOptions _opt;

    public AdmissionService(NpgsqlDataSource ds, AdmissionOptions opt)
    {
        _ds = ds;
        _opt = opt;
    }

    // ---------------------------------------------------------------- classes

    public async Task<CreatedClassDto> CreateClassAsync(CreateClassRequest? body, CancellationToken ct)
    {
        var name = body?.Name?.Trim();
        var seats = body?.TotalSeats;

        if (string.IsNullOrEmpty(name))
            throw new ApiException(400, "VALIDATION_ERROR", "name is required.");
        if (name.Length > 100)
            throw new ApiException(400, "VALIDATION_ERROR", "name must be at most 100 characters.");
        if (seats is null || seats <= 0 || seats > 100_000)
            throw new ApiException(400, "VALIDATION_ERROR", "totalSeats must be an integer between 1 and 100000.");

        var id = NewId("c_");
        await using var cmd = _ds.CreateCommand("INSERT INTO classes (id, name, total_seats) VALUES (@id, @name, @seats)");
        cmd.Parameters.AddWithValue("id", id);
        cmd.Parameters.AddWithValue("name", name);
        cmd.Parameters.AddWithValue("seats", seats.Value);
        await cmd.ExecuteNonQueryAsync(ct);

        return new CreatedClassDto(id, name, seats.Value);
    }

    public async Task<List<ClassDto>> ListClassesAsync(CancellationToken ct)
    {
        var result = new List<ClassDto>();
        await using var cmd = _ds.CreateCommand(ClassSelect + " GROUP BY c.id ORDER BY c.created_at, c.id");
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
            result.Add(ReadClass(r));
        return result;
    }

    public async Task<ClassDto> GetClassAsync(string classId, CancellationToken ct)
    {
        await using var cmd = _ds.CreateCommand(ClassSelect + " WHERE c.id = @id GROUP BY c.id");
        cmd.Parameters.AddWithValue("id", classId);
        await using var r = await cmd.ExecuteReaderAsync(ct);
        if (!await r.ReadAsync(ct))
            throw new ApiException(404, "CLASS_NOT_FOUND", "Class not found.");
        return ReadClass(r);
    }

    private static ClassDto ReadClass(NpgsqlDataReader r)
    {
        var total = r.GetInt32(2);
        var held = r.GetInt32(3);
        var confirmed = r.GetInt32(4);
        return new ClassDto(r.GetString(0), r.GetString(1), total, held, confirmed, total - held - confirmed);
    }

    // ------------------------------------------------------------------ holds

    public async Task<HoldCreatedDto> CreateHoldAsync(string classId, string studentId, CancellationToken ct)
    {
        await using var conn = await _ds.OpenConnectionAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);

        // 1) Serialize all seat changes of this class. Everything below sees a stable picture.
        int totalSeats;
        await using (var lockCmd = new NpgsqlCommand(
            "SELECT total_seats FROM classes WHERE id = @id FOR UPDATE", conn, tx))
        {
            lockCmd.Parameters.AddWithValue("id", classId);
            var o = await lockCmd.ExecuteScalarAsync(ct);
            if (o is null)
                throw new ApiException(404, "CLASS_NOT_FOUND", "Class not found.");
            totalSeats = Convert.ToInt32(o, CultureInfo.InvariantCulture);
        }

        // 2) Persist expiry of stale holds of this class so the unique index and the counts are exact.
        await using (var sweep = new NpgsqlCommand(
            "UPDATE holds SET status = 'EXPIRED' " +
            "WHERE class_id = @c AND status = 'HELD' AND expires_at <= clock_timestamp()", conn, tx))
        {
            sweep.Parameters.AddWithValue("c", classId);
            await sweep.ExecuteNonQueryAsync(ct);
        }

        // 3) One active application / confirmed admission per student per class.
        string? existing;
        await using (var dup = new NpgsqlCommand(
            "SELECT status FROM holds WHERE class_id = @c AND student_id = @s " +
            "AND status IN ('HELD','CONFIRMED') LIMIT 1", conn, tx))
        {
            dup.Parameters.AddWithValue("c", classId);
            dup.Parameters.AddWithValue("s", studentId);
            existing = (string?)await dup.ExecuteScalarAsync(ct);
        }
        if (existing == "CONFIRMED")
            throw new ApiException(409, "ALREADY_ADMITTED", "This student's admission in this class is already confirmed.");
        if (existing == "HELD")
            throw new ApiException(409, "ALREADY_APPLIED", "This student already has an active application for this class.");

        // 4) Capacity check (held + confirmed) against totalSeats.
        int taken;
        await using (var count = new NpgsqlCommand(
            "SELECT COUNT(*)::int FROM holds WHERE class_id = @c AND status IN ('HELD','CONFIRMED')", conn, tx))
        {
            count.Parameters.AddWithValue("c", classId);
            taken = Convert.ToInt32(await count.ExecuteScalarAsync(ct), CultureInfo.InvariantCulture);
        }
        if (taken >= totalSeats)
            throw new ApiException(409, "NO_SEATS_AVAILABLE", "No seats are available in this class.");

        // 5) Take the seat.
        var holdId = NewId("h_");
        DateTime expiresAt;
        try
        {
            await using var ins = new NpgsqlCommand(
                "INSERT INTO holds (id, class_id, student_id, status, expires_at) " +
                "VALUES (@id, @c, @s, 'HELD', clock_timestamp() + @secs * interval '1 second') " +
                "RETURNING expires_at", conn, tx);
            ins.Parameters.AddWithValue("id", holdId);
            ins.Parameters.AddWithValue("c", classId);
            ins.Parameters.AddWithValue("s", studentId);
            ins.Parameters.AddWithValue("secs", (double)_opt.HoldDurationSeconds);
            expiresAt = (DateTime)(await ins.ExecuteScalarAsync(ct))!;
        }
        catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.UniqueViolation)
        {
            // Unreachable while the class lock is held; kept as the database-level backstop.
            throw new ApiException(409, "ALREADY_APPLIED", "This student already has an active application for this class.");
        }

        await tx.CommitAsync(ct);
        return new HoldCreatedDto(holdId, classId, studentId, "HELD", Iso(expiresAt));
    }

    public async Task<HoldStatusDto> GetHoldAsync(string holdId, CancellationToken ct)
    {
        await using var cmd = _ds.CreateCommand(
            "SELECT id, class_id, " +
            "CASE WHEN status = 'HELD' AND expires_at <= clock_timestamp() THEN 'EXPIRED' ELSE status END, " +
            "expires_at FROM holds WHERE id = @id");
        cmd.Parameters.AddWithValue("id", holdId);
        await using var r = await cmd.ExecuteReaderAsync(ct);
        if (!await r.ReadAsync(ct))
            throw new ApiException(404, "HOLD_NOT_FOUND", "Application not found.");
        return new HoldStatusDto(r.GetString(0), r.GetString(1), r.GetString(2), Iso(r.GetFieldValue<DateTime>(3)));
    }

    public async Task<HoldConfirmedDto> ConfirmHoldAsync(string holdId, string studentId, CancellationToken ct)
    {
        await using var conn = await _ds.OpenConnectionAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);

        var hold = await LockHoldAsync(conn, tx, holdId, ct)
                   ?? throw new ApiException(404, "HOLD_NOT_FOUND", "Application not found.");

        if (hold.StudentId != studentId)
            throw new ApiException(403, "FORBIDDEN", "This application belongs to another student.");

        switch (hold.Status)
        {
            case "CONFIRMED":
                // Idempotent: confirming twice must not change any counter.
                return new HoldConfirmedDto(holdId, "CONFIRMED");
            case "EXPIRED":
                throw new ApiException(409, "HOLD_EXPIRED", "The hold has expired.");
            case "CANCELLED":
                throw new ApiException(409, "HOLD_CANCELLED", "The application was cancelled.");
        }

        if (hold.Expired)
        {
            await SetStatusAsync(conn, tx, holdId, "EXPIRED", ct);
            await tx.CommitAsync(ct);
            throw new ApiException(409, "HOLD_EXPIRED", "The hold has expired.");
        }

        // HELD -> CONFIRMED keeps (held + confirmed) unchanged, so no class lock is needed.
        await using (var cmd = new NpgsqlCommand(
            "UPDATE holds SET status = 'CONFIRMED', confirmed_at = clock_timestamp() WHERE id = @id", conn, tx))
        {
            cmd.Parameters.AddWithValue("id", holdId);
            await cmd.ExecuteNonQueryAsync(ct);
        }
        await tx.CommitAsync(ct);
        return new HoldConfirmedDto(holdId, "CONFIRMED");
    }

    public async Task CancelHoldAsync(string holdId, string studentId, CancellationToken ct)
    {
        await using var conn = await _ds.OpenConnectionAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);

        var hold = await LockHoldAsync(conn, tx, holdId, ct)
                   ?? throw new ApiException(404, "HOLD_NOT_FOUND", "Application not found.");

        if (hold.StudentId != studentId)
            throw new ApiException(403, "FORBIDDEN", "This application belongs to another student.");

        switch (hold.Status)
        {
            case "CANCELLED":
                return; // idempotent
            case "CONFIRMED":
                throw new ApiException(409, "CANNOT_CANCEL_CONFIRMED", "A confirmed admission cannot be cancelled.");
            case "EXPIRED":
                throw new ApiException(409, "HOLD_EXPIRED", "The hold has already expired.");
        }

        if (hold.Expired)
        {
            await SetStatusAsync(conn, tx, holdId, "EXPIRED", ct);
            await tx.CommitAsync(ct);
            throw new ApiException(409, "HOLD_EXPIRED", "The hold has already expired.");
        }

        await SetStatusAsync(conn, tx, holdId, "CANCELLED", ct);
        await tx.CommitAsync(ct);
    }

    public async Task<List<MyHoldDto>> GetMyHoldsAsync(string studentId, CancellationToken ct)
    {
        var result = new List<MyHoldDto>();
        await using var cmd = _ds.CreateCommand(
            "SELECT h.id, h.class_id, c.name, " +
            "CASE WHEN h.status = 'HELD' AND h.expires_at <= clock_timestamp() THEN 'EXPIRED' ELSE h.status END, " +
            "h.expires_at " +
            "FROM holds h JOIN classes c ON c.id = h.class_id " +
            "WHERE h.student_id = @s ORDER BY h.created_at DESC, h.id");
        cmd.Parameters.AddWithValue("s", studentId);
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
        {
            result.Add(new MyHoldDto(
                r.GetString(0), r.GetString(1), r.GetString(2), r.GetString(3),
                Iso(r.GetFieldValue<DateTime>(4))));
        }
        return result;
    }

    // ---------------------------------------------------------------- helpers

    private sealed record HoldRow(string StudentId, string Status, bool Expired);

    /// <summary>Row-locks one hold (so confirm/cancel/confirm races are serialized per hold).</summary>
    private static async Task<HoldRow?> LockHoldAsync(
        NpgsqlConnection conn, NpgsqlTransaction tx, string holdId, CancellationToken ct)
    {
        await using var cmd = new NpgsqlCommand(
            "SELECT student_id, status, (expires_at <= clock_timestamp()) AS is_expired " +
            "FROM holds WHERE id = @id FOR UPDATE", conn, tx);
        cmd.Parameters.AddWithValue("id", holdId);
        await using var r = await cmd.ExecuteReaderAsync(ct);
        if (!await r.ReadAsync(ct))
            return null;
        return new HoldRow(r.GetString(0), r.GetString(1), r.GetBoolean(2));
    }

    private static async Task SetStatusAsync(
        NpgsqlConnection conn, NpgsqlTransaction tx, string holdId, string status, CancellationToken ct)
    {
        await using var cmd = new NpgsqlCommand("UPDATE holds SET status = @st WHERE id = @id", conn, tx);
        cmd.Parameters.AddWithValue("st", status);
        cmd.Parameters.AddWithValue("id", holdId);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private static string NewId(string prefix) => prefix + Guid.NewGuid().ToString("N")[..12];

    private static string Iso(DateTime dt) =>
        DateTime.SpecifyKind(dt, DateTimeKind.Utc)
            .ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture);
}
