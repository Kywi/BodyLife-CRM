using BodyLife.Crm.Application.Commands;
using BodyLife.Crm.Application.Queries;
using BodyLife.Crm.Infrastructure;
using BodyLife.Crm.Infrastructure.Persistence;
using BodyLife.Crm.Infrastructure.Persistence.Freezes;
using BodyLife.Crm.Infrastructure.Persistence.Memberships;
using BodyLife.Crm.Infrastructure.Persistence.UsersRoles;
using BodyLife.Crm.Infrastructure.Persistence.Visits;
using BodyLife.Crm.Modules.Memberships;
using BodyLife.Crm.Modules.Visits;
using BodyLife.Crm.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using NpgsqlTypes;

namespace BodyLife.Crm.Infrastructure.Tests.PostgreSql;

public sealed class PostgreSqlGetMarkVisitOptionsQueryTests
{
    private static readonly DateTimeOffset TestNow = new(
        2026,
        7,
        14,
        14,
        0,
        0,
        TimeSpan.Zero);
    private static readonly DateOnly VisitDate = new(2026, 7, 14);

    [PostgreSqlFact]
    public async Task SingleCurrentMembershipIsSuggestedWithCanonicalDetails()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var dbContext = database.CreateDbContext();
        await dbContext.Database.MigrateAsync();
        var fixture = await SeedFixtureAsync(database, dbContext);
        var membershipId = await InsertMembershipAsync(
            database,
            dbContext,
            fixture,
            "Two visits / 30 days",
            startDate: new DateOnly(2026, 7, 1),
            durationDays: 30,
            visitsLimit: 2);

        var result = await CreateHandler(dbContext).ExecuteAsync(
            new GetMarkVisitOptionsQuery(
                fixture.Actor,
                fixture.ClientId,
                TestNow),
            CancellationToken.None);

        Assert.Equal(GetMarkVisitOptionsStatus.Success, result.Status);
        Assert.True(result.AllowedActions.IsAllowed(VisitActionKeys.Mark));
        Assert.Null(result.ErrorCode);
        var options = Assert.IsType<MarkVisitOptions>(result.Options);
        Assert.Equal(fixture.ClientId, options.ClientId);
        Assert.Equal(TestNow, options.OccurredAt);
        Assert.Equal(VisitDate, options.VisitDate);
        Assert.Equal(membershipId, options.SuggestedMembershipId);

