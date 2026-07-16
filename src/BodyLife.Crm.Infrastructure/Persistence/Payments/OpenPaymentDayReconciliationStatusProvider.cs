using BodyLife.Crm.Modules.Payments;

namespace BodyLife.Crm.Infrastructure.Persistence.Payments;

internal sealed class OpenPaymentDayReconciliationStatusProvider
    : IPaymentDayReconciliationStatusProvider
{
    public Task<PaymentDayReconciliationStatus> GetStatusAsync(
        DateOnly businessDate,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(PaymentDayReconciliationStatus.Open);
    }
}
