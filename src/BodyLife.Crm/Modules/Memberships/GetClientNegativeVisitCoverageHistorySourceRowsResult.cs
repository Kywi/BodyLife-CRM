namespace BodyLife.Crm.Modules.Memberships;

public sealed record GetClientNegativeVisitCoverageHistorySourceRowsResult(
    GetClientNegativeVisitCoverageHistorySourceRowsStatus Status,
    ClientNegativeVisitCoverageHistorySourceRowsPage? Page,
    string? ErrorCode,
    string? ErrorMessage,
    string? ErrorField)
{
    public static GetClientNegativeVisitCoverageHistorySourceRowsResult Succeeded(
        ClientNegativeVisitCoverageHistorySourceRowsPage page) =>
        new(GetClientNegativeVisitCoverageHistorySourceRowsStatus.Success, page, null, null, null);

    public static GetClientNegativeVisitCoverageHistorySourceRowsResult Denied() => Failure(
        GetClientNegativeVisitCoverageHistorySourceRowsStatus.PermissionDenied,
        "permission_denied",
        "An active Owner, named Admin or shared Reception/Admin session is required.",
        null);

    public static GetClientNegativeVisitCoverageHistorySourceRowsResult MissingClient() => Failure(
        GetClientNegativeVisitCoverageHistorySourceRowsStatus.NotFound,
        "not_found",
        "Client was not found.",
        "clientId");

    public static GetClientNegativeVisitCoverageHistorySourceRowsResult Invalid(
        string message,
        string? field)
    {
        if (string.IsNullOrWhiteSpace(message?.Trim()))
        {
            throw new ArgumentException("Validation message is required.", nameof(message));
        }

        return Failure(
            GetClientNegativeVisitCoverageHistorySourceRowsStatus.ValidationFailed,
            "validation_failed",
            message.Trim(),
            field);
    }

    public static GetClientNegativeVisitCoverageHistorySourceRowsResult InconsistentSource() => Failure(
        GetClientNegativeVisitCoverageHistorySourceRowsStatus.SourceInconsistent,
        "source_inconsistent",
        "Negative-visit coverage history is unavailable because canonical source or audit records are inconsistent.",
        null);

    private static GetClientNegativeVisitCoverageHistorySourceRowsResult Failure(
        GetClientNegativeVisitCoverageHistorySourceRowsStatus status,
        string code,
        string message,
        string? field) => new(status, null, code, message, field);
}
