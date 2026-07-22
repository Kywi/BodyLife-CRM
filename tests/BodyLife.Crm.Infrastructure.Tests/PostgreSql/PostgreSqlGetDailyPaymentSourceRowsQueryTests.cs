using BodyLife.Crm.Application.Commands;
using BodyLife.Crm.Application.Queries;
using BodyLife.Crm.Infrastructure;
using BodyLife.Crm.Infrastructure.Persistence;
using BodyLife.Crm.Infrastructure.Persistence.Payments;
using BodyLife.Crm.Infrastructure.Persistence.UsersRoles;
using BodyLife.Crm.Modules.Payments;
using BodyLife.Crm.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using NpgsqlTypes;

namespace BodyLife.Crm.Infrastructure.Tests.PostgreSql;

public sealed class PostgreSqlGetDailyPaymentSourceRowsQueryTests
{
    private static readonly DateOnly BusinessDate = new(2026, 7, 15);
    private static readonly DateTimeOffset TestNow = new(
        2026,
        7,
        16,
        16,
        0,
        0,
        TimeSpan.Zero);

    [PostgreSqlFact]
    public async Task QueryIncludesBothEndsOfTheKyivFallBackBusinessDay()
    {
        var fallBackDate = new DateOnly(2026, 10, 25);
        var range = BusinessTimeZone.GetUtcDayRange(fallBackDate);
        Assert.Equal(TimeSpan.FromHours(25), range.ToExclusive - range.FromInclusive);

        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var dbContext = database.CreateDbContext();
        await dbContext.Database.MigrateAsync();
        var fixture = await SeedFixtureAsync(database, dbContext);
        var firstPaymentId = await InsertPaymentAsync(
            database, fixture, fixture.ClientId, null, 100m, "one_off",
            range.FromInclusive, TestNow);
        var lastPaymentId = await InsertPaymentAsync(
            database, fixture, fixture.ClientId, null, 200m, "one_off",
            range.ToExclusive.AddTicks(-1), TestNow);
        await InsertPaymentAsync(
            database, fixture, fixture.ClientId, null, 300m, "one_off",
            range.ToExclusive, TestNow);

        var result = await CreateHandler(
            dbContext,
            new RecordingPaymentDayStatusProvider(PaymentDayReconciliationStatus.Open))
            .ExecuteAsync(
                new GetDailyPaymentSourceRowsQuery(fixture.Actor, fallBackDate),
                CancellationToken.None);

        var snapshot = AssertSuccess(result, fallBackDate);
        Assert.Equal(2, snapshot.ActivePaymentCount);
        Assert.Equal(new Money(300m, "UAH"), snapshot.DailyCashSum);
        Assert.Equal([lastPaymentId, firstPaymentId], snapshot.Rows.Select(row => row.Payment.PaymentId));
    }

    [PostgreSqlFact]
    public async Task QueryTotalsEqualActiveDrillDownAndRetainCorrectionAndCancellationRows()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var dbContext = database.CreateDbContext();
        await dbContext.Database.MigrateAsync();
        var fixture = await SeedFixtureAsync(database, dbContext);
        var originalBatchId = Guid.NewGuid();
        var originalPaymentId = await InsertPaymentAsync(
            database,
            fixture,
            fixture.ClientId,
            fixture.MembershipId,
            1000m,
            "membership_sale",
            AtBusinessTime(9),
            TestNow.AddMinutes(-30),
            entryOrigin: "paper_fallback",
            entryBatchId: originalBatchId,
            comment: "Recovered membership sale",
            status: "replaced");
        var replacementPaymentId = await InsertPaymentAsync(
            database,
            fixture,
            fixture.ClientId,
            fixture.MembershipId,
            900m,
            "membership_sale",
            AtBusinessTime(10),
            TestNow.AddMinutes(-20),
            comment: "Corrected membership sale");
        var correctionBatchId = Guid.NewGuid();
        var correctionId = await InsertCorrectionAsync(
            database,
            fixture,
            originalPaymentId,
            replacementPaymentId,
            "[\"amount\",\"occurred_at\"]",
            "Daily cash amount was entered incorrectly",
            AtBusinessTime(11),
            TestNow.AddMinutes(-15),
            "manual_backfill",
            correctionBatchId);
        var canceledPaymentId = await InsertPaymentAsync(
            database,
            fixture,
            fixture.ClientId,
            membershipId: null,
            250m,
            "one_off",
            AtBusinessTime(12),
            TestNow.AddMinutes(-10),
            status: "canceled");
        var cancellationBatchId = Guid.NewGuid();
        var cancellationId = await InsertCancellationAsync(
            database,
            fixture,
            canceledPaymentId,
            "Duplicate daily cash entry",
            AtBusinessTime(12).AddMinutes(30),
            TestNow.AddMinutes(-8),
            "paper_fallback",
            cancellationBatchId);
        var oneOffPaymentId = await InsertPaymentAsync(
            database,
            fixture,
            fixture.ClientId,
            membershipId: null,
            300m,
            "one_off",
            AtBusinessTime(13),
            TestNow.AddMinutes(-5));
        var trialPaymentId = await InsertPaymentAsync(
            database,
            fixture,
            fixture.OtherClientId,
            membershipId: null,
            100m,
            "trial",
            AtBusinessTime(14),
            TestNow.AddMinutes(-1));
        await InsertPaymentAsync(
            database,
            fixture,
            fixture.ClientId,
            membershipId: null,
            10m,
            "other",
            AtBusinessTime(0).AddTicks(-1),
            TestNow);
        await InsertPaymentAsync(
            database,
            fixture,
            fixture.ClientId,
            membershipId: null,
            20m,
            "other",
            AtBusinessTime(0).AddDays(1),
            TestNow);
        var dayProvider = new RecordingPaymentDayStatusProvider(
            PaymentDayReconciliationStatus.Open);

