using BodyLife.Crm.Application.Queries;
using BodyLife.Crm.Infrastructure.Persistence;
using BodyLife.Crm.Infrastructure.Persistence.Payments;
using BodyLife.Crm.Infrastructure.Persistence.Reports;
using BodyLife.Crm.Infrastructure.Persistence.UsersRoles;
using BodyLife.Crm.Infrastructure.Persistence.Visits;
using BodyLife.Crm.Modules.Payments;
using BodyLife.Crm.Modules.Reports;
using BodyLife.Crm.Modules.Visits;
using BodyLife.Crm.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using NpgsqlTypes;

namespace BodyLife.Crm.Infrastructure.Tests.PostgreSql;

public sealed class PostgreSqlGenerateDailyReportQueryTests
{
    private static readonly DateOnly BusinessDate = new(2026, 7, 19);
    private static readonly DateTimeOffset TestNow = new(
        2026,
        7,
        19,
        18,
        0,
        0,
        TimeSpan.Zero);

    [PostgreSqlFact]
    public async Task ReportTotalsEqualActiveRowsAndRetainCorrectionAndCancellationFacts()
    {
        await using var database = await PostgreSqlTestDatabase.CreateAsync();
        await using var dbContext = database.CreateDbContext();
        await dbContext.Database.MigrateAsync();
        var fixture = await SeedFixtureAsync(database, dbContext);
        var visitStatusProvider = new RecordingVisitDayStatusProvider(
            VisitDayReconciliationStatus.Open);
        var paymentStatusProvider = new RecordingPaymentDayStatusProvider(
            PaymentDayReconciliationStatus.Open);
        var handler = CreateHandler(
            dbContext,
            visitStatusProvider,
            paymentStatusProvider);

        var result = await handler.ExecuteAsync(
            new GenerateDailyReportQuery(
                fixture.Actor,
                BusinessDate,
                IncludeDrillDown: true),
            CancellationToken.None);

        Assert.Equal(GenerateDailyReportStatus.Success, result.Status);
        var report = Assert.IsType<DailyReportSnapshot>(result.Report);
        Assert.Equal(BusinessDate, report.BusinessDate);
        Assert.Equal(DailyReportDayStatus.Open, report.DayStatus);
        Assert.Equal(1, report.VisitCount);
        Assert.Equal(1, report.PaymentCount);
        Assert.Equal(new Money(900m, "UAH"), report.DailyCashSum);
        Assert.Equal(
            report.VisitCount,
            report.VisitRows.Count(row =>
                row.Visit.Status == ClientVisitRowStatus.Active));
        Assert.Equal(
            report.PaymentCount,
            report.PaymentRows.Count(row =>
                row.Payment.Status == ClientPaymentRowStatus.Active));
        Assert.Equal(
            report.DailyCashSum.Amount,
            report.PaymentRows
                .Where(row => row.Payment.Status == ClientPaymentRowStatus.Active)
                .Sum(row => row.Payment.Amount.Amount));
        Assert.Equal(2, report.VisitRows.Count);
        Assert.Equal(3, report.PaymentRows.Count);

        var canceledVisit = Assert.Single(report.CanceledVisitRows).Visit;
        Assert.Equal(fixture.CanceledVisitId, canceledVisit.VisitId);
        Assert.NotNull(canceledVisit.Cancellation);
        var canceledPayment = Assert.Single(report.CanceledPaymentRows).Payment;
        Assert.Equal(fixture.CanceledPaymentId, canceledPayment.PaymentId);
        Assert.NotNull(canceledPayment.Cancellation);
        Assert.Equal(
            [fixture.ReplacementPaymentId, fixture.OriginalPaymentId],
            report.CorrectedPaymentRows
                .Select(row => row.Payment.PaymentId)
                .ToArray());
        Assert.All(report.CorrectedPaymentRows, row => Assert.True(
            row.Payment.CorrectionFromOriginal is not null
            || row.Payment.CorrectionToReplacement is not null));
        Assert.Equal([BusinessDate], visitStatusProvider.RequestedDates);
        Assert.Equal([BusinessDate], paymentStatusProvider.RequestedDates);
        Assert.Null(result.ErrorCode);
        Assert.Null(result.ErrorMessage);
        Assert.Null(result.ErrorField);
        Assert.Equal(
            0L,
            await database.ExecuteScalarAsync<long>(
                "select count(*) from bodylife.business_audit_entries"));
    }

