using BodyLife.Crm.Application.Commands;
using BodyLife.Crm.Application.Queries;
using BodyLife.Crm.Modules.Payments;
using BodyLife.Crm.Modules.Reports;
using BodyLife.Crm.Modules.Visits;
using BodyLife.Crm.SharedKernel;

namespace BodyLife.Crm.Tests.Modules.Reports;

public sealed class GenerateDailyReportContractsTests
{
    private static readonly DateOnly BusinessDate = new(2026, 7, 19);
    private static readonly DateTimeOffset RecordedAt = new(
        2026,
        7,
        19,
        18,
        0,
        0,
        TimeSpan.Zero);

    [Fact]
    public void QueryCarriesActorDateAndDrillDownSelection()
    {
        var actor = CreateActor();

        var query = new GenerateDailyReportQuery(
            actor,
            BusinessDate,
            IncludeDrillDown: false);

        Assert.IsAssignableFrom<IBodyLifeQuery<GenerateDailyReportResult>>(query);
        Assert.Same(actor, query.Actor);
        Assert.Equal(BusinessDate, query.BusinessDate);
        Assert.False(query.IncludeDrillDown);
    }

    [Fact]
    public void SnapshotDerivesTotalsAndRetainsImmutableCorrectionAndCancellationRows()
    {
        var activeVisit = CreateVisit(ClientVisitRowStatus.Active);
        var canceledVisit = CreateVisit(ClientVisitRowStatus.Canceled);
        var correction = CreateCorrection();
        var replacementPayment = CreatePayment(
            ClientPaymentRowStatus.Active,
            900m,
            correctionFromOriginal: correction);
        var originalPayment = CreatePayment(
            ClientPaymentRowStatus.Replaced,
            1000m,
            correctionToReplacement: correction);
        var canceledPayment = CreatePayment(
            ClientPaymentRowStatus.Canceled,
            250m);
        var mutableVisitRows = new List<DailyVisitSourceRow>
        {
            activeVisit,
            canceledVisit,
        };
        var mutablePaymentRows = new List<DailyPaymentSourceRow>
        {
            replacementPayment,
            originalPayment,
            canceledPayment,
        };

        var created = DailyReportSnapshot.TryCreate(
            BusinessDate,
            includeDrillDown: true,
            new DailyVisitSourceSnapshot(
                BusinessDate,
                VisitDayReconciliationStatus.Open,
                mutableVisitRows),
            new DailyPaymentSourceSnapshot(
                BusinessDate,
                PaymentDayReconciliationStatus.Open,
                mutablePaymentRows),
            out var report);
        mutableVisitRows.Clear();
        mutablePaymentRows.Clear();

        Assert.True(created);
        var snapshot = Assert.IsType<DailyReportSnapshot>(report);
        Assert.Equal(BusinessDate, snapshot.BusinessDate);
        Assert.Equal(DailyReportDayStatus.Open, snapshot.DayStatus);
        Assert.Equal(1, snapshot.VisitCount);
        Assert.Equal(1, snapshot.PaymentCount);
        Assert.Equal(new Money(900m, "UAH"), snapshot.DailyCashSum);
        Assert.True(snapshot.DrillDownIncluded);
        Assert.Equal(2, snapshot.VisitRows.Count);
        Assert.Equal(3, snapshot.PaymentRows.Count);
        Assert.Same(canceledVisit, Assert.Single(snapshot.CanceledVisitRows));
        Assert.Same(canceledPayment, Assert.Single(snapshot.CanceledPaymentRows));
        Assert.Equal(
            [replacementPayment, originalPayment],
            snapshot.CorrectedPaymentRows);
        AssertReadOnly(snapshot.VisitRows, activeVisit);
        AssertReadOnly(snapshot.PaymentRows, replacementPayment);
    }

    [Fact]
    public void SummaryModeKeepsCanonicalTotalsWithoutEmbeddingDrillDownRows()
    {
        var created = DailyReportSnapshot.TryCreate(
            BusinessDate,
            includeDrillDown: false,
            new DailyVisitSourceSnapshot(
                BusinessDate,
                VisitDayReconciliationStatus.Reconciled,
                [CreateVisit(ClientVisitRowStatus.Active)]),
            new DailyPaymentSourceSnapshot(
                BusinessDate,
                PaymentDayReconciliationStatus.Reconciled,
                [CreatePayment(ClientPaymentRowStatus.Active, 300m)]),
            out var report);

        Assert.True(created);
        var snapshot = Assert.IsType<DailyReportSnapshot>(report);
        Assert.Equal(DailyReportDayStatus.Reconciled, snapshot.DayStatus);
        Assert.Equal(1, snapshot.VisitCount);
        Assert.Equal(1, snapshot.PaymentCount);
        Assert.Equal(new Money(300m, "UAH"), snapshot.DailyCashSum);
        Assert.False(snapshot.DrillDownIncluded);
        Assert.Empty(snapshot.VisitRows);
        Assert.Empty(snapshot.PaymentRows);
        Assert.Empty(snapshot.CanceledVisitRows);
        Assert.Empty(snapshot.CanceledPaymentRows);
        Assert.Empty(snapshot.CorrectedPaymentRows);
    }

