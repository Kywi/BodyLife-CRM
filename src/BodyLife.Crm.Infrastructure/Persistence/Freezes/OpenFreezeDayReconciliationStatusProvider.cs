using BodyLife.Crm.Modules.Freezes;

namespace BodyLife.Crm.Infrastructure.Persistence.Freezes;

internal sealed class OpenFreezeDayReconciliationStatusProvider
    : IFreezeDayReconciliationStatusProvider
{
    public Task<FreezeDayReconciliationStatus> GetStatusAsync(
        DateOnly businessDate,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(FreezeDayReconciliationStatus.Open);
    }
}
