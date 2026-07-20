namespace BodyLife.Crm.Modules.Visits;

public sealed record GetClientVisitHistorySourceRowsResult(
    GetClientVisitHistorySourceRowsStatus Status,
    ClientVisitHistorySourceRowsPage? Page,
    string? ErrorCode,
    string? ErrorMessage,
    string? ErrorField)
{
    public static GetClientVisitHistorySourceRowsResult Succeeded(
        ClientVisitHistorySourceRowsPage page)
    {
        ArgumentNullException.ThrowIfNull(page);

        return new GetClientVisitHistorySourceRowsResult(
            GetClientVisitHistorySourceRowsStatus.Success,
            page,
            ErrorCode: null,
            ErrorMessage: null,
            ErrorField: null);
    }

    public static GetClientVisitHistorySourceRowsResult Denied()
    {
        return Failure(
            GetClientVisitHistorySourceRowsStatus.PermissionDenied,
            "permission_denied",
            "An active Owner, named Admin or shared Reception/Admin session is required.",
            field: null);
    }

    public static GetClientVisitHistorySourceRowsResult MissingClient()
    {
        return Failure(
            GetClientVisitHistorySourceRowsStatus.NotFound,
            "not_found",
            "Client was not found.",
            "clientId");
    }

    public static GetClientVisitHistorySourceRowsResult Invalid(
        string message,
        string? field)
    {
        var normalizedMessage = message?.Trim();
        if (string.IsNullOrWhiteSpace(normalizedMessage))
        {
            throw new ArgumentException("Validation message is required.", nameof(message));
        }

        return Failure(
            GetClientVisitHistorySourceRowsStatus.ValidationFailed,
            "validation_failed",
            normalizedMessage,
            field);
    }

    public static GetClientVisitHistorySourceRowsResult InconsistentSource()
    {
        return Failure(
            GetClientVisitHistorySourceRowsStatus.SourceInconsistent,
            "source_inconsistent",
            "Visit history is unavailable because canonical source or audit records are inconsistent.",
            field: null);
    }

    private static GetClientVisitHistorySourceRowsResult Failure(
        GetClientVisitHistorySourceRowsStatus status,
        string errorCode,
        string errorMessage,
        string? field)
    {
        return new GetClientVisitHistorySourceRowsResult(
            status,
            Page: null,
            errorCode,
            errorMessage,
            field);
    }
}
