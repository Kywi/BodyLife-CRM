namespace BodyLife.Crm.Modules.Memberships;

public static class ClientMembershipStatesPolicy
{
    public static ClientMembershipStatesReadModel Create(
        Guid clientId,
        DateOnly asOfDate,
        IEnumerable<ClientMembershipStateTimelineItem> timeline)
    {
        if (clientId == Guid.Empty)
        {
            throw new ArgumentException("Client id is required.", nameof(clientId));
        }

        if (asOfDate == default)
        {
            throw new ArgumentException("As-of date is required.", nameof(asOfDate));
        }

        ArgumentNullException.ThrowIfNull(timeline);

        var timelineItems = timeline.ToArray();
        if (timelineItems.Any(item => item is null))
        {
            throw new ArgumentException(
                "Membership timeline cannot contain a missing item.",
                nameof(timeline));
        }

        if (timelineItems.Any(item => item.State.ClientId != clientId))
        {
            throw new ArgumentException(
                "Every membership timeline item must belong to the requested client.",
                nameof(timeline));
        }

        if (timelineItems.Any(item => item.State.AsOfDate != asOfDate))
        {
            throw new ArgumentException(
                "Every membership state must use the requested as-of date.",
                nameof(timeline));
        }

        if (timelineItems
            .GroupBy(item => item.State.MembershipId)
            .Any(group => group.Count() > 1))
        {
            throw new ArgumentException(
                "Membership timeline cannot contain duplicate membership ids.",
                nameof(timeline));
        }

        var orderedTimeline = timelineItems
            .OrderByDescending(item => item.State.StartDate)
            .ThenByDescending(item => item.IssuedAt)
            .ThenBy(item => item.State.MembershipId)
            .ToArray();
        var activeCandidates = orderedTimeline
            .Where(item => item.IsActiveCandidate)
            .ToArray();
        var activeCandidateSelection = activeCandidates.Length switch
        {
            0 => new ActiveMembershipCandidateSelection(
                ActiveMembershipCandidateStatus.None,
                singleCandidate: null,
                candidates: []),
            1 => new ActiveMembershipCandidateSelection(
                ActiveMembershipCandidateStatus.Single,
                activeCandidates[0],
                activeCandidates),
            _ => new ActiveMembershipCandidateSelection(
                ActiveMembershipCandidateStatus.Ambiguous,
                singleCandidate: null,
                activeCandidates),
        };

        return new ClientMembershipStatesReadModel(
            clientId,
            asOfDate,
            orderedTimeline,
            activeCandidateSelection);
    }
}
