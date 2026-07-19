namespace BodyLife.Crm.Modules.Memberships;

public sealed record GetEndingSoonMembershipStateRowsResult(
    GetEndingSoonMembershipStateRowsStatus Status,
    EndingSoonMembershipStateRowsPage? Page,
    string? ErrorCode,
    string? ErrorMessage,
    string? ErrorField)
{
    public static GetEndingSoonMembershipStateRowsResult Succeeded(
        EndingSoonMembershipStateRowsPage page)
    {
        ArgumentNullException.ThrowIfNull(page);

        return new GetEndingSoonMembershipStateRowsResult(
            GetEndingSoonMembershipStateRowsStatus.Success,
            page,
            ErrorCode: null,
            ErrorMessage: null,
            ErrorField: null);
    }

    public static GetEndingSoonMembershipStateRowsResult Denied()
    {
        return Failure(
            GetEndingSoonMembershipStateRowsStatus.PermissionDenied,
            "permission_denied",
            "An active Owner, named Admin or shared Reception/Admin session is required.",
            field: null);
    }

    public static GetEndingSoonMembershipStateRowsResult Invalid(
        string message,
        string? field)
    {
        var normalizedMessage = message?.Trim();
        if (string.IsNullOrWhiteSpace(normalizedMessage))
        {
            throw new ArgumentException("Validation message is required.", nameof(message));
        }

        return Failure(
            GetEndingSoonMembershipStateRowsStatus.ValidationFailed,
            "validation_failed",
            normalizedMessage,
            field);
    }

    public static GetEndingSoonMembershipStateRowsResult RecalculationFailed()
    {
        return Failure(
            GetEndingSoonMembershipStateRowsStatus.RecalculationFailed,
            "recalculation_failed",
            "Ending-soon Membership state is unavailable because recalculation has not completed successfully.",
            field: null);
    }

    public static GetEndingSoonMembershipStateRowsResult InconsistentSource()
    {
        return Failure(
            GetEndingSoonMembershipStateRowsStatus.SourceInconsistent,
            "source_inconsistent",
            "Ending-soon Membership state is unavailable because canonical source records are inconsistent.",
            field: null);
    }

    private static GetEndingSoonMembershipStateRowsResult Failure(
        GetEndingSoonMembershipStateRowsStatus status,
        string errorCode,
        string errorMessage,
        string? field)
    {
        return new GetEndingSoonMembershipStateRowsResult(
            status,
            Page: null,
            errorCode,
            errorMessage,
            field);
    }
}