    [Fact]
    public async Task SourceFailuresNeverReturnPartialTotals()
    {
        var actor = CreateActor();
        var paymentHandler = new StubPaymentSourceHandler(
            GetDailyPaymentSourceRowsResult.Succeeded(
                CreatePaymentSnapshot(BusinessDate)));
        var deniedHandler = new GenerateDailyReportQueryHandler(
            new StubVisitSourceHandler(GetDailyVisitSourceRowsResult.Denied()),
            paymentHandler);

        var denied = await deniedHandler.ExecuteAsync(
            new GenerateDailyReportQuery(actor, BusinessDate),
            CancellationToken.None);

        AssertFailure(denied, GenerateDailyReportStatus.PermissionDenied);
        Assert.Equal(0, paymentHandler.CallCount);

        var sourceFailure = await new GenerateDailyReportQueryHandler(
            new StubVisitSourceHandler(
                GetDailyVisitSourceRowsResult.Succeeded(
                    CreateVisitSnapshot(BusinessDate))),
            new StubPaymentSourceHandler(
                GetDailyPaymentSourceRowsResult.InconsistentSource()))
            .ExecuteAsync(
                new GenerateDailyReportQuery(actor, BusinessDate),
                CancellationToken.None);

        AssertFailure(sourceFailure, GenerateDailyReportStatus.SourceInconsistent);
    }

    [Fact]
    public async Task MismatchedSourceDatesAndDayStatusesFailClosed()
    {
        var actor = CreateActor();
        var mismatchedDate = await new GenerateDailyReportQueryHandler(
            new StubVisitSourceHandler(
                GetDailyVisitSourceRowsResult.Succeeded(
                    CreateVisitSnapshot(BusinessDate))),
            new StubPaymentSourceHandler(
                GetDailyPaymentSourceRowsResult.Succeeded(
                    CreatePaymentSnapshot(BusinessDate.AddDays(1)))))
            .ExecuteAsync(
                new GenerateDailyReportQuery(actor, BusinessDate),
                CancellationToken.None);
        var mismatchedStatus = await new GenerateDailyReportQueryHandler(
            new StubVisitSourceHandler(
                GetDailyVisitSourceRowsResult.Succeeded(
                    CreateVisitSnapshot(BusinessDate))),
            new StubPaymentSourceHandler(
                GetDailyPaymentSourceRowsResult.Succeeded(
                    CreatePaymentSnapshot(
                        BusinessDate,
                        PaymentDayReconciliationStatus.Reconciled))))
            .ExecuteAsync(
                new GenerateDailyReportQuery(actor, BusinessDate),
                CancellationToken.None);

        AssertFailure(mismatchedDate, GenerateDailyReportStatus.SourceInconsistent);
        AssertFailure(mismatchedStatus, GenerateDailyReportStatus.SourceInconsistent);
    }

