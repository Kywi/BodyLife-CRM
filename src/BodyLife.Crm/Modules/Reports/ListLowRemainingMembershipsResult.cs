namespace BodyLife.Crm.Modules.Reports;

public sealed record ListLowRemainingMembershipsResult(
    ListLowRemainingMembershipsStatus Status,
    LowRemainingMembershipsPage? Page,
    string? ErrorCode,
    string? ErrorMessage,
    string? ErrorField)
{
    public static ListLowRemainingMembershipsResult Succeeded(
        LowRemainingMembershipsPage page)
    {
        ArgumentNullException.ThrowIfNull(page);

        return new ListLowRemainingMembershipsResult(
            ListLowRemainingMembershipsStatus.Success,
            page,
            ErrorCode: null,
            ErrorMessage: null,
            ErrorField: null);
    }

    public static ListLowRemainingMembershipsResult Denied()
    {
        return Failure(
            ListLowRemainingMembershipsStatus.PermissionDenied,
            "permission_denied",
            "An active Owner, named Admin or shared Reception/Admin session is required.",
            field: null);
    }

    public static ListLowRemainingMembershipsResult Invalid(
        string message,
        string? field)
    {
        var normalizedMessage = message?.Trim();
        if (string.IsNullOrWhiteSpace(normalizedMessage))
        {
            throw new ArgumentException("Validation message is required.", nameof(message));
        }

        return Failure(
            ListLowRemainingMembershipsStatus.ValidationFailed,
            "validation_failed",
            normalizedMessage,
            field);
    }

    public static ListLowRemainingMembershipsResult RecalculationFailed()
    {
        return Failure(
            ListLowRemainingMembershipsStatus.RecalculationFailed,
            "recalculation_failed",
            "Low-remaining report is unavailable because Memberships recalculation has not completed successfully.",
            field: null);
    }

    public static ListLowRemainingMembershipsResult InconsistentSource()
    {
        return Failure(
            ListLowRemainingMembershipsStatus.SourceInconsistent,
            "source_inconsistent",
            "Low-remaining report is unavailable because canonical Memberships source rows are inconsistent.",
            field: null);
    }

    private static ListLowRemainingMembershipsResult Failure(
        ListLowRemainingMembershipsStatus status,
        string errorCode,
        string errorMessage,
        string? field)
    {
        return new ListLowRemainingMembershipsResult(
            status,
            Page: null,
            errorCode,
            errorMessage,
            field);
    }
}
