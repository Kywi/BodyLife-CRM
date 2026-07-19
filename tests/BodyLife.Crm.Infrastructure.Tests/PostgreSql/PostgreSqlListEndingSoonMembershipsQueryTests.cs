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

public sealed class PostgreSqlListEndingSoonMembershipsQueryTests
{
    private static readonly DateOnly AsOfDate = new(2026, 7, 19);
    private static readonly DateTimeOffset TestNow = new(
        2026,
        7,
        19,
        18,
        0,
        0,
        TimeSpan.Zero);

    [PostgreSqlFact]
    public async Task QueryUsesCanonicalStateWithStableFilteringOrderingAndPagination()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var dbContext = database.CreateDbContext();
        await dbContext.Database.MigrateAsync();
        var fixture = await SeedFixtureAsync(database, dbContext);
        var today = await InsertMembershipAsync(
            database,
            fixture,
            "Today",
            AsOfDate);
        var alpha = await InsertMembershipAsync(
            database,
            fixture,
            "Alpha",
            AsOfDate.AddDays(3),
            remainingVisits: 1,
            extensionDays: 1);
        var zulu = await InsertMembershipAsync(
            database,
            fixture,
            "Zulu",
            AsOfDate.AddDays(3));
        var week = await InsertMembershipAsync(
            database,
            fixture,
            "Week",
            AsOfDate.AddDays(7));
        await InsertMembershipAsync(
            database,
            fixture,
            "Outside",
            AsOfDate.AddDays(8));
        await InsertMembershipAsync(
            database,
            fixture,
            "Expired",
            AsOfDate.AddDays(-1));
        await InsertMembershipAsync(
            database,
            fixture,
            "Canceled",
            AsOfDate.AddDays(2),
            status: "canceled");
        dbContext.ChangeTracker.Clear();
        var reportHandler = CreateHandler(dbContext);

        var fullResult = await reportHandler.ExecuteAsync(
            new ListEndingSoonMembershipsQuery(
                fixture.Actor,
                AsOfDate,
                Limit: GetEndingSoonMembershipStateRowsQuery.MaxLimit),
            CancellationToken.None);

        AssertSuccessful(fullResult);
        var fullPage = fullResult.Page!;
        Assert.Equal([today.MembershipId, alpha.MembershipId, zulu.MembershipId, week.MembershipId],
            fullPage.Items.Select(row => row.MembershipId));
        Assert.Equal(
            ["Today Client", "Alpha Client", "Zulu Client", "Week Client"],
            fullPage.Items.Select(row => row.ClientDisplayName));
        Assert.Equal([0, 3, 3, 7], fullPage.Items.Select(row => row.DaysLeft));
        Assert.All(fullPage.Items, row => Assert.Contains(
            row.Warnings,
            warning => warning.Code == MembershipWarningCodes.EndingSoon));
        var alphaRow = fullPage.Items[1];
        Assert.Equal(1, alphaRow.RemainingVisits);
        Assert.True(alphaRow.HasExtensionExplanation);
        Assert.Equal(
            [MembershipWarningCodes.LowRemaining, MembershipWarningCodes.EndingSoon],
            alphaRow.Warnings.Select(warning => warning.Code));
        Assert.False(fullPage.HasMore);
        Assert.Null(fullPage.NextOffset);

        var narrowResult = await reportHandler.ExecuteAsync(
            new ListEndingSoonMembershipsQuery(
                fixture.Actor,
                AsOfDate,
                DaysThreshold: 3),
            CancellationToken.None);
        AssertSuccessful(narrowResult);
        Assert.Equal(
            [today.MembershipId, alpha.MembershipId, zulu.MembershipId],
            narrowResult.Page!.Items.Select(row => row.MembershipId));

        var firstPageResult = await reportHandler.ExecuteAsync(
            new ListEndingSoonMembershipsQuery(
                fixture.Actor,
                AsOfDate,
                Limit: 2),
            CancellationToken.None);
        AssertSuccessful(firstPageResult);
        Assert.Equal(
            [today.MembershipId, alpha.MembershipId],
            firstPageResult.Page!.Items.Select(row => row.MembershipId));
        Assert.True(firstPageResult.Page.HasMore);
        Assert.Equal(2, firstPageResult.Page.NextOffset);

