using BodyLife.Crm.Modules.Memberships;
using BodyLife.Crm.SharedKernel;

namespace BodyLife.Crm.Tests.Modules.Memberships;

public sealed class MembershipWarningRulesTests
{
    private static readonly Guid MembershipTypeId = Guid.Parse(
        "11111111-1111-1111-1111-111111111111");
    private static readonly DateOnly StartDate = new(2026, 7, 1);
    private static readonly DateOnly BaseEndDate = new(2026, 7, 30);
    private static readonly DateOnly OutsideEndingSoonDate = new(2026, 7, 22);

    [Theory]
    [InlineData(-2, MembershipWarningCodes.NegativeBalance)]
    [InlineData(0, MembershipWarningCodes.ZeroRemaining)]
    [InlineData(1, MembershipWarningCodes.LowRemaining)]
    [InlineData(2, MembershipWarningCodes.LowRemaining)]
    [InlineData(3, null)]
    public void VisitWarningsUseSpecializedNegativeZeroAndLowStates(
        int remainingVisits,
        string? expectedCode)
    {
        var warnings = MembershipWarningRules.Derive(
            CreateState(remainingVisits),
            OutsideEndingSoonDate);

        if (expectedCode is null)
        {
            Assert.Empty(warnings);
            return;
        }

        var warning = Assert.Single(warnings);
        Assert.Equal(expectedCode, warning.Code);
        Assert.Equal(
            remainingVisits < 0
                ? MembershipWarningSeverity.Danger
                : MembershipWarningSeverity.Warning,
            warning.Severity);
    }

    [Theory]
    [InlineData(2026, 7, 22, null)]
    [InlineData(2026, 7, 23, MembershipWarningCodes.EndingSoon)]
    [InlineData(2026, 7, 30, MembershipWarningCodes.EndingSoon)]
    [InlineData(2026, 7, 31, MembershipWarningCodes.ExpiredByDate)]
    public void DateWarningsUseInclusiveSevenDayBoundaryWithoutOverlap(
        int year,
        int month,
        int day,
        string? expectedCode)
    {
        var warnings = MembershipWarningRules.Derive(
            CreateState(remainingVisits: 8),
            new DateOnly(year, month, day));

        if (expectedCode is null)
        {
            Assert.Empty(warnings);
            return;
        }

        var warning = Assert.Single(warnings);
        Assert.Equal(expectedCode, warning.Code);
        Assert.Equal(
            expectedCode == MembershipWarningCodes.ExpiredByDate
                ? MembershipWarningSeverity.Danger
                : MembershipWarningSeverity.Warning,
            warning.Severity);
    }

    [Fact]
    public void IndependentDateAndVisitWarningsCombineDangerFirst()
    {
        var warnings = MembershipWarningRules.Derive(
            CreateState(remainingVisits: -1),
            BaseEndDate.AddDays(1));

        Assert.Equal(
            new[]
            {
                MembershipWarningCodes.NegativeBalance,
                MembershipWarningCodes.ExpiredByDate,
            },
            warnings.Select(warning => warning.Code));
        Assert.All(
            warnings,
            warning => Assert.Equal(MembershipWarningSeverity.Danger, warning.Severity));
    }

    [Fact]
    public void ExpiredDateDoesNotHideLowPositiveRemainingVisits()
    {
        var warnings = MembershipWarningRules.Derive(
            CreateState(remainingVisits: 2),
            BaseEndDate.AddDays(1));

        Assert.Equal(
            new[]
            {
                MembershipWarningCodes.ExpiredByDate,
                MembershipWarningCodes.LowRemaining,
            },
            warnings.Select(warning => warning.Code));
    }

    [Fact]
    public void WarningContractUsesStableCodesThresholdsAndImmutableProperties()
    {
        var warnings = new[]
        {
            MembershipWarningRules.Derive(
                CreateState(remainingVisits: -1),
                OutsideEndingSoonDate).Single(),
            MembershipWarningRules.Derive(
                CreateState(remainingVisits: 0),
                OutsideEndingSoonDate).Single(),
            MembershipWarningRules.Derive(
                CreateState(remainingVisits: 2),
                OutsideEndingSoonDate).Single(),
            MembershipWarningRules.Derive(
                CreateState(remainingVisits: 8),
                BaseEndDate).Single(),
            MembershipWarningRules.Derive(
                CreateState(remainingVisits: 8),
                BaseEndDate.AddDays(1)).Single(),
        };

        Assert.Equal(7, MembershipWarningRules.EndingSoonDaysThreshold);
        Assert.Equal(2, MembershipWarningRules.LowRemainingVisitsThreshold);
        Assert.Equal(
            new[]
            {
                "membership_negative_balance",
                "membership_zero_remaining",
                "membership_low_remaining",
                "membership_ending_soon",
                "membership_expired_by_date",
            },
            warnings.Select(warning => warning.Code));
        Assert.All(warnings, warning => Assert.False(string.IsNullOrWhiteSpace(warning.Message)));
        Assert.All(
            typeof(MembershipWarning).GetProperties(),
            property => Assert.Null(property.SetMethod));
    }

    [Fact]
    public void DerivationRejectsMissingStateAndDefaultAsOfDate()
    {
        Assert.Throws<ArgumentNullException>(() =>
            MembershipWarningRules.Derive(state: null, OutsideEndingSoonDate));
        var exception = Assert.Throws<ArgumentException>(() =>
            MembershipWarningRules.Derive(CreateState(remainingVisits: 8), default));

        Assert.Equal("asOfDate", exception.ParamName);
    }

    private static MembershipCalculatedState CreateState(int remainingVisits)
    {
        var issueTerms = MembershipIssueTerms.FromIssuedSnapshot(
            MembershipTypeId,
            new IssuedMembershipSnapshot(
                "Eight visits",
                durationDays: 30,
                visitsLimit: 8,
                new Money(1000m, "UAH")),
            StartDate,
            BaseEndDate);
        var openingState = MembershipOpeningState.FromDeclaration(
            StartDate,
            remainingVisits,
            knownEffectiveEndDate: null,
            knownExtensionDays: null);

        return MembershipStateCalculator.CalculateFromOpeningState(
            issueTerms,
            openingState);
    }
}
