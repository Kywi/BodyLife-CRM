using BodyLife.Crm.Application.Queries;
using BodyLife.Crm.Infrastructure;
using BodyLife.Crm.Infrastructure.Persistence;
using BodyLife.Crm.Infrastructure.Persistence.Memberships;
using BodyLife.Crm.Modules.Memberships;
using BodyLife.Crm.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using NpgsqlTypes;

namespace BodyLife.Crm.Infrastructure.Tests.PostgreSql;

public sealed class PostgreSqlGetMembershipStateQueryTests
{
    private static readonly DateTimeOffset TestNow = new(
        2026,
        7,
        13,
        14,
        0,
        0,
        TimeSpan.Zero);
    private static readonly DateOnly TestStartDate = new(2026, 7, 1);
    private static readonly DateOnly TestBaseEndDate = new(2026, 7, 30);
    private static readonly DateOnly ExtendedEndDate = new(2026, 8, 3);
    private static readonly Guid FirstFreezeId = Guid.Parse(
        "11111111-1111-1111-1111-111111111111");
    private static readonly Guid SecondFreezeId = Guid.Parse(
        "22222222-2222-2222-2222-222222222222");
    private static readonly Guid NonWorkingPeriodId = Guid.Parse(
        "33333333-3333-3333-3333-333333333333");
    private static readonly Guid AdjustmentId = Guid.Parse(
        "44444444-4444-4444-4444-444444444444");

    [PostgreSqlFact]
    public async Task AcceptedActorsReadCanonicalOpeningStateWithoutQuerySideEffects()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var dbContext = database.CreateDbContext();
        await dbContext.Database.MigrateAsync();
        var owner = await SeedActorAsync(database, ActorRole.Owner, AccountKind.Owner);
        var namedAdmin = await SeedActorAsync(
            database,
            ActorRole.Admin,
            AccountKind.NamedAdmin);
        var sharedAdmin = await SeedActorAsync(
            database,
            ActorRole.Admin,
            AccountKind.SharedReceptionAdmin);
        var membership = await SeedMembershipAsync(database, owner.AccountId.Value);
        await InsertOpeningStateAsync(
            database,
            membership,
            owner,
            declaredRemainingVisits: -2,
            declaredNegativeBalance: 2);
        var rebuild = await new MembershipStateCacheRebuilder(
            dbContext,
            new FixedTimeProvider(TestNow)).RebuildAsync(membership.MembershipId);
        var cacheBefore = await ReadCacheMetadataAsync(database, membership.MembershipId);
        var handler = CreateHandler(dbContext);
        var results = new List<GetMembershipStateResult>();

        foreach (var actor in new[] { owner, namedAdmin, sharedAdmin })
        {
            results.Add(await handler.ExecuteAsync(
                new GetMembershipStateQuery(actor, membership.MembershipId, ExtendedEndDate),
                CancellationToken.None));
        }

