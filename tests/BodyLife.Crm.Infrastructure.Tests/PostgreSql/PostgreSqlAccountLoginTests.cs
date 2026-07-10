using BodyLife.Crm.Infrastructure.Persistence;
using BodyLife.Crm.Infrastructure.Persistence.UsersRoles;
using Microsoft.EntityFrameworkCore;

namespace BodyLife.Crm.Infrastructure.Tests.PostgreSql;

public sealed class PostgreSqlAccountLoginTests
{
    private static readonly DateTimeOffset TestNow = new(2026, 7, 10, 9, 0, 0, TimeSpan.Zero);

    [PostgreSqlFact]
    public async Task OwnerCredentialsBootstrapperStoresLoginHash()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var dbContext = database.CreateDbContext();
        await dbContext.Database.MigrateAsync();

        var passwordHashingService = new PasswordHashingService();
        await BootstrapOwnerAsync(dbContext);
        var credentialsBootstrapper = new OwnerCredentialsBootstrapper(
            dbContext,
            passwordHashingService,
            TimeProvider.System);

        var result = await credentialsBootstrapper.SetOwnerCredentialsAsync(
            " owner ",
            "correct horse battery");

        var loginName = await database.ExecuteScalarAsync<string>(
            "select login_name from bodylife.account_credentials");
        var normalizedLoginName = await database.ExecuteScalarAsync<string>(
            "select normalized_login_name from bodylife.account_credentials");
        var passwordHash = await database.ExecuteScalarAsync<string>(
            "select password_hash from bodylife.account_credentials");

        Assert.Equal(OwnerCredentialsBootstrapStatus.Updated, result.Status);
        Assert.Equal("owner", loginName);
        Assert.Equal("OWNER", normalizedLoginName);
        Assert.NotEqual("correct horse battery", passwordHash);
        Assert.True(passwordHashingService.VerifyPassword("correct horse battery", passwordHash!));
    }

    [PostgreSqlFact]
    public async Task AccountLoginServiceCreatesAndEndsSession()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var dbContext = database.CreateDbContext();
        await dbContext.Database.MigrateAsync();

        var passwordHashingService = new PasswordHashingService();
        await BootstrapOwnerCredentialsAsync(dbContext, passwordHashingService);
        var loginService = new AccountLoginService(
            dbContext,
            passwordHashingService,
            new FixedTimeProvider(TestNow));

        var loginResult = await loginService.LoginAsync(
            "owner",
            "correct horse battery",
            " front desk tablet ");

        var activeSessionCount = await CountSessionsAsync(database, ended: false);

        Assert.Equal(AccountLoginStatus.Success, loginResult.Status);
        Assert.NotNull(loginResult.Session);
        Assert.Equal("front desk tablet", loginResult.Session.DeviceLabel);
        Assert.Equal(TestNow.Add(AccountSessionPolicy.IdleTimeout), loginResult.Session.ExpiresAt);
        Assert.Equal(1L, activeSessionCount);

        var logoutResult = await loginService.LogoutAsync(loginResult.Session.SessionId);
        var endedSessionCount = await CountSessionsAsync(database, ended: true);

        Assert.True(logoutResult);
        Assert.Equal(1L, endedSessionCount);
    }

    [PostgreSqlFact]
    public async Task AccountLoginServiceRejectsInvalidPasswordWithoutSession()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var dbContext = database.CreateDbContext();
        await dbContext.Database.MigrateAsync();

        var passwordHashingService = new PasswordHashingService();
        await BootstrapOwnerCredentialsAsync(dbContext, passwordHashingService);
        var loginService = new AccountLoginService(dbContext, passwordHashingService, TimeProvider.System);

        var loginResult = await loginService.LoginAsync("owner", "wrong password", "front desk tablet");
        var sessionCount = await CountSessionsAsync(database, ended: null);

        Assert.Equal(AccountLoginStatus.InvalidCredentials, loginResult.Status);
        Assert.Null(loginResult.Session);
        Assert.Equal(0L, sessionCount);
    }

    [PostgreSqlFact]
    public async Task AccountLoginServiceRejectsInactiveAccountWithoutSession()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var dbContext = database.CreateDbContext();
        await dbContext.Database.MigrateAsync();

        var passwordHashingService = new PasswordHashingService();
        await BootstrapOwnerCredentialsAsync(dbContext, passwordHashingService);
        await database.ExecuteScalarAsync<int>(
            """
            update bodylife.accounts
            set is_active = false,
                deactivated_at = now()
            where account_type = 'owner'
            returning 1
            """);
        dbContext.ChangeTracker.Clear();
        var loginService = new AccountLoginService(dbContext, passwordHashingService, TimeProvider.System);

        var loginResult = await loginService.LoginAsync(
            "owner",
            "correct horse battery",
            "front desk tablet");
        var sessionCount = await CountSessionsAsync(database, ended: null);

        Assert.Equal(AccountLoginStatus.InvalidCredentials, loginResult.Status);
        Assert.Null(loginResult.Session);
        Assert.Equal(0L, sessionCount);
    }

    private static async Task BootstrapOwnerCredentialsAsync(
        BodyLifeDbContext dbContext,
        PasswordHashingService passwordHashingService)
    {
        await BootstrapOwnerAsync(dbContext);
        var credentialsBootstrapper = new OwnerCredentialsBootstrapper(
            dbContext,
            passwordHashingService,
            TimeProvider.System);

        var credentialsResult = await credentialsBootstrapper.SetOwnerCredentialsAsync(
            "owner",
            "correct horse battery");

        Assert.Equal(OwnerCredentialsBootstrapStatus.Updated, credentialsResult.Status);
    }

    private static async Task BootstrapOwnerAsync(BodyLifeDbContext dbContext)
    {
        var ownerBootstrapper = new OwnerBootstrapper(dbContext, TimeProvider.System);
        var ownerResult = await ownerBootstrapper.BootstrapOwnerAsync("BodyLife Owner");

        Assert.Equal(OwnerBootstrapStatus.Created, ownerResult.Status);
    }

    private static Task<long> CountSessionsAsync(PostgreSqlTestDatabase database, bool? ended)
    {
        var predicate = ended switch
        {
            true => "ended_at is not null",
            false => "ended_at is null",
            null => "true",
        };

        return database.ExecuteScalarAsync<long>($"select count(*) from bodylife.sessions where {predicate}");
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow()
        {
            return utcNow;
        }
    }
}
