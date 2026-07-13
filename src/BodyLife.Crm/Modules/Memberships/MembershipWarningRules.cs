namespace BodyLife.Crm.Modules.Memberships;

public static class MembershipWarningRules
{
    public const int EndingSoonDaysThreshold = 7;
    public const int LowRemainingVisitsThreshold = 2;

    public static IReadOnlyList<MembershipWarning> Derive(
        MembershipCalculatedState? state,
        DateOnly asOfDate)
    {
        ArgumentNullException.ThrowIfNull(state);

        if (asOfDate == default)
        {
            throw new ArgumentException("As-of date is required.", nameof(asOfDate));
        }

        var warnings = new List<MembershipWarning>(2);
        var hasNegativeBalance = state.NegativeBalance > 0;
        var daysLeft = state.EffectiveEndDate.DayNumber - asOfDate.DayNumber;
        var isExpiredByDate = daysLeft < 0;

        if (hasNegativeBalance)
        {
            warnings.Add(new MembershipWarning(
                MembershipWarningCodes.NegativeBalance,
                MembershipWarningSeverity.Danger,
                "Membership has a negative visit balance."));
        }

        if (isExpiredByDate)
        {
            warnings.Add(new MembershipWarning(
                MembershipWarningCodes.ExpiredByDate,
                MembershipWarningSeverity.Danger,
                "Membership has expired by date."));
        }

        if (!hasNegativeBalance)
        {
            if (state.RemainingVisits == 0)
            {
                warnings.Add(new MembershipWarning(
                    MembershipWarningCodes.ZeroRemaining,
                    MembershipWarningSeverity.Warning,
                    "Membership has no remaining visits."));
            }
            else if (state.RemainingVisits is > 0 and <= LowRemainingVisitsThreshold)
            {
                warnings.Add(new MembershipWarning(
                    MembershipWarningCodes.LowRemaining,
                    MembershipWarningSeverity.Warning,
                    "Membership has 1-2 remaining visits."));
            }
        }

        if (!isExpiredByDate && daysLeft <= EndingSoonDaysThreshold)
        {
            warnings.Add(new MembershipWarning(
                MembershipWarningCodes.EndingSoon,
                MembershipWarningSeverity.Warning,
                "Membership ends within 7 days."));
        }

        return warnings.ToArray();
    }
}
