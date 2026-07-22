using System.Globalization;
using BodyLife.Crm.Web.Localization;

namespace BodyLife.Crm.Web.Tests.Localization;

[Collection(nameof(LocalizationCollection))]
public sealed class ReceptionDisplayFormatterTests
{
    [Theory]
    [InlineData(2026, 1, 15, 10, 30, 12, 30)]
    [InlineData(2026, 7, 15, 10, 30, 13, 30)]
    public void DateTimeUsesKyivLocalClockWithoutTimezoneSuffix(
        int year,
        int month,
        int day,
        int utcHour,
        int utcMinute,
        int expectedHour,
        int expectedMinute)
    {
        using var cultureScope = new CultureScope("en-US");
        var instant = new DateTimeOffset(year, month, day, utcHour, utcMinute, 0, TimeSpan.Zero);

        var actual = ReceptionDisplayFormatter.DateTime(instant);

        Assert.Equal(
            new DateTime(year, month, day, expectedHour, expectedMinute, 0)
                .ToString("g", CultureInfo.CurrentCulture),
            actual);
        Assert.DoesNotContain("UTC", actual, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("+", actual, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(2026, 1, 15, 10, 30, 12, 30)]
    [InlineData(2026, 7, 15, 10, 30, 13, 30)]
    public void DateTimeHonorsUkrainianCultureWithoutTimezoneSuffix(
        int year,
        int month,
        int day,
        int utcHour,
        int utcMinute,
        int expectedHour,
        int expectedMinute)
    {
        using var cultureScope = new CultureScope("uk-UA");
        var instant = new DateTimeOffset(year, month, day, utcHour, utcMinute, 0, TimeSpan.Zero);

        var actual = ReceptionDisplayFormatter.DateTime(instant);

        Assert.Equal(
            new DateTime(year, month, day, expectedHour, expectedMinute, 0)
                .ToString("g", CultureInfo.CurrentCulture),
            actual);
        Assert.DoesNotContain("UTC", actual, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Kyiv", actual, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Київ", actual, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("EET", actual, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotMatch("[+-]\\d{2}:?\\d{2}$", actual);
    }

    private sealed class CultureScope : IDisposable
    {
        private readonly CultureInfo previousCulture = CultureInfo.CurrentCulture;
        private readonly CultureInfo previousUiCulture = CultureInfo.CurrentUICulture;

        public CultureScope(string culture)
        {
            var selected = CultureInfo.GetCultureInfo(culture);
            CultureInfo.CurrentCulture = selected;
            CultureInfo.CurrentUICulture = selected;
        }

        public void Dispose()
        {
            CultureInfo.CurrentCulture = previousCulture;
            CultureInfo.CurrentUICulture = previousUiCulture;
        }
    }
}
