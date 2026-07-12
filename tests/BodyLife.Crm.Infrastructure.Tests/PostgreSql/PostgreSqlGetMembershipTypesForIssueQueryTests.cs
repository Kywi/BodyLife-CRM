using BodyLife.Crm.Application.Queries;
using BodyLife.Crm.Infrastructure.Persistence;
using BodyLife.Crm.Infrastructure.Persistence.MembershipTypes;
using BodyLife.Crm.Modules.MembershipTypes;
using BodyLife.Crm.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using NpgsqlTypes;

namespace BodyLife.Crm.Infrastructure.Tests.PostgreSql;

public sealed class PostgreSqlGetMembershipTypesForIssueQueryTests
{
    private static readonly DateTimeOffset TestNow = new(2026, 7, 12, 23, 30, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset SeedCreatedAt = TestNow.AddDays(-10);
    private static readonly DateTimeOffset SeedUpdatedAt = TestNow.AddDays(-1);
    private static readonly Guid AlphaFirstId = Guid.Parse("00000000-0000-0000-0000-000000000001");
    private static readonly Guid AlphaSecondId = Guid.Parse("00000000-0000-0000-0000-000000000002");
    private static readonly Guid ZuluId = Guid.Parse("00000000-0000-0000-0000-000000000003");
    private static readonly Guid InactiveId = Guid.Parse("00000000-0000-0000-0000-000000000004");

    [PostgreSqlFact]
    public async Task OrdinaryIssueQueryReturnsOnlyActiveRowsForAllOperationalRoles()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var dbContext = database.CreateDbContext();
        await dbContext.Database.MigrateAsync();
        await InsertMembershipTypeAsync(
            database,
            ZuluId,
            "Zulu plan",
            durationDays: 60,
            visitsLimit: 16,
            priceAmount: 2200m,
            comment: "Long plan");
        await InsertMembershipTypeAsync(
            database,
            AlphaSecondId,
            "Alpha plan",
            durationDays: 30,
            visitsLimit: 8,
            priceAmount: 1200m);
        await InsertMembershipTypeAsync(
            database,
            AlphaFirstId,
            "Alpha plan",
            durationDays: 14,
            visitsLimit: 4,
            priceAmount: 700m,
            comment: "Starter plan");
        await InsertMembershipTypeAsync(
            database,
            InactiveId,
            "Aardvark retired",
            isActive: false);
        var actors = new[]
        {
            await SeedActorAsync(database, ActorRole.Owner, AccountKind.Owner),
            await SeedActorAsync(database, ActorRole.Admin, AccountKind.NamedAdmin),
            await SeedActorAsync(database, ActorRole.Admin, AccountKind.SharedReceptionAdmin),
        };
        var handler = CreateHandler(dbContext);

        var results = new List<GetMembershipTypesForIssueResult>();

        foreach (var actor in actors)
        {
            results.Add(await handler.ExecuteAsync(
                new GetMembershipTypesForIssueQuery(actor),
                CancellationToken.None));
        }

        foreach (var result in results)
        {
            AssertSuccessfulResult(result);
            Assert.Equal(
                [AlphaFirstId, AlphaSecondId, ZuluId],
                result.Items.Select(item => item.MembershipTypeId));
            Assert.All(result.Items, item => Assert.True(item.IsAvailableForOrdinaryIssue));
        }

        var firstItem = results[0].Items[0];
        Assert.Equal("Alpha plan", firstItem.Name);
        Assert.Equal(14, firstItem.DurationDays);
        Assert.Equal(4, firstItem.VisitsLimit);
        Assert.Equal(700m, firstItem.Price.Amount);
        Assert.Equal("UAH", firstItem.Price.Currency);
        Assert.Equal("Starter plan", firstItem.Comment);
        Assert.Equal(SeedCreatedAt, firstItem.CreatedAt);
        Assert.Equal(SeedUpdatedAt, firstItem.UpdatedAt);
        Assert.Null(firstItem.DeactivatedAt);
        AssertOwnerPermissions(results[0].AllowedActions);
        AssertAdminPermissions(results[1].AllowedActions);
        AssertAdminPermissions(results[2].AllowedActions);
        Assert.Equal(0L, await CountRowsAsync(database, "business_audit_entries"));
        Assert.Equal(0L, await CountRowsAsync(database, "command_idempotency_keys"));
    }

