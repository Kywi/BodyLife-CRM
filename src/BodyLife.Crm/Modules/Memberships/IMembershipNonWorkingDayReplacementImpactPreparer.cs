using BodyLife.Crm.SharedKernel;

namespace BodyLife.Crm.Modules.Memberships;

public interface IMembershipNonWorkingDayReplacementImpactPreparer
{
    Task<MembershipNonWorkingDayReplacementImpactPreparation>
        PrepareReplacementImpactAsync(
            Guid replacedPeriodId,
            DateRange replacementPeriod,
            CancellationToken cancellationToken = default);
}
