namespace BodyLife.Crm.Modules.Memberships;

public sealed class ClientMembershipStateTimelineItem
{
    public ClientMembershipStateTimelineItem(
        MembershipStateReadModel state,
        IssuedMembershipLifecycleStatus lifecycleStatus,
        DateTimeOffset issuedAt)
    {
        ArgumentNullException.ThrowIfNull(state);

        if (!Enum.IsDefined(lifecycleStatus))
        {
            throw new ArgumentOutOfRangeException(
                nameof(lifecycleStatus),
                lifecycleStatus,
                "Issued membership lifecycle status is not supported.");
        }

        State = state;
        LifecycleStatus = lifecycleStatus;
        IssuedAt = issuedAt;
    }

    public MembershipStateReadModel State { get; }

    public IssuedMembershipLifecycleStatus LifecycleStatus { get; }

    public DateTimeOffset IssuedAt { get; }

    public bool IsActiveCandidate => LifecycleStatus == IssuedMembershipLifecycleStatus.Active
        && State.IsActiveByDate;
}
