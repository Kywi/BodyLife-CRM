using BodyLife.Crm.SharedKernel;

namespace BodyLife.Crm.Modules.Memberships;

public interface IMembershipNonWorkingDayImpactPreparer
{
    Task<MembershipNonWorkingDayImpactPreparation> PrepareImpactAsync(
        DateRange period,
        CancellationToken cancellationToken = default);
}
