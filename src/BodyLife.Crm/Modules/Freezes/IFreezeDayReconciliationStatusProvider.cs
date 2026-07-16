namespace BodyLife.Crm.Modules.Freezes;

public interface IFreezeDayReconciliationStatusProvider
{
    Task<FreezeDayReconciliationStatus> GetStatusAsync(
        DateOnly businessDate,
        CancellationToken cancellationToken = default);
}
