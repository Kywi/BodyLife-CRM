using System.Globalization;
using BodyLife.Crm.Modules.Memberships;
using BodyLife.Crm.Web.Localization;
using BodyLife.Crm.Web.Pages.Reports;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;

namespace BodyLife.Crm.Web.Tests.Localization;

[Collection(nameof(LocalizationCollection))]
public sealed class ReportsPresentationTests
{
    public static TheoryData<string, string, string> KnownWarnings => new()
    {
        { "en-US", MembershipWarningCodes.NegativeBalance, "This membership has a negative visit balance." },
        { "en-US", MembershipWarningCodes.ExpiredByDate, "This membership has expired by date." },
        { "en-US", MembershipWarningCodes.ZeroRemaining, "This membership has no remaining visits." },
        { "en-US", MembershipWarningCodes.EndingSoon, "This membership ends within seven days." },
        { "en-US", MembershipWarningCodes.LowRemaining, "This membership has only one or two visits remaining." },
        { "uk-UA", MembershipWarningCodes.NegativeBalance, "Абонемент має від’ємний залишок відвідувань." },
        { "uk-UA", MembershipWarningCodes.ExpiredByDate, "Строк дії абонемента минув." },
        { "uk-UA", MembershipWarningCodes.ZeroRemaining, "В абонементі не залишилося відвідувань." },
        { "uk-UA", MembershipWarningCodes.EndingSoon, "Абонемент завершується протягом семи днів." },
        { "uk-UA", MembershipWarningCodes.LowRemaining, "В абонементі залишилося лише одне або два відвідування." },
    };

    [Theory]
    [MemberData(nameof(KnownWarnings))]
    public void EveryMembershipWarningCodeUsesLocalizedReportText(
        string culture,
        string code,
        string expected)
    {
        using var cultureScope = new CultureScope(culture);

        var actual = ReportsPresentation.WarningMessage(CreateLocalizer(), code);

        Assert.Equal(expected, actual);
    }

    [Theory]
    [InlineData("en-US", "Review this membership's current state.")]
    [InlineData("uk-UA", "Перевірте поточний стан абонемента.")]
    public void UnknownMembershipWarningCodeUsesSafeLocalizedFallback(
        string culture,
        string expected)
    {
        using var cultureScope = new CultureScope(culture);

        var actual = ReportsPresentation.WarningMessage(CreateLocalizer(), "future_warning");

        Assert.Equal(expected, actual);
        Assert.DoesNotContain("future_warning", actual, StringComparison.Ordinal);
    }

    private static IStringLocalizer<Reports> CreateLocalizer()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddLocalization(options => options.ResourcesPath = "Resources");
        var factory = services
            .BuildServiceProvider()
            .GetRequiredService<IStringLocalizerFactory>();
        return new StringLocalizer<Reports>(factory);
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
