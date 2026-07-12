using BodyLife.Crm.Modules.MembershipTypes;
using BodyLife.Crm.SharedKernel;

namespace BodyLife.Crm.Tests.Modules.MembershipTypes;

public sealed class MembershipTypeCatalogRulesTests
{
    [Fact]
    public void NormalizeAndValidateReturnsCanonicalCatalogValues()
    {
        var result = MembershipTypeCatalogRules.NormalizeAndValidate(
            "  Morning\u00a0  Eight  ",
            durationDays: 30,
            visitsLimit: 8,
            new Money(1200.50m, "uah"),
            "  Before noon only.  ");

        Assert.Equal("Morning Eight", result.Name);
        Assert.Equal(30, result.DurationDays);
        Assert.Equal(8, result.VisitsLimit);
        Assert.Equal(new Money(1200.50m, "UAH"), result.Price);
        Assert.Equal("Before noon only.", result.Comment);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void NameIsRequired(string? name)
    {
        var exception = Assert.Throws<ArgumentException>(() =>
            MembershipTypeCatalogRules.NormalizeAndValidate(
                name,
                durationDays: 30,
                visitsLimit: 8,
                new Money(1000m, "UAH"),
                comment: null));

        Assert.Equal("name", exception.ParamName);
    }

    [Fact]
    public void NameRejectsUnsupportedControlCharacters()
    {
        var exception = Assert.Throws<ArgumentException>(() =>
            MembershipTypeCatalogRules.NormalizeName("Morning\0Eight"));

        Assert.Equal("name", exception.ParamName);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void DurationMustBePositive(int durationDays)
    {
        var exception = Assert.Throws<ArgumentOutOfRangeException>(() =>
            MembershipTypeCatalogRules.NormalizeAndValidate(
                "Eight visits",
                durationDays,
                visitsLimit: 8,
                new Money(1000m, "UAH"),
                comment: null));

        Assert.Equal("durationDays", exception.ParamName);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(-10)]
    public void VisitsLimitCannotBeNegative(int visitsLimit)
    {
        var exception = Assert.Throws<ArgumentOutOfRangeException>(() =>
            MembershipTypeCatalogRules.NormalizeAndValidate(
                "Eight visits",
                durationDays: 30,
                visitsLimit,
                new Money(1000m, "UAH"),
                comment: null));

        Assert.Equal("visitsLimit", exception.ParamName);
    }

    [Fact]
    public void ZeroVisitsAndZeroPriceAreValidCatalogValues()
    {
        var result = MembershipTypeCatalogRules.NormalizeAndValidate(
            "Time only",
            durationDays: 1,
            visitsLimit: 0,
            new Money(0m, "uah"),
            comment: "   ");

        Assert.Equal(0, result.VisitsLimit);
        Assert.Equal(new Money(0m, "UAH"), result.Price);
        Assert.Null(result.Comment);
    }

    [Fact]
    public void InvalidMoneyCannotEnterCatalogValues()
    {
        var missingCurrency = Assert.Throws<ArgumentException>(() =>
            MembershipTypeCatalogRules.NormalizeAndValidate(
                "Eight visits",
                durationDays: 30,
                visitsLimit: 8,
                default,
                comment: null));
        var negativePrice = Assert.Throws<ArgumentOutOfRangeException>(() =>
            new Money(-0.01m, "UAH"));

        Assert.Equal("currency", missingCurrency.ParamName);
        Assert.Equal("amount", negativePrice.ParamName);
    }
}
