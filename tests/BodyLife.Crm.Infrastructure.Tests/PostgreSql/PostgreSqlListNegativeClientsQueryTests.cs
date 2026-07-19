using BodyLife.Crm.Application.Queries;
using BodyLife.Crm.Infrastructure;
using BodyLife.Crm.Infrastructure.Persistence;
using BodyLife.Crm.Infrastructure.Persistence.Memberships;
using BodyLife.Crm.Infrastructure.Persistence.Reports;
using BodyLife.Crm.Infrastructure.Persistence.UsersRoles;
using BodyLife.Crm.Modules.Memberships;
using BodyLife.Crm.Modules.Reports;
using BodyLife.Crm.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using NpgsqlTypes;

namespace BodyLife.Crm.Infrastructure.Tests.PostgreSql;

public sealed class PostgreSqlListNegativeClientsQueryTests
{
    private static readonly DateOnly AsOfDate = new(2026, 7, 20);
    private static readonly DateTimeOffset TestNow = new(
        2026,
        7,
        20,
        18,
        0,
        0,
        TimeSpan.Zero);

    [PostgreSqlFact]
    public async Task QueryUsesCanonicalNegativeStateWithStableOrderingAndPagination()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var dbContext = database.CreateDbContext();
        await dbContext.Database.MigrateAsync();
        var fixture = await SeedFixtureAsync(database, dbContext);
        var severeVisitId = Guid.NewGuid();
        var severe = await InsertMembershipAsync(
            database,
            fixture,
            "Severe",
            remainingVisits: -3,
            effectiveEndDate: AsOfDate.AddDays(-1),
            firstNegativeVisitId: severeVisitId,
            firstNegativeVisitDate: AsOfDate.AddDays(-4));
        var alphaVisitId = Guid.NewGuid();
        var alpha = await InsertMembershipAsync(
            database,
            fixture,
            "Alpha",
            remainingVisits: -1,
            effectiveEndDate: AsOfDate.AddDays(20),
            firstNegativeVisitId: alphaVisitId,
            firstNegativeVisitDate: AsOfDate.AddDays(-2));
        var zulu = await InsertMembershipAsync(
            database,
            fixture,
            "Zulu",
            remainingVisits: -1,
            effectiveEndDate: AsOfDate.AddDays(20),
            firstNegativeVisitId: Guid.NewGuid(),
            firstNegativeVisitDate: AsOfDate.AddDays(-2));
        var opening = await InsertMembershipAsync(
            database,
            fixture,
            "Opening",
            remainingVisits: -1,
            effectiveEndDate: AsOfDate.AddDays(20),
            firstNegativeVisitId: null,
            firstNegativeVisitDate: null);
        await InsertMembershipAsync(
            database,
            fixture,
            "Zero",
            remainingVisits: 0,
            effectiveEndDate: AsOfDate.AddDays(20),
            firstNegativeVisitId: null,
            firstNegativeVisitDate: null);
        await InsertMembershipAsync(
            database,
            fixture,
            "Canceled",
            remainingVisits: -2,
            effectiveEndDate: AsOfDate.AddDays(20),
            firstNegativeVisitId: Guid.NewGuid(),
            firstNegativeVisitDate: AsOfDate.AddDays(-3),
            status: "canceled");
        dbContext.ChangeTracker.Clear();
        var reportHandler = CreateHandler(dbContext);

        var fullResult = await reportHandler.ExecuteAsync(
            new ListNegativeClientsQuery(
                fixture.Actor,
                AsOfDate,
                Limit: GetNegativeMembershipStateRowsQuery.MaxLimit),
            CancellationToken.None);

