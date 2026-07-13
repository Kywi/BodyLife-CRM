using BodyLife.Crm.Modules.Memberships;
using BodyLife.Crm.SharedKernel;

namespace BodyLife.Crm.Tests.Modules.Memberships;

public sealed class MembershipStateExtensionCalculationTests
{
    private static readonly Guid MembershipTypeId = Guid.Parse(
        "11111111-1111-1111-1111-111111111111");
    private static readonly Guid FirstNegativeVisitId = Guid.Parse(
        "22222222-2222-2222-2222-222222222222");
    private static readonly Guid FreezeId = Guid.Parse(
        "33333333-3333-3333-3333-333333333333");
    private static readonly Guid NonWorkingPeriodId = Guid.Parse(
        "44444444-4444-4444-4444-444444444444");
    private static readonly Guid AdjustmentId = Guid.Parse(
        "55555555-5555-5555-5555-555555555555");
    private static readonly DateTimeOffset LastCountedVisitAt = new(
        2026,
        7,
        13,
        8,
        30,
        0,
        TimeSpan.Zero);

    [Fact]
    public void CanonicalUnionUpdatesOnlyExtensionOwnedState()
    {
        var issueTerms = CreateIssueTerms();
        var baseline = MembershipCalculatedState.FromStoredCache(
            issueTerms,
            countedVisits: 10,
            remainingVisits: -2,
            negativeBalance: 2,
            firstNegativeVisitId: FirstNegativeVisitId,
            firstNegativeVisitDate: new DateOnly(2026, 7, 12),
            extensionDays: 0,
            effectiveEndDate: issueTerms.BaseEndDate,
            lastCountedVisitAt: LastCountedVisitAt);
        var calculation = MembershipExtensionCalculator.Calculate(
        [
            Source(
                "freeze",
                FreezeId,
                new DateOnly(2026, 7, 10),
                new DateOnly(2026, 7, 12)),
            Source(
                "non_working_period",
                NonWorkingPeriodId,
                new DateOnly(2026, 7, 11),
                new DateOnly(2026, 7, 13)),
            Source(
                "membership_adjustment",
                AdjustmentId,
                new DateOnly(2026, 7, 14),
                new DateOnly(2026, 7, 15),
                isActive: false),
        ]);

        var state = MembershipStateCalculator.ApplyExtensionCalculation(
            issueTerms,
            baseline,
            calculation);

        Assert.Equal(4, calculation.ExtensionDays);
        Assert.Equal(8, calculation.ExplanationDays.Count);
        Assert.Equal(10, state.CountedVisits);
        Assert.Equal(-2, state.RemainingVisits);
        Assert.Equal(2, state.NegativeBalance);
        Assert.Equal(FirstNegativeVisitId, state.FirstNegativeVisitId);
        Assert.Equal(new DateOnly(2026, 7, 12), state.FirstNegativeVisitDate);
        Assert.Equal(4, state.ExtensionDays);
        Assert.Equal(new DateOnly(2026, 8, 3), state.EffectiveEndDate);
        Assert.Equal(LastCountedVisitAt, state.LastCountedVisitAt);
        Assert.True(state.IsActiveByDate(new DateOnly(2026, 8, 3)));
        Assert.False(state.IsActiveByDate(new DateOnly(2026, 8, 4)));
        Assert.Equal(0, baseline.ExtensionDays);
        Assert.Equal(issueTerms.BaseEndDate, baseline.EffectiveEndDate);
    }

    [Fact]
    public void EmptyCalculationKeepsCanonicalBaseEndDate()
    {
        var issueTerms = CreateIssueTerms();

        var state = MembershipStateCalculator.ApplyExtensionCalculation(
            issueTerms,
            MembershipStateCalculator.CalculateInitial(issueTerms),
            MembershipExtensionCalculator.Calculate([]));

        Assert.Equal(0, state.ExtensionDays);
        Assert.Equal(issueTerms.BaseEndDate, state.EffectiveEndDate);
    }

    [Fact]
    public void InactiveExplanationRowsDoNotExtendTheState()
    {
        var issueTerms = CreateIssueTerms();
        var calculation = MembershipExtensionCalculator.Calculate(
        [
            Source(
                "freeze",
                FreezeId,
                new DateOnly(2026, 7, 10),
                new DateOnly(2026, 7, 12),
                isActive: false),
        ]);

        var state = MembershipStateCalculator.ApplyExtensionCalculation(
            issueTerms,
            MembershipStateCalculator.CalculateInitial(issueTerms),
            calculation);

        Assert.Equal(0, calculation.ExtensionDays);
        Assert.Equal(3, calculation.ExplanationDays.Count);
        Assert.Equal(0, state.ExtensionDays);
        Assert.Equal(issueTerms.BaseEndDate, state.EffectiveEndDate);
    }

