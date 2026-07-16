using BodyLife.Crm.SharedKernel;

namespace BodyLife.Crm.Modules.Memberships;

public sealed class MembershipNonWorkingDayApplication
{
    internal MembershipNonWorkingDayApplication(
        Guid membershipId,
        DateRange period,
        MembershipNonWorkingDayApplicationStatus status)
    {
        MembershipId = membershipId;
        Period = period;
        Status = status;
        AppliedRange = status == MembershipNonWorkingDayApplicationStatus.Eligible
            ? period
            : null;
    }

    public Guid MembershipId { get; }

    public DateRange Period { get; }

    public MembershipNonWorkingDayApplicationStatus Status { get; }

    public bool IsEligible => Status == MembershipNonWorkingDayApplicationStatus.Eligible;

    public DateRange? AppliedRange { get; }

    public int AppliedDays => AppliedRange?.InclusiveDays ?? 0;
}
