using BodyLife.Crm.Application.Commands;
using BodyLife.Crm.Infrastructure.Persistence;
using BodyLife.Crm.Infrastructure.Persistence.UsersRoles;
using BodyLife.Crm.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace BodyLife.Crm.Infrastructure.Tests.PostgreSql;

public sealed class PostgreSqlStaffCredentialsTests
{
    private static readonly DateTimeOffset TestNow = new(2026, 7, 9, 14, 0, 0, TimeSpan.Zero);

    [PostgreSqlFact]
    public async Task StaffCredentialsConfigureNamedAndSharedAccountLoginWithoutDefaultSecrets()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var dbContext = database.CreateDbContext();
        await dbContext.Database.MigrateAsync();
        var clock = new MutableTimeProvider(TestNow);
        var hashingService = new PasswordHashingService();
        var lifecycleService = new StaffAccountLifecycleService(dbContext, clock);
        var credentialsService = new StaffCredentialsService(dbContext, hashingService, clock);
        var namedAdminId = await CreateStaffAccountAsync(
            lifecycleService,
            AccountKind.NamedAdmin,
            "Main Admin");
        var sharedReceptionId = await CreateStaffAccountAsync(
            lifecycleService,
            AccountKind.SharedReceptionAdmin,
            "Front desk shared");

        var namedResult = await credentialsService.SetStaffCredentialsAsync(
            OwnerEnvelope(),
            namedAdminId,
            " main.admin ",
            "named admin password");
        var sharedResult = await credentialsService.SetStaffCredentialsAsync(
            OwnerEnvelope(),
            sharedReceptionId,
            " front.desk ",
            "shared desk password");

        Assert.Equal(StaffCredentialsStatus.Configured, namedResult.Status);
        Assert.True(namedResult.Succeeded);
        Assert.Equal(StaffCredentialsStatus.Configured, sharedResult.Status);
        Assert.True(sharedResult.Succeeded);
        Assert.Equal("MAIN.ADMIN", await ReadNormalizedLoginNameAsync(database, namedAdminId));
        Assert.Equal("FRONT.DESK", await ReadNormalizedLoginNameAsync(database, sharedReceptionId));

        var namedPasswordHash = await ReadPasswordHashAsync(database, namedAdminId);
        Assert.NotEqual("named admin password", namedPasswordHash);
        Assert.True(hashingService.VerifyPassword("named admin password", namedPasswordHash!));

        dbContext.ChangeTracker.Clear();
        var loginService = new AccountLoginService(dbContext, hashingService, clock);
        var namedLogin = await loginService.LoginAsync(
            "MAIN.ADMIN",
            "named admin password",
            "admin phone");
        var sharedLogin = await loginService.LoginAsync(
            "front.desk",
            "shared desk password",
            "front desk tablet");

