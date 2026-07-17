using BodyLife.Crm.Modules.Memberships;
using BodyLife.Crm.SharedKernel;

namespace BodyLife.Crm.Tests.Modules.Memberships;

public sealed class MembershipNonWorkingDayReplacementImpactPreparationTests
{
    private static readonly DateRange ReplacementPeriod = new(
        new DateOnly(2026, 2, 3),
        new DateOnly(2026, 2, 4));
    private static readonly Guid FirstApplicationId = Guid.Parse(
        "10000000-0000-0000-0000-000000000001");
    private static readonly Guid SecondApplicationId = Guid.Parse(
        "10000000-0000-0000-0000-000000000002");

    [Fact]
    public void ReplacementPreparationPreservesExactImmutableConfirmationMaterial()
    {
        var applicationIds = new[] { FirstApplicationId, SecondApplicationId };
        var impact = CreateImpact();

        var preparation = new MembershipNonWorkingDayReplacementImpactPreparation(
            Guid.NewGuid(),
            applicationIds,
            impact);
        applicationIds[0] = Guid.NewGuid();

        Assert.Equal(
            [FirstApplicationId, SecondApplicationId],
            preparation.ExcludedApplicationIds);
        Assert.Equal(ReplacementPeriod, preparation.ReplacementPeriod);
        Assert.Same(impact, preparation.ReplacementImpact);
        Assert.Same(impact.AffectedScope, preparation.AffectedScope);
        Assert.Same(impact.AffectedMemberships, preparation.AffectedMemberships);
        Assert.Equal(1, preparation.AffectedCount);
        Assert.Throws<NotSupportedException>(() =>
            ((IList<Guid>)preparation.ExcludedApplicationIds).Add(Guid.NewGuid()));
    }

    [Fact]
    public void ReplacementPreparationRejectsAmbiguousExcludedSourceIdentities()
    {
        var impact = CreateImpact();

        Assert.Throws<ArgumentException>(() =>
            new MembershipNonWorkingDayReplacementImpactPreparation(
                Guid.Empty,
                [FirstApplicationId],
                impact));
        Assert.Throws<ArgumentException>(() =>
            new MembershipNonWorkingDayReplacementImpactPreparation(
                Guid.NewGuid(),
                [Guid.Empty],
                impact));
        Assert.Throws<ArgumentException>(() =>
            new MembershipNonWorkingDayReplacementImpactPreparation(
                Guid.NewGuid(),
                [FirstApplicationId, FirstApplicationId],
                impact));
        Assert.Throws<ArgumentException>(() =>
            new MembershipNonWorkingDayReplacementImpactPreparation(
                Guid.NewGuid(),
                [SecondApplicationId, FirstApplicationId],
                impact));
    }

    [Fact]
    public void ExtensionSourceTypesHaveStablePersistenceValues()
    {
        Assert.Equal("freeze", MembershipExtensionSourceRange.FreezeSourceType);
        Assert.Equal(
            "non_working_period",
            MembershipExtensionSourceRange.NonWorkingPeriodSourceType);
    }

    private static MembershipNonWorkingDayImpactPreparation CreateImpact()
    {
        var membershipId = Guid.NewGuid();
        var clientId = Guid.NewGuid();
        var scope = new MembershipNonWorkingDayAffectedScope(
            ReplacementPeriod,
            [
                new MembershipNonWorkingDayAffectedScopeItem(
                    membershipId,
                    clientId,
                    ReplacementPeriod),
            ]);
        var snapshot = new IssuedMembershipSnapshot(
            "Replacement fixture",
            durationDays: 1,
            visitsLimit: 8,
            new Money(1000m, "UAH"));
        var baseEndDate = new DateOnly(2026, 2, 10);
        var terms = MembershipIssueTerms.FromIssuedSnapshot(
            Guid.NewGuid(),
            snapshot,
            baseEndDate,
            baseEndDate);
        var currentState = MembershipCalculatedState.FromStoredCache(
            terms,
            countedVisits: 0,
            remainingVisits: 8,
            negativeBalance: 0,
            firstNegativeVisitId: null,
            firstNegativeVisitDate: null,
            extensionDays: 0,
            effectiveEndDate: baseEndDate,
            lastCountedVisitAt: null);
        var estimate = MembershipNonWorkingDayImpactEstimator.Estimate(
            currentState,
            currentDateRangeExtensions: null,
            ReplacementPeriod);

        return new MembershipNonWorkingDayImpactPreparation(
            scope,
            [
                new MembershipNonWorkingDayImpactItem(
                    membershipId,
                    clientId,
                    ReplacementPeriod,
                    estimate),
            ]);
    }
}
