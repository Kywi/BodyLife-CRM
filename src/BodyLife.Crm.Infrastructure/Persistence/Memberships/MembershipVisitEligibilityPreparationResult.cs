using BodyLife.Crm.Modules.Memberships;

namespace BodyLife.Crm.Infrastructure.Persistence.Memberships;

public sealed class MembershipVisitEligibilityPreparationResult
{
    private MembershipVisitEligibilityPreparationResult(
        MembershipVisitEligibilityPreparationStatus status,
        Guid clientId,
        Guid membershipId,
        MembershipVisitEligibility? eligibility,
        MembershipStateCacheRebuildStatus? rebuildStatus)
    {
        Status = status;
        ClientId = clientId;
        MembershipId = membershipId;
        Eligibility = eligibility;
        RebuildStatus = rebuildStatus;
    }

    public MembershipVisitEligibilityPreparationStatus Status { get; }

    public Guid ClientId { get; }

    public Guid MembershipId { get; }

    public bool IsPrepared =>
        Status == MembershipVisitEligibilityPreparationStatus.Prepared;

    public MembershipVisitEligibility? Eligibility { get; }

    public MembershipStateCacheRebuildStatus? RebuildStatus { get; }

    internal static MembershipVisitEligibilityPreparationResult NotFound(
        Guid clientId,
        Guid membershipId)
    {
        return new MembershipVisitEligibilityPreparationResult(
            MembershipVisitEligibilityPreparationStatus.NotFound,
            clientId,
            membershipId,
            eligibility: null,
            rebuildStatus: null);
    }

    internal static MembershipVisitEligibilityPreparationResult Prepared(
        Guid clientId,
        Guid membershipId,
        MembershipVisitEligibility eligibility,
        MembershipStateCacheRebuildStatus rebuildStatus)
    {
        return new MembershipVisitEligibilityPreparationResult(
            MembershipVisitEligibilityPreparationStatus.Prepared,
            clientId,
            membershipId,
            eligibility,
            rebuildStatus);
    }
}
