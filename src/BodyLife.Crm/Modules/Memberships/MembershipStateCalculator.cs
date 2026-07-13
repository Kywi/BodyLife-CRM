namespace BodyLife.Crm.Modules.Memberships;

public static class MembershipStateCalculator
{
    public static MembershipCalculatedState CalculateInitial(MembershipIssueTerms? issueTerms)
    {
        ArgumentNullException.ThrowIfNull(issueTerms);

        return new MembershipCalculatedState(
            countedVisits: 0,
            remainingVisits: issueTerms.Snapshot.VisitsLimit,
            negativeBalance: 0,
            firstNegativeVisitId: null,
            firstNegativeVisitDate: null,
            extensionDays: 0,
            effectiveEndDate: issueTerms.BaseEndDate,
            lastCountedVisitAt: null);
    }
}
