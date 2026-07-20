namespace BodyLife.Crm.Modules.Reports;

public sealed record GetClientHistoryResult(
    GetClientHistoryStatus Status,
    ClientHistoryPage? Page,
    string? ErrorCode,
    string? ErrorMessage,
    string? ErrorField)
{
    public static GetClientHistoryResult Succeeded(ClientHistoryPage page)
    {
        ArgumentNullException.ThrowIfNull(page);

        return new GetClientHistoryResult(
            GetClientHistoryStatus.Success,
            page,
            ErrorCode: null,
            ErrorMessage: null,
            ErrorField: null);
    }

    public static GetClientHistoryResult Denied()
    {
        return Failure(
            GetClientHistoryStatus.PermissionDenied,
            "permission_denied",
            "An active Owner, named Admin or shared Reception/Admin session is required.",
            field: null);
    }

    public static GetClientHistoryResult MissingClient()
    {
        return Failure(
            GetClientHistoryStatus.NotFound,
            "not_found",
            "Client was not found.",
            "clientId");
    }

    public static GetClientHistoryResult Invalid(string message, string? field)
    {
        var normalizedMessage = message?.Trim();
        if (string.IsNullOrWhiteSpace(normalizedMessage))
        {
            throw new ArgumentException("Validation message is required.", nameof(message));
        }

        return Failure(
            GetClientHistoryStatus.ValidationFailed,
            "validation_failed",
            normalizedMessage,
            field);
    }

    public static GetClientHistoryResult InconsistentSource()
    {
        return Failure(
            GetClientHistoryStatus.SourceInconsistent,
            "source_inconsistent",
            "Client history is unavailable because canonical source or audit records are inconsistent.",
            field: null);
    }

    private static GetClientHistoryResult Failure(
        GetClientHistoryStatus status,
        string errorCode,
        string errorMessage,
        string? field)
    {
        return new GetClientHistoryResult(
            status,
            Page: null,
            errorCode,
            errorMessage,
            field);
    }
}