        Assert.True(rebuild.Succeeded);
        Assert.All(results, AssertSuccessful);
        var state = results[0].State!;
        Assert.Equal(membership.MembershipId, state.MembershipId);
        Assert.Equal(membership.ClientId, state.ClientId);
        Assert.Equal(membership.MembershipTypeId, state.MembershipTypeId);
        Assert.Equal("Two visits / 30 days", state.Snapshot.TypeName);
        Assert.Equal(30, state.Snapshot.DurationDays);
        Assert.Equal(2, state.Snapshot.VisitsLimit);
        Assert.Equal(1000m, state.Snapshot.Price.Amount);
        Assert.Equal("UAH", state.Snapshot.Price.Currency);
        Assert.Equal(TestStartDate, state.StartDate);
        Assert.Equal(TestBaseEndDate, state.BaseEndDate);
        Assert.Equal(ExtendedEndDate, state.EffectiveEndDate);
        Assert.Equal(0, state.CountedVisits);
        Assert.Equal(-2, state.RemainingVisits);
        Assert.Equal(2, state.NegativeBalance);
        Assert.Null(state.FirstNegativeVisitId);
        Assert.Null(state.FirstNegativeVisitDate);
        Assert.Equal(4, state.ExtensionDays);
        Assert.Empty(state.ExtensionExplanation);
        Assert.Null(state.LastCountedVisitAt);
        Assert.Equal(ExtendedEndDate, state.AsOfDate);
        Assert.True(state.IsActiveByDate);
        Assert.All(
            results,
            result => Assert.Collection(
                result.State!.Warnings,
                warning =>
                {
                    Assert.Equal(MembershipWarningCodes.NegativeBalance, warning.Code);
                    Assert.Equal(MembershipWarningSeverity.Danger, warning.Severity);
                },
                warning =>
                {
                    Assert.Equal(MembershipWarningCodes.EndingSoon, warning.Code);
                    Assert.Equal(MembershipWarningSeverity.Warning, warning.Severity);
                }));
        Assert.All(results, result => Assert.Empty(result.AllowedActions.Items));
        Assert.Equal(
            cacheBefore,
            await ReadCacheMetadataAsync(database, membership.MembershipId));
        Assert.Equal(
            0L,
            await database.ExecuteScalarAsync<long>(
                "select count(*) from bodylife.business_audit_entries"));
        Assert.Equal(
            0L,
            await database.ExecuteScalarAsync<long>(
                "select count(*) from bodylife.command_idempotency_keys"));
    }

    [PostgreSqlFact]
    public async Task ExtensionExplanationProjectsOverlapAndInactiveRowsInCanonicalOrder()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var dbContext = database.CreateDbContext();
        await dbContext.Database.MigrateAsync();
        var owner = await SeedActorAsync(database, ActorRole.Owner, AccountKind.Owner);
        var membership = await SeedMembershipAsync(database, owner.AccountId.Value);
        var rebuild = await new MembershipStateCacheRebuilder(
            dbContext,
            new FixedTimeProvider(TestNow)).RebuildAsync(membership.MembershipId);
        await SetCacheExtensionStateAsync(
            database,
            membership.MembershipId,
            extensionDays: 2,
            effectiveEndDate: TestBaseEndDate.AddDays(2));
        await InsertExtensionDayAsync(
            database,
            membership.MembershipId,
            new DateOnly(2026, 7, 11),
            "membership_adjustment",
            AdjustmentId,
            "Canceled adjustment",
            isActive: false);
        await InsertExtensionDayAsync(
            database,
            membership.MembershipId,
            new DateOnly(2026, 7, 11),
            "non_working_period",
            NonWorkingPeriodId,
            "Gym closure",
            isActive: true);
        await InsertExtensionDayAsync(
            database,
            membership.MembershipId,
            new DateOnly(2026, 7, 11),
            "freeze",
            SecondFreezeId,
            "Second summer freeze",
            isActive: true);
        await InsertExtensionDayAsync(
            database,
            membership.MembershipId,
            new DateOnly(2026, 7, 11),
            "freeze",
            FirstFreezeId,
            "First summer freeze",
            isActive: true);
        await InsertExtensionDayAsync(
            database,
            membership.MembershipId,
            new DateOnly(2026, 7, 10),
            "freeze",
            FirstFreezeId,
            "First summer freeze",
            isActive: true);
        var cacheBefore = await ReadCacheMetadataAsync(database, membership.MembershipId);
        var extensionRowCountBefore = await database.ExecuteScalarAsync<long>(
            "select count(*) from bodylife.membership_extension_days");

        var result = await CreateHandler(dbContext).ExecuteAsync(
            new GetMembershipStateQuery(
                owner,
                membership.MembershipId,
                TestBaseEndDate),
            CancellationToken.None);

        Assert.True(rebuild.Succeeded);
        AssertSuccessful(result);
        var state = result.State!;
        Assert.Equal(2, state.ExtensionDays);
        Assert.Equal(5, state.ExtensionExplanation.Count);
        Assert.Equal(
            [
                new DateOnly(2026, 7, 10),
                new DateOnly(2026, 7, 11),
                new DateOnly(2026, 7, 11),
                new DateOnly(2026, 7, 11),
                new DateOnly(2026, 7, 11),
            ],
            state.ExtensionExplanation.Select(item => item.ExtensionDate));
        Assert.Equal(
            [true, true, true, true, false],
            state.ExtensionExplanation.Select(item => item.IsActive));
        Assert.Equal(
            ["freeze", "freeze", "freeze", "non_working_period", "membership_adjustment"],
            state.ExtensionExplanation.Select(item => item.SourceType));
        Assert.Equal(
            [
                FirstFreezeId,
                FirstFreezeId,
                SecondFreezeId,
                NonWorkingPeriodId,
                AdjustmentId,
            ],
            state.ExtensionExplanation.Select(item => item.SourceId));
        Assert.Equal(
            [
                "First summer freeze",
                "First summer freeze",
                "Second summer freeze",
                "Gym closure",
                "Canceled adjustment",
            ],
            state.ExtensionExplanation.Select(item => item.SourceLabel));
        var explanationList = Assert.IsAssignableFrom<IList<MembershipExtensionDay>>(
            state.ExtensionExplanation);
        Assert.True(explanationList.IsReadOnly);
        Assert.Throws<NotSupportedException>(() => explanationList.Add(explanationList[0]));
        Assert.Equal(
            cacheBefore,
            await ReadCacheMetadataAsync(database, membership.MembershipId));
        Assert.Equal(
            extensionRowCountBefore,
            await database.ExecuteScalarAsync<long>(
                "select count(*) from bodylife.membership_extension_days"));
        Assert.Equal(
            0L,
            await database.ExecuteScalarAsync<long>(
                "select count(*) from bodylife.business_audit_entries"));
        Assert.Equal(
            0L,
            await database.ExecuteScalarAsync<long>(
                "select count(*) from bodylife.command_idempotency_keys"));
    }

    [PostgreSqlFact]
    public async Task WarningsUseCanonicalCacheStateAndRequestedAsOfDate()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var dbContext = database.CreateDbContext();
        await dbContext.Database.MigrateAsync();
        var owner = await SeedActorAsync(database, ActorRole.Owner, AccountKind.Owner);
        var membership = await SeedMembershipAsync(database, owner.AccountId.Value);
        await new MembershipStateCacheRebuilder(
            dbContext,
            new FixedTimeProvider(TestNow)).RebuildAsync(membership.MembershipId);
        var cacheBefore = await ReadCacheMetadataAsync(database, membership.MembershipId);
        var handler = CreateHandler(dbContext);

        var outsideEndingSoon = await handler.ExecuteAsync(
            new GetMembershipStateQuery(
                owner,
                membership.MembershipId,
                TestBaseEndDate.AddDays(-8)),
            CancellationToken.None);
        var endingSoon = await handler.ExecuteAsync(
            new GetMembershipStateQuery(
                owner,
                membership.MembershipId,
                TestBaseEndDate.AddDays(-7)),
            CancellationToken.None);
        var expired = await handler.ExecuteAsync(
            new GetMembershipStateQuery(
                owner,
                membership.MembershipId,
                TestBaseEndDate.AddDays(1)),
            CancellationToken.None);

        AssertSuccessful(outsideEndingSoon);
        Assert.Equal(
            [MembershipWarningCodes.LowRemaining],
            outsideEndingSoon.State!.Warnings.Select(warning => warning.Code));
        AssertSuccessful(endingSoon);
        Assert.Equal(
            [MembershipWarningCodes.LowRemaining, MembershipWarningCodes.EndingSoon],
            endingSoon.State!.Warnings.Select(warning => warning.Code));
        AssertSuccessful(expired);
        Assert.Equal(
            [MembershipWarningCodes.ExpiredByDate, MembershipWarningCodes.LowRemaining],
            expired.State!.Warnings.Select(warning => warning.Code));
        Assert.Equal(
            cacheBefore,
            await ReadCacheMetadataAsync(database, membership.MembershipId));
    }

    [PostgreSqlFact]
    public async Task OpeningStateActionIsProjectedOnlyForEligibleActiveMembership()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var dbContext = database.CreateDbContext();
        await dbContext.Database.MigrateAsync();
        var owner = await SeedActorAsync(database, ActorRole.Owner, AccountKind.Owner);
        var membership = await SeedMembershipAsync(database, owner.AccountId.Value);
        await new MembershipStateCacheRebuilder(
            dbContext,
            new FixedTimeProvider(TestNow)).RebuildAsync(membership.MembershipId);
        var handler = CreateHandler(dbContext);

        var active = await handler.ExecuteAsync(
            new GetMembershipStateQuery(owner, membership.MembershipId, TestBaseEndDate),
            CancellationToken.None);
        await UpdateMembershipStatusAsync(database, membership.MembershipId, "canceled");
        var canceled = await handler.ExecuteAsync(
            new GetMembershipStateQuery(owner, membership.MembershipId, TestBaseEndDate),
            CancellationToken.None);

        AssertSuccessful(active);
        Assert.True(active.State!.IsActiveByDate);
        var permission = Assert.Single(active.AllowedActions.Items);
        Assert.Equal(MembershipActionKeys.CreateOpeningState, permission.ActionKey);
        Assert.Equal(MembershipActionKeys.AdminOrOwnerPolicy, permission.RequiredPolicy);
        Assert.True(permission.IsAllowed);
        AssertSuccessful(canceled);
        Assert.Empty(canceled.AllowedActions.Items);
    }

    [PostgreSqlFact]
    public async Task InactiveExpiredUnknownAndForgedActorsAreDeniedWithoutState()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var dbContext = database.CreateDbContext();
        await dbContext.Database.MigrateAsync();
        var owner = await SeedActorAsync(database, ActorRole.Owner, AccountKind.Owner);
        var membership = await SeedMembershipAsync(database, owner.AccountId.Value);
        await new MembershipStateCacheRebuilder(
            dbContext,
            new FixedTimeProvider(TestNow)).RebuildAsync(membership.MembershipId);
        var inactiveAdmin = await SeedActorAsync(
            database,
            ActorRole.Admin,
            AccountKind.NamedAdmin,
            isActive: false);
        var expiredAdmin = await SeedActorAsync(
            database,
            ActorRole.Admin,
            AccountKind.NamedAdmin,
            sessionExpiresAt: TestNow.AddMinutes(-1));
        var namedAdmin = await SeedActorAsync(
            database,
            ActorRole.Admin,
            AccountKind.NamedAdmin);
        var forgedOwner = namedAdmin with
        {
            Role = ActorRole.Owner,
            AccountKind = AccountKind.Owner,
        };
        var unknownOwner = new ActorContext(
            AccountId.New(),
            ActorRole.Owner,
            AccountKind.Owner,
            SessionId.New(),
            "unknown device");
        var handler = CreateHandler(dbContext);

        foreach (var actor in new[]
                 {
                     inactiveAdmin,
                     expiredAdmin,
                     forgedOwner,
                     unknownOwner,
                 })
        {
            var result = await handler.ExecuteAsync(
                new GetMembershipStateQuery(actor, membership.MembershipId, TestBaseEndDate),
                CancellationToken.None);

            Assert.Equal(GetMembershipStateStatus.PermissionDenied, result.Status);
            Assert.Equal("permission_denied", result.ErrorCode);
            Assert.Null(result.State);
            Assert.Empty(result.AllowedActions.Items);
        }
    }

    [PostgreSqlFact]
    public async Task InvalidAndMissingSelectorsReturnStableErrors()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var dbContext = database.CreateDbContext();
        await dbContext.Database.MigrateAsync();
        var owner = await SeedActorAsync(database, ActorRole.Owner, AccountKind.Owner);
        var handler = CreateHandler(dbContext);

        var emptyId = await handler.ExecuteAsync(
            new GetMembershipStateQuery(owner, Guid.Empty, TestBaseEndDate),
            CancellationToken.None);
        var emptyDate = await handler.ExecuteAsync(
            new GetMembershipStateQuery(owner, Guid.NewGuid(), default),
            CancellationToken.None);
        var missing = await handler.ExecuteAsync(
            new GetMembershipStateQuery(owner, Guid.NewGuid(), TestBaseEndDate),
            CancellationToken.None);

        Assert.Equal(GetMembershipStateStatus.ValidationFailed, emptyId.Status);
        Assert.Equal("membershipId", emptyId.ErrorField);
        Assert.Equal(GetMembershipStateStatus.ValidationFailed, emptyDate.Status);
        Assert.Equal("asOfDate", emptyDate.ErrorField);
        Assert.Equal(GetMembershipStateStatus.NotFound, missing.Status);
        Assert.Equal("not_found", missing.ErrorCode);
        Assert.All(new[] { emptyId, emptyDate, missing }, result => Assert.Null(result.State));
    }

    [PostgreSqlFact]
    public async Task MissingStaleAndInconsistentCacheFailWithoutRepairingOnRead()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var dbContext = database.CreateDbContext();
        await dbContext.Database.MigrateAsync();
        var owner = await SeedActorAsync(database, ActorRole.Owner, AccountKind.Owner);
        var membership = await SeedMembershipAsync(database, owner.AccountId.Value);
        var handler = CreateHandler(dbContext);

        var missingCache = await handler.ExecuteAsync(
            new GetMembershipStateQuery(owner, membership.MembershipId, TestBaseEndDate),
            CancellationToken.None);
        Assert.Equal(GetMembershipStateStatus.RecalculationFailed, missingCache.Status);
        Assert.Equal("recalculation_failed", missingCache.ErrorCode);
        Assert.Equal(
            0L,
            await database.ExecuteScalarAsync<long>(
                "select count(*) from bodylife.membership_state_cache"));

        await new MembershipStateCacheRebuilder(
            dbContext,
            new FixedTimeProvider(TestNow)).RebuildAsync(membership.MembershipId);
        await SetCacheVersionAsync(database, membership.MembershipId, version: 1);
        var cacheBefore = await ReadCacheMetadataAsync(database, membership.MembershipId);

        var staleCache = await handler.ExecuteAsync(
            new GetMembershipStateQuery(owner, membership.MembershipId, TestBaseEndDate),
            CancellationToken.None);

        Assert.Equal(GetMembershipStateStatus.RecalculationFailed, staleCache.Status);
        Assert.Equal("recalculation_failed", staleCache.ErrorCode);
        Assert.Null(staleCache.State);
        Assert.Empty(staleCache.AllowedActions.Items);
        Assert.Equal(
            cacheBefore,
            await ReadCacheMetadataAsync(database, membership.MembershipId));
        Assert.Equal(1, cacheBefore.RecalculationVersion);
        Assert.Equal(
            0L,
            await database.ExecuteScalarAsync<long>(
                "select count(*) from bodylife.business_audit_entries"));
        Assert.Equal(
            0L,
            await database.ExecuteScalarAsync<long>(
                "select count(*) from bodylife.command_idempotency_keys"));

        await SetCacheVersionAsync(
            database,
            membership.MembershipId,
            MembershipStateCacheRebuilder.CurrentRecalculationVersion);
        await SetCacheEffectiveEndDateAsync(
            database,
            membership.MembershipId,
            TestBaseEndDate.AddDays(1));
        var inconsistentCacheBefore = await ReadCacheMetadataAsync(
            database,
            membership.MembershipId);

        var inconsistentCache = await handler.ExecuteAsync(
            new GetMembershipStateQuery(owner, membership.MembershipId, TestBaseEndDate),
            CancellationToken.None);

        Assert.Equal(GetMembershipStateStatus.RecalculationFailed, inconsistentCache.Status);
        Assert.Equal("recalculation_failed", inconsistentCache.ErrorCode);
        Assert.Null(inconsistentCache.State);
        Assert.Empty(inconsistentCache.AllowedActions.Items);
        Assert.Equal(
            inconsistentCacheBefore,
            await ReadCacheMetadataAsync(database, membership.MembershipId));
    }

    [Fact]
    public void PersistenceRegistrationExposesScopedMembershipStateQueryHandler()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:BodyLife"] =
                    "Host=localhost;Database=bodylife;Username=bodylife;Password=not-used",
            })
            .Build();
        var services = new ServiceCollection();

        services.AddBodyLifePersistence(configuration);

        var serviceType = typeof(IBodyLifeQueryHandler<
            GetMembershipStateQuery,
            GetMembershipStateResult>);
        var descriptor = Assert.Single(
            services,
            candidate => candidate.ServiceType == serviceType);
        Assert.Equal(ServiceLifetime.Scoped, descriptor.Lifetime);
        Assert.Equal(typeof(GetMembershipStateQueryHandler), descriptor.ImplementationType);
    }

    private static GetMembershipStateQueryHandler CreateHandler(BodyLifeDbContext dbContext)
    {
        return new GetMembershipStateQueryHandler(
            dbContext,
            new FixedTimeProvider(TestNow));
    }

    private static void AssertSuccessful(GetMembershipStateResult result)
    {
        Assert.Equal(GetMembershipStateStatus.Success, result.Status);
        Assert.NotNull(result.State);
        Assert.Null(result.ErrorCode);
        Assert.Null(result.ErrorMessage);
        Assert.Null(result.ErrorField);
    }

    private static async Task<ActorContext> SeedActorAsync(
        PostgreSqlTestDatabase database,
        ActorRole role,
        AccountKind accountKind,
        bool isActive = true,
        DateTimeOffset? sessionExpiresAt = null,
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
                @deactivated_at);

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
                null,
                @last_seen_at)
            """;
        command.Parameters.AddWithValue("account_id", accountId);
        command.Parameters.AddWithValue("display_name", $"{accountKind} membership query actor");
        command.Parameters.AddWithValue("account_type", MapAccountKind(accountKind));
        command.Parameters.AddWithValue("role", MapRole(role));
        command.Parameters.AddWithValue("is_active", isActive);
        command.Parameters.AddWithValue("created_at", TestNow.AddHours(-2));
        command.Parameters.Add("deactivated_at", NpgsqlDbType.TimestampTz).Value = isActive
            ? DBNull.Value
            : TestNow.AddHours(-1);
        command.Parameters.AddWithValue("session_id", sessionId);
        command.Parameters.Add("device_label", NpgsqlDbType.Varchar).Value =
            deviceLabel ?? (object)DBNull.Value;
        command.Parameters.AddWithValue("started_at", TestNow.AddHours(-1));
        command.Parameters.AddWithValue(
            "expires_at",
            sessionExpiresAt ?? TestNow.AddHours(10));
        command.Parameters.AddWithValue("last_seen_at", TestNow.AddMinutes(-5));
        Assert.Equal(2, await command.ExecuteNonQueryAsync());

        return new ActorContext(
            new AccountId(accountId),
            role,
            accountKind,
            new SessionId(sessionId),
            deviceLabel);
    }

    private static async Task<MembershipFixture> SeedMembershipAsync(
        PostgreSqlTestDatabase database,
        Guid issuedByAccountId,
        string membershipStatus = "active")
    {
        var fixture = new MembershipFixture(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid());
        await using var connection = new NpgsqlConnection(database.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            insert into bodylife.clients (
                id,
                surname,
                name,
                patronymic,
                normalized_full_name,
                phone_raw,
                phone_normalized,
                phone_last4,
                comment,
                operational_status,
                created_at,
                created_by_account_id,
                updated_at)
            values (
                @client_id,
                'Membership',
                'Reader',
                null,
                'MEMBERSHIP READER',
                null,
                null,
                null,
                null,
                'active',
                @recorded_at,
                @issued_by_account_id,
                @recorded_at);

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
                @membership_type_id,
                'Two visits / 30 days',
                30,
                2,
                1000,
                'UAH',
                true,
                null,
                @recorded_at,
                @recorded_at,
                null);

            insert into bodylife.issued_memberships (
                id,
                client_id,
                membership_type_id,
                type_name_snapshot,
                duration_days_snapshot,
                visits_limit_snapshot,
                price_amount_snapshot,
                price_currency_snapshot,
                start_date,
                base_end_date,
                issued_at,
                issued_by_account_id,
                status,
                entry_origin,
                entry_batch_id,
                comment)
            values (
                @membership_id,
                @client_id,
                @membership_type_id,
                'Two visits / 30 days',
                30,
                2,
                1000,
                'UAH',
                @start_date,
                @base_end_date,
                @recorded_at,
                @issued_by_account_id,
                @membership_status,
                'normal',
                null,
                null)
            """;
        command.Parameters.AddWithValue("client_id", fixture.ClientId);
        command.Parameters.AddWithValue("membership_type_id", fixture.MembershipTypeId);
        command.Parameters.AddWithValue("membership_id", fixture.MembershipId);
        command.Parameters.AddWithValue("recorded_at", TestNow.AddDays(-12));
        command.Parameters.AddWithValue("issued_by_account_id", issuedByAccountId);
        command.Parameters.AddWithValue("start_date", NpgsqlDbType.Date, TestStartDate);
        command.Parameters.AddWithValue("base_end_date", NpgsqlDbType.Date, TestBaseEndDate);
        command.Parameters.AddWithValue("membership_status", membershipStatus);
        Assert.Equal(3, await command.ExecuteNonQueryAsync());

        return fixture;
    }

    private static async Task InsertOpeningStateAsync(
        PostgreSqlTestDatabase database,
        MembershipFixture membership,
        ActorContext actor,
        int declaredRemainingVisits,
        int declaredNegativeBalance)
    {
        await using var connection = new NpgsqlConnection(database.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            insert into bodylife.membership_opening_states (
                id,
                membership_id,
                opening_as_of_date,
                declared_remaining_visits,
                declared_negative_balance,
                known_effective_end_date,
                known_extension_days,
                source_reference,
                reason,
                recorded_at,
                recorded_by_account_id,
                recorded_session_id,
                entry_origin,
                entry_batch_id,
                status)
            values (
                @id,
                @membership_id,
                @opening_as_of_date,
                @declared_remaining_visits,
                @declared_negative_balance,
                @known_effective_end_date,
                4,
                'Paper register 2026, page 12',
                'Active membership history before launch is incomplete',
                @recorded_at,
                @recorded_by_account_id,
                @recorded_session_id,
                'manual_backfill',
                null,
                'active')
            """;
        command.Parameters.AddWithValue("id", Guid.NewGuid());
        command.Parameters.AddWithValue("membership_id", membership.MembershipId);
        command.Parameters.AddWithValue(
            "opening_as_of_date",
            NpgsqlDbType.Date,
            new DateOnly(2026, 7, 13));
        command.Parameters.AddWithValue("declared_remaining_visits", declaredRemainingVisits);
        command.Parameters.AddWithValue("declared_negative_balance", declaredNegativeBalance);
        command.Parameters.AddWithValue(
            "known_effective_end_date",
            NpgsqlDbType.Date,
            ExtendedEndDate);
        command.Parameters.AddWithValue("recorded_at", TestNow.AddMinutes(-10));
        command.Parameters.AddWithValue("recorded_by_account_id", actor.AccountId.Value);
        command.Parameters.AddWithValue("recorded_session_id", actor.SessionId.Value);
        Assert.Equal(1, await command.ExecuteNonQueryAsync());
    }

    private static async Task UpdateMembershipStatusAsync(
        PostgreSqlTestDatabase database,
        Guid membershipId,
        string status)
    {
        await using var connection = new NpgsqlConnection(database.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            update bodylife.issued_memberships
            set status = @status
            where id = @membership_id
            """;
        command.Parameters.AddWithValue("status", status);
        command.Parameters.AddWithValue("membership_id", membershipId);
        Assert.Equal(1, await command.ExecuteNonQueryAsync());
    }

    private static async Task InsertExtensionDayAsync(
        PostgreSqlTestDatabase database,
        Guid membershipId,
        DateOnly extensionDate,
        string sourceType,
        Guid sourceId,
        string sourceLabel,
        bool isActive)
    {
        await using var connection = new NpgsqlConnection(database.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            insert into bodylife.membership_extension_days (
                id,
                membership_id,
                extension_date,
                source_type,
                source_id,
                source_label,
                is_active,
                recalculated_at)
            values (
                @id,
                @membership_id,
                @extension_date,
                @source_type,
                @source_id,
                @source_label,
                @is_active,
                @recalculated_at)
            """;
        command.Parameters.AddWithValue("id", Guid.NewGuid());
        command.Parameters.AddWithValue("membership_id", membershipId);
        command.Parameters.AddWithValue("extension_date", NpgsqlDbType.Date, extensionDate);
        command.Parameters.AddWithValue("source_type", sourceType);
        command.Parameters.AddWithValue("source_id", sourceId);
        command.Parameters.AddWithValue("source_label", sourceLabel);
        command.Parameters.AddWithValue("is_active", isActive);
        command.Parameters.AddWithValue("recalculated_at", TestNow);
        Assert.Equal(1, await command.ExecuteNonQueryAsync());
    }

    private static async Task SetCacheExtensionStateAsync(
        PostgreSqlTestDatabase database,
        Guid membershipId,
        int extensionDays,
        DateOnly effectiveEndDate)
    {
        await using var connection = new NpgsqlConnection(database.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            update bodylife.membership_state_cache
            set extension_days = @extension_days,
                effective_end_date = @effective_end_date
            where membership_id = @membership_id
            """;
        command.Parameters.AddWithValue("extension_days", extensionDays);
        command.Parameters.AddWithValue(
            "effective_end_date",
            NpgsqlDbType.Date,
            effectiveEndDate);
        command.Parameters.AddWithValue("membership_id", membershipId);
        Assert.Equal(1, await command.ExecuteNonQueryAsync());
    }

    private static async Task SetCacheVersionAsync(
        PostgreSqlTestDatabase database,
        Guid membershipId,
        int version)
    {
        await using var connection = new NpgsqlConnection(database.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            update bodylife.membership_state_cache
            set recalculation_version = @version
            where membership_id = @membership_id
            """;
        command.Parameters.AddWithValue("version", version);
        command.Parameters.AddWithValue("membership_id", membershipId);
        Assert.Equal(1, await command.ExecuteNonQueryAsync());
    }

    private static async Task SetCacheEffectiveEndDateAsync(
        PostgreSqlTestDatabase database,
        Guid membershipId,
        DateOnly effectiveEndDate)
    {
        await using var connection = new NpgsqlConnection(database.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            update bodylife.membership_state_cache
            set effective_end_date = @effective_end_date
            where membership_id = @membership_id
            """;
        command.Parameters.AddWithValue(
            "effective_end_date",
            NpgsqlDbType.Date,
            effectiveEndDate);
        command.Parameters.AddWithValue("membership_id", membershipId);
        Assert.Equal(1, await command.ExecuteNonQueryAsync());
    }

    private static async Task<CacheMetadata> ReadCacheMetadataAsync(
        PostgreSqlTestDatabase database,
        Guid membershipId)
    {
        await using var connection = new NpgsqlConnection(database.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            select recalculated_at,
                   recalculation_version,
                   effective_end_date
            from bodylife.membership_state_cache
            where membership_id = @membership_id
            """;
        command.Parameters.AddWithValue("membership_id", membershipId);
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        return new CacheMetadata(
            reader.GetFieldValue<DateTimeOffset>(0),
            reader.GetInt32(1),
            reader.GetFieldValue<DateOnly>(2));
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

    private sealed record MembershipFixture(
        Guid MembershipId,
        Guid ClientId,
        Guid MembershipTypeId);

    private sealed record CacheMetadata(
        DateTimeOffset RecalculatedAt,
        int RecalculationVersion,
        DateOnly EffectiveEndDate);

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow()
        {
            return utcNow;
        }
    }
}
