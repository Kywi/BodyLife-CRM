using BodyLife.Crm.SharedKernel;

namespace BodyLife.Crm.Modules.Payments;

public sealed record DailyPaymentSourceSnapshot(
    DateOnly BusinessDate,
    PaymentDayReconciliationStatus DayStatus,
    IReadOnlyList<DailyPaymentSourceRow> Rows)
{
    public const string DefaultCashCurrency = "UAH";

    public int ActivePaymentCount => Rows.Count(row =>
        row.Payment.Status == ClientPaymentRowStatus.Active);

    public Money DailyCashSum
    {
        get
        {
            var activeRows = Rows
                .Where(row => row.Payment.Status == ClientPaymentRowStatus.Active)
                .ToArray();
            var currency = activeRows.FirstOrDefault()?.Payment.Amount.Currency
                ?? DefaultCashCurrency;

            return new Money(
                activeRows.Sum(row => row.Payment.Amount.Amount),
                currency);
        }
    }
}