    [Fact]
    public void SnapshotRejectsMismatchedCanonicalSourcesAndMixedActiveCurrencies()
    {
        var visitSource = new DailyVisitSourceSnapshot(
            BusinessDate,
            VisitDayReconciliationStatus.Open,
            []);
        var otherDatePayments = new DailyPaymentSourceSnapshot(
            BusinessDate.AddDays(1),
            PaymentDayReconciliationStatus.Open,
            []);
        var reconciledPayments = new DailyPaymentSourceSnapshot(
            BusinessDate,
            PaymentDayReconciliationStatus.Reconciled,
            []);
        var mixedCurrencyPayments = new DailyPaymentSourceSnapshot(
            BusinessDate,
            PaymentDayReconciliationStatus.Open,
            [
                CreatePayment(ClientPaymentRowStatus.Active, 100m, "UAH"),
                CreatePayment(ClientPaymentRowStatus.Active, 20m, "USD"),
            ]);
        var unknownStatusPayments = new DailyPaymentSourceSnapshot(
            BusinessDate,
            PaymentDayReconciliationStatus.Open,
            [CreatePayment((ClientPaymentRowStatus)999, 100m)]);

        Assert.False(DailyReportSnapshot.TryCreate(
            BusinessDate,
            includeDrillDown: true,
            visitSource,
            otherDatePayments,
            out var otherDateReport));
        Assert.Null(otherDateReport);
        Assert.False(DailyReportSnapshot.TryCreate(
            BusinessDate,
            includeDrillDown: true,
            visitSource,
            reconciledPayments,
            out var mismatchedStatusReport));
        Assert.Null(mismatchedStatusReport);
        Assert.False(DailyReportSnapshot.TryCreate(
            BusinessDate,
            includeDrillDown: true,
            visitSource,
            mixedCurrencyPayments,
            out var mixedCurrencyReport));
        Assert.Null(mixedCurrencyReport);
        Assert.False(DailyReportSnapshot.TryCreate(
            BusinessDate,
            includeDrillDown: true,
            visitSource,
            unknownStatusPayments,
            out var unknownStatusReport));
        Assert.Null(unknownStatusReport);
    }

    [Fact]
    public void FailuresNeverCarryPartialReportData()
    {
        var failures = new[]
        {
            GenerateDailyReportResult.Denied(),
            GenerateDailyReportResult.Invalid("Business date is required.", "businessDate"),
            GenerateDailyReportResult.InconsistentSource(),
        };

        Assert.Equal(
            [
                GenerateDailyReportStatus.PermissionDenied,
                GenerateDailyReportStatus.ValidationFailed,
                GenerateDailyReportStatus.SourceInconsistent,
            ],
            failures.Select(result => result.Status));
        Assert.All(failures, result =>
        {
            Assert.Null(result.Report);
            Assert.NotNull(result.ErrorCode);
            Assert.NotNull(result.ErrorMessage);
        });
    }

    private static DailyVisitSourceRow CreateVisit(ClientVisitRowStatus status)
    {
        var cancellation = status == ClientVisitRowStatus.Canceled
            ? new ClientVisitCancellation(
                Guid.NewGuid(),
                "Duplicate Visit",
                RecordedAt,
                RecordedAt,
                Guid.NewGuid(),
                Guid.NewGuid(),
                EntryOrigin.Normal,
                EntryBatchId: null)
            : null;

        return new DailyVisitSourceRow(
            "Report Client",
            new ClientVisitRow(
                Guid.NewGuid(),
                Guid.NewGuid(),
                RecordedAt,
                RecordedAt,
                Guid.NewGuid(),
                Guid.NewGuid(),
                VisitKind.OneOff,
                EntryOrigin.Normal,
                EntryBatchId: null,
                Comment: null,
                status,
                Consumption: null,
                cancellation,
                QueryPermissionSet.Empty));
    }

    private static DailyPaymentSourceRow CreatePayment(
        ClientPaymentRowStatus status,
        decimal amount,
        string currency = "UAH",
        ClientPaymentCorrection? correctionFromOriginal = null,
        ClientPaymentCorrection? correctionToReplacement = null)
    {
        var cancellation = status == ClientPaymentRowStatus.Canceled
            ? new ClientPaymentCancellation(
                Guid.NewGuid(),
                "Duplicate Payment",
                RecordedAt,
                RecordedAt,
                Guid.NewGuid(),
                Guid.NewGuid(),
                EntryOrigin.Normal,
                EntryBatchId: null)
            : null;

        return new DailyPaymentSourceRow(
            "Report Client",
            new ClientPaymentRow(
                Guid.NewGuid(),
                Guid.NewGuid(),
                MembershipId: null,
                MembershipTypeNameSnapshot: null,
                new Money(amount, currency),
                PaymentMethod.Cash,
                PaymentContext.OneOff,
                RecordedAt,
                RecordedAt,
                Guid.NewGuid(),
                Guid.NewGuid(),
                EntryOrigin.Normal,
                EntryBatchId: null,
                Comment: null,
                status,
                cancellation,
                correctionFromOriginal,
                correctionToReplacement,
                QueryPermissionSet.Empty));
    }

    private static ClientPaymentCorrection CreateCorrection()
    {
        return new ClientPaymentCorrection(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            ["amount"],
            "Amount correction",
            RecordedAt,
            RecordedAt,
            Guid.NewGuid(),
            Guid.NewGuid(),
            EntryOrigin.Normal,
            EntryBatchId: null);
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

    private static void AssertReadOnly<T>(IReadOnlyList<T> items, T item)
    {
        var list = Assert.IsAssignableFrom<IList<T>>(items);
        Assert.True(list.IsReadOnly);
        Assert.Throws<NotSupportedException>(() => list.Add(item));
    }
}
