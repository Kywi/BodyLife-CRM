namespace BodyLife.Crm.Modules.Memberships;

public sealed class ClientMembershipStatesReadModel
{
    internal ClientMembershipStatesReadModel(
        Guid clientId,
        DateOnly asOfDate,
        IEnumerable<ClientMembershipStateTimelineItem> timeline,
        ActiveMembershipCandidateSelection activeCandidateSelection)
    {
        ArgumentNullException.ThrowIfNull(timeline);
        ArgumentNullException.ThrowIfNull(activeCandidateSelection);

        ClientId = clientId;
        AsOfDate = asOfDate;
        Timeline = Array.AsReadOnly(timeline.ToArray());
        ActiveCandidateSelection = activeCandidateSelection;
    }

    public Guid ClientId { get; }

    public DateOnly AsOfDate { get; }

    public IReadOnlyList<ClientMembershipStateTimelineItem> Timeline { get; }

    public ActiveMembershipCandidateSelection ActiveCandidateSelection { get; }
}
