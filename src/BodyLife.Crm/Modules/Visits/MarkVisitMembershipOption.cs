using BodyLife.Crm.Modules.Memberships;

namespace BodyLife.Crm.Modules.Visits;

public sealed record MarkVisitMembershipOption(
    Guid MembershipId,
    string TypeName,
    DateOnly StartDate,
    DateOnly EffectiveEndDate,
    int RemainingVisits,
    MembershipVisitEligibilityStatus EligibilityStatus,
    IReadOnlyList<MembershipVisitAcknowledgement> RequiredAcknowledgements,
    IReadOnlyList<MembershipWarning> Warnings)
{
    public bool CanSelect => EligibilityStatus == MembershipVisitEligibilityStatus.Eligible;
}
