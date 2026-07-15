using BodyLife.Crm.Application.Commands;
using BodyLife.Crm.Application.Queries;
using BodyLife.Crm.Infrastructure;
using BodyLife.Crm.Infrastructure.Persistence;
using BodyLife.Crm.Infrastructure.Persistence.UsersRoles;
using BodyLife.Crm.Infrastructure.Persistence.Visits;
using BodyLife.Crm.Modules.Visits;
using BodyLife.Crm.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using NpgsqlTypes;

namespace BodyLife.Crm.Infrastructure.Tests.PostgreSql;

public sealed class PostgreSqlGetDailyVisitSourceRowsQueryTests
{
    private static readonly DateOnly BusinessDate = new(2026, 7, 15);
    private static readonly DateTimeOffset TestNow = new(
        2026,
        7,
        15,
        18,
        0,
        0,
        TimeSpan.Zero);

    [PostgreSqlFact]
    public async Task QueryTotalsEqualActiveDrillDownAndRetainCanceledSourceRows()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var dbContext = database.CreateDbContext();
        await dbContext.Database.MigrateAsync();
        var fixture = await SeedFixtureAsync(database, dbContext);
        var membershipVisitId = await InsertVisitAsync(
            database,
            fixture,
            fixture.ClientId,
            "membership",
            AtBusinessTime(9),
            TestNow.AddMinutes(-30),
            comment: "Morning membership Visit");
        var activeConsumptionId = await InsertConsumptionAsync(
            database,
            fixture,
            membershipVisitId,
            status: "active");
        var fallbackBatchId = Guid.NewGuid();
        var oneOffVisitId = await InsertVisitAsync(
            database,
            fixture,
            fixture.ClientId,
            "one_off",
            AtBusinessTime(11),
            TestNow.AddDays(1),
            entryOrigin: "paper_fallback",
            entryBatchId: fallbackBatchId,
            comment: "Recovered from paper");
        var canceledVisitId = await InsertVisitAsync(
            database,
            fixture,
            fixture.ClientId,
            "membership",
            AtBusinessTime(12),
            TestNow.AddMinutes(-20),
            status: "canceled");
        var canceledConsumptionId = await InsertConsumptionAsync(
            database,
            fixture,
            canceledVisitId,
            status: "canceled");
        var cancellationBatchId = Guid.NewGuid();
        var cancellationId = await InsertCancellationAsync(
            database,
            fixture,
            canceledVisitId,
            "Mistaken daily report Visit",
            AtBusinessTime(13),
            TestNow.AddMinutes(-10),
            "manual_backfill",
            cancellationBatchId);
        var trialVisitId = await InsertVisitAsync(
            database,
            fixture,
            fixture.OtherClientId,
            "trial",
            AtBusinessTime(14),
            TestNow.AddMinutes(-5));
        await InsertVisitAsync(
            database,
            fixture,
            fixture.ClientId,
            "one_off",
            AtBusinessTime(0).AddTicks(-1),
            TestNow);
        await InsertVisitAsync(
            database,
            fixture,
            fixture.ClientId,
            "one_off",
            AtBusinessTime(0).AddDays(1),
            TestNow);
        var dayProvider = new RecordingVisitDayStatusProvider(
            VisitDayReconciliationStatus.Open);

        var result = await CreateHandler(dbContext, dayProvider).ExecuteAsync(
            new GetDailyVisitSourceRowsQuery(fixture.Actor, BusinessDate),
            CancellationToken.None);

        var snapshot = AssertSuccess(result, BusinessDate);
        Assert.Equal(VisitDayReconciliationStatus.Open, snapshot.DayStatus);
        Assert.Equal(3, snapshot.ActiveVisitCount);
        Assert.Equal(
            snapshot.ActiveVisitCount,
            snapshot.Rows.Count(row =>
                row.Visit.Status == ClientVisitRowStatus.Active));
        Assert.Equal(
            snapshot.ActiveVisitCount,
            await CountActiveVisitsAsync(database, BusinessDate));
        Assert.Contains(
            "ix_visits_daily_source",
            await ReadDailyVisitQueryPlanAsync(database, BusinessDate),
            StringComparison.OrdinalIgnoreCase);
        Assert.Equal(
            [trialVisitId, canceledVisitId, oneOffVisitId, membershipVisitId],
            snapshot.Rows.Select(row => row.Visit.VisitId).ToArray());
        Assert.Equal(
            [VisitKind.Trial, VisitKind.OneOff, VisitKind.Membership],
            snapshot.Rows
                .Where(row => row.Visit.Status == ClientVisitRowStatus.Active)
                .Select(row => row.Visit.VisitKind)
                .ToArray());
        Assert.All(
            snapshot.Rows.Where(row =>
                row.Visit.Status == ClientVisitRowStatus.Active),
            row => Assert.True(
                row.Visit.AllowedActions.IsAllowed(VisitActionKeys.Cancel)));

