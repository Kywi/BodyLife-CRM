using System.Globalization;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using BodyLife.Crm.Web.Localization;
using BodyLife.Crm.Web.Pages;
using BodyLife.Crm.Web.Pages.Audit;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Localization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Options;

namespace BodyLife.Crm.Web.Tests.Localization;

[Collection(nameof(LocalizationCollection))]
public sealed class LocalizationContractsTests
{
    private static readonly Regex SemanticKeyPattern = new(
        @"^[A-Za-z][A-Za-z0-9]*(?:[._][A-Za-z0-9]+)*$",
        RegexOptions.CultureInvariant);
    private static readonly Regex HtmlTagPattern = new(
        @"<\s*/?\s*[A-Za-z][^>]*>",
        RegexOptions.CultureInvariant);
    private static readonly Regex PlaceholderPattern = new(
        @"\{([0-9]+)(?::[^}]*)?\}",
        RegexOptions.CultureInvariant);

    private static readonly (string Name, Type Anchor)[] ResourcePairs =
    [
        (nameof(Audit), typeof(Audit)),
        (nameof(Authentication), typeof(Authentication)),
        (nameof(Owner), typeof(Owner)),
        (nameof(Reception), typeof(Reception)),
        (nameof(Reports), typeof(Reports)),
        (nameof(Shared), typeof(Shared)),
        (nameof(Validation), typeof(Validation)),
    ];

    [Fact]
    public void SupportedCulturesAreExactAndUkrainianIsDefault()
    {
        Assert.Equal(WebCultures.Ukrainian, WebCultures.Default);
        Assert.Equal(
            [WebCultures.Ukrainian, WebCultures.English],
            WebCultures.Supported.Select(culture => culture.Name));
        Assert.True(WebCultures.IsSupported("uk-UA"));
        Assert.True(WebCultures.IsSupported("en-US"));
        Assert.False(WebCultures.IsSupported("uk"));
        Assert.False(WebCultures.IsSupported("en"));
        Assert.False(WebCultures.IsSupported("en-GB"));
        Assert.False(WebCultures.IsSupported(null));
    }

    [Fact]
    public void RequestLocalizationUsesCookieThenAcceptLanguageAndSafeDefault()
    {
        var services = new ServiceCollection();
        services.AddBodyLifeLocalization();
        using var provider = services.BuildServiceProvider();

        var options = provider
            .GetRequiredService<IOptions<RequestLocalizationOptions>>()
            .Value;

        Assert.Equal(WebCultures.Ukrainian, options.DefaultRequestCulture.Culture.Name);
        Assert.Equal(
            [WebCultures.Ukrainian, WebCultures.English],
            options.SupportedCultures!.Select(culture => culture.Name));
        Assert.Equal(
            [WebCultures.Ukrainian, WebCultures.English],
            options.SupportedUICultures!.Select(culture => culture.Name));
        Assert.False(options.FallBackToParentCultures);
        Assert.False(options.FallBackToParentUICultures);
        Assert.Collection(
            options.RequestCultureProviders,
            provider => Assert.IsType<CookieRequestCultureProvider>(provider),
            provider => Assert.IsType<AcceptLanguageHeaderRequestCultureProvider>(provider));
    }

    [Fact]
    public void SetLanguageRejectsUnsupportedCulture()
    {
        var model = CreateSetLanguageModel(out _);

        var result = model.OnPost("en-GB", "/Reception/Index");

        Assert.IsType<BadRequestResult>(result);
    }

    [Fact]
    public void SetLanguagePersistsSupportedCultureAndRejectsOpenRedirect()
    {
        var model = CreateSetLanguageModel(out var context);

        var result = Assert.IsType<LocalRedirectResult>(model.OnPost(WebCultures.English, "https://example.test/"));

        Assert.Equal("/", result.Url);
        var cookie = Assert.Single(context.Response.Headers.SetCookie);
        Assert.StartsWith(
            ".AspNetCore.Culture=c=en-US|uic=en-US",
            Uri.UnescapeDataString(cookie!),
            StringComparison.Ordinal);
    }

