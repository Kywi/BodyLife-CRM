using BodyLife.Crm.SharedKernel;

namespace BodyLife.Crm.Tests.SharedKernel;

public sealed class BusinessTimeZoneTests
{
    [Fact]
    public void ConvertsWinterAndSummerInstantsUsingKyivOffsets()
    {
        Assert.Equal(
            new DateTime(2026, 1, 15, 14, 0, 0),
            BusinessTimeZone.ConvertInstantToLocal(
                new DateTimeOffset(2026, 1, 15, 12, 0, 0, TimeSpan.Zero)));
        Assert.Equal(
            new DateTime(2026, 7, 15, 15, 0, 0),
            BusinessTimeZone.ConvertInstantToLocal(
                new DateTimeOffset(2026, 7, 15, 12, 0, 0, TimeSpan.Zero)));
    }

    [Fact]
    public void GetsKyivBusinessDateWhenUtcDateDiffers()
    {
        Assert.Equal(
            new DateOnly(2026, 7, 16),
            BusinessTimeZone.GetBusinessDate(
                new DateTimeOffset(2026, 7, 15, 21, 30, 0, TimeSpan.Zero)));
    }

    [Fact]
    public void RejectsSpringGapAndUsesFirstOccurrenceForFallAmbiguity()
    {
        Assert.Throws<ArgumentException>(() => BusinessTimeZone.ConvertLocalToUtc(
            new DateTime(2026, 3, 29, 3, 30, 0, DateTimeKind.Unspecified)));

        var utc = BusinessTimeZone.ConvertLocalToUtc(
            new DateTime(2026, 10, 25, 3, 30, 0, DateTimeKind.Unspecified));

        Assert.Equal(
            new DateTimeOffset(2026, 10, 25, 0, 30, 0, TimeSpan.Zero),
            utc);
    }

    [Fact]
    public void ProducesDstAwareUtcHalfOpenDayRanges()
    {
        var spring = BusinessTimeZone.GetUtcDayRange(new DateOnly(2026, 3, 29));
        var fall = BusinessTimeZone.GetUtcDayRange(new DateOnly(2026, 10, 25));

        Assert.Equal(TimeSpan.FromHours(23), spring.ToExclusive - spring.FromInclusive);
        Assert.Equal(TimeSpan.FromHours(25), fall.ToExclusive - fall.FromInclusive);
        Assert.Equal(TimeSpan.Zero, spring.FromInclusive.Offset);
        Assert.Equal(TimeSpan.Zero, fall.ToExclusive.Offset);
    }

    [Fact]
    public void RejectsDefaultAndExtremeCalendarInputs()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            BusinessTimeZone.GetBusinessDate(default));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            BusinessTimeZone.GetUtcDayRange(default));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            BusinessTimeZone.GetUtcDayRange(DateOnly.MaxValue));
        Assert.Throws<ArgumentOutOfRangeException>(() => BusinessTimeZone.ConvertLocalToUtc(
            DateTime.SpecifyKind(DateTime.MaxValue, DateTimeKind.Unspecified)));
        Assert.Throws<ArgumentException>(() => BusinessTimeZone.ConvertLocalToUtc(
            DateTime.SpecifyKind(new DateTime(2026, 7, 15, 12, 0, 0), DateTimeKind.Utc)));
        Assert.Throws<ArgumentException>(() => BusinessTimeZone.ConvertLocalToUtc(
            DateTime.SpecifyKind(new DateTime(2026, 7, 15, 12, 0, 0), DateTimeKind.Local)));
    }

    [Fact]
    public void RejectsNearBoundaryInstantsWhoseKyivConversionWouldClamp()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => BusinessTimeZone.ConvertInstantToLocal(
            DateTimeOffset.MaxValue.AddTicks(-1)));
    }

    [Theory]
    [InlineData("0001-01-01T12:00:00+00:00")]
    [InlineData("9999-12-31T12:00:00+00:00")]
    public void RejectsAnyInstantWhoseKyivLocalDateIsUnsupported(string value)
    {
        Assert.False(BusinessTimeZone.TryNormalizeUtcInstant(
            DateTimeOffset.Parse(value, System.Globalization.CultureInfo.InvariantCulture),
            out _));
    }

    [Theory]
    [InlineData(1, 1, 2, 12)]
    [InlineData(9999, 12, 30, 12)]
    public void SupportsAdjacentBusinessCalendarDates(int year, int month, int day, int hour)
    {
        var local = new DateTime(year, month, day, hour, 0, 0, DateTimeKind.Unspecified);

        var utc = BusinessTimeZone.ConvertLocalToUtc(local);

        Assert.True(BusinessTimeZone.TryNormalizeUtcInstant(utc, out var normalized));
        Assert.Equal(utc, normalized);
    }

    [Theory]
    [InlineData("0001-01-01T00:00:00+00:00")]
    [InlineData("9999-12-31T23:59:59.9999999+00:00")]
    public void TryNormalizeUtcInstantRejectsUnsupportedCalendarBounds(string value)
    {
        Assert.False(BusinessTimeZone.TryNormalizeUtcInstant(
            DateTimeOffset.Parse(value, System.Globalization.CultureInfo.InvariantCulture),
            out _));
    }

    [Theory]
    [InlineData("0001-01-01")]
    [InlineData("9999-12-31")]
    public void SupportedBusinessDateExcludesCalendarBounds(string value)
    {
        Assert.False(BusinessTimeZone.IsSupportedBusinessDate(
            DateOnly.Parse(value, System.Globalization.CultureInfo.InvariantCulture)));
    }
}
