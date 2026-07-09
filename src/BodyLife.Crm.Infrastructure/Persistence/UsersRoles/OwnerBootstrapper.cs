using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace BodyLife.Crm.Infrastructure.Persistence.UsersRoles;

public sealed class OwnerBootstrapper(BodyLifeDbContext dbContext, TimeProvider timeProvider)
{
    private const string OwnerAccountType = "owner";
    private const string OwnerRole = "owner";

    public async Task<OwnerBootstrapResult> BootstrapOwnerAsync(
        string? displayName,
        CancellationToken cancellationToken = default)
    {
        var normalizedDisplayName = displayName?.Trim();

        if (string.IsNullOrWhiteSpace(normalizedDisplayName))
        {
            return OwnerBootstrapResult.ValidationFailed("Owner display name is required.");
        }

        if (normalizedDisplayName.Length > 160)
        {
            return OwnerBootstrapResult.ValidationFailed("Owner display name must be 160 characters or fewer.");
        }

        var existingOwner = await FindOwnerAsync(cancellationToken);

        if (existingOwner is not null)
        {
            return OwnerBootstrapResult.AlreadyExists(existingOwner.Id);
        }

        var ownerAccount = new AccountRecord
        {
            Id = Guid.NewGuid(),
            DisplayName = normalizedDisplayName,
            AccountType = OwnerAccountType,
            Role = OwnerRole,
            IsActive = true,
            CreatedAt = timeProvider.GetUtcNow(),
            DeactivatedAt = null,
        };

        dbContext.Set<AccountRecord>().Add(ownerAccount);

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);

            return OwnerBootstrapResult.Created(ownerAccount.Id);
        }
        catch (DbUpdateException exception) when (IsSingleOwnerConstraintViolation(exception))
        {
            var racedOwner = await FindOwnerAsync(cancellationToken);

            if (racedOwner is null)
            {
                throw;
            }

            return OwnerBootstrapResult.AlreadyExists(racedOwner.Id);
        }
    }

    private Task<AccountRecord?> FindOwnerAsync(CancellationToken cancellationToken)
    {
        return dbContext.Set<AccountRecord>()
            .SingleOrDefaultAsync(account => account.AccountType == OwnerAccountType, cancellationToken);
    }

    private static bool IsSingleOwnerConstraintViolation(DbUpdateException exception)
    {
        return exception.InnerException is PostgresException postgresException
            && postgresException.SqlState == PostgresErrorCodes.UniqueViolation
            && postgresException.ConstraintName == "ux_accounts_single_owner";
    }
}
