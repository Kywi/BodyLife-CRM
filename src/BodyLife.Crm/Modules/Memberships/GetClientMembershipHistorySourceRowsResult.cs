namespace BodyLife.Crm.Modules.Memberships;

public sealed record GetClientMembershipHistorySourceRowsResult(
    GetClientMembershipHistorySourceRowsStatus Status,
    ClientMembershipHistorySourceRowsPage? Page,
    string? ErrorCode,
    string? ErrorMessage,
    string? ErrorField)
{
    public static GetClientMembershipHistorySourceRowsResult Succeeded(
        ClientMembershipHistorySourceRowsPage page)
    {
        ArgumentNullException.ThrowIfNull(page);

        return new GetClientMembershipHistorySourceRowsResult(
            GetClientMembershipHistorySourceRowsStatus.Success,
            page,
            ErrorCode: null,
            ErrorMessage: null,
            ErrorField: null);
    }

    public static GetClientMembershipHistorySourceRowsResult Denied()
    {
        return Failure(
            GetClientMembershipHistorySourceRowsStatus.PermissionDenied,
            "permission_denied",
            "An active Owner, named Admin or shared Reception/Admin session is required.",
            field: null);
    }

    public static GetClientMembershipHistorySourceRowsResult MissingClient()
    {
        return Failure(
            GetClientMembershipHistorySourceRowsStatus.NotFound,
            "not_found",
            "Client was not found.",
            "clientId");
    }

    public static GetClientMembershipHistorySourceRowsResult Invalid(
        string message,
        string? field)
    {
        var normalizedMessage = message?.Trim();
        if (string.IsNullOrWhiteSpace(normalizedMessage))
        {
            throw new ArgumentException("Validation message is required.", nameof(message));
        }

        return Failure(
            GetClientMembershipHistorySourceRowsStatus.ValidationFailed,
            "validation_failed",
            normalizedMessage,
            field);
    }

    public static GetClientMembershipHistorySourceRowsResult InconsistentSource()
    {
        return Failure(
            GetClientMembershipHistorySourceRowsStatus.SourceInconsistent,
            "source_inconsistent",
            "Membership history is unavailable because canonical source or audit records are inconsistent.",
            field: null);
    }

    private static GetClientMembershipHistorySourceRowsResult Failure(
        GetClientMembershipHistorySourceRowsStatus status,
        string errorCode,
        string errorMessage,
        string? field)
    {
        return new GetClientMembershipHistorySourceRowsResult(
            status,
            Page: null,
            errorCode,
            errorMessage,
            field);
    }
}