    [Fact]
    public void ExtensionMayReachTheFinalSupportedCalendarDate()
    {
        var issueTerms = CreateIssueTerms(
            startDate: DateOnly.MaxValue.AddDays(-1),
            durationDays: 1);
        var calculation = MembershipExtensionCalculator.Calculate(
        [
            Source(
                "freeze",
                FreezeId,
                DateOnly.MaxValue,
                DateOnly.MaxValue),
        ]);

        var state = MembershipStateCalculator.ApplyExtensionCalculation(
            issueTerms,
            MembershipStateCalculator.CalculateInitial(issueTerms),
            calculation);

        Assert.Equal(1, state.ExtensionDays);
        Assert.Equal(DateOnly.MaxValue, state.EffectiveEndDate);
    }

    [Fact]
    public void ExtensionBeyondTheSupportedCalendarRangeIsRejected()
    {
        var issueTerms = CreateIssueTerms(
            startDate: DateOnly.MaxValue.AddDays(-1),
            durationDays: 1);
        var calculation = MembershipExtensionCalculator.Calculate(
        [
            Source(
                "freeze",
                FreezeId,
                DateOnly.MaxValue.AddDays(-1),
                DateOnly.MaxValue),
        ]);

        var exception = Assert.Throws<ArgumentOutOfRangeException>(() =>
            MembershipStateCalculator.ApplyExtensionCalculation(
                issueTerms,
                MembershipStateCalculator.CalculateInitial(issueTerms),
                calculation));

        Assert.Equal("extensionCalculation", exception.ParamName);
        Assert.Equal(2, exception.ActualValue);
    }

    [Fact]
    public void ApplicationRequiresIssueTermsBaselineAndCalculation()
    {
        var issueTerms = CreateIssueTerms();
        var baseline = MembershipStateCalculator.CalculateInitial(issueTerms);
        var calculation = MembershipExtensionCalculator.Calculate([]);

        var missingTerms = Assert.Throws<ArgumentNullException>(() =>
            MembershipStateCalculator.ApplyExtensionCalculation(
                issueTerms: null,
                baseline,
                calculation));
        var missingBaseline = Assert.Throws<ArgumentNullException>(() =>
            MembershipStateCalculator.ApplyExtensionCalculation(
                issueTerms,
                baseline: null,
                calculation));
        var missingCalculation = Assert.Throws<ArgumentNullException>(() =>
            MembershipStateCalculator.ApplyExtensionCalculation(
                issueTerms,
                baseline,
                extensionCalculation: null));

        Assert.Equal("issueTerms", missingTerms.ParamName);
        Assert.Equal("baseline", missingBaseline.ParamName);
        Assert.Equal("extensionCalculation", missingCalculation.ParamName);
    }

    [Fact]
    public void AlreadyExtendedBaselineIsRejectedInsteadOfCompoundingDays()
    {
        var issueTerms = CreateIssueTerms();
        var extendedBaseline = MembershipCalculatedState.FromStoredCache(
            issueTerms,
            countedVisits: 0,
            remainingVisits: 8,
            negativeBalance: 0,
            firstNegativeVisitId: null,
            firstNegativeVisitDate: null,
            extensionDays: 2,
            effectiveEndDate: issueTerms.BaseEndDate.AddDays(2),
            lastCountedVisitAt: null);

        var exception = Assert.Throws<ArgumentException>(() =>
            MembershipStateCalculator.ApplyExtensionCalculation(
                issueTerms,
                extendedBaseline,
                MembershipExtensionCalculator.Calculate([])));

        Assert.Equal("baseline", exception.ParamName);
    }

    [Fact]
    public void BaselineFromDifferentIssueTermsIsRejected()
    {
        var originalTerms = CreateIssueTerms(durationDays: 30);
        var differentTerms = CreateIssueTerms(durationDays: 31);
        var baseline = MembershipStateCalculator.CalculateInitial(originalTerms);

        var exception = Assert.Throws<ArgumentException>(() =>
            MembershipStateCalculator.ApplyExtensionCalculation(
                differentTerms,
                baseline,
                MembershipExtensionCalculator.Calculate([])));

        Assert.Equal("baseline", exception.ParamName);
    }

    private static MembershipIssueTerms CreateIssueTerms(
        DateOnly? startDate = null,
        int durationDays = 30)
    {
        var resolvedStartDate = startDate ?? new DateOnly(2026, 7, 1);
        var snapshot = new IssuedMembershipSnapshot(
            "Eight visits",
            durationDays,
            visitsLimit: 8,
            new Money(1000m, "UAH"));

        return MembershipIssueTerms.FromIssuedSnapshot(
            MembershipTypeId,
            snapshot,
            resolvedStartDate,
            MembershipDateRules.CalculateBaseEndDate(
                resolvedStartDate,
                durationDays));
    }

    private static MembershipExtensionSourceRange Source(
        string sourceType,
        Guid sourceId,
        DateOnly startDate,
        DateOnly endDate,
        bool isActive = true)
    {
        return new MembershipExtensionSourceRange(
            sourceType,
            sourceId,
            $"{sourceType} source",
            new DateRange(startDate, endDate),
            isActive);
    }
}
