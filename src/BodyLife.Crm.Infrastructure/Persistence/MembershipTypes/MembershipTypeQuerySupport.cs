using BodyLife.Crm.Application.Queries;
using BodyLife.Crm.Infrastructure.Persistence.UsersRoles;
using BodyLife.Crm.Modules.MembershipTypes;
using BodyLife.Crm.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace BodyLife.Crm.Infrastructure.Persistence.MembershipTypes;

internal static class MembershipTypeQuerySupport
{
    private const string OwnerAccountType = "owner";
    private const string NamedAdminAccountType = "named_admin";
    private const string SharedReceptionAdminAccountType = "shared_reception_admin";
    private const string OwnerRole = "owner";
    private const string AdminRole = "admin";
    private const string OwnerActionDeniedReason =
        "Current account is not allowed to manage membership types.";
    private static readonly QueryPermissionSet OwnerActionPermissions = new(
    [
        QueryPermissionResult.Allowed(
            MembershipTypeCatalogActionKeys.Create,
            MembershipTypeCatalogActionKeys.OwnerPolicy),
        QueryPermissionResult.Allowed(
            MembershipTypeCatalogActionKeys.Edit,
            MembershipTypeCatalogActionKeys.OwnerPolicy),
        QueryPermissionResult.Allowed(
            MembershipTypeCatalogActionKeys.Deactivate,
            MembershipTypeCatalogActionKeys.OwnerPolicy),
    ]);
    private static readonly QueryPermissionSet AdminActionPermissions = new(
    [
        DeniedOwnerAction(MembershipTypeCatalogActionKeys.Create),
        DeniedOwnerAction(MembershipTypeCatalogActionKeys.Edit),
        DeniedOwnerAction(MembershipTypeCatalogActionKeys.Deactivate),
    ]);

    internal static bool IsOwnerActor(ActorContext actor)
    {
        return actor is
        {
            Role: ActorRole.Owner,
            AccountKind: AccountKind.Owner,
        };
    }

    internal static async Task<bool> IsActorAuthorizedAsync(
        BodyLifeDbContext dbContext,
        ActorContext? actor,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        if (!IsAllowedActorShape(actor))
        {
            return false;
        }

        var accountType = MapAccountKind(actor!.AccountKind);
        var role = MapActorRole(actor.Role);
        return await (
            from account in dbContext.Set<AccountRecord>().AsNoTracking()
            join session in dbContext.Set<SessionRecord>().AsNoTracking()
                on account.Id equals session.AccountId
            where account.Id == actor.AccountId.Value
                && account.IsActive
                && account.AccountType == accountType
                && account.Role == role
                && session.Id == actor.SessionId.Value
                && session.EndedAt == null
                && session.ExpiresAt > now
            select account.Id)
            .AnyAsync(cancellationToken);
    }

    internal static QueryPermissionSet BuildActionPermissions(ActorContext actor)
    {
        return IsOwnerActor(actor)
            ? OwnerActionPermissions
            : AdminActionPermissions;
    }

    private static bool IsAllowedActorShape(ActorContext? actor)
    {
        return actor is not null
            && actor.AccountId.Value != Guid.Empty
            && actor.SessionId.Value != Guid.Empty
            && actor switch
            {
                { Role: ActorRole.Owner, AccountKind: AccountKind.Owner } => true,
                {
                    Role: ActorRole.Admin,
                    AccountKind: AccountKind.NamedAdmin or AccountKind.SharedReceptionAdmin,
                } => true,
                _ => false,
            };
    }

    private static QueryPermissionResult DeniedOwnerAction(string actionKey)
    {
        return QueryPermissionResult.Denied(
            actionKey,
            MembershipTypeCatalogActionKeys.OwnerPolicy,
            QueryPermissionDeniedReasonCodes.PermissionDenied,
            OwnerActionDeniedReason);
    }

    private static string MapAccountKind(AccountKind accountKind)
    {
        return accountKind switch
        {
            AccountKind.Owner => OwnerAccountType,
            AccountKind.NamedAdmin => NamedAdminAccountType,
            AccountKind.SharedReceptionAdmin => SharedReceptionAdminAccountType,
            _ => throw new ArgumentOutOfRangeException(nameof(accountKind), accountKind, null),
        };
    }

    private static string MapActorRole(ActorRole role)
    {
        return role switch
        {
            ActorRole.Owner => OwnerRole,
            ActorRole.Admin => AdminRole,
            _ => throw new ArgumentOutOfRangeException(nameof(role), role, null),
        };
    }
}
