namespace BodyLife.Crm.Modules.Visits;

public sealed record GetClientVisitRowsResult(
    GetClientVisitRowsStatus Status,
    ClientVisitRowsPage? Page,
    string? ErrorCode,
    string? ErrorMessage,
    string? ErrorField)
{
    public static GetClientVisitRowsResult Succeeded(ClientVisitRowsPage page)
    {
        ArgumentNullException.ThrowIfNull(page);

        return new GetClientVisitRowsResult(
            GetClientVisitRowsStatus.Success,
            page,
            ErrorCode: null,
            ErrorMessage: null,
            ErrorField: null);
    }

    public static GetClientVisitRowsResult Denied()
    {
        return Failure(
            GetClientVisitRowsStatus.PermissionDenied,
            "permission_denied",
            "An active Owner, named Admin or shared Reception/Admin session is required.",
            field: null);
    }

    public static GetClientVisitRowsResult MissingClient()
    {
        return Failure(
            GetClientVisitRowsStatus.NotFound,
            "not_found",
            "Client was not found.",
            "clientId");
    }

    public static GetClientVisitRowsResult Invalid(string message, string? field)
    {
        var normalizedMessage = message?.Trim();
        if (string.IsNullOrWhiteSpace(normalizedMessage))
        {
            throw new ArgumentException("Validation message is required.", nameof(message));
        }

        return Failure(
            GetClientVisitRowsStatus.ValidationFailed,
            "validation_failed",
            normalizedMessage,
            field);
    }

    public static GetClientVisitRowsResult InconsistentSource()
    {
        return Failure(
            GetClientVisitRowsStatus.SourceInconsistent,
            "source_inconsistent",
            "Visit rows are unavailable because canonical source records are inconsistent.",
            field: null);
    }

    private static GetClientVisitRowsResult Failure(
        GetClientVisitRowsStatus status,
        string errorCode,
        string errorMessage,
        string? field)
    {
        return new GetClientVisitRowsResult(
            status,
            Page: null,
            errorCode,
            errorMessage,
            field);
    }
}
