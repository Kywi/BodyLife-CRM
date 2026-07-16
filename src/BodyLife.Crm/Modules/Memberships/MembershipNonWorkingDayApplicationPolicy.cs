using BodyLife.Crm.SharedKernel;

namespace BodyLife.Crm.Modules.Memberships;

public static class MembershipNonWorkingDayApplicationPolicy
{
    public static MembershipNonWorkingDayApplication Evaluate(
        MembershipStateReadModel state,
        IssuedMembershipLifecycleStatus lifecycleStatus,
        DateRange period)
    {
        ArgumentNullException.ThrowIfNull(state);

        var issueTerms = MembershipIssueTerms.FromIssuedSnapshot(
            state.MembershipTypeId,
            state.Snapshot,
            state.StartDate,
            state.BaseEndDate);
        var preCommandState = MembershipCalculatedState.FromStoredCache(
            issueTerms,
            state.CountedVisits,
            state.RemainingVisits,
            state.NegativeBalance,
            state.FirstNegativeVisitId,
            state.FirstNegativeVisitDate,
            state.ExtensionDays,
            state.EffectiveEndDate,
            state.LastCountedVisitAt);

        return Evaluate(
            state.MembershipId,
            issueTerms,
            preCommandState,
            lifecycleStatus,
            period);
    }

    public static MembershipNonWorkingDayApplication Evaluate(
        Guid membershipId,
        MembershipIssueTerms? issueTerms,
        MembershipCalculatedState? preCommandState,
        IssuedMembershipLifecycleStatus lifecycleStatus,
        DateRange period)
    {
        if (membershipId == Guid.Empty)
        {
            throw new ArgumentException(
                "Membership id is required.",
                nameof(membershipId));
        }

        ArgumentNullException.ThrowIfNull(issueTerms);
        ArgumentNullException.ThrowIfNull(preCommandState);

        if (!Enum.IsDefined(lifecycleStatus))
        {
            throw new ArgumentOutOfRangeException(
                nameof(lifecycleStatus),
                lifecycleStatus,
                "Issued membership lifecycle status is not supported.");
        }

        if (lifecycleStatus != IssuedMembershipLifecycleStatus.Active)
        {
            return Ineligible(
                membershipId,
                period,
                MembershipNonWorkingDayApplicationStatus.MembershipInactive);
        }

        if (period.EndDate < issueTerms.StartDate)
        {
            return Ineligible(
                membershipId,
                period,
                MembershipNonWorkingDayApplicationStatus.PeriodEndsBeforeMembershipStart);
        }

        if (period.StartDate > preCommandState.EffectiveEndDate)
        {
            return Ineligible(
                membershipId,
                period,
                MembershipNonWorkingDayApplicationStatus
                    .PeriodStartsAfterMembershipEffectiveEnd);
        }

        return new MembershipNonWorkingDayApplication(
            membershipId,
            period,
            MembershipNonWorkingDayApplicationStatus.Eligible);
    }

    private static MembershipNonWorkingDayApplication Ineligible(
        Guid membershipId,
        DateRange period,
        MembershipNonWorkingDayApplicationStatus status)
    {
        return new MembershipNonWorkingDayApplication(
            membershipId,
            period,
            status);
    }
}
