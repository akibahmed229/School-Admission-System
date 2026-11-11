using System.Text.Json;
using Npgsql;
using SchoolAdmission.Api;

var builder = WebApplication.CreateBuilder(args);

// Everything that reads configuration is resolved lazily (inside factories), so the values are
// final even when a test host overrides them after Program has started building.
builder.Services.AddSingleton<NpgsqlDataSource>(sp =>
{
    var cs = sp.GetRequiredService<IConfiguration>().GetConnectionString("DefaultConnection");
    if (string.IsNullOrWhiteSpace(cs))
        throw new InvalidOperationException("Connection string 'ConnectionStrings__DefaultConnection' is not configured.");

    // Keep the pool well below PostgreSQL's default max_connections (100) so a burst of requests
    // (or two backend processes) queues for a connection instead of failing with "too many clients".
    var hasPoolSetting = cs.Contains("pool size", StringComparison.OrdinalIgnoreCase)
                         || cs.Contains("MaxPoolSize", StringComparison.OrdinalIgnoreCase);
    var csb = new NpgsqlConnectionStringBuilder(cs);
    if (!hasPoolSetting)
        csb.MaxPoolSize = 40;

    return NpgsqlDataSource.Create(csb.ConnectionString);
});

builder.Services.AddSingleton(sp =>
{
    var raw = sp.GetRequiredService<IConfiguration>()["HOLD_DURATION_SECONDS"];
    var seconds = int.TryParse(raw, out var parsed) && parsed > 0 ? parsed : 600;
    return new AdmissionOptions(seconds);
});

builder.Services.AddSingleton<SchemaInitializer>();
builder.Services.AddSingleton<AdmissionService>();

builder.Services.AddCors(o => o.AddDefaultPolicy(p => p
    .AllowAnyOrigin()
    .AllowAnyHeader()
    .AllowAnyMethod()
    .WithExposedHeaders("Date"))); // lets the UI correct for client/server clock skew

var app = builder.Build();

// Uniform JSON errors: { "error": "<CODE>", "message": "<text>" }
app.Use(async (ctx, next) =>
{
    try
    {
        await next();
    }
    catch (ApiException ex) when (!ctx.Response.HasStarted)
    {
        await WriteError(ctx, ex.Status, ex.Code, ex.Message);
    }
    catch (BadHttpRequestException) when (!ctx.Response.HasStarted)
    {
        await WriteError(ctx, 400, "BAD_REQUEST", "The request body is malformed.");
    }
    catch (OperationCanceledException) when (ctx.RequestAborted.IsCancellationRequested)
    {
        // client went away; nothing to send
    }
    catch (Exception ex) when (!ctx.Response.HasStarted)
    {
        ctx.RequestServices.GetRequiredService<ILogger<Program>>().LogError(ex, "Unhandled error");
        await WriteError(ctx, 500, "INTERNAL_ERROR", "Something went wrong on the server.");
    }
});

app.UseCors();

await app.Services.GetRequiredService<SchemaInitializer>().InitializeAsync();

app.MapAdmissionEndpoints();
app.MapFallback(async ctx => await WriteError(ctx, 404, "NOT_FOUND", "Resource not found."));

app.Run();

static Task WriteError(HttpContext ctx, int status, string code, string message)
{
    ctx.Response.StatusCode = status;
    ctx.Response.ContentType = "application/json";
    return ctx.Response.WriteAsync(JsonSerializer.Serialize(new { error = code, message }));
}

// Makes the entry point visible to WebApplicationFactory in the test project.
public partial class Program { }
