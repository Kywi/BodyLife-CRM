namespace BodyLife.Crm.Modules.Clients.Search;

public sealed record GetClientProfileResult(
    GetClientProfileStatus Status,
    ClientProfile? Profile,
    string? ErrorCode,
    string? ErrorMessage,
    string? ErrorField)
{
    public static GetClientProfileResult Succeeded(ClientProfile profile)
    {
        return new GetClientProfileResult(
            GetClientProfileStatus.Success,
            profile,
            ErrorCode: null,
            ErrorMessage: null,
            ErrorField: null);
    }

    public static GetClientProfileResult Denied()
    {
        return Failure(
            GetClientProfileStatus.PermissionDenied,
            "permission_denied",
            "An active Owner, named Admin or shared Reception/Admin session is required.",
            field: null);
    }

    public static GetClientProfileResult Missing()
    {
        return Failure(
            GetClientProfileStatus.NotFound,
            "not_found",
            "Client was not found.",
            "clientId");
    }

    public static GetClientProfileResult Invalid(string message, string? field)
    {
        return Failure(
            GetClientProfileStatus.ValidationFailed,
            "validation_failed",
            message,
            field);
    }

    private static GetClientProfileResult Failure(
        GetClientProfileStatus status,
        string errorCode,
        string errorMessage,
        string? field)
    {
        return new GetClientProfileResult(
            status,
            Profile: null,
            errorCode,
            errorMessage,
            field);
    }
}