        var secondPageResult = await reportHandler.ExecuteAsync(
            new ListEndingSoonMembershipsQuery(
                fixture.Actor,
                AsOfDate,
                Limit: 2,
                Offset: 2),
            CancellationToken.None);
        AssertSuccessful(secondPageResult);
        Assert.Equal(
            [zulu.MembershipId, week.MembershipId],
            secondPageResult.Page!.Items.Select(row => row.MembershipId));
        Assert.False(secondPageResult.Page.HasMore);
        Assert.Null(secondPageResult.Page.NextOffset);

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
        Assert.Equal(canonicalState.State.EffectiveEndDate, alphaRow.EffectiveEndDate);
        Assert.Equal(canonicalState.State.RemainingVisits, alphaRow.RemainingVisits);
        Assert.Equal(canonicalState.State.ExtensionDays, alphaRow.MembershipState.ExtensionDays);
        Assert.Equal(
            canonicalState.State.Warnings.Select(warning => warning.Code),
            alphaRow.Warnings.Select(warning => warning.Code));
        Assert.Equal(
            canonicalState.State.ExtensionExplanation.Select(day => day.ExtensionDate),
            alphaRow.MembershipState.ExtensionExplanation.Select(day => day.ExtensionDate));
        Assert.Contains(
            "ix_membership_state_cache_effective_end_date",
            await ReadEffectiveEndDateQueryPlanAsync(database),
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
            new ListEndingSoonMembershipsQuery(
                unknownActor,
                AsOfDate,
                DaysThreshold: -1),
            CancellationToken.None);
        var invalidThreshold = await handler.ExecuteAsync(
            new ListEndingSoonMembershipsQuery(
                fixture.Actor,
                AsOfDate,
                DaysThreshold: GetEndingSoonMembershipStateRowsQuery.MaxDaysThreshold + 1),
            CancellationToken.None);
        var invalidLimit = await handler.ExecuteAsync(
            new ListEndingSoonMembershipsQuery(
                fixture.Actor,
                AsOfDate,
                Limit: 0),
            CancellationToken.None);
        var invalidOffset = await handler.ExecuteAsync(
            new ListEndingSoonMembershipsQuery(
                fixture.Actor,
                AsOfDate,
                Offset: -1),
            CancellationToken.None);
        var calendarOverflow = await handler.ExecuteAsync(
            new ListEndingSoonMembershipsQuery(
                fixture.Actor,
                DateOnly.MaxValue,
                DaysThreshold: 1),
            CancellationToken.None);

