namespace BodyLife.Crm.Modules.Visits;

public interface IVisitDayReconciliationStatusProvider
{
    Task<VisitDayReconciliationStatus> GetStatusAsync(
        DateOnly businessDate,
        CancellationToken cancellationToken = default);
}
