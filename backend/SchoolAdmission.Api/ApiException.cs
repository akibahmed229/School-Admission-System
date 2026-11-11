namespace SchoolAdmission.Api;

/// <summary>Business/validation error that maps to a JSON error response.</summary>
public sealed class ApiException : Exception
{
    public int Status { get; }
    public string Code { get; }

    public ApiException(int status, string code, string message) : base(message)
    {
        Status = status;
        Code = code;
    }
}
