using BodyLife.Crm.Modules.Memberships;

namespace BodyLife.Crm.Modules.Visits;

public sealed class MarkVisitPreparation
{
    internal MarkVisitPreparation(
        Guid clientId,
        VisitKind visitKind,
        Guid? membershipId,
        IEnumerable<MembershipVisitAcknowledgement> requiredAcknowledgements,
        IEnumerable<MembershipVisitAcknowledgement> acceptedAcknowledgements)
    {
        ClientId = clientId;
        VisitKind = visitKind;
        MembershipId = membershipId;
        RequiredAcknowledgements = Array.AsReadOnly(requiredAcknowledgements.ToArray());
        AcceptedAcknowledgements = Array.AsReadOnly(acceptedAcknowledgements.ToArray());
    }

    public Guid ClientId { get; }

    public VisitKind VisitKind { get; }

    public Guid? MembershipId { get; }

    public IReadOnlyList<MembershipVisitAcknowledgement> RequiredAcknowledgements { get; }

    public IReadOnlyList<MembershipVisitAcknowledgement> AcceptedAcknowledgements { get; }

    public bool CreatesMembershipConsumption => VisitKind == VisitKind.Membership;

    public bool RequiresMembershipRecalculation => CreatesMembershipConsumption;
}