    [Fact]
    public void PersistenceRegistrationResolvesGenerateDailyReportQuery()
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
                        GenerateDailyReportQuery,
                        GenerateDailyReportResult>)
                && descriptor.ImplementationType
                    == typeof(GenerateDailyReportQueryHandler)
                && descriptor.Lifetime == ServiceLifetime.Scoped);
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<
            IBodyLifeQueryHandler<
                GenerateDailyReportQuery,
                GenerateDailyReportResult>>());
    }

    private static GenerateDailyReportQueryHandler CreateHandler(
        BodyLifeDbContext dbContext,
        IVisitDayReconciliationStatusProvider visitStatusProvider,
        IPaymentDayReconciliationStatusProvider paymentStatusProvider)
    {
        var timeProvider = new FixedTimeProvider(TestNow);
        return new GenerateDailyReportQueryHandler(
            new GetDailyVisitSourceRowsQueryHandler(
                dbContext,
                visitStatusProvider,
                timeProvider),
            new GetDailyPaymentSourceRowsQueryHandler(
                dbContext,
                paymentStatusProvider,
                timeProvider));
    }

    private static async Task<DailyReportFixture> SeedFixtureAsync(
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
        var activeVisitId = Guid.NewGuid();
        var canceledVisitId = Guid.NewGuid();
        var originalPaymentId = Guid.NewGuid();
        var replacementPaymentId = Guid.NewGuid();
        var canceledPaymentId = Guid.NewGuid();

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
                'Daily',
                'Report',
                null,
                'DAILY REPORT',
                null,
                null,
                null,
                null,
                'active',
                @created_at,
                @account_id,
                @created_at);

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
            values
                (
                    @active_visit_id,
                    @client_id,
                    @active_visit_occurred_at,
                    @recorded_at,
                    @account_id,
                    @session_id,
                    'one_off',
                    'normal',
                    null,
                    'Active report Visit',
                    'active'),
                (
                    @canceled_visit_id,
                    @client_id,
                    @canceled_visit_occurred_at,
                    @recorded_at,
                    @account_id,
                    @session_id,
                    'one_off',
                    'normal',
                    null,
                    'Canceled report Visit',
                    'canceled');

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
                @visit_cancellation_id,
                @canceled_visit_id,
                'Duplicate report Visit',
                @visit_cancellation_occurred_at,
                @recorded_at,
                @account_id,
                @session_id,
                'normal',
                null);

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
            values
                (
                    @original_payment_id,
                    @client_id,
                    null,
                    1000,
                    'UAH',
                    'cash',
                    'one_off',
                    @original_payment_occurred_at,
                    @recorded_at,
                    @account_id,
                    @session_id,
                    'normal',
                    null,
                    'Original report Payment',
                    'replaced'),
                (
                    @replacement_payment_id,
                    @client_id,
                    null,
                    900,
                    'UAH',
                    'cash',
                    'one_off',
                    @replacement_payment_occurred_at,
                    @recorded_at,
                    @account_id,
                    @session_id,
                    'normal',
                    null,
                    'Replacement report Payment',
                    'active'),
                (
                    @canceled_payment_id,
                    @client_id,
                    null,
                    250,
                    'UAH',
                    'cash',
                    'one_off',
                    @canceled_payment_occurred_at,
                    @recorded_at,
                    @account_id,
                    @session_id,
                    'normal',
                    null,
                    'Canceled report Payment',
                    'canceled');

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
                @payment_correction_id,
                @client_id,
                @original_payment_id,
                @replacement_payment_id,
                @changed_fields,
                'Corrected report amount',
                @payment_correction_occurred_at,
                @recorded_at,
                @account_id,
                @session_id,
                'normal',
                null);

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
                @payment_cancellation_id,
                @canceled_payment_id,
                'Duplicate report Payment',
                @payment_cancellation_occurred_at,
                @recorded_at,
                @account_id,
                @session_id,
                'normal',
                null)
            """;
        command.Parameters.AddWithValue("session_id", sessionId);
        command.Parameters.AddWithValue("account_id", accountId);
        command.Parameters.AddWithValue("started_at", TestNow.AddHours(-2));
        command.Parameters.AddWithValue("expires_at", TestNow.AddHours(8));
        command.Parameters.AddWithValue("last_seen_at", TestNow.AddMinutes(-5));
        command.Parameters.AddWithValue("client_id", clientId);
        command.Parameters.AddWithValue("created_at", TestNow.AddDays(-30));
        command.Parameters.AddWithValue("active_visit_id", activeVisitId);
        command.Parameters.AddWithValue("canceled_visit_id", canceledVisitId);
        command.Parameters.AddWithValue("visit_cancellation_id", Guid.NewGuid());
        command.Parameters.AddWithValue(
            "active_visit_occurred_at",
            AtBusinessTime(9));
        command.Parameters.AddWithValue(
            "canceled_visit_occurred_at",
            AtBusinessTime(10));
        command.Parameters.AddWithValue(
            "visit_cancellation_occurred_at",
            AtBusinessTime(11));
        command.Parameters.AddWithValue("recorded_at", TestNow.AddMinutes(-5));
        command.Parameters.AddWithValue("original_payment_id", originalPaymentId);
        command.Parameters.AddWithValue("replacement_payment_id", replacementPaymentId);
        command.Parameters.AddWithValue("canceled_payment_id", canceledPaymentId);
        command.Parameters.AddWithValue("payment_correction_id", Guid.NewGuid());
        command.Parameters.AddWithValue("payment_cancellation_id", Guid.NewGuid());
        command.Parameters.AddWithValue(
            "original_payment_occurred_at",
            AtBusinessTime(12));
        command.Parameters.AddWithValue(
            "replacement_payment_occurred_at",
            AtBusinessTime(13));
        command.Parameters.AddWithValue(
            "canceled_payment_occurred_at",
            AtBusinessTime(14));
        command.Parameters.AddWithValue(
            "payment_correction_occurred_at",
            AtBusinessTime(15));
        command.Parameters.AddWithValue(
            "payment_cancellation_occurred_at",
            AtBusinessTime(16));
        command.Parameters.Add(
            "changed_fields",
            NpgsqlDbType.Jsonb).Value = "[\"amount\"]";
        Assert.Equal(10, await command.ExecuteNonQueryAsync());

        return new DailyReportFixture(
            new ActorContext(
                new AccountId(accountId),
                ActorRole.Owner,
                AccountKind.Owner,
                new SessionId(sessionId),
                "Reports tablet"),
            canceledVisitId,
            originalPaymentId,
            replacementPaymentId,
            canceledPaymentId);
    }

    private static DateTimeOffset AtBusinessTime(int hour)
    {
        return new DateTimeOffset(
            BusinessDate.ToDateTime(new TimeOnly(hour, 0), DateTimeKind.Utc));
    }

    private static DailyVisitSourceSnapshot CreateVisitSnapshot(DateOnly businessDate)
    {
        return new DailyVisitSourceSnapshot(
            businessDate,
            VisitDayReconciliationStatus.Open,
            []);
    }

    private static DailyPaymentSourceSnapshot CreatePaymentSnapshot(
        DateOnly businessDate,
        PaymentDayReconciliationStatus status = PaymentDayReconciliationStatus.Open)
    {
        return new DailyPaymentSourceSnapshot(businessDate, status, []);
    }

    private static ActorContext CreateActor()
    {
        return new ActorContext(
            new AccountId(Guid.NewGuid()),
            ActorRole.Owner,
            AccountKind.Owner,
            new SessionId(Guid.NewGuid()),
            "Reports tablet");
    }

    private static void AssertFailure(
        GenerateDailyReportResult result,
        GenerateDailyReportStatus status)
    {
        Assert.Equal(status, result.Status);
        Assert.Null(result.Report);
        Assert.NotNull(result.ErrorCode);
        Assert.NotNull(result.ErrorMessage);
    }

    private sealed record DailyReportFixture(
        ActorContext Actor,
        Guid CanceledVisitId,
        Guid OriginalPaymentId,
        Guid ReplacementPaymentId,
        Guid CanceledPaymentId);

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

    private sealed class StubVisitSourceHandler(GetDailyVisitSourceRowsResult result)
        : IBodyLifeQueryHandler<
            GetDailyVisitSourceRowsQuery,
            GetDailyVisitSourceRowsResult>
    {
        public Task<GetDailyVisitSourceRowsResult> ExecuteAsync(
            GetDailyVisitSourceRowsQuery query,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(result);
        }
    }

    private sealed class StubPaymentSourceHandler(GetDailyPaymentSourceRowsResult result)
        : IBodyLifeQueryHandler<
            GetDailyPaymentSourceRowsQuery,
            GetDailyPaymentSourceRowsResult>
    {
        public int CallCount { get; private set; }

        public Task<GetDailyPaymentSourceRowsResult> ExecuteAsync(
            GetDailyPaymentSourceRowsQuery query,
            CancellationToken cancellationToken)
        {
            CallCount++;
            return Task.FromResult(result);
        }
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }
}
