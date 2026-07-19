using BodyLife.Crm.Application.Queries;
using BodyLife.Crm.Infrastructure;
using BodyLife.Crm.Infrastructure.Persistence;
using BodyLife.Crm.Infrastructure.Persistence.Memberships;
using BodyLife.Crm.Infrastructure.Persistence.Reports;
using BodyLife.Crm.Infrastructure.Persistence.UsersRoles;
using BodyLife.Crm.Modules.Clients.Search;
using BodyLife.Crm.Modules.Memberships;
using BodyLife.Crm.Modules.Reports;
using BodyLife.Crm.Modules.Visits;
using BodyLife.Crm.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using NpgsqlTypes;

namespace BodyLife.Crm.Infrastructure.Tests.PostgreSql;

public sealed class PostgreSqlListInactiveClientsQueryTests
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
    public async Task QueryUsesActiveVisitsWithStableThresholdOrderingAndMembershipSummary()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var dbContext = database.CreateDbContext();
        await dbContext.Database.MigrateAsync();
        var fixture = await SeedFixtureAsync(database, dbContext);
        var ancient = await InsertClientAsync(database, fixture, "Ancient");
        var ancientMembership = await InsertMembershipAsync(
            database,
            fixture,
            ancient.ClientId,
            effectiveEndDate: AsOfDate.AddDays(-20));
        var ancientVisit = await InsertVisitAsync(
            database,
            fixture,
            ancient.ClientId,
            AsOfDate.AddDays(-75),
            visitKind: "one_off");
        var canceledRecent = await InsertClientAsync(database, fixture, "Canceled");
        var canceledLastActiveVisit = await InsertVisitAsync(
            database,
            fixture,
            canceledRecent.ClientId,
            AsOfDate.AddDays(-40),
            visitKind: "one_off");
        await InsertVisitAsync(
            database,
            fixture,
            canceledRecent.ClientId,
            AsOfDate.AddDays(-1),
            visitKind: "trial",
            status: "canceled");
        var alpha = await InsertClientAsync(
            database,
            fixture,
            "Alpha",
            operationalStatus: "inactive",
            phone: "+380 67 123 4567",
            cardNumber: "BL-ALPHA");
        var alphaMembership = await InsertMembershipAsync(
            database,
            fixture,
            alpha.ClientId,
            effectiveEndDate: AsOfDate.AddDays(19));
        var alphaVisit = await InsertVisitAsync(
            database,
            fixture,
            alpha.ClientId,
            AsOfDate.AddDays(-14),
            visitKind: "one_off");
        var zulu = await InsertClientAsync(database, fixture, "Zulu");
        var zuluVisit = await InsertVisitAsync(
            database,
            fixture,
            zulu.ClientId,
            AsOfDate.AddDays(-14),
            visitKind: "trial");
        var recent = await InsertClientAsync(database, fixture, "Recent");
        await InsertVisitAsync(
            database,
            fixture,
            recent.ClientId,
            AsOfDate.AddDays(-13),
            visitKind: "one_off");
        dbContext.ChangeTracker.Clear();
        var handler = CreateHandler(dbContext);

        var result = await handler.ExecuteAsync(
            new ListInactiveClientsQuery(
                fixture.Actor,
                AsOfDate,
                ThresholdDays: 14,
                Limit: ListInactiveClientsQuery.MaxLimit),
            CancellationToken.None);

        AssertSuccessful(result);
        var page = result.Page!;
        Assert.Equal(
            [
                ancient.ClientId,
                canceledRecent.ClientId,
                alpha.ClientId,
                zulu.ClientId,
            ],
            page.Items.Select(row => row.ClientId));
        Assert.Equal(
            ["Ancient Client", "Canceled Client", "Alpha Client", "Zulu Client"],
            page.Items.Select(row => row.ClientDisplayName));
        Assert.Equal([75, 40, 14, 14], page.Items.Select(row => row.DaysInactive));
        Assert.Equal(
            [
                ancientVisit,
                canceledLastActiveVisit,
                alphaVisit,
                zuluVisit,
            ],
            page.Items.Select(row => row.LastCountedVisit!.VisitId));
        Assert.Equal(VisitKind.OneOff, page.Items[0].LastCountedVisit!.VisitKind);
        Assert.Equal(VisitKind.Trial, page.Items[3].LastCountedVisit!.VisitKind);
        Assert.Equal(ClientOperationalStatus.Inactive, page.Items[2].OperationalStatus);
        Assert.Equal("+380 67 123 4567", page.Items[2].ClientPhone);
        Assert.Equal("BL-ALPHA", page.Items[2].CurrentCardNumber);
        Assert.Equal(
            InactiveClientMembershipSummaryKind.Last,
            page.Items[0].MembershipSummary!.Kind);
        Assert.Equal(
            ancientMembership.MembershipId,
            page.Items[0].MembershipSummary!.MembershipId);
        Assert.Equal(
            InactiveClientMembershipSummaryKind.Current,
            page.Items[2].MembershipSummary!.Kind);
        Assert.Equal(
            alphaMembership.MembershipId,
            page.Items[2].MembershipSummary!.MembershipId);
        Assert.Null(page.Items[1].MembershipSummary);
        Assert.Null(page.Items[3].MembershipSummary);
        Assert.False(page.HasMore);
        Assert.Null(page.NextOffset);

        var threshold30 = await handler.ExecuteAsync(
            new ListInactiveClientsQuery(
                fixture.Actor,
                AsOfDate,
                ThresholdDays: 30),
            CancellationToken.None);
        AssertSuccessful(threshold30);
        Assert.Equal(
            [ancient.ClientId, canceledRecent.ClientId],
            threshold30.Page!.Items.Select(row => row.ClientId));

        var threshold60 = await handler.ExecuteAsync(
            new ListInactiveClientsQuery(
                fixture.Actor,
                AsOfDate,
                ThresholdDays: 60),
            CancellationToken.None);
        AssertSuccessful(threshold60);
        Assert.Equal(
            [ancient.ClientId],
            threshold60.Page!.Items.Select(row => row.ClientId));

        var firstPage = await handler.ExecuteAsync(
            new ListInactiveClientsQuery(
                fixture.Actor,
                AsOfDate,
                ThresholdDays: 14,
                Limit: 2),
            CancellationToken.None);
        AssertSuccessful(firstPage);
        Assert.Equal(
            [ancient.ClientId, canceledRecent.ClientId],
            firstPage.Page!.Items.Select(row => row.ClientId));
        Assert.True(firstPage.Page.HasMore);
        Assert.Equal(2, firstPage.Page.NextOffset);

        var secondPage = await handler.ExecuteAsync(
            new ListInactiveClientsQuery(
                fixture.Actor,
                AsOfDate,
                ThresholdDays: 14,
                Limit: 2,
                Offset: 2),
            CancellationToken.None);
        AssertSuccessful(secondPage);
        Assert.Equal(
            [alpha.ClientId, zulu.ClientId],
            secondPage.Page!.Items.Select(row => row.ClientId));
        Assert.False(secondPage.Page.HasMore);
        Assert.Null(secondPage.Page.NextOffset);

        var profileState = await new GetMembershipStateQueryHandler(
                dbContext,
                new FixedTimeProvider(TestNow))
            .ExecuteAsync(
                new GetMembershipStateQuery(
                    fixture.Actor,
                    alphaMembership.MembershipId,
                    AsOfDate),
                CancellationToken.None);
        Assert.Equal(GetMembershipStateStatus.Success, profileState.Status);
        Assert.NotNull(profileState.State);
        var reportState = page.Items[2].MembershipSummary!.MembershipState;
        Assert.Equal(profileState.State.MembershipId, reportState.MembershipId);
        Assert.Equal(profileState.State.RemainingVisits, reportState.RemainingVisits);
        Assert.Equal(profileState.State.EffectiveEndDate, reportState.EffectiveEndDate);
        Assert.Equal(
            profileState.State.Warnings.Select(warning => warning.Code),
            reportState.Warnings.Select(warning => warning.Code));
        Assert.Contains(
            "ix_visits_active_daily_report",
            await ReadActiveVisitQueryPlanAsync(database),
            StringComparison.Ordinal);
        Assert.Empty(dbContext.ChangeTracker.Entries());
        Assert.Equal(
            0L,
            await database.ExecuteScalarAsync<long>(
                "select count(*) from bodylife.business_audit_entries"));
    }

    [PostgreSqlFact]
    public async Task NeverVisitedAndFutureOnlyClientsAreLabeledWithoutInventedDates()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var dbContext = database.CreateDbContext();
        await dbContext.Database.MigrateAsync();
        var fixture = await SeedFixtureAsync(database, dbContext);
        var future = await InsertClientAsync(database, fixture, "Future");
        await InsertVisitAsync(
            database,
            fixture,
            future.ClientId,
            AsOfDate.AddDays(1),
            visitKind: "trial");
        var never = await InsertClientAsync(database, fixture, "Never");
        var today = await InsertClientAsync(database, fixture, "Today");
        await InsertVisitAsync(
            database,
            fixture,
            today.ClientId,
            AsOfDate,
            visitKind: "one_off");
        dbContext.ChangeTracker.Clear();
        var handler = CreateHandler(dbContext);

        var excluded = await handler.ExecuteAsync(
            new ListInactiveClientsQuery(
                fixture.Actor,
                AsOfDate,
                ThresholdDays: 14),
            CancellationToken.None);
        var included = await handler.ExecuteAsync(
            new ListInactiveClientsQuery(
                fixture.Actor,
                AsOfDate,
                ThresholdDays: 14,
                IncludeClientsWithNoVisits: true),
            CancellationToken.None);

        AssertSuccessful(excluded);
        Assert.Empty(excluded.Page!.Items);
        AssertSuccessful(included);
        Assert.Equal(
            [future.ClientId, never.ClientId],
            included.Page!.Items.Select(row => row.ClientId));
        Assert.All(included.Page.Items, row =>
        {
            Assert.Null(row.LastCountedVisit);
            Assert.Null(row.LastCountedVisitDate);
            Assert.Null(row.DaysInactive);
        });
        Assert.DoesNotContain(included.Page.Items, row => row.ClientId == today.ClientId);
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
            new ListInactiveClientsQuery(
                unknownActor,
                AsOfDate: default,
                ThresholdDays: 15,
                Limit: 0),
            CancellationToken.None);
        var invalidDate = await handler.ExecuteAsync(
            new ListInactiveClientsQuery(
                fixture.Actor,
                DateOnly.MaxValue,
                ThresholdDays: 14),
            CancellationToken.None);
        var invalidThreshold = await handler.ExecuteAsync(
            new ListInactiveClientsQuery(
                fixture.Actor,
                AsOfDate,
                ThresholdDays: 15),
            CancellationToken.None);
        var invalidLimit = await handler.ExecuteAsync(
            new ListInactiveClientsQuery(
                fixture.Actor,
                AsOfDate,
                ThresholdDays: 14,
                Limit: 0),
            CancellationToken.None);
        var invalidOffset = await handler.ExecuteAsync(
            new ListInactiveClientsQuery(
                fixture.Actor,
                AsOfDate,
                ThresholdDays: 14,
                Offset: -1),
            CancellationToken.None);

        AssertFailure(denied, ListInactiveClientsStatus.PermissionDenied);
        AssertFailure(
            invalidDate,
            ListInactiveClientsStatus.ValidationFailed,
            "asOfDate");
        AssertFailure(
            invalidThreshold,
            ListInactiveClientsStatus.ValidationFailed,
            "thresholdDays");
        AssertFailure(
            invalidLimit,
            ListInactiveClientsStatus.ValidationFailed,
            "limit");
        AssertFailure(
            invalidOffset,
            ListInactiveClientsStatus.ValidationFailed,
            "offset");
    }

    [PostgreSqlFact]
    public async Task VisibleMissingAndStaleMembershipCachesFailClosedOnlyWhenNeeded()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var dbContext = database.CreateDbContext();
        await dbContext.Database.MigrateAsync();
        var fixture = await SeedFixtureAsync(database, dbContext);
        var visible = await InsertClientAsync(database, fixture, "Visible");
        await InsertVisitAsync(
            database,
            fixture,
            visible.ClientId,
            AsOfDate.AddDays(-30),
            visitKind: "one_off");
        var visibleMembership = await InsertMembershipAsync(
            database,
            fixture,
            visible.ClientId,
            effectiveEndDate: AsOfDate.AddDays(10),
            includeCache: false);
        var excluded = await InsertClientAsync(database, fixture, "Excluded");
        await InsertVisitAsync(
            database,
            fixture,
            excluded.ClientId,
            AsOfDate.AddDays(-1),
            visitKind: "one_off");
        await InsertMembershipAsync(
            database,
            fixture,
            excluded.ClientId,
            effectiveEndDate: AsOfDate.AddDays(10),
            includeCache: false);
        dbContext.ChangeTracker.Clear();
        var handler = CreateHandler(dbContext);

        var missing = await handler.ExecuteAsync(
            new ListInactiveClientsQuery(
                fixture.Actor,
                AsOfDate,
                ThresholdDays: 14),
            CancellationToken.None);
        AssertFailure(missing, ListInactiveClientsStatus.RecalculationFailed);

        await InsertCacheAsync(database, visibleMembership);
        var available = await handler.ExecuteAsync(
            new ListInactiveClientsQuery(
                fixture.Actor,
                AsOfDate,
                ThresholdDays: 14),
            CancellationToken.None);
        AssertSuccessful(available);
        Assert.Equal(visible.ClientId, Assert.Single(available.Page!.Items).ClientId);

        await SetCacheVersionAsync(
            database,
            visibleMembership.MembershipId,
            MembershipStateCacheRebuilder.CurrentRecalculationVersion - 1);
        var stale = await handler.ExecuteAsync(
            new ListInactiveClientsQuery(
                fixture.Actor,
                AsOfDate,
                ThresholdDays: 14),
            CancellationToken.None);

        AssertFailure(stale, ListInactiveClientsStatus.RecalculationFailed);
        Assert.Equal(
            0L,
            await database.ExecuteScalarAsync<long>(
                "select count(*) from bodylife.business_audit_entries"));
    }

    [Fact]
    public void PersistenceRegistrationExposesScopedMembershipAndReportHandlers()
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
            GetClientMembershipReportStatesQuery,
            GetClientMembershipReportStatesResult,
            GetClientMembershipReportStatesQueryHandler>(services);
        AssertScopedRegistration<
            ListInactiveClientsQuery,
            ListInactiveClientsResult,
            ListInactiveClientsQueryHandler>(services);
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<
            IBodyLifeQueryHandler<
                GetClientMembershipReportStatesQuery,
                GetClientMembershipReportStatesResult>>());
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<
            IBodyLifeQueryHandler<ListInactiveClientsQuery, ListInactiveClientsResult>>());
    }

    private static ListInactiveClientsQueryHandler CreateHandler(
        BodyLifeDbContext dbContext)
    {
        var timeProvider = new FixedTimeProvider(TestNow);
        return new ListInactiveClientsQueryHandler(
            dbContext,
            new GetClientMembershipReportStatesQueryHandler(
                dbContext,
                timeProvider),
            timeProvider);
    }

    private static async Task<InactiveFixture> SeedFixtureAsync(
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
        command.Parameters.AddWithValue("created_at", TestNow.AddDays(-100));
        await command.ExecuteNonQueryAsync();

        return new InactiveFixture(
            new ActorContext(
                new AccountId(accountId),
                ActorRole.Owner,
                AccountKind.Owner,
                new SessionId(sessionId),
                "Reports tablet"),
            accountId,
            sessionId,
            membershipTypeId);
    }

    private static async Task<SeededClient> InsertClientAsync(
        PostgreSqlTestDatabase database,
        InactiveFixture fixture,
        string surname,
        string operationalStatus = "active",
        string? phone = null,
        string? cardNumber = null)
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
                @surname,
                'Client',
                null,
                @normalized_full_name,
                @phone_raw,
                @phone_normalized,
                @phone_last4,
                null,
                @operational_status,
                @created_at,
                @account_id,
                @created_at);

            insert into bodylife.client_card_assignments (
                id,
                client_id,
                card_number_raw,
                card_number_normalized,
                assigned_at,
                assigned_by_account_id,
                ended_at,
                ended_by_account_id,
                end_reason,
                is_current)
            select
                @card_id,
                @client_id,
                @card_number,
                @card_number,
                @created_at,
                @account_id,
                null,
                null,
                null,
                true
            where @card_number is not null;
            """;
        command.Parameters.AddWithValue("client_id", clientId);
        command.Parameters.AddWithValue("surname", surname);
        command.Parameters.AddWithValue(
            "normalized_full_name",
            $"{surname.ToUpperInvariant()} CLIENT");
        AddNullableParameter(command, "phone_raw", NpgsqlDbType.Text, phone);
        AddNullableParameter(
            command,
            "phone_normalized",
            NpgsqlDbType.Text,
            phone is null ? null : "380671234567");
        AddNullableParameter(
            command,
            "phone_last4",
            NpgsqlDbType.Text,
            phone is null ? null : "4567");
        command.Parameters.AddWithValue("operational_status", operationalStatus);
        command.Parameters.AddWithValue("created_at", TestNow.AddDays(-100));
        command.Parameters.AddWithValue("account_id", fixture.AccountId);
        command.Parameters.AddWithValue("card_id", Guid.NewGuid());
        AddNullableParameter(command, "card_number", NpgsqlDbType.Text, cardNumber);
        await command.ExecuteNonQueryAsync();
        return new SeededClient(clientId);
    }

    private static async Task<Guid> InsertVisitAsync(
        PostgreSqlTestDatabase database,
        InactiveFixture fixture,
        Guid clientId,
        DateOnly occurredDate,
        string visitKind,
        string status = "active")
    {
        var visitId = Guid.NewGuid();
        var occurredAt = new DateTimeOffset(
            occurredDate.ToDateTime(new TimeOnly(12, 0), DateTimeKind.Utc));
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
                @visit_kind,
                'normal',
                null,
                'Inactive report fixture Visit',
                @status);

            insert into bodylife.visit_cancellations (
                id,
                visit_id,
                reason,
                occurred_at,
                recorded_at,
                recorded_by_account_id,
                session_id,
                entry_origin,
                entry_batch_id)
            select
                @cancellation_id,
                @visit_id,
                'Canceled inactive report fixture Visit',
                @canceled_at,
                @canceled_at,
                @account_id,
                @session_id,
                'normal',
                null
            where @status = 'canceled';
            """;
        command.Parameters.AddWithValue("visit_id", visitId);
        command.Parameters.AddWithValue("client_id", clientId);
        command.Parameters.AddWithValue("occurred_at", occurredAt);
        command.Parameters.AddWithValue("recorded_at", occurredAt.AddMinutes(1));
        command.Parameters.AddWithValue("account_id", fixture.AccountId);
        command.Parameters.AddWithValue("session_id", fixture.SessionId);
        command.Parameters.AddWithValue("visit_kind", visitKind);
        command.Parameters.AddWithValue("status", status);
        command.Parameters.AddWithValue("cancellation_id", Guid.NewGuid());
        command.Parameters.AddWithValue("canceled_at", TestNow);
        await command.ExecuteNonQueryAsync();
        return visitId;
    }

    private static async Task<SeededMembership> InsertMembershipAsync(
        PostgreSqlTestDatabase database,
        InactiveFixture fixture,
        Guid clientId,
        DateOnly effectiveEndDate,
        bool includeCache = true)
    {
        const int durationDays = 30;
        var membershipId = Guid.NewGuid();
        var membership = new SeededMembership(
            membershipId,
            effectiveEndDate,
            RemainingVisits: 4);
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
                @effective_end_date,
                @issued_at,
                @account_id,
                'active',
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
                4,
                4,
                0,
                null,
                null,
                0,
                @effective_end_date,
                null,
                @recalculated_at,
                @recalculation_version
            where @include_cache;
            """;
        command.Parameters.AddWithValue("membership_id", membershipId);
        command.Parameters.AddWithValue("client_id", clientId);
        command.Parameters.AddWithValue("membership_type_id", fixture.MembershipTypeId);
        command.Parameters.AddWithValue(
            "start_date",
            NpgsqlDbType.Date,
            effectiveEndDate.AddDays(-(durationDays - 1)));
        command.Parameters.AddWithValue(
            "effective_end_date",
            NpgsqlDbType.Date,
            effectiveEndDate);
        command.Parameters.AddWithValue("issued_at", TestNow.AddDays(-100));
        command.Parameters.AddWithValue("account_id", fixture.AccountId);
        command.Parameters.AddWithValue("recalculated_at", TestNow);
        command.Parameters.AddWithValue(
            "recalculation_version",
            MembershipStateCacheRebuilder.CurrentRecalculationVersion);
        command.Parameters.AddWithValue("include_cache", includeCache);
        await command.ExecuteNonQueryAsync();
        return membership;
    }

    private static async Task InsertCacheAsync(
        PostgreSqlTestDatabase database,
        SeededMembership membership)
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
                4,
                @remaining_visits,
                0,
                null,
                null,
                0,
                @effective_end_date,
                null,
                @recalculated_at,
                @recalculation_version);
            """;
        command.Parameters.AddWithValue("membership_id", membership.MembershipId);
        command.Parameters.AddWithValue("remaining_visits", membership.RemainingVisits);
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

    private static async Task<string> ReadActiveVisitQueryPlanAsync(
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
            select client_id, max(occurred_at)
            from bodylife.visits
            where status = 'active'
                and occurred_at < @as_of_end
            group by client_id
            """;
        command.Parameters.AddWithValue("as_of_end", TestNow.AddHours(6));
        var planLines = new List<string>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            planLines.Add(reader.GetString(0));
        }

        return string.Join(Environment.NewLine, planLines);
    }

    private static void AddNullableParameter(
        NpgsqlCommand command,
        string name,
        NpgsqlDbType type,
        string? value)
    {
        var parameter = command.Parameters.Add(name, type);
        parameter.Value = value ?? (object)DBNull.Value;
    }

    private static void AssertSuccessful(ListInactiveClientsResult result)
    {
        Assert.Equal(ListInactiveClientsStatus.Success, result.Status);
        Assert.NotNull(result.Page);
        Assert.Null(result.ErrorCode);
        Assert.Null(result.ErrorMessage);
        Assert.Null(result.ErrorField);
    }

    private static void AssertFailure(
        ListInactiveClientsResult result,
        ListInactiveClientsStatus expectedStatus,
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

    private sealed record InactiveFixture(
        ActorContext Actor,
        Guid AccountId,
        Guid SessionId,
        Guid MembershipTypeId);

    private sealed record SeededClient(Guid ClientId);

    private sealed record SeededMembership(
        Guid MembershipId,
        DateOnly EffectiveEndDate,
        int RemainingVisits);

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow()
        {
            return utcNow;
        }
    }
}
