using BodyLife.Crm.Modules.Memberships;
using BodyLife.Crm.SharedKernel;

namespace BodyLife.Crm.Tests.Modules.Memberships;

public sealed class MembershipNonWorkingDayImpactEstimatorTests
{
    private static readonly DateRange ProposedPeriod = new(
        new DateOnly(2026, 1, 30),
        new DateOnly(2026, 2, 2));

    [Fact]
    public void FullPeriodAddsEveryDayWhenNoActiveDateSourceOverlaps()
    {
        var currentState = CreateState(
            baseEndDate: new DateOnly(2026, 2, 10),
            extensionDays: 2);

        var estimate = MembershipNonWorkingDayImpactEstimator.Estimate(
            currentState,
            currentDateRangeExtensions: null,
            ProposedPeriod);

        Assert.Equal(2, estimate.BeforeExtensionDays);
        Assert.Equal(new DateOnly(2026, 2, 12), estimate.BeforeEffectiveEndDate);
        Assert.Equal(6, estimate.EstimatedAfterExtensionDays);
        Assert.Equal(new DateOnly(2026, 2, 16), estimate.EstimatedAfterEffectiveEndDate);
        Assert.Equal(4, estimate.AddedUniqueExtensionDays);
        Assert.Equal(0, estimate.ExistingOverlapDays);
        Assert.Empty(estimate.OverlapWarnings);
        Assert.False(estimate.HasOverlapWarnings);
    }

    [Fact]
    public void ActiveFreezeAndNonWorkingOverlapUseUniqueDaysAndDeterministicWarnings()
    {
        var freezeId = Guid.Parse("10000000-0000-0000-0000-000000000001");
        var nonWorkingPeriodId = Guid.Parse("20000000-0000-0000-0000-000000000001");
        var inactiveSourceId = Guid.Parse("30000000-0000-0000-0000-000000000001");
        var dateRangeExtensions = MembershipExtensionCalculator.Calculate(
        [
            new MembershipExtensionSourceRange(
                "non_working_period",
                nonWorkingPeriodId,
                "Existing closure",
                new DateRange(
                    new DateOnly(2026, 1, 31),
                    new DateOnly(2026, 2, 1)),
                isActive: true),
            new MembershipExtensionSourceRange(
                "freeze",
                freezeId,
                "Existing freeze",
                new DateRange(
                    new DateOnly(2026, 1, 29),
                    new DateOnly(2026, 1, 31)),
                isActive: true),
            new MembershipExtensionSourceRange(
                "freeze",
                inactiveSourceId,
                "Canceled freeze",
                ProposedPeriod,
                isActive: false),
        ]);
        var currentState = CreateState(
            baseEndDate: new DateOnly(2026, 2, 10),
            extensionDays: 6);

        var estimate = MembershipNonWorkingDayImpactEstimator.Estimate(
            currentState,
            dateRangeExtensions,
            ProposedPeriod);

        Assert.Equal(6, estimate.BeforeExtensionDays);
        Assert.Equal(7, estimate.EstimatedAfterExtensionDays);
        Assert.Equal(1, estimate.AddedUniqueExtensionDays);
        Assert.Equal(3, estimate.ExistingOverlapDays);
        Assert.Equal(new DateOnly(2026, 2, 17), estimate.EstimatedAfterEffectiveEndDate);
        Assert.True(estimate.HasOverlapWarnings);
        Assert.Collection(
            estimate.OverlapWarnings,
            warning =>
            {
                Assert.Equal("freeze", warning.SourceType);
                Assert.Equal(freezeId, warning.SourceId);
                Assert.Equal("Existing freeze", warning.SourceLabel);
                Assert.Equal(
                    new DateRange(
                        new DateOnly(2026, 1, 30),
                        new DateOnly(2026, 1, 31)),
                    warning.OverlapRange);
                Assert.Equal(2, warning.OverlapDays);
            },
            warning =>
            {
                Assert.Equal("non_working_period", warning.SourceType);
                Assert.Equal(nonWorkingPeriodId, warning.SourceId);
                Assert.Equal("Existing closure", warning.SourceLabel);
                Assert.Equal(
                    new DateRange(
                        new DateOnly(2026, 1, 31),
                        new DateOnly(2026, 2, 1)),
                    warning.OverlapRange);
                Assert.Equal(2, warning.OverlapDays);
            });
        Assert.DoesNotContain(
            estimate.OverlapWarnings,
            warning => warning.SourceId == inactiveSourceId);
    }

    [Fact]
    public void InvalidPeriodInconsistentUnionAndCalendarOverflowAreRejected()
    {
        var sourceUnion = MembershipExtensionCalculator.Calculate(
        [
            new MembershipExtensionSourceRange(
                "freeze",
                Guid.NewGuid(),
                "Four active dates",
                ProposedPeriod,
                isActive: true),
        ]);
        var underreportedState = CreateState(
            baseEndDate: new DateOnly(2026, 2, 10),
            extensionDays: 3);
        var maximumState = CreateState(DateOnly.MaxValue, extensionDays: 0);

        Assert.Throws<ArgumentException>(() =>
            MembershipNonWorkingDayImpactEstimator.Estimate(
                underreportedState,
                sourceUnion,
                ProposedPeriod));
        Assert.Throws<ArgumentException>(() =>
            MembershipNonWorkingDayImpactEstimator.Estimate(
                underreportedState,
                currentDateRangeExtensions: null,
                new DateRange(default, default)));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            MembershipNonWorkingDayImpactEstimator.Estimate(
                maximumState,
                currentDateRangeExtensions: null,
                ProposedPeriod));
    }

    private static MembershipCalculatedState CreateState(
        DateOnly baseEndDate,
        int extensionDays)
    {
        var snapshot = new IssuedMembershipSnapshot(
            "Impact fixture",
            durationDays: 1,
            visitsLimit: 8,
            new Money(1000m, "UAH"));
        var terms = MembershipIssueTerms.FromIssuedSnapshot(
            Guid.NewGuid(),
            snapshot,
            baseEndDate,
            baseEndDate);
        var effectiveEndDate = DateOnly.FromDayNumber(
            baseEndDate.DayNumber + extensionDays);
        return MembershipCalculatedState.FromStoredCache(
            terms,
            countedVisits: 0,
            remainingVisits: 8,
            negativeBalance: 0,
            firstNegativeVisitId: null,
            firstNegativeVisitDate: null,
            extensionDays,
            effectiveEndDate,
            lastCountedVisitAt: null);
    }
}
