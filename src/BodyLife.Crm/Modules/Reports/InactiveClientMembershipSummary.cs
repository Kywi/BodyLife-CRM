using BodyLife.Crm.Modules.Memberships;

namespace BodyLife.Crm.Modules.Reports;

public sealed class InactiveClientMembershipSummary
{
    public InactiveClientMembershipSummary(
        InactiveClientMembershipSummaryKind kind,
        ClientMembershipStateTimelineItem timelineItem)
    {
        ArgumentNullException.ThrowIfNull(timelineItem);

        if (!Enum.IsDefined(kind))
        {
            throw new ArgumentOutOfRangeException(
                nameof(kind),
                kind,
                "Membership summary kind is not supported.");
        }

        if (kind == InactiveClientMembershipSummaryKind.Current
            && !timelineItem.IsActiveCandidate)
        {
            throw new ArgumentException(
                "A current Membership summary must use an active candidate.",
                nameof(timelineItem));
        }

        Kind = kind;
        MembershipState = timelineItem.State;
        LifecycleStatus = timelineItem.LifecycleStatus;
    }

    public InactiveClientMembershipSummaryKind Kind { get; }

    public MembershipStateReadModel MembershipState { get; }

    public IssuedMembershipLifecycleStatus LifecycleStatus { get; }

    public Guid MembershipId => MembershipState.MembershipId;

    public string MembershipTypeName => MembershipState.Snapshot.TypeName;

    public int RemainingVisits => MembershipState.RemainingVisits;

    public DateOnly EffectiveEndDate => MembershipState.EffectiveEndDate;

    public IReadOnlyList<MembershipWarning> Warnings => MembershipState.Warnings;
}
