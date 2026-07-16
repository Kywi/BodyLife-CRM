using BodyLife.Crm.Modules.Memberships;

namespace BodyLife.Crm.Infrastructure.Persistence.Memberships;

public sealed class MembershipFreezeEligibilityPreparationResult
{
    private MembershipFreezeEligibilityPreparationResult(
        MembershipFreezeEligibilityPreparationStatus status,
        Guid clientId,
        Guid membershipId,
        MembershipFreezeEligibility? eligibility,
        MembershipStateCacheRebuildStatus? rebuildStatus)
    {
        Status = status;
        ClientId = clientId;
        MembershipId = membershipId;
        Eligibility = eligibility;
        RebuildStatus = rebuildStatus;
    }

    public MembershipFreezeEligibilityPreparationStatus Status { get; }

    public Guid ClientId { get; }

    public Guid MembershipId { get; }

    public bool IsPrepared =>
        Status == MembershipFreezeEligibilityPreparationStatus.Prepared;

    public MembershipFreezeEligibility? Eligibility { get; }

    public MembershipStateCacheRebuildStatus? RebuildStatus { get; }

    internal static MembershipFreezeEligibilityPreparationResult NotFound(
        Guid clientId,
        Guid membershipId)
    {
        return new MembershipFreezeEligibilityPreparationResult(
            MembershipFreezeEligibilityPreparationStatus.NotFound,
            clientId,
            membershipId,
            eligibility: null,
            rebuildStatus: null);
    }

    internal static MembershipFreezeEligibilityPreparationResult Prepared(
        Guid clientId,
        Guid membershipId,
        MembershipFreezeEligibility eligibility,
        MembershipStateCacheRebuildStatus rebuildStatus)
    {
        return new MembershipFreezeEligibilityPreparationResult(
            MembershipFreezeEligibilityPreparationStatus.Prepared,
            clientId,
            membershipId,
            eligibility,
            rebuildStatus);
    }
}
