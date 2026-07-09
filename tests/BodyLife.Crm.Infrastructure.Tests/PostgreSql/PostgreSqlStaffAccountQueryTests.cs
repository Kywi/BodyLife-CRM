using BodyLife.Crm.Application.Commands;
using BodyLife.Crm.Infrastructure.Persistence;
using BodyLife.Crm.Infrastructure.Persistence.Audit;
using BodyLife.Crm.Infrastructure.Persistence.UsersRoles;
using BodyLife.Crm.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace BodyLife.Crm.Infrastructure.Tests.PostgreSql;

public sealed class PostgreSqlStaffAccountQueryTests
{
    private static readonly DateTimeOffset TestNow = new(2026, 7, 9, 19, 0, 0, TimeSpan.Zero);

    [PostgreSqlFact]
    public async Task StaffAccountQueryReturnsCanonicalCredentialAndSessionState()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var dbContext = database.CreateDbContext();
        await dbContext.Database.MigrateAsync();
        var auditAppender = new BusinessAuditAppender(dbContext);
        var lifecycleService = new StaffAccountLifecycleService(dbContext, auditAppender, FixedClock());
        var hashingService = new PasswordHashingService();
        var credentialsService = new StaffCredentialsService(
            dbContext,
            hashingService,
            auditAppender,
            FixedClock());
        var namedAccountId = (await lifecycleService.CreateStaffAccountAsync(
            OwnerEnvelope(),
            AccountKind.NamedAdmin,
            "Main Admin")).AccountId!.Value;
        var sharedAccountId = (await lifecycleService.CreateStaffAccountAsync(
            OwnerEnvelope(),
            AccountKind.SharedReceptionAdmin,
            "Front desk shared")).AccountId!.Value;
        await credentialsService.SetStaffCredentialsAsync(
            OwnerEnvelope(),
            sharedAccountId,
            " front.desk ",
            "shared desk password");
        dbContext.ChangeTracker.Clear();
        var loginResult = await new AccountLoginService(dbContext, hashingService, FixedClock())
            .LoginAsync("front.desk", "shared desk password", "front desk tablet");
        Assert.Equal(AccountLoginStatus.Success, loginResult.Status);
        var queryService = new StaffAccountQueryService(dbContext);

        var accounts = await queryService.ListStaffAccountsAsync();

        Assert.Collection(
            accounts,
            namedAccount =>
            {
                Assert.Equal(namedAccountId, namedAccount.AccountId);
                Assert.Equal(AccountKind.NamedAdmin, namedAccount.AccountKind);
                Assert.True(namedAccount.IsActive);
                Assert.False(namedAccount.HasCredentials);
                Assert.Null(namedAccount.LoginName);
                Assert.Equal(0, namedAccount.ActiveSessionCount);
            },
            sharedAccount =>
            {
                Assert.Equal(sharedAccountId, sharedAccount.AccountId);
                Assert.Equal(AccountKind.SharedReceptionAdmin, sharedAccount.AccountKind);
                Assert.True(sharedAccount.IsActive);
                Assert.True(sharedAccount.HasCredentials);
                Assert.Equal("front.desk", sharedAccount.LoginName);
                Assert.Equal(1, sharedAccount.ActiveSessionCount);
            });

        await lifecycleService.SetStaffAccountActiveStateAsync(
            OwnerEnvelope("End shared access"),
            sharedAccountId,
            isActive: false);
        dbContext.ChangeTracker.Clear();

        var updatedAccounts = await queryService.ListStaffAccountsAsync();
        var updatedSharedAccount = Assert.Single(
            updatedAccounts,
            account => account.AccountId == sharedAccountId);
        Assert.False(updatedSharedAccount.IsActive);
        Assert.Equal(0, updatedSharedAccount.ActiveSessionCount);
    }

    private static CommandEnvelope OwnerEnvelope(string? reason = null)
    {
        return new CommandEnvelope(
            new ActorContext(
                AccountId.New(),
                ActorRole.Owner,
                AccountKind.Owner,
                SessionId.New(),
                "owner phone"),
            new RequestCorrelationId("staff-query-test"),
            EntryOrigin.Normal,
            OccurredAt: null,
            IdempotencyKey: null,
            Reason: reason,
            Comment: null);
    }

    private static TimeProvider FixedClock()
    {
        return new FixedTimeProvider(TestNow);
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow()
        {
            return utcNow;
        }
    }
}
