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

public sealed class PostgreSqlGetClientMembershipStatesQueryTests
{
    private static readonly DateTimeOffset TestNow = new(
        2026,
        7,
        14,
        14,
        0,
        0,
        TimeSpan.Zero);
    private static readonly DateOnly AsOfDate = new(2026, 7, 14);
    private static readonly Guid ActiveMembershipId = Guid.Parse(
        "11111111-1111-1111-1111-111111111111");
    private static readonly Guid CorrectedMembershipId = Guid.Parse(
        "22222222-2222-2222-2222-222222222222");
    private static readonly Guid CanceledMembershipId = Guid.Parse(
        "33333333-3333-3333-3333-333333333333");
    private static readonly Guid ExpiredMembershipId = Guid.Parse(
        "44444444-4444-4444-4444-444444444444");
    private static readonly Guid FreezeId = Guid.Parse(
        "55555555-5555-5555-5555-555555555555");
    private static readonly Guid NonWorkingPeriodId = Guid.Parse(
        "66666666-6666-6666-6666-666666666666");
    private static readonly Guid AdjustmentId = Guid.Parse(
        "77777777-7777-7777-7777-777777777777");

    [PostgreSqlFact]
    public async Task AcceptedActorsReadEmptyClientCollectionWithoutQuerySideEffects()
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
        var clientId = await InsertClientAsync(database, owner.AccountId.Value);
        var handler = CreateHandler(dbContext);

        foreach (var actor in new[] { owner, namedAdmin, sharedAdmin })
        {
            var result = await handler.ExecuteAsync(
                Query(actor, clientId),
                CancellationToken.None);

            AssertSuccessful(result);
            Assert.Equal(clientId, result.StateCollection!.ClientId);
            Assert.Equal(AsOfDate, result.StateCollection.AsOfDate);
            Assert.Empty(result.StateCollection.Timeline);
            Assert.Equal(
                ActiveMembershipCandidateStatus.None,
                result.StateCollection.ActiveCandidateSelection.Status);
            Assert.Null(result.StateCollection.ActiveCandidateSelection.SingleCandidate);
            Assert.Empty(result.StateCollection.ActiveCandidateSelection.Candidates);
            AssertIssuePermission(result);
        }