        AssertFailure(denied, ListEndingSoonMembershipsStatus.PermissionDenied);
        AssertFailure(
            invalidThreshold,
            ListEndingSoonMembershipsStatus.ValidationFailed,
            "daysThreshold");
        AssertFailure(
            invalidLimit,
            ListEndingSoonMembershipsStatus.ValidationFailed,
            "limit");
        AssertFailure(
            invalidOffset,
            ListEndingSoonMembershipsStatus.ValidationFailed,
            "offset");
        AssertFailure(
            calendarOverflow,
            ListEndingSoonMembershipsStatus.ValidationFailed,
            "asOfDate");
    }

    [PostgreSqlFact]
    public async Task MissingAndStaleCandidateCachesFailClosedWithoutReadRepair()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var dbContext = database.CreateDbContext();
        await dbContext.Database.MigrateAsync();
        var fixture = await SeedFixtureAsync(database, dbContext);
        var membership = await InsertMembershipAsync(
            database,
            fixture,
            "Unavailable",
            AsOfDate.AddDays(3),
            includeCache: false);
        dbContext.ChangeTracker.Clear();
        var handler = CreateHandler(dbContext);

        var missing = await handler.ExecuteAsync(
            new ListEndingSoonMembershipsQuery(fixture.Actor, AsOfDate),
            CancellationToken.None);

        AssertFailure(
            missing,
            ListEndingSoonMembershipsStatus.RecalculationFailed);
        Assert.Equal(
            0L,
            await database.ExecuteScalarAsync<long>(
                "select count(*) from bodylife.membership_state_cache"));

        await InsertCacheAsync(database, membership);
        dbContext.ChangeTracker.Clear();
        var available = await handler.ExecuteAsync(
            new ListEndingSoonMembershipsQuery(fixture.Actor, AsOfDate),
            CancellationToken.None);
        AssertSuccessful(available);
        Assert.Equal(membership.MembershipId, Assert.Single(available.Page!.Items).MembershipId);

        await SetCacheVersionAsync(
            database,
            membership.MembershipId,
            MembershipStateCacheRebuilder.CurrentRecalculationVersion - 1);
        var stale = await handler.ExecuteAsync(
            new ListEndingSoonMembershipsQuery(fixture.Actor, AsOfDate),
            CancellationToken.None);

        AssertFailure(
            stale,
            ListEndingSoonMembershipsStatus.RecalculationFailed);
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
                Source: GetEndingSoonMembershipStateRowsResult.Denied(),
                Expected: ListEndingSoonMembershipsStatus.PermissionDenied,
                Field: (string?)null),
            (
                Source: GetEndingSoonMembershipStateRowsResult.Invalid(
                    "Limit is invalid.",
                    "limit"),
                Expected: ListEndingSoonMembershipsStatus.ValidationFailed,
                Field: "limit"),
            (
                Source: GetEndingSoonMembershipStateRowsResult.RecalculationFailed(),
                Expected: ListEndingSoonMembershipsStatus.RecalculationFailed,
                Field: (string?)null),
            (
                Source: GetEndingSoonMembershipStateRowsResult.InconsistentSource(),
                Expected: ListEndingSoonMembershipsStatus.SourceInconsistent,
                Field: (string?)null),
        };

        foreach (var scenario in scenarios)
        {
            var result = await new ListEndingSoonMembershipsQueryHandler(
                    new StubSourceHandler(scenario.Source))
                .ExecuteAsync(
                    new ListEndingSoonMembershipsQuery(actor, AsOfDate),
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
            GetEndingSoonMembershipStateRowsQuery,
            GetEndingSoonMembershipStateRowsResult,
            GetEndingSoonMembershipStateRowsQueryHandler>(services);
        AssertScopedRegistration<
            ListEndingSoonMembershipsQuery,
            ListEndingSoonMembershipsResult,
            ListEndingSoonMembershipsQueryHandler>(services);
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<
            IBodyLifeQueryHandler<
                GetEndingSoonMembershipStateRowsQuery,
                GetEndingSoonMembershipStateRowsResult>>());
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<
            IBodyLifeQueryHandler<
                ListEndingSoonMembershipsQuery,
                ListEndingSoonMembershipsResult>>());
    }

    private static ListEndingSoonMembershipsQueryHandler CreateHandler(
        BodyLifeDbContext dbContext)
    {
        return new ListEndingSoonMembershipsQueryHandler(
            new GetEndingSoonMembershipStateRowsQueryHandler(
                dbContext,
                new FixedTimeProvider(TestNow)));
    }

    private static async Task<EndingSoonFixture> SeedFixtureAsync(
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

        return new EndingSoonFixture(
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
        EndingSoonFixture fixture,
        string surname,
        DateOnly effectiveEndDate,
        int remainingVisits = 2,
        int extensionDays = 0,
        string status = "active",
        bool includeCache = true)
    {
        const int durationDays = 30;
        const int visitsLimit = 8;
        var clientId = Guid.NewGuid();
        var membershipId = Guid.NewGuid();
        var baseEndDate = effectiveEndDate.AddDays(-extensionDays);
        var membership = new SeededMembership(
            membershipId,
            baseEndDate,
            effectiveEndDate,
            remainingVisits,
            extensionDays);
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
                @base_end_date,
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
                0,
                null,
                null,
                @extension_days,
                @effective_end_date,
                null,
                @recorded_at,
                @recalculation_version
            where @include_cache;

            insert into bodylife.membership_extension_days (
                id,
                membership_id,
                extension_date,
                source_type,
                source_id,
                source_label,
                is_active,
                recalculated_at)
            select
                @extension_id,
                @membership_id,
                @base_end_date,
                'freeze',
                @extension_source_id,
                'Report fixture freeze',
                true,
                @recorded_at
            where @extension_days = 1;
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
            baseEndDate.AddDays(-(durationDays - 1)));
        command.Parameters.AddWithValue("base_end_date", NpgsqlDbType.Date, baseEndDate);
        command.Parameters.AddWithValue("recorded_at", TestNow.AddDays(-30));
        command.Parameters.AddWithValue("status", status);
        command.Parameters.AddWithValue("counted_visits", visitsLimit - remainingVisits);
        command.Parameters.AddWithValue("remaining_visits", remainingVisits);
        command.Parameters.AddWithValue("extension_days", extensionDays);
        command.Parameters.AddWithValue(
            "effective_end_date",
            NpgsqlDbType.Date,
            effectiveEndDate);
        command.Parameters.AddWithValue(
            "recalculation_version",
            MembershipStateCacheRebuilder.CurrentRecalculationVersion);
        command.Parameters.AddWithValue("include_cache", includeCache);
        command.Parameters.AddWithValue("extension_id", Guid.NewGuid());
        command.Parameters.AddWithValue("extension_source_id", Guid.NewGuid());
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
                0,
                null,
                null,
                @extension_days,
                @effective_end_date,
                null,
                @recalculated_at,
                @recalculation_version);
            """;
        command.Parameters.AddWithValue("membership_id", membership.MembershipId);
        command.Parameters.AddWithValue(
            "counted_visits",
            visitsLimit - membership.RemainingVisits);
        command.Parameters.AddWithValue("remaining_visits", membership.RemainingVisits);
        command.Parameters.AddWithValue("extension_days", membership.ExtensionDays);
        command.Parameters.AddWithValue(
            "effective_end_date",
            NpgsqlDbType.Date,
            membership.EffectiveEndDate);
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

    private static async Task<string> ReadEffectiveEndDateQueryPlanAsync(
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
            where effective_end_date >= @as_of_date
              and effective_end_date <= @through_date
            order by effective_end_date
            """;
        command.Parameters.AddWithValue(
            "as_of_date",
            NpgsqlDbType.Date,
            AsOfDate);
        command.Parameters.AddWithValue(
            "through_date",
            NpgsqlDbType.Date,
            AsOfDate.AddDays(MembershipWarningRules.EndingSoonDaysThreshold));
        var planLines = new List<string>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            planLines.Add(reader.GetString(0));
        }

        return string.Join(Environment.NewLine, planLines);
    }

    private static void AssertSuccessful(ListEndingSoonMembershipsResult result)
    {
        Assert.Equal(ListEndingSoonMembershipsStatus.Success, result.Status);
        Assert.NotNull(result.Page);
        Assert.Null(result.ErrorCode);
        Assert.Null(result.ErrorMessage);
        Assert.Null(result.ErrorField);
    }

    private static void AssertFailure(
        ListEndingSoonMembershipsResult result,
        ListEndingSoonMembershipsStatus expectedStatus,
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

    private sealed record EndingSoonFixture(
        ActorContext Actor,
        Guid AccountId,
        Guid MembershipTypeId);

    private sealed record SeededMembership(
        Guid MembershipId,
        DateOnly BaseEndDate,
        DateOnly EffectiveEndDate,
        int RemainingVisits,
        int ExtensionDays);

    private sealed class StubSourceHandler(
        GetEndingSoonMembershipStateRowsResult result)
        : IBodyLifeQueryHandler<
            GetEndingSoonMembershipStateRowsQuery,
            GetEndingSoonMembershipStateRowsResult>
    {
        public Task<GetEndingSoonMembershipStateRowsResult> ExecuteAsync(
            GetEndingSoonMembershipStateRowsQuery query,
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
