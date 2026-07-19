namespace BodyLife.Crm.Modules.Memberships;

public sealed record GetNegativeMembershipStateRowsResult(
    GetNegativeMembershipStateRowsStatus Status,
    NegativeMembershipStateRowsPage? Page,
    string? ErrorCode,
    string? ErrorMessage,
    string? ErrorField)
{
    public static GetNegativeMembershipStateRowsResult Succeeded(
        NegativeMembershipStateRowsPage page)
    {
        ArgumentNullException.ThrowIfNull(page);

        return new GetNegativeMembershipStateRowsResult(
            GetNegativeMembershipStateRowsStatus.Success,
            page,
            ErrorCode: null,
            ErrorMessage: null,
            ErrorField: null);
    }

    public static GetNegativeMembershipStateRowsResult Denied()
    {
        return Failure(
            GetNegativeMembershipStateRowsStatus.PermissionDenied,
            "permission_denied",
            "An active Owner, named Admin or shared Reception/Admin session is required.",
            field: null);
    }

    public static GetNegativeMembershipStateRowsResult Invalid(
        string message,
        string? field)
    {
        var normalizedMessage = message?.Trim();
        if (string.IsNullOrWhiteSpace(normalizedMessage))
        {
            throw new ArgumentException("Validation message is required.", nameof(message));
        }

        return Failure(
            GetNegativeMembershipStateRowsStatus.ValidationFailed,
            "validation_failed",
            normalizedMessage,
            field);
    }

    public static GetNegativeMembershipStateRowsResult RecalculationFailed()
    {
        return Failure(
            GetNegativeMembershipStateRowsStatus.RecalculationFailed,
            "recalculation_failed",
            "Negative Membership state is unavailable because recalculation has not completed successfully.",
            field: null);
    }

    public static GetNegativeMembershipStateRowsResult InconsistentSource()
    {
        return Failure(
            GetNegativeMembershipStateRowsStatus.SourceInconsistent,
            "source_inconsistent",
            "Negative Membership state is unavailable because canonical source records are inconsistent.",
            field: null);
    }

    private static GetNegativeMembershipStateRowsResult Failure(
        GetNegativeMembershipStateRowsStatus status,
        string errorCode,
        string errorMessage,
        string? field)
    {
        return new GetNegativeMembershipStateRowsResult(
            status,
            Page: null,
            errorCode,
            errorMessage,
            field);
    }
}