        Assert.Equal(AccountLoginStatus.Success, namedLogin.Status);
        Assert.Equal("named_admin", namedLogin.Session!.AccountType);
        Assert.Equal("admin", namedLogin.Session.Role);
        Assert.Equal(AccountLoginStatus.Success, sharedLogin.Status);
        Assert.Equal("shared_reception_admin", sharedLogin.Session!.AccountType);
        Assert.Equal("admin", sharedLogin.Session.Role);
    }

    [PostgreSqlFact]
    public async Task StaffCredentialsResetReplacesSecretAndEndsActiveSessions()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var dbContext = database.CreateDbContext();
        await dbContext.Database.MigrateAsync();
        var clock = new MutableTimeProvider(TestNow);
        var hashingService = new PasswordHashingService();
        var lifecycleService = new StaffAccountLifecycleService(dbContext, clock);
        var credentialsService = new StaffCredentialsService(dbContext, hashingService, clock);
        var accountId = await CreateStaffAccountAsync(
            lifecycleService,
            AccountKind.NamedAdmin,
            "Main Admin");
        await credentialsService.SetStaffCredentialsAsync(
            OwnerEnvelope(),
            accountId,
            "main.admin",
            "original admin password");
        dbContext.ChangeTracker.Clear();
        var loginService = new AccountLoginService(dbContext, hashingService, clock);
        var initialLogin = await loginService.LoginAsync(
            "main.admin",
            "original admin password",
            "admin phone");
        Assert.Equal(AccountLoginStatus.Success, initialLogin.Status);
        clock.Advance(TimeSpan.FromHours(1));

        var resetResult = await credentialsService.SetStaffCredentialsAsync(
            OwnerEnvelope(),
            accountId,
            "renamed.admin",
            "replacement admin password");

        Assert.Equal(StaffCredentialsStatus.Reset, resetResult.Status);
        Assert.True(resetResult.Succeeded);
        Assert.Equal(1, resetResult.EndedSessionCount);
        Assert.Equal(clock.GetUtcNow().UtcDateTime, await ReadPasswordChangedAtAsync(database, accountId));
        Assert.Equal(1L, await CountSessionsAsync(database, accountId, ended: true));
        Assert.Equal(0L, await CountSessionsAsync(database, accountId, ended: false));

        dbContext.ChangeTracker.Clear();
        var oldLogin = await loginService.LoginAsync(
            "main.admin",
            "original admin password",
            "admin phone");
        var newLogin = await loginService.LoginAsync(
            "renamed.admin",
            "replacement admin password",
            "admin phone");

        Assert.Equal(AccountLoginStatus.InvalidCredentials, oldLogin.Status);
        Assert.Equal(AccountLoginStatus.Success, newLogin.Status);
    }

    [PostgreSqlFact]
    public async Task StaffCredentialsRequireOwnerActorWithoutMutation()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var dbContext = database.CreateDbContext();
        await dbContext.Database.MigrateAsync();
        var clock = new MutableTimeProvider(TestNow);
        var lifecycleService = new StaffAccountLifecycleService(dbContext, clock);
        var accountId = await CreateStaffAccountAsync(
            lifecycleService,
            AccountKind.NamedAdmin,
            "Main Admin");
        var service = new StaffCredentialsService(dbContext, new PasswordHashingService(), clock);

        var result = await service.SetStaffCredentialsAsync(
            AdminEnvelope(),
            accountId,
            "main.admin",
            "named admin password");

        Assert.Equal(StaffCredentialsStatus.PermissionDenied, result.Status);
        Assert.False(result.Succeeded);
        Assert.Equal(0L, await CountCredentialsAsync(database));
    }

    [PostgreSqlFact]
    public async Task StaffCredentialsProtectOwnerAndReportUnknownAccount()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var dbContext = database.CreateDbContext();
        await dbContext.Database.MigrateAsync();
        var clock = new MutableTimeProvider(TestNow);
        var ownerResult = await new OwnerBootstrapper(dbContext, clock)
            .BootstrapOwnerAsync("BodyLife Owner");
        var service = new StaffCredentialsService(dbContext, new PasswordHashingService(), clock);

        var ownerCredentialsResult = await service.SetStaffCredentialsAsync(
            OwnerEnvelope(),
            ownerResult.AccountId!.Value,
            "owner",
            "owner secret password");
        var missingAccountResult = await service.SetStaffCredentialsAsync(
            OwnerEnvelope(),
            Guid.NewGuid(),
            "missing",
            "missing secret password");

        Assert.Equal(StaffCredentialsStatus.OwnerAccountProtected, ownerCredentialsResult.Status);
        Assert.Equal(StaffCredentialsStatus.NotFound, missingAccountResult.Status);
        Assert.Equal(0L, await CountCredentialsAsync(database));
    }

    [PostgreSqlFact]
    public async Task StaffCredentialsValidateLoginAndPasswordBeforeMutation()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var dbContext = database.CreateDbContext();
        await dbContext.Database.MigrateAsync();
        var clock = new MutableTimeProvider(TestNow);
        var lifecycleService = new StaffAccountLifecycleService(dbContext, clock);
        var accountId = await CreateStaffAccountAsync(
            lifecycleService,
            AccountKind.NamedAdmin,
            "Main Admin");
        var service = new StaffCredentialsService(dbContext, new PasswordHashingService(), clock);

        var missingLogin = await service.SetStaffCredentialsAsync(
            OwnerEnvelope(),
            accountId,
            " ",
            "named admin password");
        var longLogin = await service.SetStaffCredentialsAsync(
            OwnerEnvelope(),
            accountId,
            new string('a', 121),
            "named admin password");
        var missingPassword = await service.SetStaffCredentialsAsync(
            OwnerEnvelope(),
            accountId,
            "main.admin",
            " ");
        var shortPassword = await service.SetStaffCredentialsAsync(
            OwnerEnvelope(),
            accountId,
            "main.admin",
            "too short");

        Assert.Equal(StaffCredentialsStatus.ValidationFailed, missingLogin.Status);
        Assert.Equal(StaffCredentialsStatus.ValidationFailed, longLogin.Status);
        Assert.Equal(StaffCredentialsStatus.ValidationFailed, missingPassword.Status);
        Assert.Equal(StaffCredentialsStatus.ValidationFailed, shortPassword.Status);
        Assert.Equal(0L, await CountCredentialsAsync(database));
    }

    [PostgreSqlFact]
    public async Task StaffCredentialsReturnConflictForDuplicateNormalizedLoginName()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var dbContext = database.CreateDbContext();
        await dbContext.Database.MigrateAsync();
        var clock = new MutableTimeProvider(TestNow);
        var lifecycleService = new StaffAccountLifecycleService(dbContext, clock);
        var service = new StaffCredentialsService(dbContext, new PasswordHashingService(), clock);
        var firstAccountId = await CreateStaffAccountAsync(
            lifecycleService,
            AccountKind.NamedAdmin,
            "Main Admin");
        var secondAccountId = await CreateStaffAccountAsync(
            lifecycleService,
            AccountKind.SharedReceptionAdmin,
            "Front desk shared");
        await service.SetStaffCredentialsAsync(
            OwnerEnvelope(),
            firstAccountId,
            "staff.login",
            "named admin password");

        var duplicateResult = await service.SetStaffCredentialsAsync(
            OwnerEnvelope(),
            secondAccountId,
            " STAFF.LOGIN ",
            "shared desk password");

        Assert.Equal(StaffCredentialsStatus.LoginNameAlreadyInUse, duplicateResult.Status);
        Assert.False(duplicateResult.Succeeded);
        Assert.Equal(1L, await CountCredentialsAsync(database));
        Assert.Null(await ReadPasswordHashAsync(database, secondAccountId));
    }

    [PostgreSqlFact]
    public async Task InactiveStaffAccountCannotLoginAfterCredentialsAreConfigured()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var dbContext = database.CreateDbContext();
        await dbContext.Database.MigrateAsync();
        var clock = new MutableTimeProvider(TestNow);
        var hashingService = new PasswordHashingService();
        var lifecycleService = new StaffAccountLifecycleService(dbContext, clock);
        var credentialsService = new StaffCredentialsService(dbContext, hashingService, clock);
        var accountId = await CreateStaffAccountAsync(
            lifecycleService,
            AccountKind.SharedReceptionAdmin,
            "Front desk shared");
        await lifecycleService.SetStaffAccountActiveStateAsync(
            OwnerEnvelope(),
            accountId,
            isActive: false);

        var credentialsResult = await credentialsService.SetStaffCredentialsAsync(
            OwnerEnvelope(),
            accountId,
            "front.desk",
            "shared desk password");
        dbContext.ChangeTracker.Clear();
        var loginResult = await new AccountLoginService(dbContext, hashingService, clock)
            .LoginAsync("front.desk", "shared desk password", "front desk tablet");

        Assert.Equal(StaffCredentialsStatus.Configured, credentialsResult.Status);
        Assert.Equal(AccountLoginStatus.InvalidCredentials, loginResult.Status);
        Assert.Equal(0L, await CountSessionsAsync(database, accountId, ended: null));
    }

    private static async Task<Guid> CreateStaffAccountAsync(
        StaffAccountLifecycleService service,
        AccountKind accountKind,
        string displayName)
    {
        var result = await service.CreateStaffAccountAsync(
            OwnerEnvelope(),
            accountKind,
            displayName);

        Assert.Equal(StaffAccountLifecycleStatus.Created, result.Status);
        return result.AccountId!.Value;
    }

    private static CommandEnvelope OwnerEnvelope()
    {
        return Envelope(ActorRole.Owner, AccountKind.Owner);
    }

    private static CommandEnvelope AdminEnvelope()
    {
        return Envelope(ActorRole.Admin, AccountKind.NamedAdmin);
    }

    private static CommandEnvelope Envelope(ActorRole role, AccountKind accountKind)
    {
        return new CommandEnvelope(
            new ActorContext(
                AccountId.New(),
                role,
                accountKind,
                SessionId.New(),
                "test device"),
            new RequestCorrelationId("test-correlation"),
            EntryOrigin.Normal,
            OccurredAt: null,
            IdempotencyKey: null,
            Reason: null,
            Comment: null);
    }

    private static Task<string?> ReadNormalizedLoginNameAsync(
        PostgreSqlTestDatabase database,
        Guid accountId)
    {
        return database.ExecuteScalarAsync<string>(
            $"select normalized_login_name from bodylife.account_credentials where account_id = '{accountId}'::uuid");
    }

    private static Task<string?> ReadPasswordHashAsync(PostgreSqlTestDatabase database, Guid accountId)
    {
        return database.ExecuteScalarAsync<string>(
            $"select password_hash from bodylife.account_credentials where account_id = '{accountId}'::uuid");
    }

    private static async Task<DateTime?> ReadPasswordChangedAtAsync(
        PostgreSqlTestDatabase database,
        Guid accountId)
    {
        return await database.ExecuteScalarAsync<DateTime>(
            $"select password_changed_at from bodylife.account_credentials where account_id = '{accountId}'::uuid");
    }

    private static Task<long> CountCredentialsAsync(PostgreSqlTestDatabase database)
    {
        return database.ExecuteScalarAsync<long>("select count(*) from bodylife.account_credentials");
    }

    private static Task<long> CountSessionsAsync(
        PostgreSqlTestDatabase database,
        Guid accountId,
        bool? ended)
    {
        var predicate = ended switch
        {
            true => "ended_at is not null",
            false => "ended_at is null",
            null => "true",
        };

        return database.ExecuteScalarAsync<long>(
            $"select count(*) from bodylife.sessions where account_id = '{accountId}'::uuid and {predicate}");
    }

    private sealed class MutableTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow()
        {
            return utcNow;
        }

        public void Advance(TimeSpan duration)
        {
            utcNow = utcNow.Add(duration);
        }
    }
}
