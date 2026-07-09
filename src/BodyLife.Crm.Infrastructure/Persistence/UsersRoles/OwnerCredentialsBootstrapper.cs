using Microsoft.EntityFrameworkCore;

namespace BodyLife.Crm.Infrastructure.Persistence.UsersRoles;

public sealed class OwnerCredentialsBootstrapper(
    BodyLifeDbContext dbContext,
    PasswordHashingService passwordHashingService,
    TimeProvider timeProvider)
{
    private const string OwnerAccountType = "owner";
    private const int MinimumPasswordLength = 12;

    public async Task<OwnerCredentialsBootstrapResult> SetOwnerCredentialsAsync(
        string? loginName,
        string? password,
        CancellationToken cancellationToken = default)
    {
        var normalizedLoginName = NormalizeLoginName(loginName);

        if (normalizedLoginName is null)
        {
            return OwnerCredentialsBootstrapResult.ValidationFailed("Owner login name is required.");
        }

        if (normalizedLoginName.LoginName.Length > 120)
        {
            return OwnerCredentialsBootstrapResult.ValidationFailed("Owner login name must be 120 characters or fewer.");
        }

        if (string.IsNullOrWhiteSpace(password))
        {
            return OwnerCredentialsBootstrapResult.ValidationFailed("Owner password is required.");
        }

        if (password.Length < MinimumPasswordLength)
        {
            return OwnerCredentialsBootstrapResult.ValidationFailed(
                $"Owner password must be at least {MinimumPasswordLength} characters.");
        }

        var owner = await dbContext.Set<AccountRecord>()
            .SingleOrDefaultAsync(account => account.AccountType == OwnerAccountType, cancellationToken);

        if (owner is null)
        {
            return OwnerCredentialsBootstrapResult.OwnerMissing();
        }

        var credential = await dbContext.Set<AccountCredentialRecord>()
            .SingleOrDefaultAsync(value => value.AccountId == owner.Id, cancellationToken);

        if (credential is null)
        {
            credential = new AccountCredentialRecord
            {
                AccountId = owner.Id,
                LoginName = normalizedLoginName.LoginName,
                NormalizedLoginName = normalizedLoginName.NormalizedLoginName,
                PasswordHash = passwordHashingService.HashPassword(password),
                PasswordChangedAt = timeProvider.GetUtcNow(),
            };
            dbContext.Set<AccountCredentialRecord>().Add(credential);
        }
        else
        {
            credential.LoginName = normalizedLoginName.LoginName;
            credential.NormalizedLoginName = normalizedLoginName.NormalizedLoginName;
            credential.PasswordHash = passwordHashingService.HashPassword(password);
            credential.PasswordChangedAt = timeProvider.GetUtcNow();
        }

        await dbContext.SaveChangesAsync(cancellationToken);

        return OwnerCredentialsBootstrapResult.Updated(owner.Id);
    }

    private static NormalizedLogin? NormalizeLoginName(string? loginName)
    {
        var trimmedLoginName = loginName?.Trim();

        return string.IsNullOrWhiteSpace(trimmedLoginName)
            ? null
            : new NormalizedLogin(trimmedLoginName, PasswordHashingService.NormalizeLoginName(trimmedLoginName));
    }

    private sealed record NormalizedLogin(string LoginName, string NormalizedLoginName);
}
