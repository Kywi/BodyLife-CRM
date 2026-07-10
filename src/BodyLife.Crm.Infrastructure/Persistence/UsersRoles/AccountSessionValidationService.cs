using BodyLife.Crm.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace BodyLife.Crm.Infrastructure.Persistence.UsersRoles;

public sealed class AccountSessionValidationService(
    BodyLifeDbContext dbContext,
    TimeProvider timeProvider)
{
    public async Task<AccountSessionValidationStatus> ValidateAsync(
        ActorContext actor,
        CancellationToken cancellationToken = default)
    {
        var session = await dbContext.Set<SessionRecord>()
            .Include(record => record.Account)
            .SingleOrDefaultAsync(
                record => record.Id == actor.SessionId.Value,
                cancellationToken);

        if (session?.Account is null)
        {
            return AccountSessionValidationStatus.Missing;
        }

        if (!ClaimsMatchAccount(actor, session))
        {
            return AccountSessionValidationStatus.ClaimsMismatch;
        }

        if (session.EndedAt is not null)
        {
            return AccountSessionValidationStatus.Ended;
        }

        var now = timeProvider.GetUtcNow();

        if (session.ExpiresAt <= now)
        {
            session.EndedAt = now;
            await dbContext.SaveChangesAsync(cancellationToken);
            return AccountSessionValidationStatus.Expired;
        }

        if (!session.Account.IsActive)
        {
            session.EndedAt = now;
            session.LastSeenAt = now;
            await dbContext.SaveChangesAsync(cancellationToken);
            return AccountSessionValidationStatus.AccountInactive;
        }

        session.LastSeenAt = now;
        session.ExpiresAt = now.Add(AccountSessionPolicy.IdleTimeout);
        await dbContext.SaveChangesAsync(cancellationToken);

        return AccountSessionValidationStatus.Active;
    }

    private static bool ClaimsMatchAccount(ActorContext actor, SessionRecord session)
    {
        return session.AccountId == actor.AccountId.Value
            && session.Account!.AccountType == MapAccountType(actor.AccountKind)
            && session.Account.Role == MapRole(actor.Role);
    }

    private static string? MapAccountType(AccountKind accountKind)
    {
        return accountKind switch
        {
            AccountKind.Owner => "owner",
            AccountKind.NamedAdmin => "named_admin",
            AccountKind.SharedReceptionAdmin => "shared_reception_admin",
            _ => null,
        };
    }

    private static string? MapRole(ActorRole role)
    {
        return role switch
        {
            ActorRole.Owner => "owner",
            ActorRole.Admin => "admin",
            _ => null,
        };
    }
}

public enum AccountSessionValidationStatus
{
    Active,
    Missing,
    Ended,
    Expired,
    AccountInactive,
    ClaimsMismatch,
    InvalidClaims,
}
