using BodyLife.Crm.Modules.Clients.Search;
using BodyLife.Crm.Modules.Memberships;

namespace BodyLife.Crm.Modules.Reports;

public sealed class InactiveClientRow
{
    internal InactiveClientRow(
        ListInactiveClientsQuery query,
        InactiveClientSourceRow source)
    {
        ClientId = source.ClientId;
        ClientDisplayName = source.ClientDisplayName;
        ClientPhone = source.ClientPhone;
        CurrentCardNumber = source.CurrentCardNumber;
        OperationalStatus = source.OperationalStatus;
        LastCountedVisit = source.LastCountedVisit;
        DaysInactive = source.LastCountedVisit is null
            ? null
            : query.AsOfDate.DayNumber
                - source.LastCountedVisit.OccurredDateUtc.DayNumber;
        HasAmbiguousCurrentMembership = source.MembershipStates
            .ActiveCandidateSelection.Status
            == ActiveMembershipCandidateStatus.Ambiguous;
        MembershipSummary = SelectMembershipSummary(source.MembershipStates);
    }

    public Guid ClientId { get; }

    public string ClientDisplayName { get; }

    public string? ClientPhone { get; }

    public string? CurrentCardNumber { get; }

    public ClientOperationalStatus OperationalStatus { get; }

    public InactiveClientLastVisit? LastCountedVisit { get; }

    public DateOnly? LastCountedVisitDate => LastCountedVisit?.OccurredDateUtc;

    public int? DaysInactive { get; }

    public InactiveClientMembershipSummary? MembershipSummary { get; }

    public bool HasAmbiguousCurrentMembership { get; }

    private static InactiveClientMembershipSummary? SelectMembershipSummary(
        ClientMembershipStatesReadModel states)
    {
        var selection = states.ActiveCandidateSelection;
        if (selection.Status == ActiveMembershipCandidateStatus.Single)
        {
            return new InactiveClientMembershipSummary(
                InactiveClientMembershipSummaryKind.Current,
                selection.SingleCandidate
                    ?? throw new InvalidOperationException(
                        "A single active Membership selection requires its candidate."));
        }

        var lastMembership = states.Timeline.FirstOrDefault();
        return lastMembership is null
            ? null
            : new InactiveClientMembershipSummary(
                InactiveClientMembershipSummaryKind.Last,
                lastMembership);
    }
}
