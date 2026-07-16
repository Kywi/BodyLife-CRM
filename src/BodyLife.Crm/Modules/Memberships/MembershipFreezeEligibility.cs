using BodyLife.Crm.SharedKernel;

namespace BodyLife.Crm.Modules.Memberships;

public sealed class MembershipFreezeEligibility
{
    internal MembershipFreezeEligibility(
        Guid membershipId,
        DateRange range,
        MembershipFreezeEligibilityStatus status)
    {
        MembershipId = membershipId;
        Range = range;
        Status = status;
    }

    public Guid MembershipId { get; }

    public DateRange Range { get; }

    public MembershipFreezeEligibilityStatus Status { get; }

    public bool IsEligible => Status == MembershipFreezeEligibilityStatus.Eligible;

    public string? ErrorCode => Status switch
    {
        MembershipFreezeEligibilityStatus.Eligible => null,
        MembershipFreezeEligibilityStatus.ConflictsWithActiveVisit =>
            MembershipFreezeEligibilityErrorCodes.FreezeConflictsWithVisit,
        _ => MembershipFreezeEligibilityErrorCodes.MembershipNotEligible,
    };
}
