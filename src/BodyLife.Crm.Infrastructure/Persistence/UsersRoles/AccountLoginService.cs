using Microsoft.EntityFrameworkCore;

namespace BodyLife.Crm.Infrastructure.Persistence.UsersRoles;

public sealed class AccountLoginService(
    BodyLifeDbContext dbContext,
    PasswordHashingService passwordHashingService,
    TimeProvider timeProvider)
{
    public async Task<AccountLoginResult> LoginAsync(
        string? loginName,
        string? password,
        string? deviceLabel,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(loginName) || string.IsNullOrWhiteSpace(password))
        {
            return AccountLoginResult.InvalidCredentials();
        }

        var normalizedLoginName = PasswordHashingService.NormalizeLoginName(loginName);
        var credential = await dbContext.Set<AccountCredentialRecord>()
            .Include(value => value.Account)
            .SingleOrDefaultAsync(
                value => value.NormalizedLoginName == normalizedLoginName,
                cancellationToken);

        if (credential?.Account is null
            || !credential.Account.IsActive
            || !passwordHashingService.VerifyPassword(password, credential.PasswordHash))
        {
            return AccountLoginResult.InvalidCredentials();
        }

        var now = timeProvider.GetUtcNow();
        var expiresAt = now.Add(AccountSessionPolicy.IdleTimeout);
        var normalizedDeviceLabel = NormalizeDeviceLabel(deviceLabel);
        var session = new SessionRecord
        {
            Id = Guid.NewGuid(),
            AccountId = credential.AccountId,
            DeviceLabel = normalizedDeviceLabel,
            StartedAt = now,
            ExpiresAt = expiresAt,
            LastSeenAt = now,
            EndedAt = null,
        };

        dbContext.Set<SessionRecord>().Add(session);
        await dbContext.SaveChangesAsync(cancellationToken);

        return AccountLoginResult.Success(new AccountSessionSnapshot(
            credential.AccountId,
            session.Id,
            credential.Account.DisplayName,
            credential.Account.AccountType,
            credential.Account.Role,
            normalizedDeviceLabel,
            expiresAt));
    }

    public async Task<bool> LogoutAsync(Guid sessionId, CancellationToken cancellationToken = default)
    {
        var session = await dbContext.Set<SessionRecord>()
            .SingleOrDefaultAsync(
                value => value.Id == sessionId && value.EndedAt == null,
                cancellationToken);

        if (session is null)
        {
            return false;
        }

        var now = timeProvider.GetUtcNow();
        session.EndedAt = now;
        session.LastSeenAt = now;

        await dbContext.SaveChangesAsync(cancellationToken);

        return true;
    }

    private static string? NormalizeDeviceLabel(string? deviceLabel)
    {
        var trimmedDeviceLabel = deviceLabel?.Trim();

        if (string.IsNullOrWhiteSpace(trimmedDeviceLabel))
        {
            return null;
        }

        return trimmedDeviceLabel.Length <= 120
            ? trimmedDeviceLabel
            : trimmedDeviceLabel[..120];
    }
}