        AssertSuccessful(fullResult);
        var fullPage = fullResult.Page!;
        Assert.Equal(
            [
                severe.MembershipId,
                alpha.MembershipId,
                zulu.MembershipId,
                opening.MembershipId,
            ],
            fullPage.Items.Select(row => row.MembershipId));
        Assert.Equal(
            ["Severe Client", "Alpha Client", "Zulu Client", "Opening Client"],
            fullPage.Items.Select(row => row.ClientDisplayName));
        Assert.Equal([3, 1, 1, 1], fullPage.Items.Select(row => row.NegativeBalance));
        Assert.Equal([-3, -1, -1, -1], fullPage.Items.Select(row => row.RemainingVisits));
        Assert.Equal(severeVisitId, fullPage.Items[0].FirstNegativeVisitId);
        Assert.Equal(AsOfDate.AddDays(-4), fullPage.Items[0].FirstNegativeVisitDate);
        Assert.Null(fullPage.Items[3].FirstNegativeVisitId);
        Assert.Null(fullPage.Items[3].FirstNegativeVisitDate);
        Assert.Equal(
            [MembershipWarningCodes.NegativeBalance, MembershipWarningCodes.ExpiredByDate],
            fullPage.Items[0].Warnings.Select(warning => warning.Code));
        Assert.All(fullPage.Items, row =>
            Assert.Equal(AsOfDate, row.MembershipState.AsOfDate));
        Assert.False(fullPage.HasMore);
        Assert.Null(fullPage.NextOffset);

        var firstPageResult = await reportHandler.ExecuteAsync(
            new ListNegativeClientsQuery(fixture.Actor, AsOfDate, Limit: 2),
            CancellationToken.None);
        AssertSuccessful(firstPageResult);
        Assert.Equal(
            [severe.MembershipId, alpha.MembershipId],
            firstPageResult.Page!.Items.Select(row => row.MembershipId));
        Assert.True(firstPageResult.Page.HasMore);
        Assert.Equal(2, firstPageResult.Page.NextOffset);

        var secondPageResult = await reportHandler.ExecuteAsync(
            new ListNegativeClientsQuery(
                fixture.Actor,
                AsOfDate,
                Limit: 2,
                Offset: 2),
            CancellationToken.None);
        AssertSuccessful(secondPageResult);
        Assert.Equal(
            [zulu.MembershipId, opening.MembershipId],
            secondPageResult.Page!.Items.Select(row => row.MembershipId));
        Assert.False(secondPageResult.Page.HasMore);
        Assert.Null(secondPageResult.Page.NextOffset);

