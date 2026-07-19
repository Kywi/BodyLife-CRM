namespace BodyLife.Crm.Modules.Reports;

public sealed record ListInactiveClientsResult(
    ListInactiveClientsStatus Status,
    InactiveClientsPage? Page,
    string? ErrorCode,
    string? ErrorMessage,
    string? ErrorField)
{
    public static ListInactiveClientsResult Succeeded(InactiveClientsPage page)
    {
        ArgumentNullException.ThrowIfNull(page);

        return new ListInactiveClientsResult(
            ListInactiveClientsStatus.Success,
            page,
            ErrorCode: null,
            ErrorMessage: null,
            ErrorField: null);
    }

    public static ListInactiveClientsResult Denied()
    {
        return Failure(
            ListInactiveClientsStatus.PermissionDenied,
            "permission_denied",
            "An active Owner, named Admin or shared Reception/Admin session is required.",
            field: null);
    }

    public static ListInactiveClientsResult Invalid(string message, string? field)
    {
        var normalizedMessage = message?.Trim();
        if (string.IsNullOrWhiteSpace(normalizedMessage))
        {
            throw new ArgumentException("Validation message is required.", nameof(message));
        }

        return Failure(
            ListInactiveClientsStatus.ValidationFailed,
            "validation_failed",
            normalizedMessage,
            field);
    }

    public static ListInactiveClientsResult RecalculationFailed()
    {
        return Failure(
            ListInactiveClientsStatus.RecalculationFailed,
            "recalculation_failed",
            "Inactive-clients report is unavailable because Memberships recalculation has not completed successfully.",
            field: null);
    }

    public static ListInactiveClientsResult InconsistentSource()
    {
        return Failure(
            ListInactiveClientsStatus.SourceInconsistent,
            "source_inconsistent",
            "Inactive-clients report is unavailable because canonical source records are inconsistent.",
            field: null);
    }

    private static ListInactiveClientsResult Failure(
        ListInactiveClientsStatus status,
        string errorCode,
        string errorMessage,
        string? field)
    {
        return new ListInactiveClientsResult(
            status,
            Page: null,
            errorCode,
            errorMessage,
            field);
    }
}
