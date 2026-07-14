using BodyLife.Crm.Modules.Memberships;
using BodyLife.Crm.SharedKernel;

namespace BodyLife.Crm.Tests.Modules.Memberships;

public sealed class MembershipAdjustmentCalculationTests
{
    private static readonly Guid MembershipId = Guid.Parse(
        "11111111-1111-1111-1111-111111111111");
    private static readonly Guid ForeignMembershipId = Guid.Parse(
        "22222222-2222-2222-2222-222222222222");

    [Fact]
    public void ActiveVisitAndDayAdjustmentsChangeCanonicalBaselineWithoutSyntheticVisits()
    {
        var state = MembershipStateCalculator.CalculateFromAdjustmentFacts(
            MembershipId,
            CreateIssueTerms(visitsLimit: 2),
            [
                CreateFact(
                    MembershipAdjustmentTypes.VisitBalance,
                    visitsDelta: 1),
                CreateFact(
                    MembershipAdjustmentTypes.ExtensionDays,
                    daysDelta: 3),
                CreateFact(
                    MembershipAdjustmentTypes.VisitBalance,
                    visitsDelta: -4),
            ]);

        Assert.Equal(0, state.CountedVisits);
        Assert.Equal(-1, state.RemainingVisits);
        Assert.Equal(1, state.NegativeBalance);
        Assert.Null(state.FirstNegativeVisitId);
        Assert.Null(state.FirstNegativeVisitDate);
        Assert.Equal(3, state.ExtensionDays);
        Assert.Equal(new DateOnly(2026, 8, 2), state.EffectiveEndDate);
        Assert.Null(state.LastCountedVisitAt);
    }

    [Fact]
    public void OpeningBaselineAppliesOnlyExplicitlyUncoveredAdjustmentFacts()
    {
        var issueTerms = CreateIssueTerms(visitsLimit: 8);
        var openingState = MembershipOpeningState.FromDeclaration(
            new DateOnly(2026, 7, 13),
            declaredRemainingVisits: -2,
            knownExtensionDays: 4);

        var state = MembershipStateCalculator.CalculateFromOpeningStateAndAdjustmentFacts(
            MembershipId,
            issueTerms,
            openingState,
            [
                CreateFact(
                    MembershipAdjustmentTypes.VisitBalance,
                    visitsDelta: 3),
                CreateFact(
                    MembershipAdjustmentTypes.ExtensionDays,
                    daysDelta: 2),
            ]);

        Assert.Equal(0, state.CountedVisits);
        Assert.Equal(1, state.RemainingVisits);
        Assert.Equal(0, state.NegativeBalance);
        Assert.Null(state.FirstNegativeVisitId);
        Assert.Null(state.FirstNegativeVisitDate);
        Assert.Equal(6, state.ExtensionDays);
        Assert.Equal(new DateOnly(2026, 8, 5), state.EffectiveEndDate);
    }

    [Fact]
    public void CanceledAndCorrectedUnsupportedHistoryDoesNotAffectState()
    {
        var issueTerms = CreateIssueTerms(visitsLimit: 2);
        var canceledMoney = CreateFact(
            "money_correction",
            moneyDelta: 150m,
            status: MembershipAdjustmentSourceStatus.Canceled);
        var correctedMixed = CreateFact(
            "legacy_mixed",
            daysDelta: -2,
            visitsDelta: 4,
            status: MembershipAdjustmentSourceStatus.Corrected);

        var state = MembershipStateCalculator.CalculateFromAdjustmentFacts(
            MembershipId,
            issueTerms,
            [canceledMoney, correctedMixed]);

        Assert.Equal(2, state.RemainingVisits);
        Assert.Equal(0, state.NegativeBalance);
        Assert.Equal(0, state.ExtensionDays);
        Assert.Equal(issueTerms.BaseEndDate, state.EffectiveEndDate);
        Assert.False(canceledMoney.IsActive);
        Assert.False(correctedMixed.IsActive);
    }

    [Theory]
    [MemberData(nameof(UnsupportedActiveAdjustmentFacts))]
    public void UnsupportedActiveAdjustmentSemanticsAreRejected(
        MembershipAdjustmentSourceFact fact)
    {
        var exception = Assert.Throws<ArgumentException>(() =>
            MembershipStateCalculator.CalculateFromAdjustmentFacts(
                MembershipId,
                CreateIssueTerms(visitsLimit: 2),
                [fact]));

        Assert.Equal("adjustmentFacts", exception.ParamName);
    }

    [Fact]
    public void DuplicateAndForeignAdjustmentFactsAreRejected()
    {
        var issueTerms = CreateIssueTerms(visitsLimit: 2);
        var fact = CreateFact(
            MembershipAdjustmentTypes.VisitBalance,
            visitsDelta: 1);
        var foreign = CreateFact(
            MembershipAdjustmentTypes.VisitBalance,
            visitsDelta: 1,
            membershipId: ForeignMembershipId);

        var duplicateException = Assert.Throws<ArgumentException>(() =>
            MembershipStateCalculator.CalculateFromAdjustmentFacts(
                MembershipId,
                issueTerms,
                [fact, fact]));
        var foreignException = Assert.Throws<ArgumentException>(() =>
            MembershipStateCalculator.CalculateFromAdjustmentFacts(
                MembershipId,
                issueTerms,
                [foreign]));

        Assert.Equal("adjustmentFacts", duplicateException.ParamName);
        Assert.Equal("adjustmentFacts", foreignException.ParamName);
    }

