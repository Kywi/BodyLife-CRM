namespace BodyLife.Crm.Modules.Audit;

public sealed record GetAuditTimelineResult(
    GetAuditTimelineStatus Status,
    AuditTimelinePage? Page,
    string? ErrorCode,
    string? ErrorMessage,
    string? ErrorField)
{
    public static GetAuditTimelineResult Succeeded(AuditTimelinePage page)
    {
        ArgumentNullException.ThrowIfNull(page);

        return new GetAuditTimelineResult(
            GetAuditTimelineStatus.Success,
            page,
            ErrorCode: null,
            ErrorMessage: null,
            ErrorField: null);
    }

    public static GetAuditTimelineResult Denied()
    {
        return Failure(
            GetAuditTimelineStatus.PermissionDenied,
            "permission_denied",
            "An active Owner, named Admin or shared Reception/Admin session is required.",
            field: null);
    }

    public static GetAuditTimelineResult MissingClient()
    {
        return Failure(
            GetAuditTimelineStatus.NotFound,
            "not_found",
            "Client was not found.",
            "clientId");
    }

    public static GetAuditTimelineResult Invalid(string message, string? field)
    {
        var normalizedMessage = message?.Trim();
        if (string.IsNullOrWhiteSpace(normalizedMessage))
        {
            throw new ArgumentException("Validation message is required.", nameof(message));
        }

        return Failure(
            GetAuditTimelineStatus.ValidationFailed,
            "validation_failed",
            normalizedMessage,
            field);
    }

    public static GetAuditTimelineResult InconsistentSource()
    {
        return Failure(
            GetAuditTimelineStatus.SourceInconsistent,
            "source_inconsistent",
            "Audit timeline is unavailable because append-only audit records are inconsistent.",
            field: null);
    }

    private static GetAuditTimelineResult Failure(
        GetAuditTimelineStatus status,
        string errorCode,
        string errorMessage,
        string? field)
    {
        return new GetAuditTimelineResult(
            status,
            Page: null,
            errorCode,
            errorMessage,
            field);
    }
}
