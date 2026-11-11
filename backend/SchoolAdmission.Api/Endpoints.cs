namespace SchoolAdmission.Api;

public static class Endpoints
{
    private const string StudentHeader = "X-Student-Id";

    public static void MapAdmissionEndpoints(this IEndpointRouteBuilder app)
    {
        // 1
        app.MapPost("/classes", async (CreateClassRequest? body, AdmissionService svc, CancellationToken ct) =>
            Results.Json(await svc.CreateClassAsync(body, ct), statusCode: StatusCodes.Status201Created));

        // 2
        app.MapGet("/classes", async (AdmissionService svc, CancellationToken ct) =>
            Results.Ok(await svc.ListClassesAsync(ct)));

        // 3
        app.MapGet("/classes/{classId}", async (string classId, AdmissionService svc, CancellationToken ct) =>
            Results.Ok(await svc.GetClassAsync(classId, ct)));

        // 4
        app.MapPost("/classes/{classId}/holds", async (string classId, HttpRequest req, AdmissionService svc, CancellationToken ct) =>
            Results.Json(await svc.CreateHoldAsync(classId, RequireStudent(req), ct),
                statusCode: StatusCodes.Status201Created));

        // 5
        app.MapGet("/holds/{holdId}", async (string holdId, AdmissionService svc, CancellationToken ct) =>
            Results.Ok(await svc.GetHoldAsync(holdId, ct)));

        // 6
        app.MapPost("/holds/{holdId}/confirm", async (string holdId, HttpRequest req, AdmissionService svc, CancellationToken ct) =>
            Results.Ok(await svc.ConfirmHoldAsync(holdId, RequireStudent(req), ct)));

        // 7
        app.MapDelete("/holds/{holdId}", async (string holdId, HttpRequest req, AdmissionService svc, CancellationToken ct) =>
        {
            await svc.CancelHoldAsync(holdId, RequireStudent(req), ct);
            return Results.NoContent();
        });

        // 8
        app.MapGet("/me/holds", async (HttpRequest req, AdmissionService svc, CancellationToken ct) =>
            Results.Ok(await svc.GetMyHoldsAsync(RequireStudent(req), ct)));
    }

    /// <summary>
    /// The student identity is the X-Student-Id header. The UI enforces the 17-digit format;
    /// the API only requires a non-empty, reasonably short value (see ASSUMPTIONS.md).
    /// </summary>
    private static string RequireStudent(HttpRequest req)
    {
        var value = req.Headers[StudentHeader].ToString().Trim();
        if (value.Length == 0)
            throw new ApiException(400, "MISSING_STUDENT_ID", "The X-Student-Id header is required.");
        if (value.Length > 64)
            throw new ApiException(400, "INVALID_STUDENT_ID", "The X-Student-Id header is too long.");
        return value;
    }
}
