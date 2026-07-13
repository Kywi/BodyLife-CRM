using BodyLife.Crm.Application.Queries;

namespace BodyLife.Crm.Modules.Memberships;

public sealed record GetMembershipStateResult(
    GetMembershipStateStatus Status,
    MembershipStateReadModel? State,
    QueryPermissionSet AllowedActions,
    string? ErrorCode,
    string? ErrorMessage,
    string? ErrorField)
{
    public static GetMembershipStateResult Succeeded(
        MembershipStateReadModel state,
        QueryPermissionSet allowedActions)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(allowedActions);

        return new GetMembershipStateResult(
            GetMembershipStateStatus.Success,
            state,
            allowedActions,
            ErrorCode: null,
            ErrorMessage: null,
            ErrorField: null);
    }

    public static GetMembershipStateResult Denied()
    {
        return Failure(
            GetMembershipStateStatus.PermissionDenied,
            "permission_denied",
            "An active Owner, named Admin or shared Reception/Admin session is required.",
            field: null);
    }

    public static GetMembershipStateResult Missing()
    {
        return Failure(
            GetMembershipStateStatus.NotFound,
            "not_found",
            "Membership was not found.",
            "membershipId");
    }

    public static GetMembershipStateResult Invalid(string message, string? field)
    {
        return Failure(
            GetMembershipStateStatus.ValidationFailed,
            "validation_failed",
            message,
            field);
    }

    private static GetMembershipStateResult Failure(
        GetMembershipStateStatus status,
        string errorCode,
        string errorMessage,
        string? field)
    {
        return new GetMembershipStateResult(
            status,
            State: null,
            QueryPermissionSet.Empty,
            errorCode,
            errorMessage,
            field);
    }
}
