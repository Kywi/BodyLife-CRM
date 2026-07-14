using BodyLife.Crm.SharedKernel;

namespace BodyLife.Crm.Modules.Memberships;

public sealed record MembershipVisitFreezeSource
{
    public MembershipVisitFreezeSource(
        Guid membershipId,
        Guid freezeId,
        DateRange range,
        bool isActive)
    {
        if (membershipId == Guid.Empty)
        {
            throw new ArgumentException(
                "Membership id is required.",
                nameof(membershipId));
        }

        if (freezeId == Guid.Empty)
        {
            throw new ArgumentException(
                "Freeze id is required.",
                nameof(freezeId));
        }

        MembershipId = membershipId;
        FreezeId = freezeId;
        Range = range;
        IsActive = isActive;
    }

    public Guid MembershipId { get; }

    public Guid FreezeId { get; }

    public DateRange Range { get; }

    public bool IsActive { get; }
}