        var trial = snapshot.Rows[0];
        Assert.Equal("Other Visitor", trial.ClientDisplayName);
        Assert.Equal(fixture.OtherClientId, trial.Visit.ClientId);
        Assert.Null(trial.Visit.Consumption);

        var canceled = snapshot.Rows[1].Visit;
        Assert.Equal(ClientVisitRowStatus.Canceled, canceled.Status);
        Assert.Empty(canceled.AllowedActions.Items);
        var canceledConsumption = Assert.IsType<ClientVisitConsumption>(
            canceled.Consumption);
        Assert.Equal(canceledConsumptionId, canceledConsumption.ConsumptionId);
        Assert.Equal(ClientVisitConsumptionStatus.Canceled, canceledConsumption.Status);
        var cancellation = Assert.IsType<ClientVisitCancellation>(
            canceled.Cancellation);
        Assert.Equal(cancellationId, cancellation.CancellationId);
        Assert.Equal("Mistaken daily report Visit", cancellation.Reason);
        Assert.Equal(EntryOrigin.ManualBackfill, cancellation.EntryOrigin);
        Assert.Equal(cancellationBatchId, cancellation.EntryBatchId);

        var oneOff = snapshot.Rows[2].Visit;
        Assert.Equal(EntryOrigin.PaperFallback, oneOff.EntryOrigin);
        Assert.Equal(fallbackBatchId, oneOff.EntryBatchId);
        Assert.Equal(TestNow.AddDays(1), oneOff.RecordedAt);
        Assert.Equal("Recovered from paper", oneOff.Comment);

