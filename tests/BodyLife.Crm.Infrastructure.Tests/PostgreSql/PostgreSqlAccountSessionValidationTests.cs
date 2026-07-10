using BodyLife.Crm.Infrastructure.Persistence;
using BodyLife.Crm.Infrastructure.Persistence.UsersRoles;
using BodyLife.Crm.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace BodyLife.Crm.Infrastructure.Tests.PostgreSql;

public sealed class PostgreSqlAccountSessionValidationTests
{
    private static readonly DateTimeOffset TestNow = new(2026, 7, 10, 9, 0, 0, TimeSpan.Zero);

    [PostgreSqlFact]
    public async Task ActiveSessionValidationRenewsDatabaseExpiry()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var dbContext = database.CreateDbContext();
        await dbContext.Database.MigrateAsync();
        var clock = new MutableTimeProvider(TestNow);
        var session = await LoginOwnerAsync(dbContext, clock);
        clock.Advance(TimeSpan.FromHours(6));
        var validationService = new AccountSessionValidationService(dbContext, clock);

        var status = await validationService.ValidateAsync(OwnerActor(session));

        var lastSeenAt = await database.ExecuteScalarAsync<DateTime>(
            $"select last_seen_at from bodylife.sessions where id = '{session.SessionId}'::uuid");
        var expiresAt = await database.ExecuteScalarAsync<DateTime>(
            $"select expires_at from bodylife.sessions where id = '{session.SessionId}'::uuid");
        Assert.Equal(AccountSessionValidationStatus.Active, status);
        Assert.Equal(clock.GetUtcNow().UtcDateTime, lastSeenAt);
        Assert.Equal(clock.GetUtcNow().Add(AccountSessionPolicy.IdleTimeout).UtcDateTime, expiresAt);
    }

    [PostgreSqlFact]
    public async Task SessionValidationExpiresSessionAtIdleTimeoutBoundary()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var dbContext = database.CreateDbContext();
        await dbContext.Database.MigrateAsync();
        var clock = new MutableTimeProvider(TestNow);
        var session = await LoginOwnerAsync(dbContext, clock);
        clock.Advance(AccountSessionPolicy.IdleTimeout);
        var validationService = new AccountSessionValidationService(dbContext, clock);

        var status = await validationService.ValidateAsync(OwnerActor(session));

        var endedAt = await database.ExecuteScalarAsync<DateTime>(
            $"select ended_at from bodylife.sessions where id = '{session.SessionId}'::uuid");
        Assert.Equal(AccountSessionValidationStatus.Expired, status);
        Assert.Equal(clock.GetUtcNow().UtcDateTime, endedAt);
    }

    [PostgreSqlFact]
    public async Task SessionValidationRejectsEndedSessionWithoutRenewingIt()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var dbContext = database.CreateDbContext();
        await dbContext.Database.MigrateAsync();
        var clock = new MutableTimeProvider(TestNow);
        var session = await LoginOwnerAsync(dbContext, clock);
        var loginService = new AccountLoginService(dbContext, new PasswordHashingService(), clock);
        Assert.True(await loginService.LogoutAsync(session.SessionId));
        var validationService = new AccountSessionValidationService(dbContext, clock);

        var status = await validationService.ValidateAsync(OwnerActor(session));

        var expiresAt = await database.ExecuteScalarAsync<DateTime>(
            $"select expires_at from bodylife.sessions where id = '{session.SessionId}'::uuid");
        Assert.Equal(AccountSessionValidationStatus.Ended, status);
        Assert.Equal(session.ExpiresAt.UtcDateTime, expiresAt);
    }

    [PostgreSqlFact]
    public async Task SessionValidationRejectsClaimsMismatchWithoutEndingSession()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var dbContext = database.CreateDbContext();
        await dbContext.Database.MigrateAsync();
        var clock = new MutableTimeProvider(TestNow);
        var session = await LoginOwnerAsync(dbContext, clock);
        var validationService = new AccountSessionValidationService(dbContext, clock);
        var mismatchedActor = OwnerActor(session) with { AccountId = AccountId.New() };

        var status = await validationService.ValidateAsync(mismatchedActor);

        var remainsUnended = await database.ExecuteScalarAsync<bool>(
            $"select ended_at is null from bodylife.sessions where id = '{session.SessionId}'::uuid");
        Assert.Equal(AccountSessionValidationStatus.ClaimsMismatch, status);
        Assert.True(remainsUnended);
    }

    private static async Task<AccountSessionSnapshot> LoginOwnerAsync(
        BodyLifeDbContext dbContext,
        TimeProvider timeProvider)
    {
        var ownerResult = await new OwnerBootstrapper(dbContext, timeProvider)
            .BootstrapOwnerAsync("BodyLife Owner");
        Assert.Equal(OwnerBootstrapStatus.Created, ownerResult.Status);
        var hashingService = new PasswordHashingService();
        var credentialResult = await new OwnerCredentialsBootstrapper(
                dbContext,
                hashingService,
                timeProvider)
            .SetOwnerCredentialsAsync("owner", "correct horse battery");
        Assert.Equal(OwnerCredentialsBootstrapStatus.Updated, credentialResult.Status);
        var loginResult = await new AccountLoginService(dbContext, hashingService, timeProvider)
            .LoginAsync("owner", "correct horse battery", "front desk tablet");
        Assert.Equal(AccountLoginStatus.Success, loginResult.Status);
        return Assert.IsType<AccountSessionSnapshot>(loginResult.Session);
    }

    private static ActorContext OwnerActor(AccountSessionSnapshot session)
    {
        return new ActorContext(
            new AccountId(session.AccountId),
            ActorRole.Owner,
            AccountKind.Owner,
            new SessionId(session.SessionId),
            session.DeviceLabel);
    }

    private sealed class MutableTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        private DateTimeOffset _utcNow = utcNow;

        public override DateTimeOffset GetUtcNow()
        {
            return _utcNow;
        }

        public void Advance(TimeSpan duration)
        {
            _utcNow = _utcNow.Add(duration);
        }
    }
}
