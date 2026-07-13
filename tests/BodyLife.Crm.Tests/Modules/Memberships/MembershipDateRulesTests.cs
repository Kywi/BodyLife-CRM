using BodyLife.Crm.Modules.Memberships;

namespace BodyLife.Crm.Tests.Modules.Memberships;

public sealed class MembershipDateRulesTests
{
    [Theory]
    [InlineData(2026, 7, 1, 30, 2026, 7, 30)]
    [InlineData(2026, 12, 20, 20, 2027, 1, 8)]
    [InlineData(2028, 2, 28, 2, 2028, 2, 29)]
    [InlineData(2026, 7, 1, 1, 2026, 7, 1)]
    public void BaseEndDateUsesInclusiveDuration(
        int startYear,
        int startMonth,
        int startDay,
        int durationDays,
        int endYear,
        int endMonth,
        int endDay)
    {
        var result = MembershipDateRules.CalculateBaseEndDate(
            new DateOnly(startYear, startMonth, startDay),
            durationDays);

        Assert.Equal(new DateOnly(endYear, endMonth, endDay), result);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void DurationMustBePositive(int durationDays)
    {
        var exception = Assert.Throws<ArgumentOutOfRangeException>(() =>
            MembershipDateRules.CalculateBaseEndDate(
                new DateOnly(2026, 7, 1),
                durationDays));

        Assert.Equal("durationDays", exception.ParamName);
        Assert.Equal(durationDays, exception.ActualValue);
    }

    [Fact]
    public void DurationCannotExtendBeyondSupportedCalendar()
    {
        var exception = Assert.Throws<ArgumentOutOfRangeException>(() =>
            MembershipDateRules.CalculateBaseEndDate(DateOnly.MaxValue, durationDays: 2));

        Assert.Equal("durationDays", exception.ParamName);
        Assert.Equal(2, exception.ActualValue);
    }
}
