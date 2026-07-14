using BodyLife.Crm.Modules.Memberships;

namespace BodyLife.Crm.Infrastructure.Persistence.Memberships;

public sealed class MembershipVisitEligibilityEvaluator
    : IMembershipVisitEligibilityEvaluator
{
    public MembershipVisitEligibility Evaluate(
        MembershipStateReadModel state,
        IssuedMembershipLifecycleStatus lifecycleStatus,
        DateOnly visitDate,
        IReadOnlyList<MembershipVisitFreezeSource> freezeSources)
    {
        return MembershipVisitEligibilityPolicy.Evaluate(
            state,
            lifecycleStatus,
            visitDate,
            freezeSources);
    }
}
