using BodyLife.Crm.Infrastructure.Persistence.UsersRoles;
using BodyLife.Crm.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace BodyLife.Crm.Infrastructure.Persistence.NonWorkingDays;

internal static class NonWorkingDayQuerySupport
{
    internal static async Task<bool> IsOwnerAuthorizedAsync(
        BodyLifeDbContext dbContext,
        ActorContext? actor,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        if (actor is not
            {
                Role: ActorRole.Owner,
                AccountKind: AccountKind.Owner,
            }
            || actor.AccountId.Value == Guid.Empty
            || actor.SessionId.Value == Guid.Empty)
        {
            return false;
        }

        return await (
            from account in dbContext.Set<AccountRecord>().AsNoTracking()
            join session in dbContext.Set<SessionRecord>().AsNoTracking()
                on account.Id equals session.AccountId
            where account.Id == actor.AccountId.Value
                && account.IsActive
                && account.AccountType == "owner"
                && account.Role == "owner"
                && session.Id == actor.SessionId.Value
                && session.EndedAt == null
                && session.ExpiresAt > now
            select account.Id)
            .AnyAsync(cancellationToken);
    }
}
