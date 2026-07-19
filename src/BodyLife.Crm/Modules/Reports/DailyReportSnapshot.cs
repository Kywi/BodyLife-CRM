using BodyLife.Crm.Modules.Payments;
using BodyLife.Crm.Modules.Visits;
using BodyLife.Crm.SharedKernel;

namespace BodyLife.Crm.Modules.Reports;

public sealed class DailyReportSnapshot
{
    private static readonly IReadOnlyList<DailyVisitSourceRow> EmptyVisitRows =
        Array.AsReadOnly(Array.Empty<DailyVisitSourceRow>());
    private static readonly IReadOnlyList<DailyPaymentSourceRow> EmptyPaymentRows =
        Array.AsReadOnly(Array.Empty<DailyPaymentSourceRow>());

    private DailyReportSnapshot(
        DateOnly businessDate,
        DailyReportDayStatus dayStatus,
        int visitCount,
        int paymentCount,
        Money dailyCashSum,
        bool drillDownIncluded,
        IReadOnlyList<DailyVisitSourceRow> visitRows,
        IReadOnlyList<DailyPaymentSourceRow> paymentRows,
        IReadOnlyList<DailyVisitSourceRow> canceledVisitRows,
        IReadOnlyList<DailyPaymentSourceRow> canceledPaymentRows,
        IReadOnlyList<DailyPaymentSourceRow> correctedPaymentRows)
    {
        BusinessDate = businessDate;
        DayStatus = dayStatus;
        VisitCount = visitCount;
        PaymentCount = paymentCount;
        DailyCashSum = dailyCashSum;
        DrillDownIncluded = drillDownIncluded;
        VisitRows = visitRows;
        PaymentRows = paymentRows;
        CanceledVisitRows = canceledVisitRows;
        CanceledPaymentRows = canceledPaymentRows;
        CorrectedPaymentRows = correctedPaymentRows;
    }

    public DateOnly BusinessDate { get; }

    public DailyReportDayStatus DayStatus { get; }

    public int VisitCount { get; }

    public int PaymentCount { get; }

    public Money DailyCashSum { get; }

    public bool DrillDownIncluded { get; }

    public IReadOnlyList<DailyVisitSourceRow> VisitRows { get; }

    public IReadOnlyList<DailyPaymentSourceRow> PaymentRows { get; }

    public IReadOnlyList<DailyVisitSourceRow> CanceledVisitRows { get; }

    public IReadOnlyList<DailyPaymentSourceRow> CanceledPaymentRows { get; }

    public IReadOnlyList<DailyPaymentSourceRow> CorrectedPaymentRows { get; }

    public static bool TryCreate(
        DateOnly businessDate,
        bool includeDrillDown,
        DailyVisitSourceSnapshot visitSource,
        DailyPaymentSourceSnapshot paymentSource,
        out DailyReportSnapshot? report)
    {
        ArgumentNullException.ThrowIfNull(visitSource);
        ArgumentNullException.ThrowIfNull(paymentSource);

        report = null;
        if (businessDate == default
            || businessDate == DateOnly.MaxValue
            || visitSource.BusinessDate != businessDate
            || paymentSource.BusinessDate != businessDate
            || !TryMapDayStatus(visitSource.DayStatus, paymentSource.DayStatus, out var dayStatus)
            || visitSource.Rows is null
            || paymentSource.Rows is null)
        {
            return false;
        }

        var visitRows = visitSource.Rows.ToArray();
        var paymentRows = paymentSource.Rows.ToArray();
        if (visitRows.Any(row => row is null
                || row.Visit is null
                || !Enum.IsDefined(row.Visit.Status))
            || paymentRows.Any(row => row is null
                || row.Payment is null
                || !Enum.IsDefined(row.Payment.Status)))
        {
            return false;
        }

        var activeVisitRows = visitRows
            .Where(row => row.Visit.Status == ClientVisitRowStatus.Active)
            .ToArray();
        var activePaymentRows = paymentRows
            .Where(row => row.Payment.Status == ClientPaymentRowStatus.Active)
            .ToArray();
        var activeCurrencies = activePaymentRows
            .Select(row => row.Payment.Amount.Currency)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (activeCurrencies.Length > 1)
        {
            return false;
        }

        var currency = activeCurrencies.SingleOrDefault()
            ?? DailyPaymentSourceSnapshot.DefaultCashCurrency;
        var cashSum = new Money(
            activePaymentRows.Sum(row => row.Payment.Amount.Amount),
            currency);
        var includedVisitRows = includeDrillDown
            ? Array.AsReadOnly(visitRows)
            : EmptyVisitRows;
        var includedPaymentRows = includeDrillDown
            ? Array.AsReadOnly(paymentRows)
            : EmptyPaymentRows;
        var canceledVisitRows = includeDrillDown
            ? Array.AsReadOnly(visitRows
                .Where(row => row.Visit.Status == ClientVisitRowStatus.Canceled)
                .ToArray())
            : EmptyVisitRows;
        var canceledPaymentRows = includeDrillDown
            ? Array.AsReadOnly(paymentRows
                .Where(row => row.Payment.Status == ClientPaymentRowStatus.Canceled)
                .ToArray())
            : EmptyPaymentRows;
        var correctedPaymentRows = includeDrillDown
            ? Array.AsReadOnly(paymentRows
                .Where(row => row.Payment.CorrectionFromOriginal is not null
                    || row.Payment.CorrectionToReplacement is not null)
                .ToArray())
            : EmptyPaymentRows;

        report = new DailyReportSnapshot(
            businessDate,
            dayStatus,
            activeVisitRows.Length,
            activePaymentRows.Length,
            cashSum,
            includeDrillDown,
            includedVisitRows,
            includedPaymentRows,
            canceledVisitRows,
            canceledPaymentRows,
            correctedPaymentRows);
        return true;
    }

    private static bool TryMapDayStatus(
        VisitDayReconciliationStatus visitStatus,
        PaymentDayReconciliationStatus paymentStatus,
        out DailyReportDayStatus dayStatus)
    {
        if (visitStatus == VisitDayReconciliationStatus.Open
            && paymentStatus == PaymentDayReconciliationStatus.Open)
        {
            dayStatus = DailyReportDayStatus.Open;
            return true;
        }

        if (visitStatus == VisitDayReconciliationStatus.Reconciled
            && paymentStatus == PaymentDayReconciliationStatus.Reconciled)
        {
            dayStatus = DailyReportDayStatus.Reconciled;
            return true;
        }

        dayStatus = default;
        return false;
    }
}