        var membership = Assert.Single(options.MembershipOptions);
        Assert.Equal(membershipId, membership.MembershipId);
        Assert.Equal("Two visits / 30 days", membership.TypeName);
        Assert.Equal(new DateOnly(2026, 7, 1), membership.StartDate);
        Assert.Equal(new DateOnly(2026, 7, 30), membership.EffectiveEndDate);
        Assert.Equal(2, membership.RemainingVisits);
        Assert.True(membership.CanSelect);
        Assert.Equal(MembershipVisitEligibilityStatus.Eligible, membership.EligibilityStatus);
        Assert.Empty(membership.RequiredAcknowledgements);
        Assert.Equal(0L, await CountRowsAsync(database, "business_audit_entries"));
        Assert.Equal(0L, await CountRowsAsync(database, "command_idempotency_keys"));
    }

    [PostgreSqlFact]
    public async Task ClientWithoutMembershipStillGetsExplicitNonMembershipContext()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var dbContext = database.CreateDbContext();
        await dbContext.Database.MigrateAsync();
        var fixture = await SeedFixtureAsync(database, dbContext);

        var result = await CreateHandler(dbContext).ExecuteAsync(
            new GetMarkVisitOptionsQuery(
                fixture.Actor,
                fixture.ClientId,
                TestNow),
            CancellationToken.None);

        Assert.Equal(GetMarkVisitOptionsStatus.Success, result.Status);
        Assert.True(result.AllowedActions.IsAllowed(VisitActionKeys.Mark));
        var options = Assert.IsType<MarkVisitOptions>(result.Options);
        Assert.Empty(options.MembershipOptions);
        Assert.Null(options.SuggestedMembershipId);
    }

    [PostgreSqlFact]
    public async Task ExpiredZeroAndNegativeStatesExposeExactTypedAcknowledgements()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var dbContext = database.CreateDbContext();
        await dbContext.Database.MigrateAsync();
        var fixture = await SeedFixtureAsync(database, dbContext);
        var membershipId = await InsertMembershipAsync(
            database,
            dbContext,
            fixture,
            "Expired zero membership",
            startDate: new DateOnly(2026, 7, 1),
            durationDays: 13,
            visitsLimit: 0);
        var handler = CreateHandler(dbContext);

        var zeroResult = await handler.ExecuteAsync(
            new GetMarkVisitOptionsQuery(
                fixture.Actor,
                fixture.ClientId,
                TestNow),
            CancellationToken.None);

        var zeroOptions = Assert.IsType<MarkVisitOptions>(zeroResult.Options);
        var zeroMembership = Assert.Single(zeroOptions.MembershipOptions);
        Assert.Null(zeroOptions.SuggestedMembershipId);
        Assert.True(zeroMembership.CanSelect);
        Assert.Equal(
            [
                MembershipVisitAcknowledgement.Expired,
                MembershipVisitAcknowledgement.ZeroRemaining,
            ],
            zeroMembership.RequiredAcknowledgements);

        await InsertCountedVisitAsync(database, fixture, membershipId);
        var rebuild = await new MembershipStateCacheRebuilder(
                dbContext,
                new FixedTimeProvider(TestNow))
            .RebuildAsync(membershipId);
        Assert.True(rebuild.Succeeded);

        var negativeResult = await handler.ExecuteAsync(
            new GetMarkVisitOptionsQuery(
                fixture.Actor,
                fixture.ClientId,
                TestNow),
            CancellationToken.None);

        var negativeMembership = Assert.Single(
            Assert.IsType<MarkVisitOptions>(negativeResult.Options).MembershipOptions);
        Assert.Equal(-1, negativeMembership.RemainingVisits);
        Assert.Equal(
            [
                MembershipVisitAcknowledgement.Expired,
                MembershipVisitAcknowledgement.NegativeRemaining,
            ],
            negativeMembership.RequiredAcknowledgements);
        Assert.Contains(
            negativeMembership.Warnings,
            warning => warning.Code == MembershipWarningCodes.NegativeBalance);
    }

    [PostgreSqlFact]
    public async Task AmbiguousFutureAndFrozenMembershipsRemainExplicitWithoutSuggestion()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var dbContext = database.CreateDbContext();
        await dbContext.Database.MigrateAsync();
        var fixture = await SeedFixtureAsync(database, dbContext);
        var firstCurrentId = await InsertMembershipAsync(
            database,
            dbContext,
            fixture,
            "Current A",
            new DateOnly(2026, 7, 1),
            durationDays: 30,
            visitsLimit: 8);
        var secondCurrentId = await InsertMembershipAsync(
            database,
            dbContext,
            fixture,
            "Current B",
            new DateOnly(2026, 7, 2),
            durationDays: 30,
            visitsLimit: 6);
        var frozenId = await InsertMembershipAsync(
            database,
            dbContext,
            fixture,
            "Frozen current",
            new DateOnly(2026, 7, 3),
            durationDays: 30,
            visitsLimit: 4);
        var futureId = await InsertMembershipAsync(
            database,
            dbContext,
            fixture,
            "Future membership",
            new DateOnly(2026, 7, 20),
            durationDays: 30,
            visitsLimit: 10);
        var canceledId = await InsertMembershipAsync(
            database,
            dbContext,
            fixture,
            "Canceled membership",
            new DateOnly(2026, 7, 1),
            durationDays: 30,
            visitsLimit: 3,
            status: "canceled");
        await InsertFreezeAsync(database, fixture, frozenId, status: "active");
        await InsertFreezeAsync(database, fixture, firstCurrentId, status: "canceled");

        var result = await CreateHandler(dbContext).ExecuteAsync(
            new GetMarkVisitOptionsQuery(
                fixture.Actor,
                fixture.ClientId,
                TestNow),
            CancellationToken.None);

        var options = Assert.IsType<MarkVisitOptions>(result.Options);
        Assert.Null(options.SuggestedMembershipId);
        Assert.Equal(4, options.MembershipOptions.Count);
        Assert.DoesNotContain(
            options.MembershipOptions,
            option => option.MembershipId == canceledId);

        Assert.True(FindOption(options, firstCurrentId).CanSelect);
        Assert.True(FindOption(options, secondCurrentId).CanSelect);
        Assert.Equal(
            MembershipVisitEligibilityStatus.DuringActiveFreeze,
            FindOption(options, frozenId).EligibilityStatus);
        Assert.False(FindOption(options, frozenId).CanSelect);
        Assert.Equal(
            MembershipVisitEligibilityStatus.BeforeMembershipStart,
            FindOption(options, futureId).EligibilityStatus);
        Assert.False(FindOption(options, futureId).CanSelect);
    }

    [PostgreSqlFact]
    public async Task InvalidDeniedMissingAndStaleCacheReturnStableFailures()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var dbContext = database.CreateDbContext();
        await dbContext.Database.MigrateAsync();
        var fixture = await SeedFixtureAsync(database, dbContext);
        var membershipId = await InsertMembershipAsync(
            database,
            dbContext,
            fixture,
            "Failure fixture",
            new DateOnly(2026, 7, 1),
            durationDays: 30,
            visitsLimit: 8);
        var handler = CreateHandler(dbContext);

        var invalidClient = await handler.ExecuteAsync(
            new GetMarkVisitOptionsQuery(fixture.Actor, Guid.Empty, TestNow),
            CancellationToken.None);
        var invalidOccurredAt = await handler.ExecuteAsync(
            new GetMarkVisitOptionsQuery(
                fixture.Actor,
                fixture.ClientId,
                default),
            CancellationToken.None);
        var missing = await handler.ExecuteAsync(
            new GetMarkVisitOptionsQuery(fixture.Actor, Guid.NewGuid(), TestNow),
            CancellationToken.None);
        var denied = await handler.ExecuteAsync(
            new GetMarkVisitOptionsQuery(
                fixture.Actor with { SessionId = SessionId.New() },
                fixture.ClientId,
                TestNow),
            CancellationToken.None);
        await DeleteCacheAsync(database, membershipId);
        var staleCache = await handler.ExecuteAsync(
            new GetMarkVisitOptionsQuery(
                fixture.Actor,
                fixture.ClientId,
                TestNow),
            CancellationToken.None);

        AssertFailure(
            invalidClient,
            GetMarkVisitOptionsStatus.ValidationFailed,
            "clientId");
        AssertFailure(
            invalidOccurredAt,
            GetMarkVisitOptionsStatus.ValidationFailed,
            "occurredAt");
        AssertFailure(missing, GetMarkVisitOptionsStatus.NotFound, "clientId");
        AssertFailure(denied, GetMarkVisitOptionsStatus.PermissionDenied);
        AssertFailure(staleCache, GetMarkVisitOptionsStatus.RecalculationFailed);
    }

    [Fact]
    public void PersistenceRegistrationComposesVisitQueryAndCommandBoundaries()
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

        AssertScopedRegistration<
            IBodyLifeQueryHandler<GetMarkVisitOptionsQuery, GetMarkVisitOptionsResult>,
            GetMarkVisitOptionsQueryHandler>(services);
        AssertScopedRegistration<
            IBodyLifeCommandHandler<MarkVisitCommand>,
            MarkVisitCommandHandler>(services);
        Assert.Contains(
            services,
            descriptor => descriptor.ServiceType
                == typeof(IMembershipVisitFreezeSourceProvider)
                && descriptor.Lifetime == ServiceLifetime.Scoped);
        Assert.Contains(
            services,
            descriptor => descriptor.ServiceType
                == typeof(IMembershipVisitFreezeSourceSnapshotProvider)
                && descriptor.Lifetime == ServiceLifetime.Scoped);
        AssertScopedRegistration<
            IMembershipVisitEligibilityEvaluator,
            MembershipVisitEligibilityEvaluator>(services);
        AssertScopedRegistration<
            IMembershipStateRecalculator,
            MembershipStateRecalculator>(services);
        AssertScopedRegistration<
            MembershipVisitEligibilityPreparer,
            MembershipVisitEligibilityPreparer>(services);

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var concreteReader = scope.ServiceProvider
            .GetRequiredService<MembershipVisitFreezeSourceReader>();
        Assert.Same(
            concreteReader,
            scope.ServiceProvider.GetRequiredService<
                IMembershipVisitFreezeSourceProvider>());
        Assert.Same(
            concreteReader,
            scope.ServiceProvider.GetRequiredService<
                IMembershipVisitFreezeSourceSnapshotProvider>());
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<
            IBodyLifeCommandHandler<MarkVisitCommand>>());
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<
            IBodyLifeQueryHandler<
                GetMarkVisitOptionsQuery,
                GetMarkVisitOptionsResult>>());
    }

    private static GetMarkVisitOptionsQueryHandler CreateHandler(
        BodyLifeDbContext dbContext)
    {
        var timeProvider = new FixedTimeProvider(TestNow);
        return new GetMarkVisitOptionsQueryHandler(
            new GetClientMembershipStatesQueryHandler(dbContext, timeProvider),
            new MembershipVisitFreezeSourceReader(dbContext),
            new MembershipVisitEligibilityEvaluator());
    }

    private static async Task<MarkVisitOptionsFixture> SeedFixtureAsync(
        PostgreSqlTestDatabase database,
        BodyLifeDbContext dbContext)
    {
        var bootstrap = await new OwnerBootstrapper(
                dbContext,
                new FixedTimeProvider(TestNow))
            .BootstrapOwnerAsync("BodyLife Owner");
        Assert.Equal(OwnerBootstrapStatus.Created, bootstrap.Status);

        var accountId = bootstrap.AccountId!.Value;
        var sessionId = Guid.NewGuid();
        var clientId = Guid.NewGuid();
        var membershipTypeId = Guid.NewGuid();
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
                'Reception tablet',
                @started_at,
                @expires_at,
                null,
                @last_seen_at);

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
                'Options',
                'Client',
                null,
                'OPTIONS CLIENT',
                null,
                null,
                null,
                null,
                'active',
                @created_at,
                @account_id,
                @created_at);

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
                'Mark Visit options source',
                30,
                8,
                1000,
                'UAH',
                true,
                null,
                @created_at,
                @created_at,
                null)
            """;
        command.Parameters.AddWithValue("session_id", sessionId);
        command.Parameters.AddWithValue("account_id", accountId);
        command.Parameters.AddWithValue("started_at", TestNow.AddHours(-1));
        command.Parameters.AddWithValue("expires_at", TestNow.AddHours(8));
        command.Parameters.AddWithValue("last_seen_at", TestNow.AddMinutes(-5));
        command.Parameters.AddWithValue("client_id", clientId);
        command.Parameters.AddWithValue("created_at", TestNow.AddDays(-30));
        command.Parameters.AddWithValue("membership_type_id", membershipTypeId);
        Assert.Equal(3, await command.ExecuteNonQueryAsync());

        return new MarkVisitOptionsFixture(
            new ActorContext(
                new AccountId(accountId),
                ActorRole.Owner,
                AccountKind.Owner,
                new SessionId(sessionId),
                "Reception tablet"),
            clientId,
            membershipTypeId);
    }

    private static async Task<Guid> InsertMembershipAsync(
        PostgreSqlTestDatabase database,
        BodyLifeDbContext dbContext,
        MarkVisitOptionsFixture fixture,
        string typeName,
        DateOnly startDate,
        int durationDays,
        int visitsLimit,
        string status = "active")
    {
        var membershipId = Guid.NewGuid();
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
                @id,
                @client_id,
                @membership_type_id,
                @type_name,
                @duration_days,
                @visits_limit,
                1000,
                'UAH',
                @start_date,
                @base_end_date,
                @issued_at,
                @account_id,
                @status,
                'normal',
                null,
                null)
            """;
        command.Parameters.AddWithValue("id", membershipId);
        command.Parameters.AddWithValue("client_id", fixture.ClientId);
        command.Parameters.AddWithValue("membership_type_id", fixture.MembershipTypeId);
        command.Parameters.AddWithValue("type_name", typeName);
        command.Parameters.AddWithValue("duration_days", durationDays);
        command.Parameters.AddWithValue("visits_limit", visitsLimit);
        command.Parameters.AddWithValue("start_date", NpgsqlDbType.Date, startDate);
        command.Parameters.AddWithValue(
            "base_end_date",
            NpgsqlDbType.Date,
            startDate.AddDays(durationDays - 1));
        command.Parameters.AddWithValue("issued_at", TestNow.AddDays(-10));
        command.Parameters.AddWithValue("account_id", fixture.Actor.AccountId.Value);
        command.Parameters.AddWithValue("status", status);
        Assert.Equal(1, await command.ExecuteNonQueryAsync());

        var rebuild = await new MembershipStateCacheRebuilder(
                dbContext,
                new FixedTimeProvider(TestNow))
            .RebuildAsync(membershipId);
        Assert.True(rebuild.Succeeded);
        return membershipId;
    }

    private static async Task InsertCountedVisitAsync(
        PostgreSqlTestDatabase database,
        MarkVisitOptionsFixture fixture,
        Guid membershipId)
    {
        var visitId = Guid.NewGuid();
        await using var connection = new NpgsqlConnection(database.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            insert into bodylife.visits (
                id,
                client_id,
                occurred_at,
                recorded_at,
                recorded_by_account_id,
                session_id,
                visit_kind,
                entry_origin,
                entry_batch_id,
                comment,
                status)
            values (
                @visit_id,
                @client_id,
                @occurred_at,
                @recorded_at,
                @account_id,
                @session_id,
                'membership',
                'normal',
                null,
                null,
                'active');

            insert into bodylife.visit_consumptions (
                id,
                visit_id,
                client_id,
                visit_kind,
                membership_id,
                consumption_type,
                source_fact_type,
                source_fact_id,
                recorded_at,
                recorded_by_account_id,
                recorded_session_id,
                status)
            values (
                @consumption_id,
                @visit_id,
                @client_id,
                'membership',
                @membership_id,
                'counted',
                'visit',
                @visit_id,
                @recorded_at,
                @account_id,
                @session_id,
                'active')
            """;
        command.Parameters.AddWithValue("visit_id", visitId);
        command.Parameters.AddWithValue("consumption_id", Guid.NewGuid());
        command.Parameters.AddWithValue("client_id", fixture.ClientId);
        command.Parameters.AddWithValue("membership_id", membershipId);
        command.Parameters.AddWithValue("occurred_at", TestNow.AddMinutes(-15));
        command.Parameters.AddWithValue("recorded_at", TestNow.AddMinutes(-10));
        command.Parameters.AddWithValue("account_id", fixture.Actor.AccountId.Value);
        command.Parameters.AddWithValue("session_id", fixture.Actor.SessionId.Value);
        Assert.Equal(2, await command.ExecuteNonQueryAsync());
    }

    private static async Task InsertFreezeAsync(
        PostgreSqlTestDatabase database,
        MarkVisitOptionsFixture fixture,
        Guid membershipId,
        string status)
    {
        await using var connection = new NpgsqlConnection(database.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            insert into bodylife.freezes (
                id,
                client_id,
                membership_id,
                start_date,
                end_date,
                reason,
                occurred_at,
                recorded_at,
                recorded_by_account_id,
                session_id,
                entry_origin,
                entry_batch_id,
                status)
            values (
                @id,
                @client_id,
                @membership_id,
                @visit_date,
                @visit_date,
                'Visit options fixture',
                @recorded_at,
                @recorded_at,
                @account_id,
                @session_id,
                'normal',
                null,
                @status)
            """;
        command.Parameters.AddWithValue("id", Guid.NewGuid());
        command.Parameters.AddWithValue("client_id", fixture.ClientId);
        command.Parameters.AddWithValue("membership_id", membershipId);
        command.Parameters.AddWithValue("visit_date", NpgsqlDbType.Date, VisitDate);
        command.Parameters.AddWithValue("recorded_at", TestNow.AddHours(-1));
        command.Parameters.AddWithValue("account_id", fixture.Actor.AccountId.Value);
        command.Parameters.AddWithValue("session_id", fixture.Actor.SessionId.Value);
        command.Parameters.AddWithValue("status", status);
        Assert.Equal(1, await command.ExecuteNonQueryAsync());
    }

    private static async Task DeleteCacheAsync(
        PostgreSqlTestDatabase database,
        Guid membershipId)
    {
        await using var connection = new NpgsqlConnection(database.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            "delete from bodylife.membership_state_cache where membership_id = @id";
        command.Parameters.AddWithValue("id", membershipId);
        Assert.Equal(1, await command.ExecuteNonQueryAsync());
    }

    private static MarkVisitMembershipOption FindOption(
        MarkVisitOptions options,
        Guid membershipId)
    {
        return Assert.Single(
            options.MembershipOptions,
            option => option.MembershipId == membershipId);
    }

    private static void AssertFailure(
        GetMarkVisitOptionsResult result,
        GetMarkVisitOptionsStatus status,
        string? field = null)
    {
        Assert.Equal(status, result.Status);
        Assert.Null(result.Options);
        Assert.Empty(result.AllowedActions.Items);
        Assert.NotNull(result.ErrorCode);
        Assert.NotNull(result.ErrorMessage);
        if (field is not null)
        {
            Assert.Equal(field, result.ErrorField);
        }
    }

    private static void AssertScopedRegistration<TService, TImplementation>(
        IServiceCollection services)
    {
        var descriptor = Assert.Single(
            services,
            candidate => candidate.ServiceType == typeof(TService));
        Assert.Equal(ServiceLifetime.Scoped, descriptor.Lifetime);
        Assert.Equal(typeof(TImplementation), descriptor.ImplementationType);
    }

    private static Task<long> CountRowsAsync(
        PostgreSqlTestDatabase database,
        string tableName)
    {
        return database.ExecuteScalarAsync<long>(
            $"select count(*) from bodylife.{tableName}");
    }

    private sealed record MarkVisitOptionsFixture(
        ActorContext Actor,
        Guid ClientId,
        Guid MembershipTypeId);

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }
}
