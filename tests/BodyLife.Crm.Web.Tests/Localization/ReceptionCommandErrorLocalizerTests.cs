using System.Globalization;
using BodyLife.Crm.Application.Commands;
using BodyLife.Crm.Web.Localization;
using BodyLife.Crm.Web.Pages.Reception;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;

namespace BodyLife.Crm.Web.Tests.Localization;

[Collection(nameof(LocalizationCollection))]
public sealed class ReceptionCommandErrorLocalizerTests
{
    [Theory]
    [InlineData("en-US", "Enter an amount greater than zero.")]
    [InlineData("uk-UA", "Вкажіть суму, більшу за нуль.")]
    public void ValidationFieldMappingUsesTheActiveCulture(
        string culture,
        string expected)
    {
        using var cultureScope = new CultureScope(culture);
        var error = new CommandError(
            CommandErrorCode.ValidationFailed,
            "RAW APPLICATION ERROR MUST NOT RENDER",
            "amount");

        var actual = ReceptionCommandErrorLocalizer.Display(CreateLocalizer(), error);

        Assert.Equal(expected, actual);
        Assert.DoesNotContain("RAW APPLICATION ERROR", actual, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("en-US")]
    [InlineData("uk-UA")]
    public void EveryStableCommandErrorCodeMapsToSafeResourceText(string culture)
    {
        using var cultureScope = new CultureScope(culture);
        var localizer = CreateLocalizer();

        foreach (var code in Enum.GetValues<CommandErrorCode>())
        {
            var actual = ReceptionCommandErrorLocalizer.Display(
                localizer,
                new CommandError(code, "RAW APPLICATION ERROR MUST NOT RENDER"));

            Assert.False(string.IsNullOrWhiteSpace(actual));
            Assert.DoesNotContain("RAW APPLICATION ERROR", actual, StringComparison.Ordinal);
            Assert.False(actual.StartsWith("Error.", StringComparison.Ordinal));
        }
    }

    private static IStringLocalizer<Reception> CreateLocalizer()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddLocalization(options => options.ResourcesPath = "Resources");
        var factory = services
            .BuildServiceProvider()
            .GetRequiredService<IStringLocalizerFactory>();
        return new StringLocalizer<Reception>(factory);
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
