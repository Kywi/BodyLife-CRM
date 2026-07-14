using BodyLife.Crm.Modules.Memberships;
using BodyLife.Crm.SharedKernel;

namespace BodyLife.Crm.Tests.Modules.Memberships;

public sealed class MembershipVisitEligibilityPolicyTests
{
    private static readonly Guid MembershipId = Guid.Parse(
        "11111111-1111-1111-1111-111111111111");
    private static readonly Guid FreezeId = Guid.Parse(
        "22222222-2222-2222-2222-222222222222");

    [Fact]
    public void ExplicitSelectedMembershipWithinTermIsEligibleWithoutAcknowledgements()
    {
        var issueTerms = CreateIssueTerms(visitsLimit: 8);
        var state = CreateState(issueTerms, remainingVisits: 5);

        var eligibility = MembershipVisitEligibilityPolicy.Evaluate(
            MembershipId,
            issueTerms,
            state,
            IssuedMembershipLifecycleStatus.Active,
            new DateOnly(2026, 7, 15),
            freezeSources: []);

        Assert.Equal(MembershipId, eligibility.MembershipId);
        Assert.Equal(MembershipVisitEligibilityStatus.Eligible, eligibility.Status);
        Assert.True(eligibility.IsEligible);
        Assert.Null(eligibility.ErrorCode);
        Assert.Empty(eligibility.RequiredAcknowledgements);
    }

    [Theory]
    [InlineData(0, MembershipVisitAcknowledgement.ZeroRemaining)]
    [InlineData(-1, MembershipVisitAcknowledgement.NegativeRemaining)]
    public void ExpiredZeroOrNegativeSelectionStaysEligibleWithEveryRequiredAcknowledgement(
        int remainingVisits,
        MembershipVisitAcknowledgement balanceAcknowledgement)
    {
        var issueTerms = CreateIssueTerms(visitsLimit: 2);
        var state = CreateState(issueTerms, remainingVisits);

        var eligibility = MembershipVisitEligibilityPolicy.Evaluate(
            MembershipId,
            issueTerms,
            state,
            IssuedMembershipLifecycleStatus.Active,
            issueTerms.BaseEndDate.AddDays(1),
            freezeSources: []);

        Assert.True(eligibility.IsEligible);
        Assert.Equal(
            [MembershipVisitAcknowledgement.Expired, balanceAcknowledgement],
            eligibility.RequiredAcknowledgements);

        var acknowledgementList = Assert.IsAssignableFrom<IList<MembershipVisitAcknowledgement>>(
            eligibility.RequiredAcknowledgements);
        Assert.Throws<NotSupportedException>(() =>
            acknowledgementList.Add(MembershipVisitAcknowledgement.Expired));
    }

    [Fact]
    public void FutureStartMembershipIsIneligibleWithoutAcknowledgementOverride()
    {
        var issueTerms = CreateIssueTerms(visitsLimit: 8);

        var eligibility = MembershipVisitEligibilityPolicy.Evaluate(
            MembershipId,
            issueTerms,
            CreateState(issueTerms, remainingVisits: 8),
            IssuedMembershipLifecycleStatus.Active,
            issueTerms.StartDate.AddDays(-1),
            freezeSources: []);

        Assert.False(eligibility.IsEligible);
        Assert.Equal(
            MembershipVisitEligibilityStatus.BeforeMembershipStart,
            eligibility.Status);
        Assert.Equal(
            MembershipVisitEligibilityErrorCodes.MembershipNotEligible,
            eligibility.ErrorCode);
        Assert.Empty(eligibility.RequiredAcknowledgements);
    }

    [Theory]
    [InlineData(IssuedMembershipLifecycleStatus.Canceled)]
    [InlineData(IssuedMembershipLifecycleStatus.Corrected)]
    public void InactiveLifecycleMembershipIsIneligible(
        IssuedMembershipLifecycleStatus lifecycleStatus)
    {
        var issueTerms = CreateIssueTerms(visitsLimit: 8);

        var eligibility = MembershipVisitEligibilityPolicy.Evaluate(
            MembershipId,
            issueTerms,
            CreateState(issueTerms, remainingVisits: 8),
            lifecycleStatus,
            new DateOnly(2026, 7, 15),
            freezeSources: []);

        Assert.False(eligibility.IsEligible);
        Assert.Equal(
            MembershipVisitEligibilityStatus.MembershipInactive,
            eligibility.Status);
        Assert.Equal(
            MembershipVisitEligibilityErrorCodes.MembershipNotEligible,
            eligibility.ErrorCode);
    }

    [Fact]
    public void ActiveFreezeBlocksBothInclusiveEndpoints()
    {
        var issueTerms = CreateIssueTerms(visitsLimit: 8);
        var state = CreateState(issueTerms, remainingVisits: 8);
        var freeze = new MembershipVisitFreezeSource(
            MembershipId,
            FreezeId,
            new DateRange(
                new DateOnly(2026, 7, 10),
                new DateOnly(2026, 7, 12)),
            isActive: true);

        var onStart = MembershipVisitEligibilityPolicy.Evaluate(
            MembershipId,
            issueTerms,
            state,
            IssuedMembershipLifecycleStatus.Active,
            freeze.Range.StartDate,
            [freeze]);
        var onEnd = MembershipVisitEligibilityPolicy.Evaluate(
            MembershipId,
            issueTerms,
            state,
            IssuedMembershipLifecycleStatus.Active,
            freeze.Range.EndDate,
            [freeze]);

        Assert.All(
            new[] { onStart, onEnd },
            eligibility =>
            {
                Assert.False(eligibility.IsEligible);
                Assert.Equal(
                    MembershipVisitEligibilityStatus.DuringActiveFreeze,
                    eligibility.Status);
                Assert.Equal(
                    MembershipVisitEligibilityErrorCodes.VisitDuringFreeze,
                    eligibility.ErrorCode);
                Assert.Empty(eligibility.RequiredAcknowledgements);
            });
    }

