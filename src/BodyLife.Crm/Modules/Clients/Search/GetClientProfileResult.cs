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

    public static GetClientProfileResult RecalculationFailed()
    {
        return Failure(
            GetClientProfileStatus.RecalculationFailed,
            "recalculation_failed",
            "Client profile is unavailable because membership recalculation has not completed successfully.",
            field: null);
    }

    public static GetClientProfileResult InconsistentSource()
    {
        return Failure(
            GetClientProfileStatus.SourceInconsistent,
            "source_inconsistent",
            "Client profile is unavailable because canonical source records are inconsistent.",
            field: null);
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
