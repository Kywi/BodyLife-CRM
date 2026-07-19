namespace BodyLife.Crm.Modules.Reports;

public sealed record ListEndingSoonMembershipsResult(
    ListEndingSoonMembershipsStatus Status,
    EndingSoonMembershipsPage? Page,
    string? ErrorCode,
    string? ErrorMessage,
    string? ErrorField)
{
    public static ListEndingSoonMembershipsResult Succeeded(
        EndingSoonMembershipsPage page)
    {
        ArgumentNullException.ThrowIfNull(page);

        return new ListEndingSoonMembershipsResult(
            ListEndingSoonMembershipsStatus.Success,
            page,
            ErrorCode: null,
            ErrorMessage: null,
            ErrorField: null);
    }

    public static ListEndingSoonMembershipsResult Denied()
    {
        return Failure(
            ListEndingSoonMembershipsStatus.PermissionDenied,
            "permission_denied",
            "An active Owner, named Admin or shared Reception/Admin session is required.",
            field: null);
    }

    public static ListEndingSoonMembershipsResult Invalid(
        string message,
        string? field)
    {
        var normalizedMessage = message?.Trim();
        if (string.IsNullOrWhiteSpace(normalizedMessage))
        {
            throw new ArgumentException("Validation message is required.", nameof(message));
        }

        return Failure(
            ListEndingSoonMembershipsStatus.ValidationFailed,
            "validation_failed",
            normalizedMessage,
            field);
    }

    public static ListEndingSoonMembershipsResult RecalculationFailed()
    {
        return Failure(
            ListEndingSoonMembershipsStatus.RecalculationFailed,
            "recalculation_failed",
            "Ending-soon report is unavailable because Memberships recalculation has not completed successfully.",
            field: null);
    }

    public static ListEndingSoonMembershipsResult InconsistentSource()
    {
        return Failure(
            ListEndingSoonMembershipsStatus.SourceInconsistent,
            "source_inconsistent",
            "Ending-soon report is unavailable because canonical Memberships source rows are inconsistent.",
            field: null);
    }

    private static ListEndingSoonMembershipsResult Failure(
        ListEndingSoonMembershipsStatus status,
        string errorCode,
        string errorMessage,
        string? field)
    {
        return new ListEndingSoonMembershipsResult(
            status,
            Page: null,
            errorCode,
            errorMessage,
            field);
    }
}
