namespace BodyLife.Crm.Modules.Memberships;

public enum MembershipNonWorkingDayApplicationStatus
{
    Eligible = 1,
    MembershipInactive,
    PeriodEndsBeforeMembershipStart,
    PeriodStartsAfterMembershipEffectiveEnd,
}
