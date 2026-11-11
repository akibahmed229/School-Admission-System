namespace SchoolAdmission.Api;

public sealed record AdmissionOptions(int HoldDurationSeconds);

public sealed record CreateClassRequest(string? Name, int? TotalSeats);

// POST /classes -> 201
public sealed record CreatedClassDto(string Id, string Name, int TotalSeats);

// GET /classes, GET /classes/{id}
public sealed record ClassDto(
    string Id, string Name, int TotalSeats,
    int HeldSeats, int ConfirmedSeats, int AvailableSeats);

// POST /classes/{classId}/holds -> 201
public sealed record HoldCreatedDto(
    string HoldId, string ClassId, string StudentId, string Status, string ExpiresAt);

// GET /holds/{holdId}
public sealed record HoldStatusDto(
    string HoldId, string ClassId, string Status, string ExpiresAt);

// POST /holds/{holdId}/confirm -> 200
public sealed record HoldConfirmedDto(string HoldId, string Status);

// GET /me/holds
public sealed record MyHoldDto(
    string HoldId, string ClassId, string ClassName, string Status, string ExpiresAt);
