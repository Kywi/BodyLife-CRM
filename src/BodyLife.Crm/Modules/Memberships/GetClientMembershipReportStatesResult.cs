namespace BodyLife.Crm.Modules.Memberships;

public sealed record GetClientMembershipReportStatesResult(
    GetClientMembershipReportStatesStatus Status,
    ClientMembershipReportStates? States,
    string? ErrorCode,
    string? ErrorMessage,
    string? ErrorField)
{
    public static GetClientMembershipReportStatesResult Succeeded(
        ClientMembershipReportStates states)
    {
        ArgumentNullException.ThrowIfNull(states);

        return new GetClientMembershipReportStatesResult(
            GetClientMembershipReportStatesStatus.Success,
            states,
            ErrorCode: null,
            ErrorMessage: null,
            ErrorField: null);
    }

    public static GetClientMembershipReportStatesResult Denied()
    {
        return Failure(
            GetClientMembershipReportStatesStatus.PermissionDenied,
            "permission_denied",
            "An active Owner, named Admin or shared Reception/Admin session is required.",
            field: null);
    }

    public static GetClientMembershipReportStatesResult Invalid(
        string message,
        string? field)
    {
        var normalizedMessage = message?.Trim();
        if (string.IsNullOrWhiteSpace(normalizedMessage))
        {
            throw new ArgumentException("Validation message is required.", nameof(message));
        }

        return Failure(
            GetClientMembershipReportStatesStatus.ValidationFailed,
            "validation_failed",
            normalizedMessage,
            field);
    }

    public static GetClientMembershipReportStatesResult RecalculationFailed()
    {
        return Failure(
            GetClientMembershipReportStatesStatus.RecalculationFailed,
            "recalculation_failed",
            "Client Membership report state is unavailable because recalculation has not completed successfully.",
            field: null);
    }

    public static GetClientMembershipReportStatesResult InconsistentSource()
    {
        return Failure(
            GetClientMembershipReportStatesStatus.SourceInconsistent,
            "source_inconsistent",
            "Client Membership report state is unavailable because canonical source records are inconsistent.",
            field: null);
    }

    private static GetClientMembershipReportStatesResult Failure(
        GetClientMembershipReportStatesStatus status,
        string errorCode,
        string errorMessage,
        string? field)
    {
        return new GetClientMembershipReportStatesResult(
            status,
            States: null,
            errorCode,
            errorMessage,
            field);
    }
}
