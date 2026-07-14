using BodyLife.Crm.Application.Queries;

namespace BodyLife.Crm.Modules.Memberships;

public sealed record GetClientMembershipStatesResult(
    GetClientMembershipStatesStatus Status,
    ClientMembershipStatesReadModel? StateCollection,
    QueryPermissionSet AllowedActions,
    string? ErrorCode,
    string? ErrorMessage,
    string? ErrorField)
{
    public static GetClientMembershipStatesResult Succeeded(
        ClientMembershipStatesReadModel stateCollection,
        QueryPermissionSet allowedActions)
    {
        ArgumentNullException.ThrowIfNull(stateCollection);
        ArgumentNullException.ThrowIfNull(allowedActions);

        return new GetClientMembershipStatesResult(
            GetClientMembershipStatesStatus.Success,
            stateCollection,
            allowedActions,
            ErrorCode: null,
            ErrorMessage: null,
            ErrorField: null);
    }

    public static GetClientMembershipStatesResult Denied()
    {
        return Failure(
            GetClientMembershipStatesStatus.PermissionDenied,
            "permission_denied",
            "An active Owner, named Admin or shared Reception/Admin session is required.",
            field: null);
    }

    public static GetClientMembershipStatesResult MissingClient()
    {
        return Failure(
            GetClientMembershipStatesStatus.NotFound,
            "not_found",
            "Client was not found.",
            "clientId");
    }

    public static GetClientMembershipStatesResult Invalid(string message, string? field)
    {
        var normalizedMessage = message?.Trim();
        if (string.IsNullOrWhiteSpace(normalizedMessage))
        {
            throw new ArgumentException("Validation message is required.", nameof(message));
        }

        return Failure(
            GetClientMembershipStatesStatus.ValidationFailed,
            "validation_failed",
            normalizedMessage,
            field);
    }

    public static GetClientMembershipStatesResult RecalculationFailed()
    {
        return Failure(
            GetClientMembershipStatesStatus.RecalculationFailed,
            "recalculation_failed",
            "Client membership state is unavailable because recalculation has not completed successfully.",
            field: null);
    }

    private static GetClientMembershipStatesResult Failure(
        GetClientMembershipStatesStatus status,
        string errorCode,
        string errorMessage,
        string? field)
    {
        return new GetClientMembershipStatesResult(
            status,
            StateCollection: null,
            QueryPermissionSet.Empty,
            errorCode,
            errorMessage,
            field);
    }
}
