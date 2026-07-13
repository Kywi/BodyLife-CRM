using BodyLife.Crm.Application.Queries;
using BodyLife.Crm.Modules.Memberships;
using BodyLife.Crm.SharedKernel;

namespace BodyLife.Crm.Infrastructure.Persistence.Memberships;

internal static class MembershipQuerySupport
{
    internal const string ActiveMembershipStatus = "active";
    internal const string ActiveOpeningStateStatus = "active";
    private static readonly QueryPermissionSet OpeningStateActionPermissions = new(
    [
        QueryPermissionResult.Allowed(
            MembershipActionKeys.CreateOpeningState,
            MembershipActionKeys.AdminOrOwnerPolicy),
    ]);
    private static readonly QueryPermissionSet IssueActionPermissions = new(
    [
        QueryPermissionResult.Allowed(
            MembershipActionKeys.Issue,
            MembershipActionKeys.AdminOrOwnerPolicy),
    ]);

    internal static Task<bool> IsActorAuthorizedAsync(
        BodyLifeDbContext dbContext,
        ActorContext? actor,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        return actor is not null && MembershipCommandSupport.IsAllowedActorShape(actor)
            ? MembershipCommandSupport.IsCanonicalActorAuthorizedAsync(
                dbContext,
                actor,
                now,
                cancellationToken)
            : Task.FromResult(false);
    }

    internal static QueryPermissionSet BuildActionPermissions(
        string membershipStatus,
        bool hasActiveOpeningState)
    {
        return membershipStatus == ActiveMembershipStatus && !hasActiveOpeningState
            ? OpeningStateActionPermissions
            : QueryPermissionSet.Empty;
    }

    internal static QueryPermissionSet BuildIssueActionPermissions()
    {
        return IssueActionPermissions;
    }
}
