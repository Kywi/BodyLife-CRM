using BodyLife.Crm.Modules.Memberships;
using BodyLife.Crm.SharedKernel;

namespace BodyLife.Crm.Tests.Modules.Memberships;

public sealed class MembershipFreezeEligibilityPolicyTests
{
    private static readonly Guid MembershipId = Guid.Parse(
        "11111111-1111-1111-1111-111111111111");
    private static readonly Guid MembershipTypeId = Guid.Parse(
        "22222222-2222-2222-2222-222222222222");
    private static readonly Guid ClientId = Guid.Parse(
        "33333333-3333-3333-3333-333333333333");

    [Fact]
    public void InclusiveStartBoundsAreEligibleAndEndIsNotClipped()
    {
        var issueTerms = CreateIssueTerms();
        var state = CreateState(issueTerms, extensionDays: 2);
        var fromMembershipStart = new DateRange(
            issueTerms.StartDate,
            issueTerms.StartDate);
        var throughPreviousEffectiveEnd = new DateRange(
            state.EffectiveEndDate,
            state.EffectiveEndDate.AddDays(5));

        var onMembershipStart = MembershipFreezeEligibilityPolicy.Evaluate(
            MembershipId,
            issueTerms,
            state,
            IssuedMembershipLifecycleStatus.Active,
            fromMembershipStart,
            visitSources: []);
        var onEffectiveEnd = MembershipFreezeEligibilityPolicy.Evaluate(
            MembershipId,
            issueTerms,
            state,
            IssuedMembershipLifecycleStatus.Active,
            throughPreviousEffectiveEnd,
            visitSources: []);

        Assert.All(
            new[] { onMembershipStart, onEffectiveEnd },
            eligibility =>
            {
                Assert.Equal(MembershipId, eligibility.MembershipId);
                Assert.Equal(MembershipFreezeEligibilityStatus.Eligible, eligibility.Status);
                Assert.True(eligibility.IsEligible);
                Assert.Null(eligibility.ErrorCode);
            });
        Assert.Equal(fromMembershipStart, onMembershipStart.Range);
        Assert.Equal(throughPreviousEffectiveEnd, onEffectiveEnd.Range);
    }

    [Theory]
    [InlineData(IssuedMembershipLifecycleStatus.Canceled)]
    [InlineData(IssuedMembershipLifecycleStatus.Corrected)]
    public void HistoricalLifecycleMembershipIsIneligible(
        IssuedMembershipLifecycleStatus lifecycleStatus)
    {
        var issueTerms = CreateIssueTerms();
        var range = CreateOrdinaryRange();

        var eligibility = MembershipFreezeEligibilityPolicy.Evaluate(
            MembershipId,
            issueTerms,
            CreateState(issueTerms),
            lifecycleStatus,
            range,
            visitSources: []);

        Assert.False(eligibility.IsEligible);
        Assert.Equal(
            MembershipFreezeEligibilityStatus.MembershipInactive,
            eligibility.Status);
        Assert.Equal(
            MembershipFreezeEligibilityErrorCodes.MembershipNotEligible,
            eligibility.ErrorCode);
        Assert.Equal(range, eligibility.Range);
    }

    [Fact]
    public void FreezeBeforeMembershipStartIsIneligible()
    {
        var issueTerms = CreateIssueTerms();

        var eligibility = MembershipFreezeEligibilityPolicy.Evaluate(
            MembershipId,
            issueTerms,
            CreateState(issueTerms),
            IssuedMembershipLifecycleStatus.Active,
            new DateRange(
                issueTerms.StartDate.AddDays(-1),
                issueTerms.StartDate),
            visitSources: []);

        Assert.False(eligibility.IsEligible);
        Assert.Equal(
            MembershipFreezeEligibilityStatus.BeforeMembershipStart,
            eligibility.Status);
        Assert.Equal(
            MembershipFreezeEligibilityErrorCodes.MembershipNotEligible,
            eligibility.ErrorCode);
    }

    [Fact]
    public void FreezeStartingAfterCanonicalEffectiveEndIsIneligible()
    {
        var issueTerms = CreateIssueTerms();
        var state = CreateState(issueTerms, extensionDays: 3);
        var startDate = state.EffectiveEndDate.AddDays(1);

        var eligibility = MembershipFreezeEligibilityPolicy.Evaluate(
            MembershipId,
            issueTerms,
            state,
            IssuedMembershipLifecycleStatus.Active,
            new DateRange(startDate, startDate.AddDays(1)),
            visitSources: []);

        Assert.False(eligibility.IsEligible);
        Assert.Equal(
            MembershipFreezeEligibilityStatus.AfterMembershipEffectiveEnd,
            eligibility.Status);
        Assert.Equal(
            MembershipFreezeEligibilityErrorCodes.MembershipNotEligible,
            eligibility.ErrorCode);
    }

    [Theory]
    [InlineData(10)]
    [InlineData(12)]
    public void ActiveCountedVisitOnEitherInclusiveEndpointBlocksFreeze(int visitDay)
    {
        var issueTerms = CreateIssueTerms();
        var range = CreateOrdinaryRange();
        var visit = CreateVisit(
            Guid.NewGuid(),
            new DateOnly(2026, 7, visitDay),
            MembershipVisitSourceStatus.Active);

        var eligibility = MembershipFreezeEligibilityPolicy.Evaluate(
            MembershipId,
            issueTerms,
            CreateState(issueTerms),
            IssuedMembershipLifecycleStatus.Active,
            range,
            [visit]);

        Assert.False(eligibility.IsEligible);
        Assert.Equal(
            MembershipFreezeEligibilityStatus.ConflictsWithActiveVisit,
            eligibility.Status);
        Assert.Equal(
            MembershipFreezeEligibilityErrorCodes.FreezeConflictsWithVisit,
            eligibility.ErrorCode);
    }

