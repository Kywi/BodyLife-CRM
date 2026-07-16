using BodyLife.Crm.Modules.Memberships;
using BodyLife.Crm.SharedKernel;

namespace BodyLife.Crm.Tests.Modules.Memberships;

public sealed class MembershipNonWorkingDayApplicationPolicyTests
{
    private static readonly Guid MembershipId = Guid.Parse(
        "11111111-1111-1111-1111-111111111111");
    private static readonly Guid MembershipTypeId = Guid.Parse(
        "22222222-2222-2222-2222-222222222222");
    private static readonly Guid ClientId = Guid.Parse(
        "33333333-3333-3333-3333-333333333333");

    [Fact]
    public void PeriodEndingOnMembershipStartAppliesTheFullRange()
    {
        var issueTerms = CreateIssueTerms(new DateOnly(2026, 2, 1));
        var period = new DateRange(
            new DateOnly(2026, 1, 30),
            issueTerms.StartDate);

        var application = MembershipNonWorkingDayApplicationPolicy.Evaluate(
            MembershipId,
            issueTerms,
            CreateState(issueTerms),
            IssuedMembershipLifecycleStatus.Active,
            period);

        AssertEligibleFullPeriod(application, period);
        Assert.True(application.AppliedRange!.Value.StartDate < issueTerms.StartDate);
    }

    [Fact]
    public void PeriodStartingOnEffectiveEndAppliesTheFullRange()
    {
        var issueTerms = CreateIssueTerms(new DateOnly(2026, 1, 1));
        var state = CreateState(issueTerms);
        var period = new DateRange(
            state.EffectiveEndDate,
            state.EffectiveEndDate.AddDays(3));

        var application = MembershipNonWorkingDayApplicationPolicy.Evaluate(
            MembershipId,
            issueTerms,
            state,
            IssuedMembershipLifecycleStatus.Active,
            period);

        AssertEligibleFullPeriod(application, period);
        Assert.True(application.AppliedRange!.Value.EndDate > state.EffectiveEndDate);
    }

    [Theory]
    [InlineData(IssuedMembershipLifecycleStatus.Canceled)]
    [InlineData(IssuedMembershipLifecycleStatus.Corrected)]
    public void HistoricalLifecycleMembershipIsExcluded(
        IssuedMembershipLifecycleStatus lifecycleStatus)
    {
        var issueTerms = CreateIssueTerms(new DateOnly(2026, 1, 1));
        var period = CreateOrdinaryPeriod();

        var application = MembershipNonWorkingDayApplicationPolicy.Evaluate(
            MembershipId,
            issueTerms,
            CreateState(issueTerms),
            lifecycleStatus,
            period);

        AssertIneligible(
            application,
            period,
            MembershipNonWorkingDayApplicationStatus.MembershipInactive);
    }

    [Fact]
    public void PeriodEndingBeforeMembershipStartIsExcluded()
    {
        var issueTerms = CreateIssueTerms(new DateOnly(2026, 2, 1));
        var period = new DateRange(
            issueTerms.StartDate.AddDays(-3),
            issueTerms.StartDate.AddDays(-1));

        var application = MembershipNonWorkingDayApplicationPolicy.Evaluate(
            MembershipId,
            issueTerms,
            CreateState(issueTerms),
            IssuedMembershipLifecycleStatus.Active,
            period);

        AssertIneligible(
            application,
            period,
            MembershipNonWorkingDayApplicationStatus.PeriodEndsBeforeMembershipStart);
    }

    [Fact]
    public void PeriodStartingAfterEffectiveEndIsExcluded()
    {
        var issueTerms = CreateIssueTerms(new DateOnly(2026, 1, 1));
        var state = CreateState(issueTerms);
        var period = new DateRange(
            state.EffectiveEndDate.AddDays(1),
            state.EffectiveEndDate.AddDays(4));

        var application = MembershipNonWorkingDayApplicationPolicy.Evaluate(
            MembershipId,
            issueTerms,
            state,
            IssuedMembershipLifecycleStatus.Active,
            period);

        AssertIneligible(
            application,
            period,
            MembershipNonWorkingDayApplicationStatus
                .PeriodStartsAfterMembershipEffectiveEnd);
    }

    [Fact]
    public void ProposedPeriodCannotMakeItselfEligible()
    {
        var issueTerms = CreateIssueTerms(new DateOnly(2026, 1, 1));
        var preCommandState = CreateState(issueTerms);
        var proposedPeriod = new DateRange(
            preCommandState.EffectiveEndDate.AddDays(1),
            preCommandState.EffectiveEndDate.AddDays(3));

        var application = MembershipNonWorkingDayApplicationPolicy.Evaluate(
            MembershipId,
            issueTerms,
            preCommandState,
            IssuedMembershipLifecycleStatus.Active,
            proposedPeriod);

        AssertIneligible(
            application,
            proposedPeriod,
            MembershipNonWorkingDayApplicationStatus
                .PeriodStartsAfterMembershipEffectiveEnd);
    }

