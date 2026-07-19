namespace BodyLife.Crm.Modules.Memberships;

public sealed record GetLowRemainingMembershipStateRowsResult(
    GetLowRemainingMembershipStateRowsStatus Status,
    LowRemainingMembershipStateRowsPage? Page,
    string? ErrorCode,
    string? ErrorMessage,
    string? ErrorField)
{
    public static GetLowRemainingMembershipStateRowsResult Succeeded(
        LowRemainingMembershipStateRowsPage page)
    {
        ArgumentNullException.ThrowIfNull(page);

        return new GetLowRemainingMembershipStateRowsResult(
            GetLowRemainingMembershipStateRowsStatus.Success,
            page,
            ErrorCode: null,
            ErrorMessage: null,
            ErrorField: null);
    }

    public static GetLowRemainingMembershipStateRowsResult Denied()
    {
        return Failure(
            GetLowRemainingMembershipStateRowsStatus.PermissionDenied,
            "permission_denied",
            "An active Owner, named Admin or shared Reception/Admin session is required.",
            field: null);
    }

    public static GetLowRemainingMembershipStateRowsResult Invalid(
        string message,
        string? field)
    {
        var normalizedMessage = message?.Trim();
        if (string.IsNullOrWhiteSpace(normalizedMessage))
        {
            throw new ArgumentException("Validation message is required.", nameof(message));
        }

        return Failure(
            GetLowRemainingMembershipStateRowsStatus.ValidationFailed,
            "validation_failed",
            normalizedMessage,
            field);
    }

    public static GetLowRemainingMembershipStateRowsResult RecalculationFailed()
    {
        return Failure(
            GetLowRemainingMembershipStateRowsStatus.RecalculationFailed,
            "recalculation_failed",
            "Low-remaining Membership state is unavailable because recalculation has not completed successfully.",
            field: null);
    }

    public static GetLowRemainingMembershipStateRowsResult InconsistentSource()
    {
        return Failure(
            GetLowRemainingMembershipStateRowsStatus.SourceInconsistent,
            "source_inconsistent",
            "Low-remaining Membership state is unavailable because canonical source records are inconsistent.",
            field: null);
    }

    private static GetLowRemainingMembershipStateRowsResult Failure(
        GetLowRemainingMembershipStateRowsStatus status,
        string errorCode,
        string errorMessage,
        string? field)
    {
        return new GetLowRemainingMembershipStateRowsResult(
            status,
            Page: null,
            errorCode,
            errorMessage,
            field);
    }
}