        var membership = snapshot.Rows[3];
        Assert.Equal("Daily Reader", membership.ClientDisplayName);
        var activeConsumption = Assert.IsType<ClientVisitConsumption>(
            membership.Visit.Consumption);
        Assert.Equal(activeConsumptionId, activeConsumption.ConsumptionId);
        Assert.Equal(fixture.MembershipId, activeConsumption.MembershipId);
        Assert.Equal("Daily report fixture", activeConsumption.MembershipTypeNameSnapshot);
        Assert.Equal(ClientVisitConsumptionStatus.Active, activeConsumption.Status);
        Assert.Equal([BusinessDate], dayProvider.RequestedDates);
        Assert.Equal(
            0L,
            await database.ExecuteScalarAsync<long>(
                "select count(*) from bodylife.business_audit_entries"));
    }

    [PostgreSqlFact]
    public async Task QueryUsesHalfOpenUtcDateRangeAndDeterministicOrdering()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var dbContext = database.CreateDbContext();
        await dbContext.Database.MigrateAsync();
        var fixture = await SeedFixtureAsync(database, dbContext);
        var firstId = Guid.Parse("00000000-0000-0000-0000-000000000001");
        var secondId = Guid.Parse("00000000-0000-0000-0000-000000000002");
        var thirdId = Guid.Parse("00000000-0000-0000-0000-000000000003");
        foreach (var visitId in new[] { firstId, secondId, thirdId })
        {
            await InsertVisitAsync(
                database,
                fixture,
                fixture.ClientId,
                "one_off",
                AtBusinessTime(10),
                TestNow,
                visitId: visitId);
        }

        await InsertVisitAsync(
            database,
            fixture,
            fixture.ClientId,
            "one_off",
            AtBusinessTime(0).AddTicks(-1),
            TestNow);
        await InsertVisitAsync(
            database,
            fixture,
            fixture.ClientId,
            "one_off",
            AtBusinessTime(0).AddDays(1),
            TestNow);
        var dayProvider = new RecordingVisitDayStatusProvider(
            VisitDayReconciliationStatus.Open);
        var handler = CreateHandler(dbContext, dayProvider);

        var result = await handler.ExecuteAsync(
            new GetDailyVisitSourceRowsQuery(fixture.Actor, BusinessDate),
            CancellationToken.None);
        var emptyResult = await handler.ExecuteAsync(
            new GetDailyVisitSourceRowsQuery(
                fixture.Actor,
                BusinessDate.AddDays(2)),
            CancellationToken.None);

        var snapshot = AssertSuccess(result, BusinessDate);
        Assert.Equal(
            [thirdId, secondId, firstId],
            snapshot.Rows.Select(row => row.Visit.VisitId).ToArray());
        Assert.Equal(3, snapshot.ActiveVisitCount);
        var emptySnapshot = AssertSuccess(
            emptyResult,
            BusinessDate.AddDays(2));
        Assert.Empty(emptySnapshot.Rows);
        Assert.Equal(0, emptySnapshot.ActiveVisitCount);
        Assert.Equal(
            [BusinessDate, BusinessDate.AddDays(2)],
            dayProvider.RequestedDates);
    }

    [PostgreSqlFact]
    public async Task ReconciledDayReturnsOwnerAndAdminCancellationPermissions()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var dbContext = database.CreateDbContext();
        await dbContext.Database.MigrateAsync();
        var fixture = await SeedFixtureAsync(database, dbContext);
        await InsertVisitAsync(
            database,
            fixture,
            fixture.ClientId,
            "one_off",
            AtBusinessTime(10),
            TestNow);
        var dayProvider = new RecordingVisitDayStatusProvider(
            VisitDayReconciliationStatus.Reconciled);
        var handler = CreateHandler(dbContext, dayProvider);

        var ownerResult = await handler.ExecuteAsync(
            new GetDailyVisitSourceRowsQuery(fixture.Actor, BusinessDate),
            CancellationToken.None);

        var ownerSnapshot = AssertSuccess(ownerResult, BusinessDate);
        var ownerPermission = Assert.Single(
            Assert.Single(ownerSnapshot.Rows).Visit.AllowedActions.Items);
        Assert.True(ownerPermission.IsAllowed);
        Assert.Equal(VisitActionKeys.OwnerPolicy, ownerPermission.RequiredPolicy);

        await UpdateActorIdentityAsync(
            database,
            fixture.Actor.AccountId.Value,
            "named_admin",
            "admin");
        var adminActor = fixture.Actor with
        {
            Role = ActorRole.Admin,
            AccountKind = AccountKind.NamedAdmin,
        };

        var adminResult = await handler.ExecuteAsync(
            new GetDailyVisitSourceRowsQuery(adminActor, BusinessDate),
            CancellationToken.None);

        var adminSnapshot = AssertSuccess(adminResult, BusinessDate);
        var adminPermission = Assert.Single(
            Assert.Single(adminSnapshot.Rows).Visit.AllowedActions.Items);
        Assert.False(adminPermission.IsAllowed);
        Assert.Equal(VisitActionKeys.OwnerPolicy, adminPermission.RequiredPolicy);
        Assert.Equal("day_closed_requires_owner", adminPermission.DeniedReasonCode);
        Assert.Equal(
            [BusinessDate, BusinessDate],
            dayProvider.RequestedDates);
    }

    [PostgreSqlFact]
    public async Task EmptyValidationAndInactiveActorReturnNoAuthoritativeRows()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var dbContext = database.CreateDbContext();
        await dbContext.Database.MigrateAsync();
        var fixture = await SeedFixtureAsync(database, dbContext);
        var dayProvider = new RecordingVisitDayStatusProvider(
            VisitDayReconciliationStatus.Open);
        var handler = CreateHandler(dbContext, dayProvider);

        var empty = await handler.ExecuteAsync(
            new GetDailyVisitSourceRowsQuery(fixture.Actor, BusinessDate),
            CancellationToken.None);
        var invalidDefault = await handler.ExecuteAsync(
            new GetDailyVisitSourceRowsQuery(fixture.Actor, default),
            CancellationToken.None);
        var invalidMaximum = await handler.ExecuteAsync(
            new GetDailyVisitSourceRowsQuery(fixture.Actor, DateOnly.MaxValue),
            CancellationToken.None);
        await DeactivateActorAsync(database, fixture.Actor.AccountId.Value);
        var denied = await handler.ExecuteAsync(
            new GetDailyVisitSourceRowsQuery(fixture.Actor, BusinessDate),
            CancellationToken.None);

        var emptySnapshot = AssertSuccess(empty, BusinessDate);
        Assert.Empty(emptySnapshot.Rows);
        Assert.Equal(0, emptySnapshot.ActiveVisitCount);
        AssertFailure(
            invalidDefault,
            GetDailyVisitSourceRowsStatus.ValidationFailed,
            "businessDate");
        AssertFailure(
            invalidMaximum,
            GetDailyVisitSourceRowsStatus.ValidationFailed,
            "businessDate");
        AssertFailure(denied, GetDailyVisitSourceRowsStatus.PermissionDenied);
        Assert.Equal([BusinessDate], dayProvider.RequestedDates);
    }

    [PostgreSqlFact]
    public async Task InconsistentCanonicalSourceFailsTheWholeSnapshot()
    {
        await AssertInconsistentSourceAsync("missing_cancellation");
        await AssertInconsistentSourceAsync("missing_consumption");
    }

    private static async Task AssertInconsistentSourceAsync(string inconsistency)
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var dbContext = database.CreateDbContext();
        await dbContext.Database.MigrateAsync();
        var fixture = await SeedFixtureAsync(database, dbContext);

        if (inconsistency == "missing_cancellation")
        {
            await InsertVisitAsync(
                database,
                fixture,
                fixture.ClientId,
                "one_off",
                AtBusinessTime(10),
                TestNow,
                status: "canceled");
        }
        else
        {
            Assert.Equal("missing_consumption", inconsistency);
            await InsertVisitAsync(
                database,
                fixture,
                fixture.ClientId,
                "membership",
                AtBusinessTime(10),
                TestNow);
        }

        var result = await CreateHandler(
            dbContext,
            new RecordingVisitDayStatusProvider(
                VisitDayReconciliationStatus.Open)).ExecuteAsync(
                new GetDailyVisitSourceRowsQuery(fixture.Actor, BusinessDate),
                CancellationToken.None);

        AssertFailure(result, GetDailyVisitSourceRowsStatus.SourceInconsistent);
        Assert.Equal("source_inconsistent", result.ErrorCode);
    }

    [Fact]
    public void PersistenceRegistrationResolvesDailyVisitSourceRowsQuery()
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
            IBodyLifeQueryHandler<
                GetDailyVisitSourceRowsQuery,
                GetDailyVisitSourceRowsResult>,
            GetDailyVisitSourceRowsQueryHandler>(services);

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<
            IBodyLifeQueryHandler<
                GetDailyVisitSourceRowsQuery,
                GetDailyVisitSourceRowsResult>>());
    }

    private static GetDailyVisitSourceRowsQueryHandler CreateHandler(
        BodyLifeDbContext dbContext,
        IVisitDayReconciliationStatusProvider dayStatusProvider)
    {
        return new GetDailyVisitSourceRowsQueryHandler(
            dbContext,
            dayStatusProvider,
            new FixedTimeProvider(TestNow));
    }

    private static DateTimeOffset AtBusinessTime(int hour)
    {
        return new DateTimeOffset(
            BusinessDate.ToDateTime(new TimeOnly(hour, 0), DateTimeKind.Utc));
    }

    private static async Task<DailyVisitSourceFixture> SeedFixtureAsync(
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
        var otherClientId = Guid.NewGuid();
        var membershipTypeId = Guid.NewGuid();
        var membershipId = Guid.NewGuid();

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
                'Daily report tablet',
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
            values
                (
                    @client_id,
                    'Daily',
                    'Reader',
                    null,
                    'DAILY READER',
                    null,
                    null,
                    null,
                    null,
                    'active',
                    @created_at,
                    @account_id,
                    @created_at),
                (
                    @other_client_id,
                    'Other',
                    'Visitor',
                    null,
                    'OTHER VISITOR',
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
                'Daily report fixture',
                30,
                8,
                1000,
                'UAH',
                true,
                null,
                @created_at,
                @created_at,
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
                'Daily report fixture',
                30,
                8,
                1000,
                'UAH',
                @start_date,
                @base_end_date,
                @created_at,
                @account_id,
                'active',
                'normal',
                null,
                null)
            """;
        command.Parameters.AddWithValue("session_id", sessionId);
        command.Parameters.AddWithValue("account_id", accountId);
        command.Parameters.AddWithValue("started_at", TestNow.AddHours(-2));
        command.Parameters.AddWithValue("expires_at", TestNow.AddHours(8));
        command.Parameters.AddWithValue("last_seen_at", TestNow.AddMinutes(-5));
        command.Parameters.AddWithValue("client_id", clientId);
        command.Parameters.AddWithValue("other_client_id", otherClientId);
        command.Parameters.AddWithValue("created_at", TestNow.AddDays(-30));
        command.Parameters.AddWithValue("membership_type_id", membershipTypeId);
        command.Parameters.AddWithValue("membership_id", membershipId);
        command.Parameters.AddWithValue(
            "start_date",
            NpgsqlDbType.Date,
            new DateOnly(2026, 7, 1));
        command.Parameters.AddWithValue(
            "base_end_date",
            NpgsqlDbType.Date,
            new DateOnly(2026, 7, 30));
        Assert.Equal(5, await command.ExecuteNonQueryAsync());

        return new DailyVisitSourceFixture(
            new ActorContext(
                new AccountId(accountId),
                ActorRole.Owner,
                AccountKind.Owner,
                new SessionId(sessionId),
                "Daily report tablet"),
            clientId,
            otherClientId,
            membershipId);
    }

    private static async Task<Guid> InsertVisitAsync(
        PostgreSqlTestDatabase database,
        DailyVisitSourceFixture fixture,
        Guid clientId,
        string visitKind,
        DateTimeOffset occurredAt,
        DateTimeOffset recordedAt,
        string entryOrigin = "normal",
        Guid? entryBatchId = null,
        string? comment = null,
        string status = "active",
        Guid? visitId = null)
    {
        var id = visitId ?? Guid.NewGuid();
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
                @id,
                @client_id,
                @occurred_at,
                @recorded_at,
                @account_id,
                @session_id,
                @visit_kind,
                @entry_origin,
                @entry_batch_id,
                @comment,
                @status)
            """;
        command.Parameters.AddWithValue("id", id);
        command.Parameters.AddWithValue("client_id", clientId);
        command.Parameters.AddWithValue("occurred_at", occurredAt);
        command.Parameters.AddWithValue("recorded_at", recordedAt);
        command.Parameters.AddWithValue("account_id", fixture.Actor.AccountId.Value);
        command.Parameters.AddWithValue("session_id", fixture.Actor.SessionId.Value);
        command.Parameters.AddWithValue("visit_kind", visitKind);
        command.Parameters.AddWithValue("entry_origin", entryOrigin);
        command.Parameters.Add("entry_batch_id", NpgsqlDbType.Uuid).Value =
            entryBatchId ?? (object)DBNull.Value;
        command.Parameters.Add("comment", NpgsqlDbType.Varchar).Value =
            comment ?? (object)DBNull.Value;
        command.Parameters.AddWithValue("status", status);
        Assert.Equal(1, await command.ExecuteNonQueryAsync());
        return id;
    }

    private static async Task<Guid> InsertConsumptionAsync(
        PostgreSqlTestDatabase database,
        DailyVisitSourceFixture fixture,
        Guid visitId,
        string status)
    {
        var consumptionId = Guid.NewGuid();
        await using var connection = new NpgsqlConnection(database.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
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
                @id,
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
                @status)
            """;
        command.Parameters.AddWithValue("id", consumptionId);
        command.Parameters.AddWithValue("visit_id", visitId);
        command.Parameters.AddWithValue("client_id", fixture.ClientId);
        command.Parameters.AddWithValue("membership_id", fixture.MembershipId);
        command.Parameters.AddWithValue("recorded_at", TestNow);
        command.Parameters.AddWithValue("account_id", fixture.Actor.AccountId.Value);
        command.Parameters.AddWithValue("session_id", fixture.Actor.SessionId.Value);
        command.Parameters.AddWithValue("status", status);
        Assert.Equal(1, await command.ExecuteNonQueryAsync());
        return consumptionId;
    }

    private static async Task<Guid> InsertCancellationAsync(
        PostgreSqlTestDatabase database,
        DailyVisitSourceFixture fixture,
        Guid visitId,
        string reason,
        DateTimeOffset occurredAt,
        DateTimeOffset recordedAt,
        string entryOrigin,
        Guid? entryBatchId)
    {
        var cancellationId = Guid.NewGuid();
        await using var connection = new NpgsqlConnection(database.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
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
            values (
                @id,
                @visit_id,
                @reason,
                @occurred_at,
                @recorded_at,
                @account_id,
                @session_id,
                @entry_origin,
                @entry_batch_id)
            """;
        command.Parameters.AddWithValue("id", cancellationId);
        command.Parameters.AddWithValue("visit_id", visitId);
        command.Parameters.AddWithValue("reason", reason);
        command.Parameters.AddWithValue("occurred_at", occurredAt);
        command.Parameters.AddWithValue("recorded_at", recordedAt);
        command.Parameters.AddWithValue("account_id", fixture.Actor.AccountId.Value);
        command.Parameters.AddWithValue("session_id", fixture.Actor.SessionId.Value);
        command.Parameters.AddWithValue("entry_origin", entryOrigin);
        command.Parameters.Add("entry_batch_id", NpgsqlDbType.Uuid).Value =
            entryBatchId ?? (object)DBNull.Value;
        Assert.Equal(1, await command.ExecuteNonQueryAsync());
        return cancellationId;
    }

    private static async Task<int> CountActiveVisitsAsync(
        PostgreSqlTestDatabase database,
        DateOnly businessDate)
    {
        var dayStart = new DateTimeOffset(
            businessDate.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc));
        await using var connection = new NpgsqlConnection(database.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            select count(*)
            from bodylife.visits
            where occurred_at >= @day_start
              and occurred_at < @next_day_start
              and status = 'active'
            """;
        command.Parameters.AddWithValue("day_start", dayStart);
        command.Parameters.AddWithValue("next_day_start", dayStart.AddDays(1));
        return Convert.ToInt32(await command.ExecuteScalarAsync());
    }

    private static async Task<string> ReadDailyVisitQueryPlanAsync(
        PostgreSqlTestDatabase database,
        DateOnly businessDate)
    {
        var dayStart = new DateTimeOffset(
            businessDate.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc));
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
            select visit.id
            from bodylife.visits visit
            inner join bodylife.clients client on client.id = visit.client_id
            where visit.occurred_at >= @day_start
              and visit.occurred_at < @next_day_start
            order by visit.occurred_at desc,
                     visit.recorded_at desc,
                     visit.id desc
            """;
        command.Parameters.AddWithValue("day_start", dayStart);
        command.Parameters.AddWithValue("next_day_start", dayStart.AddDays(1));
        var planLines = new List<string>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            planLines.Add(reader.GetString(0));
        }

        return string.Join(Environment.NewLine, planLines);
    }

    private static async Task UpdateActorIdentityAsync(
        PostgreSqlTestDatabase database,
        Guid accountId,
        string accountType,
        string role)
    {
        await using var connection = new NpgsqlConnection(database.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            update bodylife.accounts
            set account_type = @account_type,
                role = @role
            where id = @id
            """;
        command.Parameters.AddWithValue("account_type", accountType);
        command.Parameters.AddWithValue("role", role);
        command.Parameters.AddWithValue("id", accountId);
        Assert.Equal(1, await command.ExecuteNonQueryAsync());
    }

    private static async Task DeactivateActorAsync(
        PostgreSqlTestDatabase database,
        Guid accountId)
    {
        await using var connection = new NpgsqlConnection(database.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            update bodylife.accounts
            set is_active = false,
                deactivated_at = @deactivated_at
            where id = @id
            """;
        command.Parameters.AddWithValue("deactivated_at", TestNow);
        command.Parameters.AddWithValue("id", accountId);
        Assert.Equal(1, await command.ExecuteNonQueryAsync());
    }

    private static DailyVisitSourceSnapshot AssertSuccess(
        GetDailyVisitSourceRowsResult result,
        DateOnly businessDate)
    {
        Assert.Equal(GetDailyVisitSourceRowsStatus.Success, result.Status);
        var snapshot = Assert.IsType<DailyVisitSourceSnapshot>(result.Snapshot);
        Assert.Equal(businessDate, snapshot.BusinessDate);
        Assert.Null(result.ErrorCode);
        Assert.Null(result.ErrorMessage);
        Assert.Null(result.ErrorField);
        return snapshot;
    }

    private static void AssertFailure(
        GetDailyVisitSourceRowsResult result,
        GetDailyVisitSourceRowsStatus status,
        string? field = null)
    {
        Assert.Equal(status, result.Status);
        Assert.Null(result.Snapshot);
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
        Assert.Contains(
            services,
            descriptor => descriptor.ServiceType == typeof(TService)
                && descriptor.ImplementationType == typeof(TImplementation)
                && descriptor.Lifetime == ServiceLifetime.Scoped);
    }

    private sealed record DailyVisitSourceFixture(
        ActorContext Actor,
        Guid ClientId,
        Guid OtherClientId,
        Guid MembershipId);

    private sealed class RecordingVisitDayStatusProvider(
        VisitDayReconciliationStatus status)
        : IVisitDayReconciliationStatusProvider
    {
        public List<DateOnly> RequestedDates { get; } = [];

        public Task<VisitDayReconciliationStatus> GetStatusAsync(
            DateOnly businessDate,
            CancellationToken cancellationToken = default)
        {
            RequestedDates.Add(businessDate);
            return Task.FromResult(status);
        }
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }
}