    [Fact]
    public void CanceledAndOutsideVisitsDoNotBlockFreeze()
    {
        var issueTerms = CreateIssueTerms();
        var range = CreateOrdinaryRange();
        var canceledInside = CreateVisit(
            Guid.Parse("44444444-4444-4444-4444-444444444444"),
            new DateOnly(2026, 7, 11),
            MembershipVisitSourceStatus.Canceled);
        var activeOutside = CreateVisit(
            Guid.Parse("55555555-5555-5555-5555-555555555555"),
            range.EndDate.AddDays(1),
            MembershipVisitSourceStatus.Active);

        var eligibility = MembershipFreezeEligibilityPolicy.Evaluate(
            MembershipId,
            issueTerms,
            CreateState(issueTerms),
            IssuedMembershipLifecycleStatus.Active,
            range,
            [canceledInside, activeOutside]);

        Assert.True(eligibility.IsEligible);
        Assert.Null(eligibility.ErrorCode);
    }

    [Fact]
    public void CanonicalReadModelOverloadUsesStoredEffectiveEnd()
    {
        var issueTerms = CreateIssueTerms();
        var calculatedState = CreateState(issueTerms, extensionDays: 2);
        var readModel = new MembershipStateReadModel(
            MembershipId,
            ClientId,
            issueTerms,
            calculatedState,
            new DateOnly(2026, 8, 15));
        var range = new DateRange(
            calculatedState.EffectiveEndDate,
            calculatedState.EffectiveEndDate.AddDays(2));

        var eligibility = MembershipFreezeEligibilityPolicy.Evaluate(
            readModel,
            IssuedMembershipLifecycleStatus.Active,
            range,
            visitSources: []);

        Assert.True(eligibility.IsEligible);
        Assert.Equal(range, eligibility.Range);
    }

    [Fact]
    public void EligibilityRejectsMissingOrInconsistentCanonicalInputs()
    {
        var issueTerms = CreateIssueTerms();
        var state = CreateState(issueTerms);
        var range = CreateOrdinaryRange();
        var visitId = Guid.Parse("66666666-6666-6666-6666-666666666666");
        var foreignVisit = CreateVisit(
            visitId,
            range.StartDate,
            MembershipVisitSourceStatus.Active,
            Guid.Parse("77777777-7777-7777-7777-777777777777"));
        var duplicateVisit = CreateVisit(
            visitId,
            range.StartDate,
            MembershipVisitSourceStatus.Active);

        var missingMembership = Assert.Throws<ArgumentException>(() =>
            MembershipFreezeEligibilityPolicy.Evaluate(
                Guid.Empty,
                issueTerms,
                state,
                IssuedMembershipLifecycleStatus.Active,
                range,
                visitSources: []));
        var foreignSource = Assert.Throws<ArgumentException>(() =>
            MembershipFreezeEligibilityPolicy.Evaluate(
                MembershipId,
                issueTerms,
                state,
                IssuedMembershipLifecycleStatus.Active,
                range,
                [foreignVisit]));
        var duplicateSource = Assert.Throws<ArgumentException>(() =>
            MembershipFreezeEligibilityPolicy.Evaluate(
                MembershipId,
                issueTerms,
                state,
                IssuedMembershipLifecycleStatus.Active,
                range,
                [duplicateVisit, duplicateVisit]));
        var unsupportedLifecycle = Assert.Throws<ArgumentOutOfRangeException>(() =>
            MembershipFreezeEligibilityPolicy.Evaluate(
                MembershipId,
                issueTerms,
                state,
                (IssuedMembershipLifecycleStatus)999,
                range,
                visitSources: []));

        Assert.Equal("membershipId", missingMembership.ParamName);
        Assert.Equal("visitSources", foreignSource.ParamName);
        Assert.Equal("visitSources", duplicateSource.ParamName);
        Assert.Equal("lifecycleStatus", unsupportedLifecycle.ParamName);
    }

    private static MembershipIssueTerms CreateIssueTerms()
    {
        var snapshot = new IssuedMembershipSnapshot(
            "Freeze eligibility membership",
            durationDays: 30,
            visitsLimit: 8,
            new Money(1000m, "UAH"));

        return MembershipIssueTerms.FromIssuedSnapshot(
            MembershipTypeId,
            snapshot,
            new DateOnly(2026, 7, 1),
            new DateOnly(2026, 7, 30));
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

    private static DateRange CreateOrdinaryRange()
    {
        return new DateRange(
            new DateOnly(2026, 7, 10),
            new DateOnly(2026, 7, 12));
    }

    private static MembershipVisitSourceFact CreateVisit(
        Guid visitId,
        DateOnly businessDate,
        MembershipVisitSourceStatus status,
        Guid? membershipId = null)
    {
        var occurredAt = new DateTimeOffset(
            businessDate.Year,
            businessDate.Month,
            businessDate.Day,
            10,
            0,
            0,
            TimeSpan.Zero);

        return new MembershipVisitSourceFact(
            membershipId ?? MembershipId,
            visitId,
            businessDate,
            occurredAt,
            occurredAt.AddMinutes(1),
            status);
    }
}
