namespace BodyLife.Crm.Modules.Payments;

public interface IPaymentDayReconciliationStatusProvider
{
    Task<PaymentDayReconciliationStatus> GetStatusAsync(
        DateOnly businessDate,
        CancellationToken cancellationToken = default);
}
