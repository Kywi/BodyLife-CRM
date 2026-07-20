namespace BodyLife.Crm.Modules.Audit;

public sealed record GetClientAuditEntriesResult(
    GetClientAuditEntriesStatus Status,
    ClientAuditEntriesPage? Page,
    string? ErrorCode,
    string? ErrorMessage,
    string? ErrorField)
{
    public static GetClientAuditEntriesResult Succeeded(ClientAuditEntriesPage page)
    {
        ArgumentNullException.ThrowIfNull(page);

        return new GetClientAuditEntriesResult(
            GetClientAuditEntriesStatus.Success,
            page,
            ErrorCode: null,
            ErrorMessage: null,
            ErrorField: null);
    }

    public static GetClientAuditEntriesResult Denied()
    {
        return Failure(
            GetClientAuditEntriesStatus.PermissionDenied,
            "permission_denied",
            "An active Owner, named Admin or shared Reception/Admin session is required.",
            field: null);
    }

    public static GetClientAuditEntriesResult MissingClient()
    {
        return Failure(
            GetClientAuditEntriesStatus.NotFound,
            "not_found",
            "Client was not found.",
            "clientId");
    }

    public static GetClientAuditEntriesResult Invalid(string message, string? field)
    {
        var normalizedMessage = message?.Trim();
        if (string.IsNullOrWhiteSpace(normalizedMessage))
        {
            throw new ArgumentException("Validation message is required.", nameof(message));
        }

        return Failure(
            GetClientAuditEntriesStatus.ValidationFailed,
            "validation_failed",
            normalizedMessage,
            field);
    }

    public static GetClientAuditEntriesResult InconsistentSource()
    {
        return Failure(
            GetClientAuditEntriesStatus.SourceInconsistent,
            "source_inconsistent",
            "Client audit entries are unavailable because audit source records are inconsistent.",
            field: null);
    }

    private static GetClientAuditEntriesResult Failure(
        GetClientAuditEntriesStatus status,
        string errorCode,
        string errorMessage,
        string? field)
    {
        return new GetClientAuditEntriesResult(
            status,
            Page: null,
            errorCode,
            errorMessage,
            field);
    }
}