    [Fact]
    public void AdjustmentCalculationRejectsUnrepresentableState()
    {
        var issueTerms = CreateIssueTerms(visitsLimit: 8);
        var openingState = MembershipOpeningState.FromDeclaration(
            new DateOnly(2026, 7, 13),
            declaredRemainingVisits: int.MaxValue);

        var exception = Assert.Throws<ArgumentOutOfRangeException>(() =>
            MembershipStateCalculator.CalculateFromOpeningStateAndAdjustmentFacts(
                MembershipId,
                issueTerms,
                openingState,
                [
                    CreateFact(
                        MembershipAdjustmentTypes.VisitBalance,
                        visitsDelta: 1),
                ]));

        Assert.Equal(
            "adjustmentFactsNotIncludedInOpeningState",
            exception.ParamName);
    }

    [Fact]
    public void SourceContractNormalizesTypeAndPreservesControlledValues()
    {
        var effectiveDate = new DateOnly(2026, 7, 14);
        var fact = new MembershipAdjustmentSourceFact(
            MembershipId,
            Guid.Parse("33333333-3333-3333-3333-333333333333"),
            "  visit_balance  ",
            daysDelta: null,
            visitsDelta: -2,
            moneyDelta: null,
            effectiveDate,
            MembershipAdjustmentSourceStatus.Active);

        Assert.Equal(MembershipAdjustmentTypes.VisitBalance, fact.AdjustmentType);
        Assert.Equal(-2, fact.VisitsDelta);
        Assert.Equal(effectiveDate, fact.EffectiveDate);
        Assert.True(fact.IsActive);
    }

    [Fact]
    public void SourceContractRejectsMissingIdentityDeltaAndUnsupportedStatus()
    {
        var effectiveDate = new DateOnly(2026, 7, 14);

        var missingMembership = Assert.Throws<ArgumentException>(() =>
            new MembershipAdjustmentSourceFact(
                Guid.Empty,
                Guid.NewGuid(),
                MembershipAdjustmentTypes.VisitBalance,
                daysDelta: null,
                visitsDelta: 1,
                moneyDelta: null,
                effectiveDate,
                MembershipAdjustmentSourceStatus.Active));
        var missingDelta = Assert.Throws<ArgumentException>(() =>
            new MembershipAdjustmentSourceFact(
                MembershipId,
                Guid.NewGuid(),
                MembershipAdjustmentTypes.VisitBalance,
                daysDelta: null,
                visitsDelta: null,
                moneyDelta: null,
                effectiveDate,
                MembershipAdjustmentSourceStatus.Active));
        var unsupportedStatus = Assert.Throws<ArgumentOutOfRangeException>(() =>
            new MembershipAdjustmentSourceFact(
                MembershipId,
                Guid.NewGuid(),
                MembershipAdjustmentTypes.VisitBalance,
                daysDelta: null,
                visitsDelta: 1,
                moneyDelta: null,
                effectiveDate,
                (MembershipAdjustmentSourceStatus)999));

        Assert.Equal("membershipId", missingMembership.ParamName);
        Assert.Equal("daysDelta", missingDelta.ParamName);
        Assert.Equal("status", unsupportedStatus.ParamName);
    }

    public static TheoryData<MembershipAdjustmentSourceFact>
        UnsupportedActiveAdjustmentFacts => new()
        {
            CreateFact("money_correction", moneyDelta: 100m),
            CreateFact("future_adjustment", visitsDelta: 1),
            CreateFact(MembershipAdjustmentTypes.ExtensionDays, daysDelta: -1),
            CreateFact(
                MembershipAdjustmentTypes.VisitBalance,
                daysDelta: 1,
                visitsDelta: 1),
        };

    private static MembershipIssueTerms CreateIssueTerms(int visitsLimit)
    {
        var snapshot = new IssuedMembershipSnapshot(
            "Adjustment calculation membership",
            durationDays: 30,
            visitsLimit,
            new Money(1000m, "UAH"));

        return MembershipIssueTerms.FromIssuedSnapshot(
            Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
            snapshot,
            new DateOnly(2026, 7, 1),
            new DateOnly(2026, 7, 30));
    }

    private static MembershipAdjustmentSourceFact CreateFact(
        string adjustmentType,
        int? daysDelta = null,
        int? visitsDelta = null,
        decimal? moneyDelta = null,
        MembershipAdjustmentSourceStatus status = MembershipAdjustmentSourceStatus.Active,
        Guid? membershipId = null)
    {
        return new MembershipAdjustmentSourceFact(
            membershipId ?? MembershipId,
            Guid.NewGuid(),
            adjustmentType,
            daysDelta,
            visitsDelta,
            moneyDelta,
            new DateOnly(2026, 7, 14),
            status);
    }
}
