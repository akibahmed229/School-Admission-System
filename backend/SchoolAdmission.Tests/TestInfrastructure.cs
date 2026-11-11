using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Npgsql;
using Xunit;

namespace SchoolAdmission.Tests;

/// <summary>
/// The tests need a REAL PostgreSQL (row locks and real concurrency cannot be faked with an
/// in-memory provider). A throw-away database is created per test run and dropped afterwards,
/// so the application's own data is never touched.
/// Connection comes from TEST_DB_CONNECTION (see docker-compose.yml, profile "test").
/// </summary>
public sealed class PostgresFixture : IAsyncLifetime
{
    private string _adminConnectionString = "";
    private string _dbName = "";

    public string ConnectionString { get; private set; } = "";

    public async Task InitializeAsync()
    {
        _adminConnectionString = Environment.GetEnvironmentVariable("TEST_DB_CONNECTION")
            ?? "Host=localhost;Port=5432;Database=postgres;Username=postgres;Password=postgres";
        _dbName = "sa_test_" + Guid.NewGuid().ToString("N");

        await using (var ds = NpgsqlDataSource.Create(_adminConnectionString))
        await using (var cmd = ds.CreateCommand($"CREATE DATABASE {_dbName}"))
        {
            await cmd.ExecuteNonQueryAsync();
        }

        ConnectionString = new NpgsqlConnectionStringBuilder(_adminConnectionString)
        {
            Database = _dbName
        }.ConnectionString;
    }

    public async Task DisposeAsync()
    {
        NpgsqlConnection.ClearAllPools();
        await using var ds = NpgsqlDataSource.Create(_adminConnectionString);
        await using var cmd = ds.CreateCommand($"DROP DATABASE IF EXISTS {_dbName} WITH (FORCE)");
        await cmd.ExecuteNonQueryAsync();
    }
}

[CollectionDefinition("db")]
public sealed class DbCollection : ICollectionFixture<PostgresFixture> { }

/// <summary>Boots the real API in-process against the test database.</summary>
public sealed class ApiFactory : WebApplicationFactory<Program>
{
    private readonly string _connectionString;
    private readonly int _holdSeconds;

    public ApiFactory(string connectionString, int holdSeconds = 600)
    {
        _connectionString = connectionString;
        _holdSeconds = holdSeconds;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("ConnectionStrings:DefaultConnection", _connectionString);
        builder.UseSetting("HOLD_DURATION_SECONDS", _holdSeconds.ToString());
    }
}

public static class TestApi
{
    public const string StudentHeader = "X-Student-Id";

    /// <summary>A valid 17-digit birth registration number for student number n.</summary>
    public static string Student(int n) => (20_000_000_000_000_000L + n).ToString();

    public static async Task<string> CreateClassAsync(HttpClient http, int seats, string name = "Test Class")
    {
        var res = await http.PostAsJsonAsync("/classes", new { name, totalSeats = seats });
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync());
        return doc.RootElement.GetProperty("id").GetString()!;
    }

    public static Task<HttpResponseMessage> SendAsync(
        HttpClient http, HttpMethod method, string url, string? student = null)
    {
        var req = new HttpRequestMessage(method, url);
        if (student is not null)
            req.Headers.Add(StudentHeader, student);
        return http.SendAsync(req);
    }

    public static async Task<JsonElement> JsonAsync(HttpResponseMessage res)
    {
        using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync());
        return doc.RootElement.Clone();
    }

    public static async Task<JsonElement> GetClassAsync(HttpClient http, string classId)
    {
        var res = await http.GetAsync($"/classes/{classId}");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        return await JsonAsync(res);
    }
}
