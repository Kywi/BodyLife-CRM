namespace BodyLife.Crm.Modules.Memberships;

public enum MembershipFreezeEligibilityStatus
{
    Eligible = 1,
    MembershipInactive,
    BeforeMembershipStart,
    AfterMembershipEffectiveEnd,
    ConflictsWithActiveVisit,
}
