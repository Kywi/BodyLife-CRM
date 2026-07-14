namespace BodyLife.Crm.Modules.Memberships;

public interface IMembershipVisitEligibilityEvaluator
{
    MembershipVisitEligibility Evaluate(
        MembershipStateReadModel state,
        IssuedMembershipLifecycleStatus lifecycleStatus,
        DateOnly visitDate,
        IReadOnlyList<MembershipVisitFreezeSource> freezeSources);
}
