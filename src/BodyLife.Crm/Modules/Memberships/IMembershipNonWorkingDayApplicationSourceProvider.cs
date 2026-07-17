namespace BodyLife.Crm.Modules.Memberships;

public interface IMembershipNonWorkingDayApplicationSourceProvider
{
    Task<IReadOnlyList<Guid>> GetApplicationIdsForPeriodAsync(
        Guid nonWorkingPeriodId,
        CancellationToken cancellationToken = default);
}
