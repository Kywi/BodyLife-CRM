namespace BodyLife.Crm.Modules.Memberships;

public sealed class ActiveMembershipCandidateSelection
{
    internal ActiveMembershipCandidateSelection(
        ActiveMembershipCandidateStatus status,
        ClientMembershipStateTimelineItem? singleCandidate,
        IEnumerable<ClientMembershipStateTimelineItem> candidates)
    {
        ArgumentNullException.ThrowIfNull(candidates);

        Status = status;
        SingleCandidate = singleCandidate;
        Candidates = Array.AsReadOnly(candidates.ToArray());
    }

    public ActiveMembershipCandidateStatus Status { get; }

    public ClientMembershipStateTimelineItem? SingleCandidate { get; }

    public IReadOnlyList<ClientMembershipStateTimelineItem> Candidates { get; }
}
