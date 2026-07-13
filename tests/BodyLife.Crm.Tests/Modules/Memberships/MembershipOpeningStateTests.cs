using BodyLife.Crm.Modules.Memberships;
using BodyLife.Crm.SharedKernel;

namespace BodyLife.Crm.Tests.Modules.Memberships;

public sealed class MembershipOpeningStateTests
{
    private static readonly DateOnly MembershipStartDate = new(2026, 7, 1);
    private static readonly DateOnly MembershipBaseEndDate = new(2026, 7, 30);
    private static readonly DateOnly OpeningAsOfDate = new(2026, 7, 13);

    [Theory]
    [InlineData(3, 0)]
    [InlineData(0, 0)]
    [InlineData(-2, 2)]
    public void DeclarationDerivesNegativeBalanceFromSignedRemainingVisits(
        int declaredRemainingVisits,
        int expectedNegativeBalance)
    {
        var openingState = MembershipOpeningState.FromDeclaration(
            OpeningAsOfDate,
            declaredRemainingVisits);

        Assert.Equal(OpeningAsOfDate, openingState.OpeningAsOfDate);
        Assert.Equal(declaredRemainingVisits, openingState.DeclaredRemainingVisits);
        Assert.Equal(expectedNegativeBalance, openingState.DeclaredNegativeBalance);
        Assert.Null(openingState.KnownEffectiveEndDate);
        Assert.Null(openingState.KnownExtensionDays);
    }

    [Fact]
    public void StoredSourceRejectsNegativeBalanceDrift()
    {
        var exception = Assert.Throws<ArgumentException>(() =>
            MembershipOpeningState.FromStoredSource(
                OpeningAsOfDate,
                declaredRemainingVisits: -2,
                declaredNegativeBalance: 1,
                knownEffectiveEndDate: null,
                knownExtensionDays: null));

        Assert.Equal("declaredNegativeBalance", exception.ParamName);
    }

    [Fact]
    public void DeclarationRejectsUnrepresentableNegativeBalance()
    {
        var exception = Assert.Throws<ArgumentOutOfRangeException>(() =>
            MembershipOpeningState.FromDeclaration(
                OpeningAsOfDate,
                int.MinValue));

        Assert.Equal("declaredRemainingVisits", exception.ParamName);
        Assert.Equal(int.MinValue, exception.ActualValue);
    }

    [Fact]
    public void OpeningMetadataRejectsNegativeExtensionAndEndBeforeOpening()
    {
        var negativeExtension = Assert.Throws<ArgumentOutOfRangeException>(() =>
            MembershipOpeningState.FromDeclaration(
                OpeningAsOfDate,
                declaredRemainingVisits: 3,
                knownExtensionDays: -1));
        var endBeforeOpening = Assert.Throws<ArgumentException>(() =>
            MembershipOpeningState.FromDeclaration(
                OpeningAsOfDate,
                declaredRemainingVisits: 3,
                knownEffectiveEndDate: OpeningAsOfDate.AddDays(-1)));

        Assert.Equal("knownExtensionDays", negativeExtension.ParamName);
        Assert.Equal("knownEffectiveEndDate", endBeforeOpening.ParamName);
    }

    [Fact]
    public void BaselineUsesDeclaredBalanceWithoutSyntheticHistoricalVisits()
    {
        var issueTerms = CreateIssueTerms(visitsLimit: 8);
        var openingState = MembershipOpeningState.FromDeclaration(
            OpeningAsOfDate,
            declaredRemainingVisits: 3);

        var state = MembershipStateCalculator.CalculateFromOpeningState(
            issueTerms,
            openingState);

        Assert.Equal(0, state.CountedVisits);
        Assert.Equal(3, state.RemainingVisits);
        Assert.Equal(0, state.NegativeBalance);
        Assert.Null(state.FirstNegativeVisitId);
        Assert.Null(state.FirstNegativeVisitDate);
        Assert.Equal(0, state.ExtensionDays);
        Assert.Equal(MembershipBaseEndDate, state.EffectiveEndDate);
        Assert.Null(state.LastCountedVisitAt);
    }

    [Fact]
    public void NegativeBaselineDoesNotInventFirstNegativeVisitMetadata()
    {
        var issueTerms = CreateIssueTerms(visitsLimit: 8);
        var openingState = MembershipOpeningState.FromStoredSource(
            OpeningAsOfDate,
            declaredRemainingVisits: -2,
            declaredNegativeBalance: 2,
            knownEffectiveEndDate: new DateOnly(2026, 8, 3),
            knownExtensionDays: 4);

        var state = MembershipStateCalculator.CalculateFromOpeningState(
            issueTerms,
            openingState);

        Assert.Equal(0, state.CountedVisits);
        Assert.Equal(-2, state.RemainingVisits);
        Assert.Equal(2, state.NegativeBalance);
        Assert.Null(state.FirstNegativeVisitId);
        Assert.Null(state.FirstNegativeVisitDate);
        Assert.Equal(4, state.ExtensionDays);
        Assert.Equal(new DateOnly(2026, 8, 3), state.EffectiveEndDate);
        Assert.Null(state.LastCountedVisitAt);
    }

    [Fact]
    public void KnownEffectiveEndAloneDerivesExtensionDays()
    {
        var state = MembershipStateCalculator.CalculateFromOpeningState(
            CreateIssueTerms(visitsLimit: 8),
            MembershipOpeningState.FromDeclaration(
                OpeningAsOfDate,
                declaredRemainingVisits: 3,
                knownEffectiveEndDate: new DateOnly(2026, 8, 3)));

        Assert.Equal(4, state.ExtensionDays);
        Assert.Equal(new DateOnly(2026, 8, 3), state.EffectiveEndDate);
    }