        var result = await CreateHandler(dbContext, dayProvider).ExecuteAsync(
            new GetDailyPaymentSourceRowsQuery(fixture.Actor, BusinessDate),
            CancellationToken.None);

        var snapshot = AssertSuccess(result, BusinessDate);
        Assert.Equal(PaymentDayReconciliationStatus.Open, snapshot.DayStatus);
        Assert.Equal(3, snapshot.ActivePaymentCount);
        Assert.Equal(new Money(1300m, "UAH"), snapshot.DailyCashSum);
        Assert.Equal(
            snapshot.ActivePaymentCount,
            snapshot.Rows.Count(row =>
                row.Payment.Status == ClientPaymentRowStatus.Active));
        Assert.Equal(
            snapshot.DailyCashSum.Amount,
            snapshot.Rows
                .Where(row => row.Payment.Status == ClientPaymentRowStatus.Active)
                .Sum(row => row.Payment.Amount.Amount));
        Assert.Equal(
            new DailyCashSnapshot(3, 1300m),
            await ReadDailyCashAsync(database, BusinessDate));
        Assert.Contains(
            "ix_payments_daily_source",
            await ReadDailyPaymentQueryPlanAsync(database, BusinessDate),
            StringComparison.OrdinalIgnoreCase);
        Assert.Equal(
            [
                trialPaymentId,
                oneOffPaymentId,
                canceledPaymentId,
                replacementPaymentId,
                originalPaymentId,
            ],
            snapshot.Rows.Select(row => row.Payment.PaymentId).ToArray());

        var trial = snapshot.Rows[0];
        Assert.Equal("Other Cash Client", trial.ClientDisplayName);
        Assert.Equal(fixture.OtherClientId, trial.Payment.ClientId);

        var canceled = snapshot.Rows[2].Payment;
        Assert.Equal(ClientPaymentRowStatus.Canceled, canceled.Status);
        Assert.Empty(canceled.AllowedActions.Items);
        var cancellation = Assert.IsType<ClientPaymentCancellation>(
            canceled.Cancellation);
        Assert.Equal(cancellationId, cancellation.CancellationId);
        Assert.Equal("Duplicate daily cash entry", cancellation.Reason);
        Assert.Equal(EntryOrigin.PaperFallback, cancellation.EntryOrigin);
        Assert.Equal(cancellationBatchId, cancellation.EntryBatchId);

        var replacement = snapshot.Rows[3];
        Assert.Equal("Daily Cash", replacement.ClientDisplayName);
        Assert.Equal("Daily payment fixture", replacement.Payment.MembershipTypeNameSnapshot);
        Assert.Equal(new Money(900m, "UAH"), replacement.Payment.Amount);
        var incomingCorrection = Assert.IsType<ClientPaymentCorrection>(
            replacement.Payment.CorrectionFromOriginal);
        AssertCorrection(
            incomingCorrection,
            correctionId,
            originalPaymentId,
            replacementPaymentId,
            correctionBatchId);
        Assert.True(
            replacement.Payment.AllowedActions.IsAllowed(PaymentActionKeys.Correct));