    [PostgreSqlFact]
    public async Task OwnerCatalogQueryIncludesInactiveRowsActiveFirstWithLifecycleMetadata()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var dbContext = database.CreateDbContext();
        await dbContext.Database.MigrateAsync();
        await InsertMembershipTypeAsync(database, ZuluId, "Zulu active");
        await InsertMembershipTypeAsync(
            database,
            InactiveId,
            "Aardvark retired",
            isActive: false,
            deactivatedAt: SeedUpdatedAt);
        var owner = await SeedActorAsync(database, ActorRole.Owner, AccountKind.Owner);

        var result = await CreateHandler(dbContext).ExecuteAsync(
            new GetMembershipTypesForIssueQuery(owner, IncludeInactive: true),
            CancellationToken.None);

        AssertSuccessfulResult(result);
        Assert.Equal([ZuluId, InactiveId], result.Items.Select(item => item.MembershipTypeId));
        Assert.True(result.Items[0].IsActive);
        Assert.Null(result.Items[0].DeactivatedAt);
        Assert.False(result.Items[1].IsActive);
        Assert.False(result.Items[1].IsAvailableForOrdinaryIssue);
        Assert.Equal(SeedUpdatedAt, result.Items[1].DeactivatedAt);
        AssertOwnerPermissions(result.AllowedActions);
    }

    [PostgreSqlFact]
    public async Task NonOwnerIncludeInactiveRequestIsDeniedWithoutRowsOrActions()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var dbContext = database.CreateDbContext();
        await dbContext.Database.MigrateAsync();
        await InsertMembershipTypeAsync(database, AlphaFirstId, "Active plan");
        await InsertMembershipTypeAsync(
            database,
            InactiveId,
            "Retired plan",
            isActive: false);
        var actors = new[]
        {
            await SeedActorAsync(database, ActorRole.Admin, AccountKind.NamedAdmin),
            await SeedActorAsync(database, ActorRole.Admin, AccountKind.SharedReceptionAdmin),
        };
        var handler = CreateHandler(dbContext);

        foreach (var actor in actors)
        {
            var result = await handler.ExecuteAsync(
                new GetMembershipTypesForIssueQuery(actor, IncludeInactive: true),
                CancellationToken.None);

            AssertDeniedResult(result, "Only an active Owner session");
        }
    }

    [PostgreSqlFact]
    public async Task InvalidForgedExpiredAndEndedActorsAreDenied()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var dbContext = database.CreateDbContext();
        await dbContext.Database.MigrateAsync();
        await InsertMembershipTypeAsync(database, AlphaFirstId, "Active plan");
        var namedAdmin = await SeedActorAsync(
            database,
            ActorRole.Admin,
            AccountKind.NamedAdmin);
        var forgedOwner = namedAdmin with
        {
            Role = ActorRole.Owner,
            AccountKind = AccountKind.Owner,
        };
        var expiredOwner = await SeedActorAsync(
            database,
            ActorRole.Owner,
            AccountKind.Owner,
            sessionExpiresAt: TestNow.AddMinutes(-1));
        var endedOwner = await SeedSessionAsync(
            database,
            expiredOwner.AccountId,
            ActorRole.Owner,
            AccountKind.Owner,
            sessionExpiresAt: TestNow.AddHours(1),
            sessionEndedAt: TestNow.AddMinutes(-1));
        var unknownOwner = new ActorContext(
            AccountId.New(),
            ActorRole.Owner,
            AccountKind.Owner,
            SessionId.New(),
            "unknown device");
        var invalidActor = new ActorContext(
            new AccountId(Guid.Empty),
            ActorRole.Owner,
            AccountKind.Owner,
            new SessionId(Guid.Empty),
            null);
        var actors = new[]
        {
            forgedOwner,
            expiredOwner,
            endedOwner,
            unknownOwner,
            invalidActor,
        };
        var handler = CreateHandler(dbContext);

        foreach (var actor in actors)
        {
            var result = await handler.ExecuteAsync(
                new GetMembershipTypesForIssueQuery(actor),
                CancellationToken.None);

            AssertDeniedResult(result, "An active Owner, named Admin or shared Reception/Admin session");
        }
    }

    [PostgreSqlFact]
    public async Task InactiveOwnerAccountIsDenied()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var dbContext = database.CreateDbContext();
        await dbContext.Database.MigrateAsync();
        await InsertMembershipTypeAsync(database, AlphaFirstId, "Active plan");
        var inactiveOwner = await SeedActorAsync(
            database,
            ActorRole.Owner,
            AccountKind.Owner,
            isActive: false);

        var result = await CreateHandler(dbContext).ExecuteAsync(
            new GetMembershipTypesForIssueQuery(inactiveOwner),
            CancellationToken.None);

        AssertDeniedResult(result, "An active Owner, named Admin or shared Reception/Admin session");
    }

    [PostgreSqlFact]
    public async Task EmptyCatalogReturnsSuccessWithRoleAppropriateActionMetadata()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var dbContext = database.CreateDbContext();
        await dbContext.Database.MigrateAsync();
        var owner = await SeedActorAsync(database, ActorRole.Owner, AccountKind.Owner);
        var admin = await SeedActorAsync(database, ActorRole.Admin, AccountKind.NamedAdmin);
        var handler = CreateHandler(dbContext);

        var ownerResult = await handler.ExecuteAsync(
            new GetMembershipTypesForIssueQuery(owner),
            CancellationToken.None);
        var adminResult = await handler.ExecuteAsync(
            new GetMembershipTypesForIssueQuery(admin),
            CancellationToken.None);

        AssertSuccessfulResult(ownerResult);
        AssertSuccessfulResult(adminResult);
        Assert.Empty(ownerResult.Items);
        Assert.Empty(adminResult.Items);
        AssertOwnerPermissions(ownerResult.AllowedActions);
        AssertAdminPermissions(adminResult.AllowedActions);
    }

    [PostgreSqlFact]
    public async Task RepeatedQueriesReadCurrentLifecycleWithoutAuditOrIdempotencySideEffects()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var dbContext = database.CreateDbContext();
        await dbContext.Database.MigrateAsync();
        await InsertMembershipTypeAsync(database, AlphaFirstId, "Current plan");
        var owner = await SeedActorAsync(database, ActorRole.Owner, AccountKind.Owner);
        var handler = CreateHandler(dbContext);

        var before = await handler.ExecuteAsync(
            new GetMembershipTypesForIssueQuery(owner),
            CancellationToken.None);
        await DeactivateMembershipTypeAsync(database, AlphaFirstId);
        var ordinaryAfter = await handler.ExecuteAsync(
            new GetMembershipTypesForIssueQuery(owner),
            CancellationToken.None);
        var catalogAfter = await handler.ExecuteAsync(
            new GetMembershipTypesForIssueQuery(owner, IncludeInactive: true),
            CancellationToken.None);

        Assert.Single(before.Items);
        Assert.Empty(ordinaryAfter.Items);
        var inactive = Assert.Single(catalogAfter.Items);
        Assert.False(inactive.IsActive);
        Assert.Equal(TestNow, inactive.UpdatedAt);
        Assert.Equal(TestNow, inactive.DeactivatedAt);
        Assert.Equal(1L, await CountRowsAsync(database, "membership_types"));
        Assert.Equal(0L, await CountRowsAsync(database, "business_audit_entries"));
        Assert.Equal(0L, await CountRowsAsync(database, "command_idempotency_keys"));
    }

    private static GetMembershipTypesForIssueQueryHandler CreateHandler(BodyLifeDbContext dbContext)
    {
        return new GetMembershipTypesForIssueQueryHandler(
            dbContext,
            new FixedTimeProvider(TestNow));
    }

    private static async Task<ActorContext> SeedActorAsync(
        PostgreSqlTestDatabase database,
        ActorRole role,
        AccountKind accountKind,
        bool isActive = true,
        DateTimeOffset? sessionExpiresAt = null,
        DateTimeOffset? sessionEndedAt = null,
        string? deviceLabel = "test device")
    {
        var accountId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();
        await using var connection = new NpgsqlConnection(database.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            insert into bodylife.accounts (
                id,
                display_name,
                account_type,
                role,
                is_active,
                created_at,
                deactivated_at)
            values (
                @account_id,
                @display_name,
                @account_type,
                @role,
                @is_active,
                @created_at,
                @account_deactivated_at);

            insert into bodylife.sessions (
                id,
                account_id,
                device_label,
                started_at,
                expires_at,
                ended_at,
                last_seen_at)
            values (
                @session_id,
                @account_id,
                @device_label,
                @started_at,
                @expires_at,
                @session_ended_at,
                @last_seen_at);
            """;
        command.Parameters.AddWithValue("account_id", accountId);
        command.Parameters.AddWithValue("display_name", $"{accountKind} query actor");
        command.Parameters.AddWithValue("account_type", MapAccountKind(accountKind));
        command.Parameters.AddWithValue("role", MapRole(role));
        command.Parameters.AddWithValue("is_active", isActive);
        command.Parameters.AddWithValue("created_at", TestNow.AddHours(-1));
        command.Parameters.Add("account_deactivated_at", NpgsqlDbType.TimestampTz).Value = isActive
            ? DBNull.Value
            : TestNow;
        command.Parameters.AddWithValue("session_id", sessionId);
        command.Parameters.Add("device_label", NpgsqlDbType.Varchar).Value =
            deviceLabel ?? (object)DBNull.Value;
        command.Parameters.AddWithValue("started_at", TestNow.AddHours(-1));
        command.Parameters.AddWithValue(
            "expires_at",
            sessionExpiresAt ?? TestNow.AddHours(11));
        command.Parameters.Add("session_ended_at", NpgsqlDbType.TimestampTz).Value =
            sessionEndedAt ?? (object)DBNull.Value;
        command.Parameters.AddWithValue("last_seen_at", TestNow.AddMinutes(-5));
        await command.ExecuteNonQueryAsync();

        return new ActorContext(
            new AccountId(accountId),
            role,
            accountKind,
            new SessionId(sessionId),
            deviceLabel);
    }

    private static async Task InsertMembershipTypeAsync(
        PostgreSqlTestDatabase database,
        Guid membershipTypeId,
        string name,
        bool isActive = true,
        int durationDays = 30,
        int visitsLimit = 8,
        decimal priceAmount = 1200m,
        string priceCurrency = "UAH",
        string? comment = null,
        DateTimeOffset? deactivatedAt = null)
    {
        await using var connection = new NpgsqlConnection(database.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            insert into bodylife.membership_types (
                id,
                name,
                duration_days,
                visits_limit,
                price_amount,
                price_currency,
                is_active,
                comment,
                created_at,
                updated_at,
                deactivated_at)
            values (
                @id,
                @name,
                @duration_days,
                @visits_limit,
                @price_amount,
                @price_currency,
                @is_active,
                @comment,
                @created_at,
                @updated_at,
                @deactivated_at)
            """;
        command.Parameters.AddWithValue("id", membershipTypeId);
        command.Parameters.AddWithValue("name", name);
        command.Parameters.AddWithValue("duration_days", durationDays);
        command.Parameters.AddWithValue("visits_limit", visitsLimit);
        command.Parameters.AddWithValue("price_amount", priceAmount);
        command.Parameters.AddWithValue("price_currency", priceCurrency);
        command.Parameters.AddWithValue("is_active", isActive);
        command.Parameters.Add("comment", NpgsqlDbType.Text).Value = comment ?? (object)DBNull.Value;
        command.Parameters.AddWithValue("created_at", SeedCreatedAt);
        command.Parameters.AddWithValue("updated_at", SeedUpdatedAt);
        command.Parameters.Add("deactivated_at", NpgsqlDbType.TimestampTz).Value = isActive
            ? DBNull.Value
            : deactivatedAt ?? SeedUpdatedAt;
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<ActorContext> SeedSessionAsync(
        PostgreSqlTestDatabase database,
        AccountId accountId,
        ActorRole role,
        AccountKind accountKind,
        DateTimeOffset sessionExpiresAt,
        DateTimeOffset? sessionEndedAt = null,
        string? deviceLabel = "test device")
    {
        var sessionId = Guid.NewGuid();
        await using var connection = new NpgsqlConnection(database.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            insert into bodylife.sessions (
                id,
                account_id,
                device_label,
                started_at,
                expires_at,
                ended_at,
                last_seen_at)
            values (
                @session_id,
                @account_id,
                @device_label,
                @started_at,
                @expires_at,
                @ended_at,
                @last_seen_at)
            """;
        command.Parameters.AddWithValue("session_id", sessionId);
        command.Parameters.AddWithValue("account_id", accountId.Value);
        command.Parameters.Add("device_label", NpgsqlDbType.Varchar).Value =
            deviceLabel ?? (object)DBNull.Value;
        command.Parameters.AddWithValue("started_at", TestNow.AddHours(-1));
        command.Parameters.AddWithValue("expires_at", sessionExpiresAt);
        command.Parameters.Add("ended_at", NpgsqlDbType.TimestampTz).Value =
            sessionEndedAt ?? (object)DBNull.Value;
        command.Parameters.AddWithValue("last_seen_at", TestNow.AddMinutes(-5));
        await command.ExecuteNonQueryAsync();

        return new ActorContext(
            accountId,
            role,
            accountKind,
            new SessionId(sessionId),
            deviceLabel);
    }

    private static async Task DeactivateMembershipTypeAsync(
        PostgreSqlTestDatabase database,
        Guid membershipTypeId)
    {
        await using var connection = new NpgsqlConnection(database.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            update bodylife.membership_types
            set is_active = false,
                updated_at = @updated_at,
                deactivated_at = @deactivated_at
            where id = @id
            """;
        command.Parameters.AddWithValue("id", membershipTypeId);
        command.Parameters.AddWithValue("updated_at", TestNow);
        command.Parameters.AddWithValue("deactivated_at", TestNow);
        Assert.Equal(1, await command.ExecuteNonQueryAsync());
    }

    private static Task<long> CountRowsAsync(
        PostgreSqlTestDatabase database,
        string tableName)
    {
        return database.ExecuteScalarAsync<long>($"select count(*) from bodylife.{tableName}");
    }

    private static void AssertSuccessfulResult(GetMembershipTypesForIssueResult result)
    {
        Assert.Equal(GetMembershipTypesForIssueStatus.Success, result.Status);
        Assert.Null(result.ErrorCode);
        Assert.Null(result.ErrorMessage);
    }

    private static void AssertDeniedResult(
        GetMembershipTypesForIssueResult result,
        string expectedMessagePart)
    {
        Assert.Equal(GetMembershipTypesForIssueStatus.PermissionDenied, result.Status);
        Assert.Equal("permission_denied", result.ErrorCode);
        Assert.Contains(expectedMessagePart, result.ErrorMessage, StringComparison.Ordinal);
        Assert.Empty(result.Items);
        Assert.Empty(result.AllowedActions.Items);
    }

    private static void AssertOwnerPermissions(QueryPermissionSet permissions)
    {
        AssertCatalogPermissions(permissions, expectedAllowed: true);
    }

    private static void AssertAdminPermissions(QueryPermissionSet permissions)
    {
        AssertCatalogPermissions(permissions, expectedAllowed: false);
    }

    private static void AssertCatalogPermissions(
        QueryPermissionSet permissions,
        bool expectedAllowed)
    {
        var actionKeys = new[]
        {
            MembershipTypeCatalogActionKeys.Create,
            MembershipTypeCatalogActionKeys.Edit,
            MembershipTypeCatalogActionKeys.Deactivate,
        };
        Assert.Equal(actionKeys.Length, permissions.Items.Count);

        foreach (var actionKey in actionKeys)
        {
            Assert.True(permissions.TryGet(actionKey, out var permission));
            Assert.Equal(MembershipTypeCatalogActionKeys.OwnerPolicy, permission.RequiredPolicy);
            Assert.Equal(expectedAllowed, permission.IsAllowed);

            if (expectedAllowed)
            {
                Assert.Null(permission.DeniedReasonCode);
                Assert.Null(permission.DeniedReason);
            }
            else
            {
                Assert.Equal(
                    QueryPermissionDeniedReasonCodes.PermissionDenied,
                    permission.DeniedReasonCode);
                Assert.NotNull(permission.DeniedReason);
            }
        }
    }

    private static string MapAccountKind(AccountKind accountKind)
    {
        return accountKind switch
        {
            AccountKind.Owner => "owner",
            AccountKind.NamedAdmin => "named_admin",
            AccountKind.SharedReceptionAdmin => "shared_reception_admin",
            _ => throw new ArgumentOutOfRangeException(nameof(accountKind), accountKind, null),
        };
    }

    private static string MapRole(ActorRole role)
    {
        return role switch
        {
            ActorRole.Owner => "owner",
            ActorRole.Admin => "admin",
            _ => throw new ArgumentOutOfRangeException(nameof(role), role, null),
        };
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow()
        {
            return utcNow;
        }
    }
}
