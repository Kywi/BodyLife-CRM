using BodyLife.Crm.SharedKernel;

namespace BodyLife.Crm.Modules.Memberships;

public interface IMembershipNonWorkingDayAffectedScopePreparer
{
    Task<MembershipNonWorkingDayAffectedScope> PrepareAsync(
        DateRange period,
        CancellationToken cancellationToken = default);
}
