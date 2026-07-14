using BodyLife.Crm.Modules.Visits;

namespace BodyLife.Crm.Infrastructure.Persistence.Visits;

internal sealed class OpenVisitDayReconciliationStatusProvider
    : IVisitDayReconciliationStatusProvider
{
    public Task<VisitDayReconciliationStatus> GetStatusAsync(
        DateOnly businessDate,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(VisitDayReconciliationStatus.Open);
    }
}
