using BodyLife.Crm.Modules.Memberships;
using BodyLife.Crm.SharedKernel;

namespace BodyLife.Crm.Tests.Modules.Memberships;

public sealed class MembershipExtensionCalculatorTests
{
    private static readonly Guid FreezeId = Guid.Parse(
        "11111111-1111-1111-1111-111111111111");
    private static readonly Guid NonWorkingPeriodId = Guid.Parse(
        "22222222-2222-2222-2222-222222222222");
    private static readonly Guid AdjustmentId = Guid.Parse(
        "33333333-3333-3333-3333-333333333333");

    [Fact]
    public void SingleActiveRangeExpandsBothInclusiveEdges()
    {
        var source = CreateSource(
            "freeze",
            FreezeId,
            "Freeze 2026-01-10..2026-01-12",
            new DateOnly(2026, 1, 10),
            new DateOnly(2026, 1, 12));

        var calculation = MembershipExtensionCalculator.Calculate([source]);

        Assert.Equal(3, calculation.ExtensionDays);
        Assert.Equal(
            [
                new DateOnly(2026, 1, 10),
                new DateOnly(2026, 1, 11),
                new DateOnly(2026, 1, 12),
            ],
            calculation.ExplanationDays.Select(day => day.ExtensionDate));
        Assert.All(
            calculation.ExplanationDays,
            day =>
            {
                Assert.Equal("freeze", day.SourceType);
                Assert.Equal(FreezeId, day.SourceId);
                Assert.Equal("Freeze 2026-01-10..2026-01-12", day.SourceLabel);
                Assert.True(day.IsActive);
            });
    }

    [Fact]
    public void OverlappingActiveSourcesCountUnionAndPreserveEveryAttribution()
    {
        var calculation = MembershipExtensionCalculator.Calculate(
        [
            CreateSource(
                "freeze",
                FreezeId,
                "Freeze",
                new DateOnly(2026, 1, 1),
                new DateOnly(2026, 1, 3)),
            CreateSource(
                "non_working_period",
                NonWorkingPeriodId,
                "Gym closure",
                new DateOnly(2026, 1, 2),
                new DateOnly(2026, 1, 3)),
            CreateSource(
                "membership_adjustment",
                AdjustmentId,
                "Exceptional extension",
                new DateOnly(2026, 1, 3),
                new DateOnly(2026, 1, 3)),
        ]);

        Assert.Equal(3, calculation.ExtensionDays);
        Assert.Equal(6, calculation.ExplanationDays.Count);
        Assert.Equal(
            [
                "2026-01-01|freeze",
                "2026-01-02|freeze",
                "2026-01-02|non_working_period",
                "2026-01-03|freeze",
                "2026-01-03|membership_adjustment",
                "2026-01-03|non_working_period",
            ],
            calculation.ExplanationDays.Select(
                day => $"{day.ExtensionDate:yyyy-MM-dd}|{day.SourceType}"));
        Assert.Equal(
            3,
            calculation.ExplanationDays.Count(
                day => day.ExtensionDate == new DateOnly(2026, 1, 3)));
    }

    [Fact]
    public void InactiveSourcesRemainExplainableButDoNotExtendTheUnion()
    {
        var calculation = MembershipExtensionCalculator.Calculate(
        [
            CreateSource(
                "freeze",
                FreezeId,
                "Active freeze",
                new DateOnly(2026, 1, 1),
                new DateOnly(2026, 1, 2)),
            CreateSource(
                "non_working_period",
                NonWorkingPeriodId,
                "Canceled closure",
                new DateOnly(2026, 1, 2),
                new DateOnly(2026, 1, 4),
                isActive: false),
        ]);

        Assert.Equal(2, calculation.ExtensionDays);
        Assert.Equal(5, calculation.ExplanationDays.Count);
        Assert.Equal(3, calculation.ExplanationDays.Count(day => !day.IsActive));
        Assert.Equal(
            [true, false],
            calculation.ExplanationDays
                .Where(day => day.ExtensionDate == new DateOnly(2026, 1, 2))
                .Select(day => day.IsActive));
    }

    [Fact]
    public void EmptySourcesProduceAnEmptyCalculation()
    {
        var calculation = MembershipExtensionCalculator.Calculate([]);

        Assert.Equal(0, calculation.ExtensionDays);
        Assert.Empty(calculation.ExplanationDays);
    }

    [Fact]
    public void SourceMetadataIsTrimmedAndRequiresStableIdentity()
    {
        var range = new DateRange(
            new DateOnly(2026, 1, 1),
            new DateOnly(2026, 1, 1));
        var source = new MembershipExtensionSourceRange(
            "  freeze  ",
            FreezeId,
            "  Winter freeze  ",
            range,
            isActive: true);

        Assert.Equal("freeze", source.SourceType);
        Assert.Equal("Winter freeze", source.SourceLabel);
        Assert.Equal(range, source.Range);
        Assert.Throws<ArgumentException>(() =>
            new MembershipExtensionSourceRange(
                "freeze",
                Guid.Empty,
                "Freeze",
                range,
                isActive: true));
    }

