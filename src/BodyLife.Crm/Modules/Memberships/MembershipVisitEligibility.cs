namespace BodyLife.Crm.Modules.Memberships;

public sealed class MembershipVisitEligibility
{
    internal MembershipVisitEligibility(
        Guid membershipId,
        MembershipVisitEligibilityStatus status,
        IEnumerable<MembershipVisitAcknowledgement> requiredAcknowledgements)
    {
        MembershipId = membershipId;
        Status = status;
        RequiredAcknowledgements = Array.AsReadOnly(
            requiredAcknowledgements.ToArray());
    }

    public Guid MembershipId { get; }

    public MembershipVisitEligibilityStatus Status { get; }

    public IReadOnlyList<MembershipVisitAcknowledgement> RequiredAcknowledgements { get; }

    public bool IsEligible => Status == MembershipVisitEligibilityStatus.Eligible;

    public string? ErrorCode => Status switch
    {
        MembershipVisitEligibilityStatus.Eligible => null,
        MembershipVisitEligibilityStatus.DuringActiveFreeze =>
            MembershipVisitEligibilityErrorCodes.VisitDuringFreeze,
        _ => MembershipVisitEligibilityErrorCodes.MembershipNotEligible,
    };
}
