namespace BodyLife.Crm.Modules.Reports;

public sealed record ListNegativeClientsResult(
    ListNegativeClientsStatus Status,
    NegativeClientsPage? Page,
    string? ErrorCode,
    string? ErrorMessage,
    string? ErrorField)
{
    public static ListNegativeClientsResult Succeeded(NegativeClientsPage page)
    {
        ArgumentNullException.ThrowIfNull(page);

        return new ListNegativeClientsResult(
            ListNegativeClientsStatus.Success,
            page,
            ErrorCode: null,
            ErrorMessage: null,
            ErrorField: null);
    }

    public static ListNegativeClientsResult Denied()
    {
        return Failure(
            ListNegativeClientsStatus.PermissionDenied,
            "permission_denied",
            "An active Owner, named Admin or shared Reception/Admin session is required.",
            field: null);
    }

    public static ListNegativeClientsResult Invalid(string message, string? field)
    {
        var normalizedMessage = message?.Trim();
        if (string.IsNullOrWhiteSpace(normalizedMessage))
        {
            throw new ArgumentException("Validation message is required.", nameof(message));
        }

        return Failure(
            ListNegativeClientsStatus.ValidationFailed,
            "validation_failed",
            normalizedMessage,
            field);
    }

    public static ListNegativeClientsResult RecalculationFailed()
    {
        return Failure(
            ListNegativeClientsStatus.RecalculationFailed,
            "recalculation_failed",
            "Negative-clients report is unavailable because Memberships recalculation has not completed successfully.",
            field: null);
    }

    public static ListNegativeClientsResult InconsistentSource()
    {
        return Failure(
            ListNegativeClientsStatus.SourceInconsistent,
            "source_inconsistent",
            "Negative-clients report is unavailable because canonical Memberships source rows are inconsistent.",
            field: null);
    }

    private static ListNegativeClientsResult Failure(
        ListNegativeClientsStatus status,
        string errorCode,
        string errorMessage,
        string? field)
    {
        return new ListNegativeClientsResult(
            status,
            Page: null,
            errorCode,
            errorMessage,
            field);
    }
}