    [Theory]
    [InlineData(null, "Freeze", "sourceType")]
    [InlineData("   ", "Freeze", "sourceType")]
    [InlineData("freeze", null, "sourceLabel")]
    [InlineData("freeze", "   ", "sourceLabel")]
    public void SourceMetadataRejectsMissingValues(
        string? sourceType,
        string? sourceLabel,
        string expectedParameter)
    {
        var exception = Assert.Throws<ArgumentException>(() =>
            new MembershipExtensionSourceRange(
                sourceType,
                FreezeId,
                sourceLabel,
                new DateRange(
                    new DateOnly(2026, 1, 1),
                    new DateOnly(2026, 1, 1)),
                isActive: true));

        Assert.Equal(expectedParameter, exception.ParamName);
    }

    [Fact]
    public void SourceMetadataRejectsValuesBeyondStorageContractLengths()
    {
        var range = new DateRange(
            new DateOnly(2026, 1, 1),
            new DateOnly(2026, 1, 1));
        var sourceTypeException = Assert.Throws<ArgumentException>(() =>
            new MembershipExtensionSourceRange(
                new string('s', MembershipExtensionSourceRange.MaxSourceTypeLength + 1),
                FreezeId,
                "Freeze",
                range,
                isActive: true));
        var sourceLabelException = Assert.Throws<ArgumentException>(() =>
            new MembershipExtensionSourceRange(
                "freeze",
                FreezeId,
                new string('l', MembershipExtensionSourceRange.MaxSourceLabelLength + 1),
                range,
                isActive: true));

        Assert.Equal("sourceType", sourceTypeException.ParamName);
        Assert.Equal("sourceLabel", sourceLabelException.ParamName);
    }

    [Fact]
    public void CalculatorRejectsMissingAndDuplicateSourceIdentities()
    {
        var source = CreateSource(
            "freeze",
            FreezeId,
            "Freeze",
            new DateOnly(2026, 1, 1),
            new DateOnly(2026, 1, 1));

        var missingSources = Assert.Throws<ArgumentNullException>(() =>
            MembershipExtensionCalculator.Calculate(sources: null));
        var missingItem = Assert.Throws<ArgumentException>(() =>
            MembershipExtensionCalculator.Calculate([source, null!]));
        var duplicateIdentity = Assert.Throws<ArgumentException>(() =>
            MembershipExtensionCalculator.Calculate(
            [
                source,
                CreateSource(
                    "freeze",
                    FreezeId,
                    "Conflicting freeze projection",
                    new DateOnly(2026, 1, 2),
                    new DateOnly(2026, 1, 2)),
            ]));

        Assert.Equal("sources", missingSources.ParamName);
        Assert.Equal("sources", missingItem.ParamName);
        Assert.Equal("sources", duplicateIdentity.ParamName);
    }

    [Fact]
    public void SameSourceIdMayBelongToDifferentSourceTypes()
    {
        var calculation = MembershipExtensionCalculator.Calculate(
        [
            CreateSource(
                "freeze",
                FreezeId,
                "Freeze",
                new DateOnly(2026, 1, 1),
                new DateOnly(2026, 1, 1)),
            CreateSource(
                "membership_adjustment",
                FreezeId,
                "Adjustment",
                new DateOnly(2026, 1, 2),
                new DateOnly(2026, 1, 2)),
        ]);

        Assert.Equal(2, calculation.ExtensionDays);
        Assert.Equal(2, calculation.ExplanationDays.Count);
    }

    [Fact]
    public void CalculationAndExplanationContractsAreImmutable()
    {
        var calculation = MembershipExtensionCalculator.Calculate(
        [
            CreateSource(
                "freeze",
                FreezeId,
                "Freeze",
                DateOnly.MaxValue,
                DateOnly.MaxValue),
        ]);

        Assert.Equal(1, calculation.ExtensionDays);
        Assert.Equal(DateOnly.MaxValue, Assert.Single(calculation.ExplanationDays).ExtensionDate);
        var explanationList = Assert.IsAssignableFrom<IList<MembershipExtensionDay>>(
            calculation.ExplanationDays);
        Assert.True(explanationList.IsReadOnly);
        Assert.Throws<NotSupportedException>(() =>
            explanationList.Add(calculation.ExplanationDays[0]));
        Assert.All(
            typeof(MembershipExtensionCalculation).GetProperties(),
            property => Assert.Null(property.SetMethod));
        Assert.All(
            typeof(MembershipExtensionDay).GetProperties(),
            property => Assert.Null(property.SetMethod));
    }

    private static MembershipExtensionSourceRange CreateSource(
        string sourceType,
        Guid sourceId,
        string sourceLabel,
        DateOnly startDate,
        DateOnly endDate,
        bool isActive = true)
    {
        return new MembershipExtensionSourceRange(
            sourceType,
            sourceId,
            sourceLabel,
            new DateRange(startDate, endDate),
            isActive);
    }
}