        var original = snapshot.Rows[4].Payment;
        Assert.Equal(ClientPaymentRowStatus.Replaced, original.Status);
        Assert.Equal(EntryOrigin.PaperFallback, original.EntryOrigin);
        Assert.Equal(originalBatchId, original.EntryBatchId);
        var outgoingCorrection = Assert.IsType<ClientPaymentCorrection>(
            original.CorrectionToReplacement);
        AssertCorrection(
            outgoingCorrection,
            correctionId,
            originalPaymentId,
            replacementPaymentId,
            correctionBatchId);
        Assert.Empty(original.AllowedActions.Items);
        Assert.Equal([BusinessDate], dayProvider.RequestedDates);
        Assert.Equal(
            0L,
            await database.ExecuteScalarAsync<long>(
                "select count(*) from bodylife.business_audit_entries"));
    }

    [PostgreSqlFact]
    public async Task CorrectionAcrossDatesKeepsBothBusinessDatesExplainable()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var dbContext = database.CreateDbContext();
        await dbContext.Database.MigrateAsync();
        var fixture = await SeedFixtureAsync(database, dbContext);
        var replacementDate = BusinessDate.AddDays(1);
        var originalPaymentId = await InsertPaymentAsync(
            database,
            fixture,
            fixture.ClientId,
            fixture.MembershipId,
            1000m,
            "membership_sale",
            AtBusinessTime(10),
            TestNow.AddMinutes(-10),
            status: "replaced");
        var replacementPaymentId = await InsertPaymentAsync(
            database,
            fixture,
            fixture.ClientId,
            fixture.MembershipId,
            900m,
            "membership_sale",
            AtBusinessTime(11).AddDays(1),
            TestNow.AddMinutes(-5));
        var correctionId = await InsertCorrectionAsync(
            database,
            fixture,
            originalPaymentId,
            replacementPaymentId,
            "[\"amount\",\"occurred_at\"]",
            "Moved payment to the correct business date",
            AtBusinessTime(12).AddDays(1),
            TestNow,
            "normal",
            entryBatchId: null);
        var dayProvider = new RecordingPaymentDayStatusProvider(
            PaymentDayReconciliationStatus.Open);
        var handler = CreateHandler(dbContext, dayProvider);

        var originalResult = await handler.ExecuteAsync(
            new GetDailyPaymentSourceRowsQuery(fixture.Actor, BusinessDate),
            CancellationToken.None);
        var replacementResult = await handler.ExecuteAsync(
            new GetDailyPaymentSourceRowsQuery(fixture.Actor, replacementDate),
            CancellationToken.None);

        var originalSnapshot = AssertSuccess(originalResult, BusinessDate);
        Assert.Equal(0, originalSnapshot.ActivePaymentCount);
        Assert.Equal(new Money(0m, "UAH"), originalSnapshot.DailyCashSum);
        var original = Assert.Single(originalSnapshot.Rows).Payment;
        Assert.Equal(ClientPaymentRowStatus.Replaced, original.Status);
        Assert.Equal(
            correctionId,
            Assert.IsType<ClientPaymentCorrection>(original.CorrectionToReplacement)
                .CorrectionId);

        var replacementSnapshot = AssertSuccess(replacementResult, replacementDate);
        Assert.Equal(1, replacementSnapshot.ActivePaymentCount);
        Assert.Equal(new Money(900m, "UAH"), replacementSnapshot.DailyCashSum);
        var replacement = Assert.Single(replacementSnapshot.Rows).Payment;
        Assert.Equal(ClientPaymentRowStatus.Active, replacement.Status);
        Assert.Equal(
            correctionId,
            Assert.IsType<ClientPaymentCorrection>(replacement.CorrectionFromOriginal)
                .CorrectionId);
        Assert.Equal([BusinessDate, replacementDate], dayProvider.RequestedDates);
    }

    [PostgreSqlFact]
    public async Task QueryUsesKyivBusinessDayHalfOpenRangeAndDeterministicOrdering()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var dbContext = database.CreateDbContext();
        await dbContext.Database.MigrateAsync();
        var fixture = await SeedFixtureAsync(database, dbContext);
        var firstId = Guid.Parse("00000000-0000-0000-0000-000000000001");
        var secondId = Guid.Parse("00000000-0000-0000-0000-000000000002");
        var thirdId = Guid.Parse("00000000-0000-0000-0000-000000000003");
        foreach (var paymentId in new[] { firstId, secondId, thirdId })
        {
            await InsertPaymentAsync(
                database,
                fixture,
                fixture.ClientId,
                membershipId: null,
                100m,
                "one_off",
                AtBusinessTime(10),
                TestNow,
                paymentId: paymentId);
        }

        await InsertPaymentAsync(
            database,
            fixture,
            fixture.ClientId,
            membershipId: null,
            10m,
            "other",
            AtBusinessTime(0).AddTicks(-1),
            TestNow);
        await InsertPaymentAsync(
            database,
            fixture,
            fixture.ClientId,
            membershipId: null,
            20m,
            "other",
            AtBusinessTime(0).AddDays(1),
            TestNow);
        var dayProvider = new RecordingPaymentDayStatusProvider(
            PaymentDayReconciliationStatus.Open);
        var handler = CreateHandler(dbContext, dayProvider);

        var result = await handler.ExecuteAsync(
            new GetDailyPaymentSourceRowsQuery(fixture.Actor, BusinessDate),
            CancellationToken.None);
        var emptyResult = await handler.ExecuteAsync(
            new GetDailyPaymentSourceRowsQuery(
                fixture.Actor,
                BusinessDate.AddDays(2)),
            CancellationToken.None);

        var snapshot = AssertSuccess(result, BusinessDate);
        Assert.Equal(
            [thirdId, secondId, firstId],
            snapshot.Rows.Select(row => row.Payment.PaymentId).ToArray());
        Assert.Equal(3, snapshot.ActivePaymentCount);
        Assert.Equal(new Money(300m, "UAH"), snapshot.DailyCashSum);
        var emptySnapshot = AssertSuccess(emptyResult, BusinessDate.AddDays(2));
        Assert.Empty(emptySnapshot.Rows);
        Assert.Equal(0, emptySnapshot.ActivePaymentCount);
        Assert.Equal(new Money(0m, "UAH"), emptySnapshot.DailyCashSum);
        Assert.Equal(
            [BusinessDate, BusinessDate.AddDays(2)],
            dayProvider.RequestedDates);
    }

    [PostgreSqlFact]
    public async Task QueryIncludesKyivBusinessDayAcrossUtcDateAndExactBoundaries()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var dbContext = database.CreateDbContext();
        await dbContext.Database.MigrateAsync();
        var fixture = await SeedFixtureAsync(database, dbContext);
        var range = BusinessTimeZone.GetUtcDayRange(BusinessDate);
        var firstId = await InsertPaymentAsync(
            database,
            fixture,
            fixture.ClientId,
            membershipId: null,
            100m,
            "one_off",
            range.FromInclusive,
            TestNow);
        var lastId = await InsertPaymentAsync(
            database,
            fixture,
            fixture.ClientId,
            membershipId: null,
            200m,
            "one_off",
            range.ToExclusive.AddTicks(-1),
            TestNow);
        await InsertPaymentAsync(
            database,
            fixture,
            fixture.ClientId,
            membershipId: null,
            300m,
            "one_off",
            range.ToExclusive,
            TestNow);

        var result = await CreateHandler(
                dbContext,
                new RecordingPaymentDayStatusProvider(PaymentDayReconciliationStatus.Open))
            .ExecuteAsync(
                new GetDailyPaymentSourceRowsQuery(fixture.Actor, BusinessDate),
                CancellationToken.None);

        var snapshot = AssertSuccess(result, BusinessDate);
        Assert.NotEqual(BusinessDate, DateOnly.FromDateTime(range.FromInclusive.UtcDateTime));
        Assert.Equal(new Money(300m, "UAH"), snapshot.DailyCashSum);
        Assert.Equal([lastId, firstId], snapshot.Rows.Select(row => row.Payment.PaymentId));
    }

    [PostgreSqlFact]
    public async Task ReconciledDayReturnsOwnerAndAdminPermissionsAndReservesNegativeClosure()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var dbContext = database.CreateDbContext();
        await dbContext.Database.MigrateAsync();
        var fixture = await SeedFixtureAsync(database, dbContext);
        var ordinaryPaymentId = await InsertPaymentAsync(
            database,
            fixture,
            fixture.ClientId,
            membershipId: null,
            300m,
            "one_off",
            AtBusinessTime(10),
            TestNow);
        var negativeClosurePaymentId = await InsertPaymentAsync(
            database,
            fixture,
            fixture.ClientId,
            fixture.MembershipId,
            100m,
            "negative_closure",
            AtBusinessTime(11),
            TestNow);
        var dayProvider = new RecordingPaymentDayStatusProvider(
            PaymentDayReconciliationStatus.Reconciled);
        var handler = CreateHandler(dbContext, dayProvider);

        var ownerResult = await handler.ExecuteAsync(
            new GetDailyPaymentSourceRowsQuery(fixture.Actor, BusinessDate),
            CancellationToken.None);

        var ownerSnapshot = AssertSuccess(ownerResult, BusinessDate);
        Assert.Equal(new Money(400m, "UAH"), ownerSnapshot.DailyCashSum);
        var ownerOrdinary = Assert.Single(ownerSnapshot.Rows, row =>
            row.Payment.PaymentId == ordinaryPaymentId).Payment;
        var ownerPermission = Assert.Single(ownerOrdinary.AllowedActions.Items);
        Assert.True(ownerPermission.IsAllowed);
        Assert.Equal(PaymentActionKeys.OwnerPolicy, ownerPermission.RequiredPolicy);
        Assert.Empty(Assert.Single(ownerSnapshot.Rows, row =>
            row.Payment.PaymentId == negativeClosurePaymentId)
            .Payment.AllowedActions.Items);

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
            new GetDailyPaymentSourceRowsQuery(adminActor, BusinessDate),
            CancellationToken.None);

        var adminSnapshot = AssertSuccess(adminResult, BusinessDate);
        var adminOrdinary = Assert.Single(adminSnapshot.Rows, row =>
            row.Payment.PaymentId == ordinaryPaymentId).Payment;
        var adminPermission = Assert.Single(adminOrdinary.AllowedActions.Items);
        Assert.False(adminPermission.IsAllowed);
        Assert.Equal(PaymentActionKeys.OwnerPolicy, adminPermission.RequiredPolicy);
        Assert.Equal("day_closed_requires_owner", adminPermission.DeniedReasonCode);
        Assert.Empty(Assert.Single(adminSnapshot.Rows, row =>
            row.Payment.PaymentId == negativeClosurePaymentId)
            .Payment.AllowedActions.Items);
        Assert.Equal([BusinessDate, BusinessDate], dayProvider.RequestedDates);
    }

    [PostgreSqlFact]
    public async Task EmptyValidationAndInactiveActorReturnNoAuthoritativeRows()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var dbContext = database.CreateDbContext();
        await dbContext.Database.MigrateAsync();
        var fixture = await SeedFixtureAsync(database, dbContext);
        var dayProvider = new RecordingPaymentDayStatusProvider(
            PaymentDayReconciliationStatus.Open);
        var handler = CreateHandler(dbContext, dayProvider);

        var empty = await handler.ExecuteAsync(
            new GetDailyPaymentSourceRowsQuery(fixture.Actor, BusinessDate),
            CancellationToken.None);
        var invalidDefault = await handler.ExecuteAsync(
            new GetDailyPaymentSourceRowsQuery(fixture.Actor, default),
            CancellationToken.None);
        var invalidMaximum = await handler.ExecuteAsync(
            new GetDailyPaymentSourceRowsQuery(fixture.Actor, DateOnly.MaxValue),
            CancellationToken.None);
        await DeactivateActorAsync(database, fixture.Actor.AccountId.Value);
        var denied = await handler.ExecuteAsync(
            new GetDailyPaymentSourceRowsQuery(fixture.Actor, BusinessDate),
            CancellationToken.None);

        var emptySnapshot = AssertSuccess(empty, BusinessDate);
        Assert.Empty(emptySnapshot.Rows);
        Assert.Equal(0, emptySnapshot.ActivePaymentCount);
        Assert.Equal(new Money(0m, "UAH"), emptySnapshot.DailyCashSum);
        AssertFailure(
            invalidDefault,
            GetDailyPaymentSourceRowsStatus.ValidationFailed,
            "businessDate");
        AssertFailure(
            invalidMaximum,
            GetDailyPaymentSourceRowsStatus.ValidationFailed,
            "businessDate");
        AssertFailure(denied, GetDailyPaymentSourceRowsStatus.PermissionDenied);
        Assert.Equal([BusinessDate], dayProvider.RequestedDates);
    }

    [PostgreSqlFact]
    public async Task InconsistentCanonicalSourceFailsTheWholeSnapshot()
    {
        await AssertInconsistentSourceAsync("missing_cancellation");
        await AssertInconsistentSourceAsync("missing_correction");
        await AssertInconsistentSourceAsync("mixed_active_currencies");
    }

    [Fact]
    public void PersistenceRegistrationResolvesDailyPaymentSourceRowsQuery()
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

        Assert.Contains(
            services,
            descriptor => descriptor.ServiceType == typeof(
                    IBodyLifeQueryHandler<
                        GetDailyPaymentSourceRowsQuery,
                        GetDailyPaymentSourceRowsResult>)
                && descriptor.ImplementationType
                    == typeof(GetDailyPaymentSourceRowsQueryHandler)
                && descriptor.Lifetime == ServiceLifetime.Scoped);

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<
            IBodyLifeQueryHandler<
                GetDailyPaymentSourceRowsQuery,
                GetDailyPaymentSourceRowsResult>>());
    }

    private static async Task AssertInconsistentSourceAsync(string inconsistency)
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var dbContext = database.CreateDbContext();
        await dbContext.Database.MigrateAsync();
        var fixture = await SeedFixtureAsync(database, dbContext);

        if (inconsistency == "missing_cancellation")
        {
            await InsertPaymentAsync(
                database,
                fixture,
                fixture.ClientId,
                membershipId: null,
                100m,
                "one_off",
                AtBusinessTime(10),
                TestNow,
                status: "canceled");
        }
        else if (inconsistency == "missing_correction")
        {
            await InsertPaymentAsync(
                database,
                fixture,
                fixture.ClientId,
                membershipId: null,
                100m,
                "one_off",
                AtBusinessTime(10),
                TestNow,
                status: "replaced");
        }
        else
        {
            Assert.Equal("mixed_active_currencies", inconsistency);
            await InsertPaymentAsync(
                database,
                fixture,
                fixture.ClientId,
                membershipId: null,
                100m,
                "one_off",
                AtBusinessTime(10),
                TestNow);
            await InsertPaymentAsync(
                database,
                fixture,
                fixture.ClientId,
                membershipId: null,
                10m,
                "one_off",
                AtBusinessTime(11),
                TestNow,
                currency: "USD");
        }

        var result = await CreateHandler(
            dbContext,
            new RecordingPaymentDayStatusProvider(
                PaymentDayReconciliationStatus.Open)).ExecuteAsync(
                new GetDailyPaymentSourceRowsQuery(fixture.Actor, BusinessDate),
                CancellationToken.None);

        AssertFailure(result, GetDailyPaymentSourceRowsStatus.SourceInconsistent);
        Assert.Equal("source_inconsistent", result.ErrorCode);
    }

    private static GetDailyPaymentSourceRowsQueryHandler CreateHandler(
        BodyLifeDbContext dbContext,
        IPaymentDayReconciliationStatusProvider dayStatusProvider)
    {
        return new GetDailyPaymentSourceRowsQueryHandler(
            dbContext,
            dayStatusProvider,
            new FixedTimeProvider(TestNow));
    }

    private static DateTimeOffset AtBusinessTime(int hour)
    {
        return BusinessTimeZone.ConvertLocalToUtc(
            BusinessDate.ToDateTime(new TimeOnly(hour, 0), DateTimeKind.Unspecified));
    }

    private static async Task<DailyPaymentSourceFixture> SeedFixtureAsync(
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
                'Daily cash tablet',
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
                    'Cash',
                    null,
                    'DAILY CASH',
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
                    'Cash',
                    'Client',
                    'OTHER CASH CLIENT',
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
                'Daily payment fixture',
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
                'Daily payment fixture',
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

        return new DailyPaymentSourceFixture(
            new ActorContext(
                new AccountId(accountId),
                ActorRole.Owner,
                AccountKind.Owner,
                new SessionId(sessionId),
                "Daily cash tablet"),
            clientId,
            otherClientId,
            membershipId);
    }

    private static async Task<Guid> InsertPaymentAsync(
        PostgreSqlTestDatabase database,
        DailyPaymentSourceFixture fixture,
        Guid clientId,
        Guid? membershipId,
        decimal amount,
        string paymentContext,
        DateTimeOffset occurredAt,
        DateTimeOffset recordedAt,
        string entryOrigin = "normal",
        Guid? entryBatchId = null,
        string? comment = null,
        string status = "active",
        Guid? paymentId = null,
        string currency = "UAH")
    {
        var id = paymentId ?? Guid.NewGuid();
        await using var connection = new NpgsqlConnection(database.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            insert into bodylife.payments (
                id,
                client_id,
                membership_id,
                amount,
                currency,
                method,
                payment_context,
                occurred_at,
                recorded_at,
                recorded_by_account_id,
                session_id,
                entry_origin,
                entry_batch_id,
                comment,
                status)
            values (
                @id,
                @client_id,
                @membership_id,
                @amount,
                @currency,
                'cash',
                @payment_context,
                @occurred_at,
                @recorded_at,
                @account_id,
                @session_id,
                @entry_origin,
                @entry_batch_id,
                @comment,
                @status)
            """;
        command.Parameters.AddWithValue("id", id);
        command.Parameters.AddWithValue("client_id", clientId);
        command.Parameters.Add("membership_id", NpgsqlDbType.Uuid).Value =
            membershipId ?? (object)DBNull.Value;
        command.Parameters.AddWithValue("amount", amount);
        command.Parameters.AddWithValue("currency", currency);
        command.Parameters.AddWithValue("payment_context", paymentContext);
        command.Parameters.AddWithValue("occurred_at", occurredAt);
        command.Parameters.AddWithValue("recorded_at", recordedAt);
        command.Parameters.AddWithValue("account_id", fixture.Actor.AccountId.Value);
        command.Parameters.AddWithValue("session_id", fixture.Actor.SessionId.Value);
        command.Parameters.AddWithValue("entry_origin", entryOrigin);
        command.Parameters.Add("entry_batch_id", NpgsqlDbType.Uuid).Value =
            entryBatchId ?? (object)DBNull.Value;
        command.Parameters.Add("comment", NpgsqlDbType.Varchar).Value =
            comment ?? (object)DBNull.Value;
        command.Parameters.AddWithValue("status", status);
        Assert.Equal(1, await command.ExecuteNonQueryAsync());
        return id;
    }

    private static async Task<Guid> InsertCancellationAsync(
        PostgreSqlTestDatabase database,
        DailyPaymentSourceFixture fixture,
        Guid paymentId,
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
            insert into bodylife.payment_cancellations (
                id,
                payment_id,
                reason,
                occurred_at,
                recorded_at,
                recorded_by_account_id,
                session_id,
                entry_origin,
                entry_batch_id)
            values (
                @id,
                @payment_id,
                @reason,
                @occurred_at,
                @recorded_at,
                @account_id,
                @session_id,
                @entry_origin,
                @entry_batch_id)
            """;
        command.Parameters.AddWithValue("id", cancellationId);
        command.Parameters.AddWithValue("payment_id", paymentId);
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

    private static async Task<Guid> InsertCorrectionAsync(
        PostgreSqlTestDatabase database,
        DailyPaymentSourceFixture fixture,
        Guid originalPaymentId,
        Guid replacementPaymentId,
        string changedFieldsJson,
        string reason,
        DateTimeOffset occurredAt,
        DateTimeOffset recordedAt,
        string entryOrigin,
        Guid? entryBatchId)
    {
        var correctionId = Guid.NewGuid();
        await using var connection = new NpgsqlConnection(database.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            insert into bodylife.payment_corrections (
                id,
                client_id,
                original_payment_id,
                replacement_payment_id,
                changed_fields,
                reason,
                occurred_at,
                recorded_at,
                recorded_by_account_id,
                session_id,
                entry_origin,
                entry_batch_id)
            values (
                @id,
                @client_id,
                @original_payment_id,
                @replacement_payment_id,
                @changed_fields,
                @reason,
                @occurred_at,
                @recorded_at,
                @account_id,
                @session_id,
                @entry_origin,
                @entry_batch_id)
            """;
        command.Parameters.AddWithValue("id", correctionId);
        command.Parameters.AddWithValue("client_id", fixture.ClientId);
        command.Parameters.AddWithValue("original_payment_id", originalPaymentId);
        command.Parameters.AddWithValue("replacement_payment_id", replacementPaymentId);
        command.Parameters.AddWithValue(
            "changed_fields",
            NpgsqlDbType.Jsonb,
            changedFieldsJson);
        command.Parameters.AddWithValue("reason", reason);
        command.Parameters.AddWithValue("occurred_at", occurredAt);
        command.Parameters.AddWithValue("recorded_at", recordedAt);
        command.Parameters.AddWithValue("account_id", fixture.Actor.AccountId.Value);
        command.Parameters.AddWithValue("session_id", fixture.Actor.SessionId.Value);
        command.Parameters.AddWithValue("entry_origin", entryOrigin);
        command.Parameters.Add("entry_batch_id", NpgsqlDbType.Uuid).Value =
            entryBatchId ?? (object)DBNull.Value;
        Assert.Equal(1, await command.ExecuteNonQueryAsync());
        return correctionId;
    }

    private static async Task<DailyCashSnapshot> ReadDailyCashAsync(
        PostgreSqlTestDatabase database,
        DateOnly businessDate)
    {
        var dayRange = BusinessTimeZone.GetUtcDayRange(businessDate);
        await using var connection = new NpgsqlConnection(database.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            select count(*), coalesce(sum(amount), 0)
            from bodylife.payments
            where occurred_at >= @day_start
              and occurred_at < @next_day_start
              and status = 'active'
              and method = 'cash'
            """;
        command.Parameters.AddWithValue("day_start", dayRange.FromInclusive);
        command.Parameters.AddWithValue("next_day_start", dayRange.ToExclusive);
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        return new DailyCashSnapshot(reader.GetInt32(0), reader.GetDecimal(1));
    }

    private static async Task<string> ReadDailyPaymentQueryPlanAsync(
        PostgreSqlTestDatabase database,
        DateOnly businessDate)
    {
        var dayRange = BusinessTimeZone.GetUtcDayRange(businessDate);
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
            select payment.id
            from bodylife.payments payment
            inner join bodylife.clients client on client.id = payment.client_id
            left join bodylife.issued_memberships membership
                on membership.id = payment.membership_id
            where payment.occurred_at >= @day_start
              and payment.occurred_at < @next_day_start
            order by payment.occurred_at desc,
                     payment.recorded_at desc,
                     payment.id desc
            """;
        command.Parameters.AddWithValue("day_start", dayRange.FromInclusive);
        command.Parameters.AddWithValue("next_day_start", dayRange.ToExclusive);
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

    private static DailyPaymentSourceSnapshot AssertSuccess(
        GetDailyPaymentSourceRowsResult result,
        DateOnly businessDate)
    {
        Assert.Equal(GetDailyPaymentSourceRowsStatus.Success, result.Status);
        var snapshot = Assert.IsType<DailyPaymentSourceSnapshot>(result.Snapshot);
        Assert.Equal(businessDate, snapshot.BusinessDate);
        Assert.Null(result.ErrorCode);
        Assert.Null(result.ErrorMessage);
        Assert.Null(result.ErrorField);
        return snapshot;
    }

    private static void AssertFailure(
        GetDailyPaymentSourceRowsResult result,
        GetDailyPaymentSourceRowsStatus status,
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

    private static void AssertCorrection(
        ClientPaymentCorrection correction,
        Guid correctionId,
        Guid originalPaymentId,
        Guid replacementPaymentId,
        Guid correctionBatchId)
    {
        Assert.Equal(correctionId, correction.CorrectionId);
        Assert.Equal(originalPaymentId, correction.OriginalPaymentId);
        Assert.Equal(replacementPaymentId, correction.ReplacementPaymentId);
        Assert.Equal(["amount", "occurred_at"], correction.ChangedFields);
        Assert.Equal("Daily cash amount was entered incorrectly", correction.Reason);
        Assert.Equal(EntryOrigin.ManualBackfill, correction.EntryOrigin);
        Assert.Equal(correctionBatchId, correction.EntryBatchId);
    }

    private sealed record DailyPaymentSourceFixture(
        ActorContext Actor,
        Guid ClientId,
        Guid OtherClientId,
        Guid MembershipId);

    private sealed record DailyCashSnapshot(int PaymentCount, decimal CashSum);

    private sealed class RecordingPaymentDayStatusProvider(
        PaymentDayReconciliationStatus status)
        : IPaymentDayReconciliationStatusProvider
    {
        public List<DateOnly> RequestedDates { get; } = [];

        public Task<PaymentDayReconciliationStatus> GetStatusAsync(
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