    [Fact]
    public void CanonicalReadModelOverloadUsesStoredEffectiveEnd()
    {
        var issueTerms = CreateIssueTerms(new DateOnly(2026, 1, 1));
        var calculatedState = CreateState(issueTerms, extensionDays: 3);
        var readModel = new MembershipStateReadModel(
            MembershipId,
            ClientId,
            issueTerms,
            calculatedState,
            new DateOnly(2026, 1, 15));
        var period = new DateRange(
            calculatedState.EffectiveEndDate,
            calculatedState.EffectiveEndDate.AddDays(2));

        var application = MembershipNonWorkingDayApplicationPolicy.Evaluate(
            readModel,
            IssuedMembershipLifecycleStatus.Active,
            period);

        AssertEligibleFullPeriod(application, period);
    }

    [Fact]
    public void PolicyRejectsMissingOrUnsupportedCanonicalInputs()
    {
        var issueTerms = CreateIssueTerms(new DateOnly(2026, 1, 1));
        var state = CreateState(issueTerms);
        var period = CreateOrdinaryPeriod();

        var missingMembership = Assert.Throws<ArgumentException>(() =>
            MembershipNonWorkingDayApplicationPolicy.Evaluate(
                Guid.Empty,
                issueTerms,
                state,
                IssuedMembershipLifecycleStatus.Active,
                period));
        var missingTerms = Assert.Throws<ArgumentNullException>(() =>
            MembershipNonWorkingDayApplicationPolicy.Evaluate(
                MembershipId,
                issueTerms: null,
                state,
                IssuedMembershipLifecycleStatus.Active,
                period));
        var missingState = Assert.Throws<ArgumentNullException>(() =>
            MembershipNonWorkingDayApplicationPolicy.Evaluate(
                MembershipId,
                issueTerms,
                preCommandState: null,
                IssuedMembershipLifecycleStatus.Active,
                period));
        var unsupportedLifecycle = Assert.Throws<ArgumentOutOfRangeException>(() =>
            MembershipNonWorkingDayApplicationPolicy.Evaluate(
                MembershipId,
                issueTerms,
                state,
                (IssuedMembershipLifecycleStatus)999,
                period));

        Assert.Equal("membershipId", missingMembership.ParamName);
        Assert.Equal("issueTerms", missingTerms.ParamName);
        Assert.Equal("preCommandState", missingState.ParamName);
        Assert.Equal("lifecycleStatus", unsupportedLifecycle.ParamName);
    }

    private static void AssertEligibleFullPeriod(
        MembershipNonWorkingDayApplication application,
        DateRange expectedPeriod)
    {
        Assert.Equal(MembershipId, application.MembershipId);
        Assert.Equal(expectedPeriod, application.Period);
        Assert.Equal(
            MembershipNonWorkingDayApplicationStatus.Eligible,
            application.Status);
        Assert.True(application.IsEligible);
        Assert.Equal(expectedPeriod, application.AppliedRange);
        Assert.Equal(expectedPeriod.InclusiveDays, application.AppliedDays);
    }

    private static void AssertIneligible(
        MembershipNonWorkingDayApplication application,
        DateRange expectedPeriod,
        MembershipNonWorkingDayApplicationStatus expectedStatus)
    {
        Assert.Equal(MembershipId, application.MembershipId);
        Assert.Equal(expectedPeriod, application.Period);
        Assert.Equal(expectedStatus, application.Status);
        Assert.False(application.IsEligible);
        Assert.Null(application.AppliedRange);
        Assert.Equal(0, application.AppliedDays);
    }

    private static MembershipIssueTerms CreateIssueTerms(DateOnly startDate)
    {
        const int durationDays = 30;
        var snapshot = new IssuedMembershipSnapshot(
            "Non-working day policy membership",
            durationDays,
            visitsLimit: 8,
            new Money(1000m, "UAH"));

        return MembershipIssueTerms.FromIssuedSnapshot(
            MembershipTypeId,
            snapshot,
            startDate,
            MembershipDateRules.CalculateBaseEndDate(startDate, durationDays));
    }

    private static MembershipCalculatedState CreateState(
        MembershipIssueTerms issueTerms,
        int extensionDays = 0)
    {
        return MembershipCalculatedState.FromStoredCache(
            issueTerms,
            countedVisits: 0,
            remainingVisits: issueTerms.Snapshot.VisitsLimit,
            negativeBalance: 0,
            firstNegativeVisitId: null,
            firstNegativeVisitDate: null,
            extensionDays,
            effectiveEndDate: issueTerms.BaseEndDate.AddDays(extensionDays),
            lastCountedVisitAt: null);
    }

    private static DateRange CreateOrdinaryPeriod()
    {
        return new DateRange(
            new DateOnly(2026, 1, 10),
            new DateOnly(2026, 1, 12));
    }
}
