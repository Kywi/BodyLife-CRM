using BodyLife.Crm.SharedKernel;

namespace BodyLife.Crm.Tests.SharedKernel;

public sealed class DateRangeTests
{
    [Fact]
    public void InclusiveDaysCountsBothRangeEdges()
    {
        var range = new DateRange(
            new DateOnly(2026, 1, 10),
            new DateOnly(2026, 1, 12));

        Assert.Equal(3, range.InclusiveDays);
        Assert.True(range.Contains(new DateOnly(2026, 1, 10)));
        Assert.True(range.Contains(new DateOnly(2026, 1, 12)));
        Assert.False(range.Contains(new DateOnly(2026, 1, 13)));
    }

    [Fact]
    public void ConstructorRejectsInvertedRanges()
    {
        var exception = Assert.Throws<ArgumentException>(
            () => new DateRange(
                new DateOnly(2026, 1, 12),
                new DateOnly(2026, 1, 10)));

        Assert.Equal("endDate", exception.ParamName);
    }
}