    [Fact]
    public void CanceledFreezeDoesNotBlockMembershipVisit()
    {
        var issueTerms = CreateIssueTerms(visitsLimit: 8);
        var canceledFreeze = new MembershipVisitFreezeSource(
            MembershipId,
            FreezeId,
            new DateRange(
                new DateOnly(2026, 7, 10),
                new DateOnly(2026, 7, 12)),
            isActive: false);

        var eligibility = MembershipVisitEligibilityPolicy.Evaluate(
            MembershipId,
            issueTerms,
            CreateState(issueTerms, remainingVisits: 8),
            IssuedMembershipLifecycleStatus.Active,
            new DateOnly(2026, 7, 11),
            [canceledFreeze]);

        Assert.True(eligibility.IsEligible);
        Assert.Empty(eligibility.RequiredAcknowledgements);
    }

    [Fact]
    public void CanonicalReadModelOverloadRequiresTheVisitDateAndPreservesRules()
    {
        var issueTerms = CreateIssueTerms(visitsLimit: 2);
        var visitDate = issueTerms.BaseEndDate.AddDays(1);
        var readModel = new MembershipStateReadModel(
            MembershipId,
            Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"),
            issueTerms,
            CreateState(issueTerms, remainingVisits: 0),
            visitDate);

        var eligibility = MembershipVisitEligibilityPolicy.Evaluate(
            readModel,
            IssuedMembershipLifecycleStatus.Active,
            visitDate,
            freezeSources: []);

        Assert.Equal(
            [
                MembershipVisitAcknowledgement.Expired,
                MembershipVisitAcknowledgement.ZeroRemaining,
            ],
            eligibility.RequiredAcknowledgements);
        var mismatchedDate = Assert.Throws<ArgumentException>(() =>
            MembershipVisitEligibilityPolicy.Evaluate(
                readModel,
                IssuedMembershipLifecycleStatus.Active,
                visitDate.AddDays(1),
                freezeSources: []));
        Assert.Equal("state", mismatchedDate.ParamName);
    }

    [Fact]
    public void EligibilityRejectsMissingOrMismatchedExplicitInputs()
    {
        var issueTerms = CreateIssueTerms(visitsLimit: 8);
        var state = CreateState(issueTerms, remainingVisits: 8);
        var visitDate = new DateOnly(2026, 7, 15);
        var foreignFreeze = new MembershipVisitFreezeSource(
            Guid.Parse("33333333-3333-3333-3333-333333333333"),
            FreezeId,
            new DateRange(visitDate, visitDate),
            isActive: true);

        var missingMembership = Assert.Throws<ArgumentException>(() =>
            MembershipVisitEligibilityPolicy.Evaluate(
                Guid.Empty,
                issueTerms,
                state,
                IssuedMembershipLifecycleStatus.Active,
                visitDate,
                freezeSources: []));
        var foreignSource = Assert.Throws<ArgumentException>(() =>
            MembershipVisitEligibilityPolicy.Evaluate(
                MembershipId,
                issueTerms,
                state,
                IssuedMembershipLifecycleStatus.Active,
                visitDate,
                [foreignFreeze]));
        var unsupportedLifecycle = Assert.Throws<ArgumentOutOfRangeException>(() =>
            MembershipVisitEligibilityPolicy.Evaluate(
                MembershipId,
                issueTerms,
                state,
                (IssuedMembershipLifecycleStatus)999,
                visitDate,
                freezeSources: []));

        Assert.Equal("membershipId", missingMembership.ParamName);
        Assert.Equal("freezeSources", foreignSource.ParamName);
        Assert.Equal("lifecycleStatus", unsupportedLifecycle.ParamName);
    }

    private static MembershipIssueTerms CreateIssueTerms(int visitsLimit)
    {
        var snapshot = new IssuedMembershipSnapshot(
            "Visit eligibility membership",
            durationDays: 30,
            visitsLimit,
            new Money(1000m, "UAH"));

        return MembershipIssueTerms.FromIssuedSnapshot(
            Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
            snapshot,
            new DateOnly(2026, 7, 1),
            new DateOnly(2026, 7, 30));
    }

    private static MembershipCalculatedState CreateState(
        MembershipIssueTerms issueTerms,
        int remainingVisits)
    {
        return MembershipCalculatedState.FromStoredCache(
            issueTerms,
            countedVisits: issueTerms.Snapshot.VisitsLimit - remainingVisits,
            remainingVisits,
            negativeBalance: Math.Max(0, -remainingVisits),
            firstNegativeVisitId: null,
            firstNegativeVisitDate: null,
            extensionDays: 0,
            effectiveEndDate: issueTerms.BaseEndDate,
            lastCountedVisitAt: null);
    }
}