    [Fact]
    public void SetLanguagePreservesLocalReturnAndWritesProtectedCookie()
    {
        var model = CreateSetLanguageModel(out var context);
        context.Request.Scheme = "https";
        const string returnUrl = "/Reports/Daily?date=2026-07-22";

        var result = Assert.IsType<LocalRedirectResult>(
            model.OnPost(WebCultures.Ukrainian, returnUrl));

        Assert.Equal(returnUrl, result.Url);
        var cookie = Assert.Single(context.Response.Headers.SetCookie)
            ?? throw new InvalidOperationException("The culture cookie header was null.");
        Assert.Contains("httponly", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("secure", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=lax", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("expires=", cookie, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("en-US", 1, "1 visit", "1 day")]
    [InlineData("en-US", 2, "2 visits", "2 days")]
    [InlineData("en-US", 5, "5 visits", "5 days")]
    [InlineData("en-US", 21, "21 visits", "21 days")]
    [InlineData("uk-UA", 1, "1 відвідування", "1 день")]
    [InlineData("uk-UA", 2, "2 відвідування", "2 дні")]
    [InlineData("uk-UA", 5, "5 відвідувань", "5 днів")]
    [InlineData("uk-UA", 21, "21 відвідування", "21 день")]
    public void PluralizerUsesTheActiveCulture(string culture, int count, string visits, string days)
    {
        using var cultureScope = new CultureScope(culture);
        var localizer = CreateLocalizer<Shared>();

        Assert.Equal(visits, WebPluralizer.Visits(localizer, count));
        Assert.Equal(days, WebPluralizer.Days(localizer, count));
    }

    [Theory]
    [InlineData("en-US", 1, "1 client shown", "1 membership shown", "1 entry shown", "1 row", "+1 unique day")]
    [InlineData("en-US", 2, "2 clients shown", "2 memberships shown", "2 entries shown", "2 rows", "+2 unique days")]
    [InlineData("en-US", 5, "5 clients shown", "5 memberships shown", "5 entries shown", "5 rows", "+5 unique days")]
    [InlineData("en-US", 21, "21 clients shown", "21 memberships shown", "21 entries shown", "21 rows", "+21 unique days")]
    [InlineData("uk-UA", 1, "Показано 1 клієнта", "Показано 1 абонемент", "Показано 1 запис", "1 рядок", "+1 унікальний день")]
    [InlineData("uk-UA", 2, "Показано 2 клієнтів", "Показано 2 абонементи", "Показано 2 записи", "2 рядки", "+2 унікальні дні")]
    [InlineData("uk-UA", 5, "Показано 5 клієнтів", "Показано 5 абонементів", "Показано 5 записів", "5 рядків", "+5 унікальних днів")]
    [InlineData("uk-UA", 21, "Показано 21 клієнта", "Показано 21 абонемент", "Показано 21 запис", "21 рядок", "+21 унікальний день")]
    public void ResultCountPluralizersUseTheActiveCulture(
        string culture,
        int count,
        string clients,
        string memberships,
        string entries,
        string rows,
        string uniqueDays)
    {
        using var cultureScope = new CultureScope(culture);
        var localizer = CreateLocalizer<Shared>();

        Assert.Equal(clients, WebPluralizer.Clients(localizer, count));
        Assert.Equal(memberships, WebPluralizer.Memberships(localizer, count));
        Assert.Equal(entries, WebPluralizer.Entries(localizer, count));
        Assert.Equal(rows, WebPluralizer.Rows(localizer, count));
        Assert.Equal(uniqueDays, WebPluralizer.UniqueDays(localizer, count));
    }

    [Fact]
    public void EveryResourcePairHasExactNonemptyKeyParity()
    {
        foreach (var (name, _) in ResourcePairs)
        {
            var english = ReadResource(name, WebCultures.English);
            var ukrainian = ReadResource(name, WebCultures.Ukrainian);

            Assert.Equal(english.Keys.Order(), ukrainian.Keys.Order());
            Assert.All(english.Values, value => Assert.False(string.IsNullOrWhiteSpace(value)));
            Assert.All(ukrainian.Values, value => Assert.False(string.IsNullOrWhiteSpace(value)));
        }
    }

    [Fact]
    public void ResourcePairsUseSemanticKeysMatchingPlaceholdersAndNoHtmlMarkup()
    {
        foreach (var (name, _) in ResourcePairs)
        {
            var english = ReadResource(name, WebCultures.English);
            var ukrainian = ReadResource(name, WebCultures.Ukrainian);

            foreach (var key in english.Keys)
            {
                Assert.Matches(SemanticKeyPattern, key);
                Assert.DoesNotMatch(HtmlTagPattern, english[key]);
                Assert.DoesNotMatch(HtmlTagPattern, ukrainian[key]);
                Assert.Equal(
                    PlaceholderIndexes(english[key]),
                    PlaceholderIndexes(ukrainian[key]));
            }
        }
    }

    [Theory]
    [InlineData("en-US", "Unavailable")]
    [InlineData("uk-UA", "Недоступно")]
    public void EveryCanonicalAuditActionHasARealLocalizedTitleAndMissingKeysFailClosed(
        string culture,
        string expectedFallback)
    {
        using var cultureScope = new CultureScope(culture);
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddBodyLifeLocalization();
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var presentation = scope.ServiceProvider.GetRequiredService<AuditPresentation>();
        var actions = AuditEntryExplanationPresenter.ReadableActionTypes.ToArray();

        Assert.Equal(26, actions.Length);
        Assert.All(
            actions,
            action =>
            {
                var title = presentation.Action(action);
                Assert.False(string.IsNullOrWhiteSpace(title));
                Assert.NotEqual(expectedFallback, title);
                Assert.NotEqual($"Action.{action}", title);
            });
        Assert.Equal(expectedFallback, presentation.Text("Missing.Semantic.Key"));
    }

    [Theory]
    [InlineData("en-US")]
    [InlineData("uk-UA")]
    public void EverySemanticResourceKeyResolvesWithoutFallback(string culture)
    {
        using var cultureScope = new CultureScope(culture);
        var factory = CreateFactory();

        foreach (var (name, anchor) in ResourcePairs)
        {
            var localizer = factory.Create(anchor);
            foreach (var key in ReadResource(name, culture).Keys)
            {
                var value = localizer[key];
                Assert.False(value.ResourceNotFound, $"{culture} resource {name}.{key} was not found.");
                Assert.NotEqual(key, value.Value);
                Assert.False(string.IsNullOrWhiteSpace(value.Value));
            }
        }
    }

    [Fact]
    public void EveryLiteralProductionResourceUseExistsInBothCultures()
    {
        var repositoryRoot = FindRepositoryRoot();
        var pagesRoot = Path.Combine(
            repositoryRoot,
            "src",
            "BodyLife.Crm.Web",
            "Pages");
        var zones = new (string Resource, string SearchRoot, Regex[] Patterns)[]
        {
            (
                nameof(Owner),
                Path.Combine(pagesRoot, "Owner"),
                [
                    new Regex(@"(?:Model\.)?T\(\s*""([^""]+)""", RegexOptions.CultureInvariant),
                    new Regex(@"(?:OwnerLocalizer|\bL)\[\s*""([^""]+)""", RegexOptions.CultureInvariant),
                ]),
            (
                nameof(Reception),
                Path.Combine(pagesRoot, "Reception"),
                [new Regex(@"(?:\bL|localizer)\[\s*""([^""]+)""", RegexOptions.CultureInvariant)]),
            (
                nameof(Reports),
                Path.Combine(pagesRoot, "Reports"),
                [new Regex(@"(?:\bL|localizer)\[\s*""([^""]+)""", RegexOptions.CultureInvariant)]),
            (
                nameof(Audit),
                Path.Combine(pagesRoot, "Audit"),
                [new Regex(@"(?:\bP|Presentation|presentation)\.Text\(\s*""([^""]+)""", RegexOptions.CultureInvariant)]),
            (
                nameof(Authentication),
                pagesRoot,
                [new Regex(@"AuthenticationLocalizer\[\s*""([^""]+)""", RegexOptions.CultureInvariant)]),
            (
                nameof(Shared),
                pagesRoot,
                [new Regex(@"(?:SharedLocalizer|SharedL)\[\s*""([^""]+)""", RegexOptions.CultureInvariant)]),
            (
                nameof(Validation),
                Path.Combine(repositoryRoot, "src", "BodyLife.Crm.Web", "Localization"),
                [new Regex(@"localizer\[\s*""([^""]+)""", RegexOptions.CultureInvariant)]),
        };

        foreach (var (resource, searchRoot, patterns) in zones)
        {
            var english = ReadResource(resource, WebCultures.English);
            var ukrainian = ReadResource(resource, WebCultures.Ukrainian);
            var uses = Directory.EnumerateFiles(
                    searchRoot,
                    "*",
                    SearchOption.AllDirectories)
                .Where(path => path.EndsWith(".cs", StringComparison.Ordinal)
                    || path.EndsWith(".cshtml", StringComparison.Ordinal))
                .SelectMany(path => patterns.SelectMany(pattern => pattern
                    .Matches(File.ReadAllText(path))
                    .Select(match => (
                        Key: match.Groups[1].Value,
                        File: Path.GetRelativePath(repositoryRoot, path)))))
                .Distinct()
                .ToArray();

            Assert.NotEmpty(uses);
            foreach (var (key, file) in uses)
            {
                Assert.True(
                    english.ContainsKey(key),
                    $"{file} references missing {resource}.{key} in en-US.");
                Assert.True(
                    ukrainian.ContainsKey(key),
                    $"{file} references missing {resource}.{key} in uk-UA.");
            }
        }
    }

    private static IStringLocalizer<T> CreateLocalizer<T>() where T : class
    {
        var factory = CreateFactory();
        return new StringLocalizer<T>(factory);
    }

    private static IStringLocalizerFactory CreateFactory()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddLocalization(options => options.ResourcesPath = "Resources");
        return services.BuildServiceProvider().GetRequiredService<IStringLocalizerFactory>();
    }

    private static IReadOnlyDictionary<string, string> ReadResource(string name, string culture)
    {
        var path = Path.Combine(
            FindRepositoryRoot(),
            "src",
            "BodyLife.Crm.Web",
            "Resources",
            $"Localization.{name}.{culture}.resx");

        return XDocument.Load(path)
            .Root!
            .Elements("data")
            .ToDictionary(
                element => element.Attribute("name")!.Value,
                element => element.Element("value")!.Value);
    }

    private static string[] PlaceholderIndexes(string value) =>
        PlaceholderPattern
            .Matches(value)
            .Select(match => match.Groups[1].Value)
            .Order(StringComparer.Ordinal)
            .ToArray();

    private static SetLanguageModel CreateSetLanguageModel(out DefaultHttpContext context)
    {
        context = new DefaultHttpContext();
        context.RequestServices = new ServiceCollection()
            .AddSingleton<IUrlHelperFactory, TestUrlHelperFactory>()
            .BuildServiceProvider();

        return new SetLanguageModel
        {
            PageContext = new PageContext(new ActionContext(context, new RouteData(), new PageActionDescriptor())),
        };
    }

    private sealed class TestUrlHelperFactory : IUrlHelperFactory
    {
        public IUrlHelper GetUrlHelper(ActionContext context) => new UrlHelper(context);
    }

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "BodyLife.Crm.sln")))
            {
                return directory.FullName;
            }
        }

        throw new DirectoryNotFoundException("Could not locate the repository root.");
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

[CollectionDefinition(nameof(LocalizationCollection), DisableParallelization = true)]
public sealed class LocalizationCollection;