        var alphaRow = fullPage.Items[1];
        var canonicalState = await new GetMembershipStateQueryHandler(
                dbContext,
                new FixedTimeProvider(TestNow))
            .ExecuteAsync(
                new GetMembershipStateQuery(
                    fixture.Actor,
                    alpha.MembershipId,
                    AsOfDate),
                CancellationToken.None);
        Assert.Equal(GetMembershipStateStatus.Success, canonicalState.Status);
        Assert.NotNull(canonicalState.State);
        Assert.Equal(canonicalState.State.NegativeBalance, alphaRow.NegativeBalance);
        Assert.Equal(canonicalState.State.RemainingVisits, alphaRow.RemainingVisits);
        Assert.Equal(canonicalState.State.FirstNegativeVisitId, alphaRow.FirstNegativeVisitId);
        Assert.Equal(canonicalState.State.FirstNegativeVisitDate, alphaRow.FirstNegativeVisitDate);
        Assert.Equal(canonicalState.State.EffectiveEndDate, alphaRow.EffectiveEndDate);
        Assert.Equal(canonicalState.State.LastCountedVisitAt, alphaRow.LastCountedVisitAt);
        Assert.Equal(
            canonicalState.State.Warnings.Select(warning => warning.Code),
            alphaRow.Warnings.Select(warning => warning.Code));
        Assert.Equal(alphaVisitId, alphaRow.FirstNegativeVisitId);
        Assert.Contains(
            "ix_membership_state_cache_negative_balance_open",
            await ReadNegativeBalanceQueryPlanAsync(database),
            StringComparison.Ordinal);
        Assert.Empty(dbContext.ChangeTracker.Entries());
        Assert.Equal(
            0L,
            await database.ExecuteScalarAsync<long>(
                "select count(*) from bodylife.business_audit_entries"));
    }

    [PostgreSqlFact]
    public async Task AuthorizationPrecedesValidationAndInvalidSelectorsCarryNoPage()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var dbContext = database.CreateDbContext();
        await dbContext.Database.MigrateAsync();
        var fixture = await SeedFixtureAsync(database, dbContext);
        dbContext.ChangeTracker.Clear();
        var handler = CreateHandler(dbContext);
        var unknownActor = new ActorContext(
            new AccountId(Guid.NewGuid()),
            ActorRole.Owner,
            AccountKind.Owner,
            new SessionId(Guid.NewGuid()),
            "Unknown reports tablet");

        var denied = await handler.ExecuteAsync(
            new ListNegativeClientsQuery(unknownActor, AsOfDate: default, Limit: 0),
            CancellationToken.None);
        var invalidDate = await handler.ExecuteAsync(
            new ListNegativeClientsQuery(fixture.Actor, AsOfDate: default),
            CancellationToken.None);
        var invalidLimit = await handler.ExecuteAsync(
            new ListNegativeClientsQuery(fixture.Actor, AsOfDate, Limit: 0),
            CancellationToken.None);
        var invalidOffset = await handler.ExecuteAsync(
            new ListNegativeClientsQuery(fixture.Actor, AsOfDate, Offset: -1),
            CancellationToken.None);

        AssertFailure(denied, ListNegativeClientsStatus.PermissionDenied);
        AssertFailure(
            invalidDate,
            ListNegativeClientsStatus.ValidationFailed,
            "asOfDate");
        AssertFailure(
            invalidLimit,
            ListNegativeClientsStatus.ValidationFailed,
            "limit");
        AssertFailure(
            invalidOffset,
            ListNegativeClientsStatus.ValidationFailed,
            "offset");
    }

    [PostgreSqlFact]
    public async Task MissingAndStaleActiveCachesFailClosedWithoutReadRepair()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var dbContext = database.CreateDbContext();
        await dbContext.Database.MigrateAsync();
        var fixture = await SeedFixtureAsync(database, dbContext);
        var activeMembership = await InsertMembershipAsync(
            database,
            fixture,
            "Unavailable",
            remainingVisits: 3,
            effectiveEndDate: AsOfDate.AddDays(20),
            firstNegativeVisitId: null,
            firstNegativeVisitDate: null,
            includeCache: false);
        await InsertMembershipAsync(
            database,
            fixture,
            "Canceled unavailable",
            remainingVisits: -1,
            effectiveEndDate: AsOfDate.AddDays(20),
            firstNegativeVisitId: Guid.NewGuid(),
            firstNegativeVisitDate: AsOfDate.AddDays(-1),
            status: "canceled",
            includeCache: false);
        dbContext.ChangeTracker.Clear();
        var handler = CreateHandler(dbContext);

        var missing = await handler.ExecuteAsync(
            new ListNegativeClientsQuery(fixture.Actor, AsOfDate),
            CancellationToken.None);

        AssertFailure(missing, ListNegativeClientsStatus.RecalculationFailed);
        Assert.Equal(
            0L,
            await database.ExecuteScalarAsync<long>(
                "select count(*) from bodylife.membership_state_cache"));

        await InsertCacheAsync(database, activeMembership);
        var available = await handler.ExecuteAsync(
            new ListNegativeClientsQuery(fixture.Actor, AsOfDate),
            CancellationToken.None);
        AssertSuccessful(available);
        Assert.Empty(available.Page!.Items);

        await SetCacheVersionAsync(
            database,
            activeMembership.MembershipId,
            MembershipStateCacheRebuilder.CurrentRecalculationVersion - 1);
        var stale = await handler.ExecuteAsync(
            new ListNegativeClientsQuery(fixture.Actor, AsOfDate),
            CancellationToken.None);

        AssertFailure(stale, ListNegativeClientsStatus.RecalculationFailed);
        Assert.Equal(
            MembershipStateCacheRebuilder.CurrentRecalculationVersion - 1,
            await database.ExecuteScalarAsync<int>(
                "select recalculation_version from bodylife.membership_state_cache"));
        Assert.Equal(
            0L,
            await database.ExecuteScalarAsync<long>(
                "select count(*) from bodylife.business_audit_entries"));
    }

    [Fact]
    public async Task SourceFailuresNeverReturnPartialReportRows()
    {
        var actor = new ActorContext(
            new AccountId(Guid.NewGuid()),
            ActorRole.Owner,
            AccountKind.Owner,
            new SessionId(Guid.NewGuid()),
            "Reports tablet");
        var scenarios = new[]
        {
            (
                Source: GetNegativeMembershipStateRowsResult.Denied(),
                Expected: ListNegativeClientsStatus.PermissionDenied,
                Field: (string?)null),
            (
                Source: GetNegativeMembershipStateRowsResult.Invalid(
                    "Limit is invalid.",
                    "limit"),
                Expected: ListNegativeClientsStatus.ValidationFailed,
                Field: "limit"),
            (
                Source: GetNegativeMembershipStateRowsResult.RecalculationFailed(),
                Expected: ListNegativeClientsStatus.RecalculationFailed,
                Field: (string?)null),
            (
                Source: GetNegativeMembershipStateRowsResult.InconsistentSource(),
                Expected: ListNegativeClientsStatus.SourceInconsistent,
                Field: (string?)null),
        };

        foreach (var scenario in scenarios)
        {
            var result = await new ListNegativeClientsQueryHandler(
                    new StubSourceHandler(scenario.Source))
                .ExecuteAsync(
                    new ListNegativeClientsQuery(actor, AsOfDate),
                    CancellationToken.None);

            AssertFailure(result, scenario.Expected, scenario.Field);
        }
    }

    [Fact]
    public void PersistenceRegistrationExposesScopedSourceAndReportHandlers()
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
            GetNegativeMembershipStateRowsQuery,
            GetNegativeMembershipStateRowsResult,
            GetNegativeMembershipStateRowsQueryHandler>(services);
        AssertScopedRegistration<
            ListNegativeClientsQuery,
            ListNegativeClientsResult,
            ListNegativeClientsQueryHandler>(services);
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<
            IBodyLifeQueryHandler<
                GetNegativeMembershipStateRowsQuery,
                GetNegativeMembershipStateRowsResult>>());
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<
            IBodyLifeQueryHandler<ListNegativeClientsQuery, ListNegativeClientsResult>>());
    }

    private static ListNegativeClientsQueryHandler CreateHandler(
        BodyLifeDbContext dbContext)
    {
        return new ListNegativeClientsQueryHandler(
            new GetNegativeMembershipStateRowsQueryHandler(
                dbContext,
                new FixedTimeProvider(TestNow)));
    }

    private static async Task<NegativeClientsFixture> SeedFixtureAsync(
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
                'Reports tablet',
                @started_at,
                @expires_at,
                null,
                @last_seen_at);

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
                @created_at,
                @created_at,
                null);
            """;
        command.Parameters.AddWithValue("session_id", sessionId);
        command.Parameters.AddWithValue("account_id", accountId);
        command.Parameters.AddWithValue("started_at", TestNow.AddMinutes(-5));
        command.Parameters.AddWithValue("expires_at", TestNow.AddHours(8));
        command.Parameters.AddWithValue("last_seen_at", TestNow);
        command.Parameters.AddWithValue("membership_type_id", membershipTypeId);
        command.Parameters.AddWithValue("created_at", TestNow.AddDays(-30));
        await command.ExecuteNonQueryAsync();

        return new NegativeClientsFixture(
            new ActorContext(
                new AccountId(accountId),
                ActorRole.Owner,
                AccountKind.Owner,
                new SessionId(sessionId),
                "Reports tablet"),
            accountId,
            membershipTypeId);
    }

    private static async Task<SeededMembership> InsertMembershipAsync(
        PostgreSqlTestDatabase database,
        NegativeClientsFixture fixture,
        string surname,
        int remainingVisits,
        DateOnly effectiveEndDate,
        Guid? firstNegativeVisitId,
        DateOnly? firstNegativeVisitDate,
        string status = "active",
        bool includeCache = true)
    {
        const int durationDays = 30;
        const int visitsLimit = 8;
        var clientId = Guid.NewGuid();
        var membershipId = Guid.NewGuid();
        var membership = new SeededMembership(
            membershipId,
            effectiveEndDate,
            remainingVisits,
            firstNegativeVisitId,
            firstNegativeVisitDate);
        await using var connection = new NpgsqlConnection(database.ConnectionString);
        await connection.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
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
                @surname,
                'Client',
                null,
                @normalized_full_name,
                '+380 67 123 4567',
                '380671234567',
                '4567',
                null,
                'active',
                @recorded_at,
                @account_id,
                @recorded_at);

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
                @effective_end_date,
                @recorded_at,
                @account_id,
                @status,
                'normal',
                null,
                null);

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
            select
                @membership_id,
                @counted_visits,
                @remaining_visits,
                @negative_balance,
                @first_negative_visit_id,
                @first_negative_visit_date,
                0,
                @effective_end_date,
                @last_counted_visit_at,
                @recalculated_at,
                @recalculation_version
            where @include_cache;
            """;
        command.Parameters.AddWithValue("client_id", clientId);
        command.Parameters.AddWithValue("surname", surname);
        command.Parameters.AddWithValue(
            "normalized_full_name",
            $"{surname.ToUpperInvariant()} CLIENT");
        command.Parameters.AddWithValue("account_id", fixture.AccountId);
        command.Parameters.AddWithValue("membership_id", membershipId);
        command.Parameters.AddWithValue("membership_type_id", fixture.MembershipTypeId);
        command.Parameters.AddWithValue(
            "start_date",
            NpgsqlDbType.Date,
            effectiveEndDate.AddDays(-(durationDays - 1)));
        command.Parameters.AddWithValue(
            "effective_end_date",
            NpgsqlDbType.Date,
            effectiveEndDate);
        command.Parameters.AddWithValue("recorded_at", TestNow.AddDays(-30));
        command.Parameters.AddWithValue("status", status);
        command.Parameters.AddWithValue("counted_visits", visitsLimit - remainingVisits);
        command.Parameters.AddWithValue("remaining_visits", remainingVisits);
        command.Parameters.AddWithValue("negative_balance", Math.Max(0, -remainingVisits));
        AddNullableParameter(
            command,
            "first_negative_visit_id",
            NpgsqlDbType.Uuid,
            firstNegativeVisitId);
        AddNullableParameter(
            command,
            "first_negative_visit_date",
            NpgsqlDbType.Date,
            firstNegativeVisitDate);
        command.Parameters.AddWithValue("last_counted_visit_at", TestNow.AddDays(-2));
        command.Parameters.AddWithValue("recalculated_at", TestNow);
        command.Parameters.AddWithValue(
            "recalculation_version",
            MembershipStateCacheRebuilder.CurrentRecalculationVersion);
        command.Parameters.AddWithValue("include_cache", includeCache);
        await command.ExecuteNonQueryAsync();
        await transaction.CommitAsync();
        return membership;
    }

    private static async Task InsertCacheAsync(
        PostgreSqlTestDatabase database,
        SeededMembership membership)
    {
        const int visitsLimit = 8;
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
                @first_negative_visit_id,
                @first_negative_visit_date,
                0,
                @effective_end_date,
                @last_counted_visit_at,
                @recalculated_at,
                @recalculation_version);
            """;
        command.Parameters.AddWithValue("membership_id", membership.MembershipId);
        command.Parameters.AddWithValue(
            "counted_visits",
            visitsLimit - membership.RemainingVisits);
        command.Parameters.AddWithValue("remaining_visits", membership.RemainingVisits);
        command.Parameters.AddWithValue(
            "negative_balance",
            Math.Max(0, -membership.RemainingVisits));
        AddNullableParameter(
            command,
            "first_negative_visit_id",
            NpgsqlDbType.Uuid,
            membership.FirstNegativeVisitId);
        AddNullableParameter(
            command,
            "first_negative_visit_date",
            NpgsqlDbType.Date,
            membership.FirstNegativeVisitDate);
        command.Parameters.AddWithValue(
            "effective_end_date",
            NpgsqlDbType.Date,
            membership.EffectiveEndDate);
        command.Parameters.AddWithValue("last_counted_visit_at", TestNow.AddDays(-2));
        command.Parameters.AddWithValue("recalculated_at", TestNow);
        command.Parameters.AddWithValue(
            "recalculation_version",
            MembershipStateCacheRebuilder.CurrentRecalculationVersion);
        await command.ExecuteNonQueryAsync();
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

    private static async Task<string> ReadNegativeBalanceQueryPlanAsync(
        PostgreSqlTestDatabase database)
    {
        await using var connection = new NpgsqlConnection(database.ConnectionString);
        await connection.OpenAsync();
        await using (var settingsCommand = connection.CreateCommand())
        {
            settingsCommand.CommandText = "set enable_seqscan = off";
            await settingsCommand.ExecuteNonQueryAsync();
        }

        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            explain (costs off)
            select membership_id
            from bodylife.membership_state_cache
            where negative_balance > 0
            order by negative_balance desc
            """;
        var planLines = new List<string>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            planLines.Add(reader.GetString(0));
        }

        return string.Join(Environment.NewLine, planLines);
    }

    private static void AddNullableParameter<T>(
        NpgsqlCommand command,
        string name,
        NpgsqlDbType type,
        T? value)
        where T : struct
    {
        var parameter = command.Parameters.Add(name, type);
        parameter.Value = value.HasValue ? value.Value : DBNull.Value;
    }

    private static void AssertSuccessful(ListNegativeClientsResult result)
    {
        Assert.Equal(ListNegativeClientsStatus.Success, result.Status);
        Assert.NotNull(result.Page);
        Assert.Null(result.ErrorCode);
        Assert.Null(result.ErrorMessage);
        Assert.Null(result.ErrorField);
    }

    private static void AssertFailure(
        ListNegativeClientsResult result,
        ListNegativeClientsStatus expectedStatus,
        string? expectedField = null)
    {
        Assert.Equal(expectedStatus, result.Status);
        Assert.Null(result.Page);
        Assert.NotNull(result.ErrorCode);
        Assert.NotNull(result.ErrorMessage);
        Assert.Equal(expectedField, result.ErrorField);
    }

    private static void AssertScopedRegistration<TQuery, TResult, THandler>(
        IServiceCollection services)
        where TQuery : IBodyLifeQuery<TResult>
    {
        var descriptor = Assert.Single(
            services,
            candidate => candidate.ServiceType == typeof(
                IBodyLifeQueryHandler<TQuery, TResult>));
        Assert.Equal(ServiceLifetime.Scoped, descriptor.Lifetime);
        Assert.Equal(typeof(THandler), descriptor.ImplementationType);
    }

    private sealed record NegativeClientsFixture(
        ActorContext Actor,
        Guid AccountId,
        Guid MembershipTypeId);

    private sealed record SeededMembership(
        Guid MembershipId,
        DateOnly EffectiveEndDate,
        int RemainingVisits,
        Guid? FirstNegativeVisitId,
        DateOnly? FirstNegativeVisitDate);

    private sealed class StubSourceHandler(
        GetNegativeMembershipStateRowsResult result)
        : IBodyLifeQueryHandler<
            GetNegativeMembershipStateRowsQuery,
            GetNegativeMembershipStateRowsResult>
    {
        public Task<GetNegativeMembershipStateRowsResult> ExecuteAsync(
            GetNegativeMembershipStateRowsQuery query,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(result);
        }
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow()
        {
            return utcNow;
        }
    }
}