    [Fact]
    public void KnownExtensionAloneDerivesEffectiveEnd()
    {
        var state = MembershipStateCalculator.CalculateFromOpeningState(
            CreateIssueTerms(visitsLimit: 8),
            MembershipOpeningState.FromDeclaration(
                OpeningAsOfDate,
                declaredRemainingVisits: 3,
                knownExtensionDays: 4));

        Assert.Equal(4, state.ExtensionDays);
        Assert.Equal(new DateOnly(2026, 8, 3), state.EffectiveEndDate);
    }

    [Fact]
    public void KnownEndAndExtensionMustDescribeTheSameState()
    {
        var openingState = MembershipOpeningState.FromDeclaration(
            OpeningAsOfDate,
            declaredRemainingVisits: 3,
            knownEffectiveEndDate: new DateOnly(2026, 8, 3),
            knownExtensionDays: 3);

        var exception = Assert.Throws<ArgumentException>(() =>
            MembershipStateCalculator.CalculateFromOpeningState(
                CreateIssueTerms(visitsLimit: 8),
                openingState));

        Assert.Equal("openingState", exception.ParamName);
    }

    [Fact]
    public void KnownEffectiveEndCannotShortenCanonicalBaseTerm()
    {
        var openingState = MembershipOpeningState.FromDeclaration(
            OpeningAsOfDate,
            declaredRemainingVisits: 3,
            knownEffectiveEndDate: new DateOnly(2026, 7, 20));

        var exception = Assert.Throws<ArgumentException>(() =>
            MembershipStateCalculator.CalculateFromOpeningState(
                CreateIssueTerms(visitsLimit: 8),
                openingState));

        Assert.Equal("openingState", exception.ParamName);
    }

    [Fact]
    public void OpeningDateMustFallInsideInclusiveActiveTerm()
    {
        var issueTerms = CreateIssueTerms(visitsLimit: 8);
        var beforeStart = MembershipOpeningState.FromDeclaration(
            MembershipStartDate.AddDays(-1),
            declaredRemainingVisits: 3);
        var afterEnd = MembershipOpeningState.FromDeclaration(
            MembershipBaseEndDate.AddDays(1),
            declaredRemainingVisits: 3);
        var onEnd = MembershipOpeningState.FromDeclaration(
            MembershipBaseEndDate,
            declaredRemainingVisits: 3);

        var beforeStartException = Assert.Throws<ArgumentException>(() =>
            MembershipStateCalculator.CalculateFromOpeningState(issueTerms, beforeStart));
        var afterEndException = Assert.Throws<ArgumentException>(() =>
            MembershipStateCalculator.CalculateFromOpeningState(issueTerms, afterEnd));
        var stateOnEnd = MembershipStateCalculator.CalculateFromOpeningState(
            issueTerms,
            onEnd);

        Assert.Equal("openingState", beforeStartException.ParamName);
        Assert.Equal("openingState", afterEndException.ParamName);
        Assert.True(stateOnEnd.IsActiveByDate(MembershipBaseEndDate));
    }

    [Fact]
    public void OpeningCalculationRejectsUnsupportedCalendarExtension()
    {
        var openingState = MembershipOpeningState.FromDeclaration(
            OpeningAsOfDate,
            declaredRemainingVisits: 3,
            knownExtensionDays: int.MaxValue);

        var exception = Assert.Throws<ArgumentException>(() =>
            MembershipStateCalculator.CalculateFromOpeningState(
                CreateIssueTerms(visitsLimit: 8),
                openingState));

        Assert.Equal("openingState", exception.ParamName);
    }

    [Fact]
    public void OpeningCalculationRequiresIssueTermsAndOpeningState()
    {
        var issueTerms = CreateIssueTerms(visitsLimit: 8);
        var openingState = MembershipOpeningState.FromDeclaration(
            OpeningAsOfDate,
            declaredRemainingVisits: 3);

        var missingTerms = Assert.Throws<ArgumentNullException>(() =>
            MembershipStateCalculator.CalculateFromOpeningState(
                issueTerms: null,
                openingState));
        var missingOpeningState = Assert.Throws<ArgumentNullException>(() =>
            MembershipStateCalculator.CalculateFromOpeningState(
                issueTerms,
                openingState: null));

        Assert.Equal("issueTerms", missingTerms.ParamName);
        Assert.Equal("openingState", missingOpeningState.ParamName);
    }

    [Fact]
    public void OpeningStateCannotBePubliclyConstructedOrMutated()
    {
        var properties = typeof(MembershipOpeningState).GetProperties();

        Assert.Empty(typeof(MembershipOpeningState).GetConstructors());
        Assert.All(
            properties,
            property => Assert.False(property.SetMethod?.IsPublic == true));
    }

    private static MembershipIssueTerms CreateIssueTerms(int visitsLimit)
    {
        var snapshot = new IssuedMembershipSnapshot(
            "Historical membership",
            durationDays: 30,
            visitsLimit,
            new Money(1000m, "UAH"));

        return MembershipIssueTerms.FromIssuedSnapshot(
            Guid.Parse("11111111-1111-1111-1111-111111111111"),
            snapshot,
            MembershipStartDate,
            MembershipBaseEndDate);
    }
}