        Assert.Empty(dbContext.ChangeTracker.Entries());
        await AssertNoQuerySideEffectsAsync(database);
    }

    [PostgreSqlFact]
    public async Task TimelineProjectsCanonicalStatesLifecycleAndExtensionsInStableOrder()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var dbContext = database.CreateDbContext();
        await dbContext.Database.MigrateAsync();
        var owner = await SeedActorAsync(database, ActorRole.Owner, AccountKind.Owner);
        var fixture = await SeedClientCatalogAsync(database, owner.AccountId.Value);
        var active = await InsertMembershipAsync(
            database,
            fixture,
            owner.AccountId.Value,
            ActiveMembershipId,
            startDate: new DateOnly(2026, 7, 10),
            issuedAt: TestNow.AddDays(-2),
            status: "active",
            remainingVisits: 1,
            extensionDays: 2);
        await InsertMembershipAsync(
            database,
            fixture,
            owner.AccountId.Value,
            CorrectedMembershipId,
            startDate: new DateOnly(2026, 7, 10),
            issuedAt: TestNow.AddDays(-3),
            status: "corrected");
        await InsertMembershipAsync(
            database,
            fixture,
            owner.AccountId.Value,
            CanceledMembershipId,
            startDate: new DateOnly(2026, 7, 1),
            issuedAt: TestNow.AddDays(-10),
            status: "canceled");
        await InsertMembershipAsync(
            database,
            fixture,
            owner.AccountId.Value,
            ExpiredMembershipId,
            startDate: new DateOnly(2026, 6, 1),
            issuedAt: TestNow.AddDays(-40),
            status: "active");
        await InsertExtensionDayAsync(
            database,
            active.MembershipId,
            new DateOnly(2026, 7, 12),
            "non_working_period",
            NonWorkingPeriodId,
            "Gym closure",
            isActive: true);
        await InsertExtensionDayAsync(
            database,
            active.MembershipId,
            new DateOnly(2026, 7, 11),
            "membership_adjustment",
            AdjustmentId,
            "Canceled adjustment",
            isActive: false);
        await InsertExtensionDayAsync(
            database,
            active.MembershipId,
            new DateOnly(2026, 7, 11),
            "freeze",
            FreezeId,
            "Summer freeze",
            isActive: true);
        var cacheBefore = await ReadCacheMetadataAsync(database, active.MembershipId);

        var result = await CreateHandler(dbContext).ExecuteAsync(
            Query(owner, fixture.ClientId),
            CancellationToken.None);

        AssertSuccessful(result);
        var collection = result.StateCollection!;
        Assert.Collection(
            collection.Timeline,
            item =>
            {
                Assert.Equal(ActiveMembershipId, item.State.MembershipId);
                Assert.Equal(IssuedMembershipLifecycleStatus.Active, item.LifecycleStatus);
            },
            item =>
            {
                Assert.Equal(CorrectedMembershipId, item.State.MembershipId);
                Assert.Equal(IssuedMembershipLifecycleStatus.Corrected, item.LifecycleStatus);
            },
            item =>
            {
                Assert.Equal(CanceledMembershipId, item.State.MembershipId);
                Assert.Equal(IssuedMembershipLifecycleStatus.Canceled, item.LifecycleStatus);
            },
            item =>
            {
                Assert.Equal(ExpiredMembershipId, item.State.MembershipId);
                Assert.Equal(IssuedMembershipLifecycleStatus.Active, item.LifecycleStatus);
            });
        var activeItem = collection.Timeline[0];
        Assert.Equal(fixture.ClientId, activeItem.State.ClientId);
        Assert.Equal(fixture.MembershipTypeId, activeItem.State.MembershipTypeId);
        Assert.Equal("Eight visits / 30 days", activeItem.State.Snapshot.TypeName);
        Assert.Equal(30, activeItem.State.Snapshot.DurationDays);
        Assert.Equal(8, activeItem.State.Snapshot.VisitsLimit);
        Assert.Equal(1200m, activeItem.State.Snapshot.Price.Amount);
        Assert.Equal("UAH", activeItem.State.Snapshot.Price.Currency);
        Assert.Equal(active.BaseEndDate.AddDays(2), activeItem.State.EffectiveEndDate);
        Assert.Equal(7, activeItem.State.CountedVisits);
        Assert.Equal(1, activeItem.State.RemainingVisits);
        Assert.Equal(2, activeItem.State.ExtensionDays);
        Assert.Equal(AsOfDate, activeItem.State.AsOfDate);
        Assert.True(activeItem.State.IsActiveByDate);
        Assert.Equal(
            [MembershipWarningCodes.LowRemaining],
            activeItem.State.Warnings.Select(warning => warning.Code));
        Assert.Collection(
            activeItem.State.ExtensionExplanation,
            explanation =>
            {
                Assert.Equal(new DateOnly(2026, 7, 11), explanation.ExtensionDate);
                Assert.Equal("freeze", explanation.SourceType);
                Assert.Equal(FreezeId, explanation.SourceId);
                Assert.True(explanation.IsActive);
            },
            explanation =>
            {
                Assert.Equal(new DateOnly(2026, 7, 11), explanation.ExtensionDate);
                Assert.Equal("membership_adjustment", explanation.SourceType);
                Assert.Equal(AdjustmentId, explanation.SourceId);
                Assert.False(explanation.IsActive);
            },
            explanation =>
            {
                Assert.Equal(new DateOnly(2026, 7, 12), explanation.ExtensionDate);
                Assert.Equal("non_working_period", explanation.SourceType);
                Assert.Equal(NonWorkingPeriodId, explanation.SourceId);
                Assert.True(explanation.IsActive);
            });
        Assert.False(collection.Timeline[3].State.IsActiveByDate);
        Assert.Equal(
            ActiveMembershipCandidateStatus.Single,
            collection.ActiveCandidateSelection.Status);
        Assert.Same(
            activeItem,
            collection.ActiveCandidateSelection.SingleCandidate);
        Assert.Same(
            activeItem,
            Assert.Single(collection.ActiveCandidateSelection.Candidates));
        AssertIssuePermission(result);
        Assert.Equal(
            cacheBefore,
            await ReadCacheMetadataAsync(database, active.MembershipId));
        Assert.Empty(dbContext.ChangeTracker.Entries());
        await AssertNoQuerySideEffectsAsync(database);
    }

    [PostgreSqlFact]
    public async Task MultipleDateActiveLifecycleRowsRemainExplicitlyAmbiguous()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var dbContext = database.CreateDbContext();
        await dbContext.Database.MigrateAsync();
        var owner = await SeedActorAsync(database, ActorRole.Owner, AccountKind.Owner);
        var fixture = await SeedClientCatalogAsync(database, owner.AccountId.Value);
        await InsertMembershipAsync(
            database,
            fixture,
            owner.AccountId.Value,
            ActiveMembershipId,
            startDate: new DateOnly(2026, 7, 10),
            issuedAt: TestNow.AddDays(-1),
            status: "active");
        await InsertMembershipAsync(
            database,
            fixture,
            owner.AccountId.Value,
            CorrectedMembershipId,
            startDate: new DateOnly(2026, 7, 1),
            issuedAt: TestNow.AddDays(-10),
            status: "active");

        var result = await CreateHandler(dbContext).ExecuteAsync(
            Query(owner, fixture.ClientId),
            CancellationToken.None);

        AssertSuccessful(result);
        var selection = result.StateCollection!.ActiveCandidateSelection;
        Assert.Equal(ActiveMembershipCandidateStatus.Ambiguous, selection.Status);
        Assert.Null(selection.SingleCandidate);
        Assert.Collection(
            selection.Candidates,
            item => Assert.Equal(ActiveMembershipId, item.State.MembershipId),
            item => Assert.Equal(CorrectedMembershipId, item.State.MembershipId));
    }

    [PostgreSqlFact]
    public async Task MissingStaleAndInconsistentCacheFailWithoutReadRepair()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var dbContext = database.CreateDbContext();
        await dbContext.Database.MigrateAsync();
        var owner = await SeedActorAsync(database, ActorRole.Owner, AccountKind.Owner);
        var fixture = await SeedClientCatalogAsync(database, owner.AccountId.Value);
        await InsertMembershipAsync(
            database,
            fixture,
            owner.AccountId.Value,
            ActiveMembershipId,
            startDate: new DateOnly(2026, 7, 1),
            issuedAt: TestNow.AddDays(-10),
            status: "active");
        var missingCache = await InsertMembershipAsync(
            database,
            fixture,
            owner.AccountId.Value,
            CorrectedMembershipId,
            startDate: new DateOnly(2026, 7, 10),
            issuedAt: TestNow.AddDays(-1),
            status: "active",
            includeCache: false);
        var handler = CreateHandler(dbContext);

        AssertRecalculationFailed(await handler.ExecuteAsync(
            Query(owner, fixture.ClientId),
            CancellationToken.None));
        Assert.Equal(
            1L,
            await database.ExecuteScalarAsync<long>(
                "select count(*) from bodylife.membership_state_cache"));

        await InsertCacheAsync(
            database,
            missingCache,
            remainingVisits: 8,
            extensionDays: 0,
            recalculationVersion:
                MembershipStateCacheRebuilder.CurrentRecalculationVersion - 1);
        var staleBefore = await ReadCacheMetadataAsync(
            database,
            missingCache.MembershipId);

        AssertRecalculationFailed(await handler.ExecuteAsync(
            Query(owner, fixture.ClientId),
            CancellationToken.None));
        Assert.Equal(
            staleBefore,
            await ReadCacheMetadataAsync(database, missingCache.MembershipId));

        await UpdateCacheVersionAndEffectiveEndAsync(
            database,
            missingCache.MembershipId,
            MembershipStateCacheRebuilder.CurrentRecalculationVersion,
            missingCache.BaseEndDate.AddDays(1));
        var inconsistentBefore = await ReadCacheMetadataAsync(
            database,
            missingCache.MembershipId);

        AssertRecalculationFailed(await handler.ExecuteAsync(
            Query(owner, fixture.ClientId),
            CancellationToken.None));
        Assert.Equal(
            inconsistentBefore,
            await ReadCacheMetadataAsync(database, missingCache.MembershipId));
        await AssertNoQuerySideEffectsAsync(database);
    }

    [PostgreSqlFact]
    public async Task InvalidSelectorsAndMissingClientUseStableErrors()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var dbContext = database.CreateDbContext();
        await dbContext.Database.MigrateAsync();
        var owner = await SeedActorAsync(database, ActorRole.Owner, AccountKind.Owner);
        var handler = CreateHandler(dbContext);

        var missingClientId = await handler.ExecuteAsync(
            Query(owner, Guid.Empty),
            CancellationToken.None);
        var missingAsOfDate = await handler.ExecuteAsync(
            new GetClientMembershipStatesQuery(owner, Guid.NewGuid(), default),
            CancellationToken.None);
        var unknownClient = await handler.ExecuteAsync(
            Query(owner, Guid.NewGuid()),
            CancellationToken.None);

        AssertValidationFailure(missingClientId, "clientId");
        AssertValidationFailure(missingAsOfDate, "asOfDate");
        Assert.Equal(GetClientMembershipStatesStatus.NotFound, unknownClient.Status);
        Assert.Equal("not_found", unknownClient.ErrorCode);
        Assert.Equal("clientId", unknownClient.ErrorField);
        Assert.Null(unknownClient.StateCollection);
        Assert.Empty(unknownClient.AllowedActions.Items);
    }

    [PostgreSqlFact]
    public async Task InactiveExpiredEndedUnknownAndForgedActorsAreDenied()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var dbContext = database.CreateDbContext();
        await dbContext.Database.MigrateAsync();
        var owner = await SeedActorAsync(database, ActorRole.Owner, AccountKind.Owner);
        var clientId = await InsertClientAsync(database, owner.AccountId.Value);
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
        var endedAdmin = await SeedActorAsync(
            database,
            ActorRole.Admin,
            AccountKind.NamedAdmin,
            sessionEndedAt: TestNow.AddMinutes(-1));
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
        var invalidActor = new ActorContext(
            new AccountId(Guid.Empty),
            ActorRole.Owner,
            AccountKind.Owner,
            new SessionId(Guid.Empty),
            null);
        var handler = CreateHandler(dbContext);

        foreach (var actor in new[]
                 {
                     inactiveAdmin,
                     expiredAdmin,
                     endedAdmin,
                     forgedOwner,
                     unknownOwner,
                     invalidActor,
                 })
        {
            var result = await handler.ExecuteAsync(
                Query(actor, clientId),
                CancellationToken.None);

            Assert.Equal(GetClientMembershipStatesStatus.PermissionDenied, result.Status);
            Assert.Equal("permission_denied", result.ErrorCode);
            Assert.Null(result.StateCollection);
            Assert.Empty(result.AllowedActions.Items);
        }
    }

    [Fact]
    public void PersistenceRegistrationExposesScopedClientMembershipStatesHandler()
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
            GetClientMembershipStatesQuery,
            GetClientMembershipStatesResult>);
        var descriptor = Assert.Single(
            services,
            candidate => candidate.ServiceType == serviceType);
        Assert.Equal(ServiceLifetime.Scoped, descriptor.Lifetime);
        Assert.Equal(
            typeof(GetClientMembershipStatesQueryHandler),
            descriptor.ImplementationType);
    }

    private static GetClientMembershipStatesQueryHandler CreateHandler(
        BodyLifeDbContext dbContext)
    {
        return new GetClientMembershipStatesQueryHandler(
            dbContext,
            new FixedTimeProvider(TestNow));
    }

    private static GetClientMembershipStatesQuery Query(
        ActorContext actor,
        Guid clientId)
    {
        return new GetClientMembershipStatesQuery(actor, clientId, AsOfDate);
    }

    private static void AssertSuccessful(GetClientMembershipStatesResult result)
    {
        Assert.Equal(GetClientMembershipStatesStatus.Success, result.Status);
        Assert.NotNull(result.StateCollection);
        Assert.Null(result.ErrorCode);
        Assert.Null(result.ErrorMessage);
        Assert.Null(result.ErrorField);
    }

    private static void AssertIssuePermission(GetClientMembershipStatesResult result)
    {
        var permission = Assert.Single(result.AllowedActions.Items);
        Assert.Equal(MembershipActionKeys.Issue, permission.ActionKey);
        Assert.Equal(MembershipActionKeys.AdminOrOwnerPolicy, permission.RequiredPolicy);
        Assert.True(permission.IsAllowed);
    }

    private static void AssertValidationFailure(
        GetClientMembershipStatesResult result,
        string errorField)
    {
        Assert.Equal(GetClientMembershipStatesStatus.ValidationFailed, result.Status);
        Assert.Equal("validation_failed", result.ErrorCode);
        Assert.Equal(errorField, result.ErrorField);
        Assert.Null(result.StateCollection);
        Assert.Empty(result.AllowedActions.Items);
    }

    private static void AssertRecalculationFailed(GetClientMembershipStatesResult result)
    {
        Assert.Equal(GetClientMembershipStatesStatus.RecalculationFailed, result.Status);
        Assert.Equal("recalculation_failed", result.ErrorCode);
        Assert.Null(result.StateCollection);
        Assert.Empty(result.AllowedActions.Items);
    }

    private static async Task AssertNoQuerySideEffectsAsync(
        PostgreSqlTestDatabase database)
    {
        Assert.Equal(
            0L,
            await database.ExecuteScalarAsync<long>(
                "select count(*) from bodylife.business_audit_entries"));
        Assert.Equal(
            0L,
            await database.ExecuteScalarAsync<long>(
                "select count(*) from bodylife.command_idempotency_keys"));
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
                @ended_at,
                @last_seen_at)
            """;
        command.Parameters.AddWithValue("account_id", accountId);
        command.Parameters.AddWithValue(
            "display_name",
            $"{accountKind} client membership query actor");
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
        command.Parameters.Add("ended_at", NpgsqlDbType.TimestampTz).Value =
            sessionEndedAt ?? (object)DBNull.Value;
        command.Parameters.AddWithValue("last_seen_at", TestNow.AddMinutes(-5));
        Assert.Equal(2, await command.ExecuteNonQueryAsync());

        return new ActorContext(
            new AccountId(accountId),
            role,
            accountKind,
            new SessionId(sessionId),
            deviceLabel);
    }

    private static async Task<Guid> InsertClientAsync(
        PostgreSqlTestDatabase database,
        Guid createdByAccountId)
    {
        var clientId = Guid.NewGuid();
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
                'Timeline',
                'Reader',
                null,
                'TIMELINE READER',
                null,
                null,
                null,
                null,
                'active',
                @recorded_at,
                @created_by_account_id,
                @recorded_at)
            """;
        command.Parameters.AddWithValue("client_id", clientId);
        command.Parameters.AddWithValue("recorded_at", TestNow.AddDays(-60));
        command.Parameters.AddWithValue("created_by_account_id", createdByAccountId);
        Assert.Equal(1, await command.ExecuteNonQueryAsync());
        return clientId;
    }

    private static async Task<ClientCatalogFixture> SeedClientCatalogAsync(
        PostgreSqlTestDatabase database,
        Guid createdByAccountId)
    {
        var clientId = await InsertClientAsync(database, createdByAccountId);
        var membershipTypeId = Guid.NewGuid();
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
                @membership_type_id,
                'Eight visits / 30 days',
                30,
                8,
                1200,
                'UAH',
                true,
                null,
                @recorded_at,
                @recorded_at,
                null)
            """;
        command.Parameters.AddWithValue("membership_type_id", membershipTypeId);
        command.Parameters.AddWithValue("recorded_at", TestNow.AddDays(-60));
        Assert.Equal(1, await command.ExecuteNonQueryAsync());
        return new ClientCatalogFixture(clientId, membershipTypeId);
    }

    private static async Task<MembershipFixture> InsertMembershipAsync(
        PostgreSqlTestDatabase database,
        ClientCatalogFixture fixture,
        Guid issuedByAccountId,
        Guid membershipId,
        DateOnly startDate,
        DateTimeOffset issuedAt,
        string status,
        int remainingVisits = 8,
        int extensionDays = 0,
        bool includeCache = true)
    {
        const int durationDays = 30;
        var baseEndDate = startDate.AddDays(durationDays - 1);
        var membership = new MembershipFixture(membershipId, baseEndDate);
        await using var connection = new NpgsqlConnection(database.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
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
                'Eight visits / 30 days',
                30,
                8,
                1200,
                'UAH',
                @start_date,
                @base_end_date,
                @issued_at,
                @issued_by_account_id,
                @status,
                'normal',
                null,
                null)
            """;
        command.Parameters.AddWithValue("membership_id", membershipId);
        command.Parameters.AddWithValue("client_id", fixture.ClientId);
        command.Parameters.AddWithValue("membership_type_id", fixture.MembershipTypeId);
        command.Parameters.AddWithValue("start_date", NpgsqlDbType.Date, startDate);
        command.Parameters.AddWithValue("base_end_date", NpgsqlDbType.Date, baseEndDate);
        command.Parameters.AddWithValue("issued_at", issuedAt);
        command.Parameters.AddWithValue("issued_by_account_id", issuedByAccountId);
        command.Parameters.AddWithValue("status", status);
        Assert.Equal(1, await command.ExecuteNonQueryAsync());

        if (includeCache)
        {
            await InsertCacheAsync(
                database,
                membership,
                remainingVisits,
                extensionDays,
                MembershipStateCacheRebuilder.CurrentRecalculationVersion);
        }

        return membership;
    }

    private static async Task InsertCacheAsync(
        PostgreSqlTestDatabase database,
        MembershipFixture membership,
        int remainingVisits,
        int extensionDays,
        int recalculationVersion)
    {
        await using var connection = new NpgsqlConnection(database.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            insert into bodylife.membership_state_cache (
                membership_id,
                counted_visits,
                remaining_visits,
                negative_balance,
                first_negative_visit_id,
                first_negative_visit_date,
                extension_days,
                effective_end_date,
                last_counted_visit_at,
                recalculated_at,
                recalculation_version)
            values (
                @membership_id,
                @counted_visits,
                @remaining_visits,
                @negative_balance,
                null,
                null,
                @extension_days,
                @effective_end_date,
                @last_counted_visit_at,
                @recalculated_at,
                @recalculation_version)
            """;
        command.Parameters.AddWithValue("membership_id", membership.MembershipId);
        command.Parameters.AddWithValue("counted_visits", 8 - remainingVisits);
        command.Parameters.AddWithValue("remaining_visits", remainingVisits);
        command.Parameters.AddWithValue("negative_balance", Math.Max(0, -remainingVisits));
        command.Parameters.AddWithValue("extension_days", extensionDays);
        command.Parameters.AddWithValue(
            "effective_end_date",
            NpgsqlDbType.Date,
            membership.BaseEndDate.AddDays(extensionDays));
        command.Parameters.AddWithValue("last_counted_visit_at", TestNow.AddDays(-1));
        command.Parameters.AddWithValue("recalculated_at", TestNow.AddMinutes(-20));
        command.Parameters.AddWithValue("recalculation_version", recalculationVersion);
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
        command.Parameters.AddWithValue("recalculated_at", TestNow.AddMinutes(-20));
        Assert.Equal(1, await command.ExecuteNonQueryAsync());
    }

    private static async Task UpdateCacheVersionAndEffectiveEndAsync(
        PostgreSqlTestDatabase database,
        Guid membershipId,
        int recalculationVersion,
        DateOnly effectiveEndDate)
    {
        await using var connection = new NpgsqlConnection(database.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            update bodylife.membership_state_cache
            set recalculation_version = @recalculation_version,
                effective_end_date = @effective_end_date
            where membership_id = @membership_id
            """;
        command.Parameters.AddWithValue("recalculation_version", recalculationVersion);
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
            select recalculation_version, effective_end_date, recalculated_at
            from bodylife.membership_state_cache
            where membership_id = @membership_id
            """;
        command.Parameters.AddWithValue("membership_id", membershipId);
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        return new CacheMetadata(
            reader.GetInt32(0),
            reader.GetFieldValue<DateOnly>(1),
            reader.GetFieldValue<DateTimeOffset>(2));
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

    private static string MapAccountKind(AccountKind accountKind)
    {
        return accountKind switch
        {
            AccountKind.Owner => "owner",
            AccountKind.NamedAdmin => "named_admin",
            AccountKind.SharedReceptionAdmin => "shared_reception_admin",
            _ => throw new ArgumentOutOfRangeException(
                nameof(accountKind),
                accountKind,
                null),
        };
    }

    private sealed record ClientCatalogFixture(Guid ClientId, Guid MembershipTypeId);

    private sealed record MembershipFixture(Guid MembershipId, DateOnly BaseEndDate);

    private sealed record CacheMetadata(
        int RecalculationVersion,
        DateOnly EffectiveEndDate,
        DateTimeOffset RecalculatedAt);

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow()
        {
            return utcNow;
        }
    }
}
