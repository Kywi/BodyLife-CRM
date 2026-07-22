using Microsoft.Playwright;

namespace BodyLife.Crm.Ui.SmokeTests;

public sealed class LocalizationSmokeTests : IClassFixture<ReceptionAppFixture>, IAsyncLifetime
{
    private const string English = "en-US";
    private const string Ukrainian = "uk-UA";

    private readonly ReceptionAppFixture _app;
    private IPlaywright? _playwright;
    private IBrowser? _browser;

    public LocalizationSmokeTests(ReceptionAppFixture app)
    {
        _app = app;
    }

    public async Task InitializeAsync()
    {
        _playwright = await Playwright.CreateAsync();
        _browser = await _playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
        {
            Headless = true,
        });
    }

    public async Task DisposeAsync()
    {
        if (_browser is not null)
        {
            await _browser.CloseAsync();
        }

        _playwright?.Dispose();
    }

    [Fact]
    public async Task LoginDefaultsUnsupportedCulturesToUkrainianAndLanguagePostsStayLocal()
    {
        var context = await CreateContextAsync(1024, 768, "fr-FR");

        try
        {
            var page = await context.NewPageAsync();
            await page.GotoAsync(new Uri(_app.BaseAddress, "/Login?returnUrl=%2FReception%2FIndex%3Fq%3DBL-1001").ToString());

            await AssertCultureAsync(page, Ukrainian);
            await ExpectVisibleAsync(page.Locator("#login-title"), "Вхід", "Ukrainian fallback login title");

            var unsupportedCultureStatus = await page.EvaluateAsync<int>("""
                async () => {
                    const token = document.querySelector("form.language-selector-form input[name='__RequestVerificationToken']").value;
                    return (await fetch('/SetLanguage', {
                        method: 'POST',
                        headers: { 'Content-Type': 'application/x-www-form-urlencoded' },
                        body: new URLSearchParams({
                            __RequestVerificationToken: token,
                            culture: 'fr-FR',
                            returnUrl: '/'
                        })
                    })).status;
                }
                """);
            Assert.Equal(400, unsupportedCultureStatus);

            await page.GotoAsync(_app.BaseAddress.ToString());
            await PostLanguageFormAsync(page, English, "https://example.test/not-local");
            Assert.StartsWith(_app.BaseAddress.GetLeftPart(UriPartial.Authority), page.Url, StringComparison.Ordinal);
            Assert.DoesNotContain("example.test", page.Url, StringComparison.OrdinalIgnoreCase);
            await AssertCultureAsync(page, English);

            await page.GotoAsync(_app.BaseAddress.ToString());
            var missingTokenResponse = await page.EvaluateAsync<int>("""
                async () => (await fetch('/SetLanguage', {
                    method: 'POST',
                    headers: { 'Content-Type': 'application/x-www-form-urlencoded' },
                    body: 'culture=uk-UA&returnUrl=%2F'
                })).status
                """);
            Assert.Equal(400, missingTokenResponse);
        }
        finally
        {
            await context.CloseAsync();
        }
    }

    [Fact]
    public async Task SelectorPersistsCulturePreservesLocalUrlAndKeepsHtmxReceptionInSelectedCulture()
    {
        var context = await CreateContextAsync(1024, 768, "fr-FR");

        try
        {
            var page = await context.NewPageAsync();
            await page.GotoAsync(_app.BaseAddress.ToString());
            await LoginAsync(page, _app.LoginName, _app.Password, "localization selector owner");
            await page.GotoAsync(new Uri(_app.BaseAddress, "/Reception/Index?q=BL-1001").ToString());
            await AssertCultureAsync(page, Ukrainian);

            await SwitchCultureAsync(page, English);
            Assert.Contains("/Reception/Index?q=BL-1001", page.Url, StringComparison.Ordinal);
            await AssertCultureAsync(page, English);
            await ExpectVisibleAsync(page.Locator("#reception-title"), "Reception", "English reception title");

            await SubmitHtmxSearchAsync(page, "BL-1001");
            await ExpectVisibleAsync(page.Locator("#client-profile"), "Client profile", "English htmx profile heading");
            await ExpectVisibleAsync(page.Locator("#client-profile"), "No current membership", "English htmx membership text");

            await SwitchCultureAsync(page, Ukrainian);
            Assert.Contains("q=BL-1001", page.Url, StringComparison.Ordinal);
            await AssertCultureAsync(page, Ukrainian);
            await ExpectVisibleAsync(page.Locator("#reception-title"), "Панель рецепції", "Ukrainian reception title");

            await SubmitHtmxSearchAsync(page, "BL-1001");
            await ExpectVisibleAsync(page.Locator("#client-profile"), "Профіль клієнта", "Ukrainian htmx profile heading");
            await ExpectVisibleAsync(page.Locator("#client-profile"), "Немає поточного абонемента", "Ukrainian htmx membership text");

            await page.ReloadAsync(new PageReloadOptions { WaitUntil = WaitUntilState.NetworkIdle });
            await AssertCultureAsync(page, Ukrainian);
        }
        finally
        {
            await context.CloseAsync();
        }
    }

    [Fact]
    public async Task BrowserContextsDoNotLeakCultureCookies()
    {
        var englishContext = await CreateContextAsync(1024, 768, "fr-FR");
        var ukrainianContext = await CreateContextAsync(1024, 768, "fr-FR");

        try
        {
            var englishPage = await englishContext.NewPageAsync();
            var ukrainianPage = await ukrainianContext.NewPageAsync();
            await englishPage.GotoAsync(_app.BaseAddress.ToString());
            await ukrainianPage.GotoAsync(_app.BaseAddress.ToString());

            await SwitchCultureAsync(englishPage, English);
            await AssertCultureAsync(englishPage, English);
            await AssertCultureAsync(ukrainianPage, Ukrainian);

            await ukrainianPage.ReloadAsync(new PageReloadOptions { WaitUntil = WaitUntilState.NetworkIdle });
            await AssertCultureAsync(ukrainianPage, Ukrainian);
            await englishPage.ReloadAsync(new PageReloadOptions { WaitUntil = WaitUntilState.NetworkIdle });
            await AssertCultureAsync(englishPage, English);
        }
        finally
        {
            await englishContext.CloseAsync();
            await ukrainianContext.CloseAsync();
        }
    }

    [Theory]
    [InlineData(English, "Payment added", false)]
    [InlineData(Ukrainian, "Платіж додано", true)]
    public async Task PaymentSubmissionUsesSelectedCultureAndCanonicalDecimalTransport(
        string culture,
        string successText,
        bool usePhoneClient)
    {
        const decimal expectedAmount = 125.50m;
        var clientId = usePhoneClient
            ? _app.PaymentPhoneClientId
            : _app.PaymentTabletClientId;
        var context = await CreateContextAsync(1024, 768, "fr-FR");

        try
        {
            var page = await context.NewPageAsync();
            await page.GotoAsync(_app.BaseAddress.ToString());
            await SwitchCultureAsync(page, culture);
            await LoginAsync(
                page,
                _app.LoginName,
                _app.Password,
                $"localization {culture} payment");
            await page.GotoAsync(
                new Uri(
                    _app.BaseAddress,
                    $"/Reception/Index?clientId={clientId}").ToString(),
                new PageGotoOptions { WaitUntil = WaitUntilState.NetworkIdle });

            await AssertCultureAsync(page, culture);
            var panel = page.Locator("#add-payment-action-panel");
            if (await panel.GetAttributeAsync("open") is null)
            {
                await panel.Locator("summary").ClickAsync();
            }

            var form = panel.Locator("form");
            await form.Locator("select[name='form.PaymentContext']")
                .SelectOptionAsync("OneOff");
            await form.Locator("input[name='form.Amount']").FillAsync("125.50");
            var comment = $"{culture} localized payment";
            await form.Locator("textarea[name='form.Comment']").FillAsync(comment);

            var responseTask = page.WaitForResponseAsync(response =>
                response.Request.Method == "POST"
                && response.Url.Contains(
                    "handler=CreatePayment",
                    StringComparison.OrdinalIgnoreCase));
            await form.Locator("[data-add-payment-submit]").ClickAsync();
            var response = await responseTask;
            Assert.True(
                response.Ok,
                $"Localized Payment htmx request returned HTTP {response.Status}.");
            Assert.True(response.Request.Headers.TryGetValue(
                "hx-request",
                out var htmxRequest));
            Assert.Equal("true", htmxRequest);
            await page.WaitForFunctionAsync(
                "() => document.querySelector('.htmx-request') === null");

            var successMessage = page.Locator(
                ".profile-operation-message.operation-success");
            await successMessage.WaitForAsync(new LocatorWaitForOptions
            {
                State = WaitForSelectorState.Visible,
                Timeout = 5_000,
            });
            Assert.StartsWith(
                $"{successText}.",
                (await successMessage.InnerTextAsync()).Trim(),
                StringComparison.Ordinal);
            Assert.Equal(1L, await _app.CountActivePaymentsAsync(clientId));
            Assert.Equal(1L, await _app.CountCreatePaymentAuditEntriesAsync(clientId));
            Assert.Equal(1L, await _app.CountCreatePaymentIdempotencyKeysAsync(clientId));
            var payment = await _app.ReadLatestActivePaymentAsync(clientId);
            Assert.Equal(expectedAmount, payment.Amount);
            Assert.Equal("UAH", payment.Currency);
            Assert.Equal("one_off", payment.PaymentContext);
            Assert.Equal(comment, payment.Comment);
        }
        finally
        {
            await context.CloseAsync();
        }
    }

    [Theory]
    [InlineData(
        English,
        "Reception",
        "Issue membership",
        "Mark visit",
        "Add payment",
        "Add freeze",
        "Daily report",
        "Membership types",
        "Non-working days",
        "Audit timeline",
        "Client history")]
    [InlineData(
        Ukrainian,
        "Панель рецепції",
        "Видати абонемент",
        "Позначити відвідування",
        "Додати платіж",
        "Додати замороження",
        "Щоденний звіт",
        "Типи абонементів",
        "Неробочі дні",
        "Журнал аудиту",
        "Історія клієнта")]
    public async Task AuthenticatedPagesRenderRepresentativeTranslations(
        string culture,
        string receptionTitle,
        string issueMembership,
        string markVisit,
        string addPayment,
        string addFreeze,
        string reportTitle,
        string ownerTitle,
        string nonWorkingDaysTitle,
        string auditTitle,
        string historyTitle)
    {
        var context = await CreateContextAsync(1024, 768);

        try
        {
            var page = await context.NewPageAsync();
            await page.GotoAsync(_app.BaseAddress.ToString());
            await SwitchCultureAsync(page, culture);
            await LoginAsync(page, _app.LoginName, _app.Password, $"localization {culture} owner");

            await AssertCultureAsync(page, culture);
            await ExpectVisibleAsync(page.Locator("#reception-title"), receptionTitle, "localized Reception title");

            await page.GotoAsync(new Uri(
                _app.BaseAddress,
                $"/Reception/Index?clientId={_app.FreezeTabletClientId}").ToString());
            var profile = page.Locator("#client-profile");
            await ExpectLocatorTextAsync(
                profile.Locator("#issue-membership-action-panel > summary"),
                issueMembership,
                "localized issue Membership action");
            await ExpectLocatorTextAsync(
                profile.Locator("#mark-visit-action-panel > summary"),
                markVisit,
                "localized mark Visit action");
            await ExpectLocatorTextAsync(
                profile.Locator("#add-payment-action-panel > summary"),
                addPayment,
                "localized add Payment action");
            await ExpectLocatorTextAsync(
                profile.Locator("#add-freeze-action-panel > summary"),
                addFreeze,
                "localized add Freeze action");

            await page.GotoAsync(new Uri(_app.BaseAddress, "/Reports/Daily").ToString());
            await ExpectVisibleAsync(page.Locator("#daily-report-title"), reportTitle, "localized report title");

            await page.GotoAsync(new Uri(_app.BaseAddress, "/Owner/MembershipTypes").ToString());
            await ExpectVisibleAsync(page.Locator("#membership-types-title"), ownerTitle, "localized Owner title");

            await page.GotoAsync(new Uri(_app.BaseAddress, "/Owner/NonWorkingDays").ToString());
            await ExpectVisibleAsync(
                page.Locator("#non-working-days-title"),
                nonWorkingDaysTitle,
                "localized Non-Working Days title");

            await page.GotoAsync(new Uri(_app.BaseAddress, "/Audit/Timeline").ToString());
            await ExpectVisibleAsync(page.Locator("#audit-timeline-title"), auditTitle, "localized Audit title");

            await page.GotoAsync(new Uri(_app.BaseAddress, $"/Audit/ClientHistory?clientId={_app.PaymentHistoryClientId}").ToString());
            await ExpectVisibleAsync(page.Locator("#client-history-title"), historyTitle, "localized Client History title");
        }
        finally
        {
            await context.CloseAsync();
        }
    }

    [Theory]
    [InlineData(English)]
    [InlineData(Ukrainian)]
    public async Task OwnerTabletAndAdminPhoneLocalizedScreenMatrixHasNoHorizontalOverflow(string culture)
    {
        await CaptureOwnerTabletMatrixAsync(culture);
        await CaptureAdminPhoneMatrixAsync(culture);
    }

    private async Task CaptureOwnerTabletMatrixAsync(string culture)
    {
        var context = await CreateContextAsync(1024, 768);

        try
        {
            var page = await context.NewPageAsync();
            await page.GotoAsync(_app.BaseAddress.ToString());
            await SwitchCultureAsync(page, culture);
            await LoginAsync(page, _app.LoginName, _app.Password, $"localization {culture} owner tablet");

            await CaptureAndAssertFitsAsync(page, $"owner-tablet-{culture}-reception");
            await page.GotoAsync(new Uri(_app.BaseAddress, "/Owner/MembershipTypes").ToString());
            await CaptureAndAssertFitsAsync(page, $"owner-tablet-{culture}-owner");
            await page.GotoAsync(new Uri(_app.BaseAddress, "/Reports/Daily").ToString());
            await CaptureAndAssertFitsAsync(page, $"owner-tablet-{culture}-reports");
            await page.GotoAsync(new Uri(_app.BaseAddress, "/Audit/Timeline").ToString());
            await CaptureAndAssertFitsAsync(page, $"owner-tablet-{culture}-audit");
        }
        finally
        {
            await context.CloseAsync();
        }
    }

    private async Task CaptureAdminPhoneMatrixAsync(string culture)
    {
        var context = await CreateContextAsync(390, 844);

        try
        {
            var page = await context.NewPageAsync();
            await page.GotoAsync(_app.BaseAddress.ToString());
            await SwitchCultureAsync(page, culture);
            await LoginAsync(page, _app.AdminLoginName, _app.AdminPassword, $"localization {culture} admin phone");

            await CaptureAndAssertFitsAsync(page, $"admin-phone-{culture}-reception");
            await page.GotoAsync(new Uri(_app.BaseAddress, "/Reports/Daily").ToString());
            await CaptureAndAssertFitsAsync(page, $"admin-phone-{culture}-reports");
            await page.GotoAsync(new Uri(_app.BaseAddress, "/Audit/Timeline").ToString());
            await CaptureAndAssertFitsAsync(page, $"admin-phone-{culture}-audit");
        }
        finally
        {
            await context.CloseAsync();
        }
    }

    private async Task<IBrowserContext> CreateContextAsync(int width, int height, string? locale = null)
    {
        return await _browser!.NewContextAsync(new BrowserNewContextOptions
        {
            Locale = locale,
            ViewportSize = new ViewportSize { Width = width, Height = height },
        });
    }

    private static async Task LoginAsync(IPage page, string loginName, string password, string deviceLabel)
    {
        await page.Locator("#LoginName").FillAsync(loginName);
        await page.Locator("#Password").FillAsync(password);
        await page.Locator("#DeviceLabel").FillAsync(deviceLabel);
        await page.Locator("form.auth-form button[type='submit']").ClickAsync();
        await page.WaitForURLAsync("**/");
        await page.WaitForLoadStateAsync(LoadState.NetworkIdle);
    }

    private static async Task SwitchCultureAsync(IPage page, string culture)
    {
        if (string.Equals(
            await page.Locator("html").GetAttributeAsync("lang"),
            culture,
            StringComparison.Ordinal))
        {
            return;
        }

        var form = page.Locator($"form.language-selector-form:has(input[name='culture'][value='{culture}'])");
        await form.Locator("button[type='submit']").ClickAsync();
        await page.WaitForLoadStateAsync(LoadState.NetworkIdle);
    }

    private static async Task PostLanguageFormAsync(IPage page, string culture, string returnUrl)
    {
        var form = page.Locator("form.language-selector-form").Last;
        await form.Locator("input[name='culture']").EvaluateAsync("(input, value) => input.value = value", culture);
        await form.Locator("input[name='returnUrl']").EvaluateAsync("(input, value) => input.value = value", returnUrl);
        await form.Locator("button[type='submit']").ClickAsync();
        await page.WaitForLoadStateAsync(LoadState.NetworkIdle);
    }

    private static async Task SubmitHtmxSearchAsync(IPage page, string query)
    {
        await page.Locator("#client-search").FillAsync(query);
        var responseTask = page.WaitForResponseAsync(response =>
            response.Request.Method == "GET"
            && response.Url.Contains("handler=Search", StringComparison.OrdinalIgnoreCase));
        await page.Locator("#reception-search button[type='submit']").ClickAsync();
        var response = await responseTask;
        Assert.True(response.Ok, $"Localized htmx search returned HTTP {response.Status}.");
        await page.WaitForFunctionAsync("() => document.querySelector('.htmx-request') === null");
    }

    private static async Task AssertCultureAsync(IPage page, string culture)
    {
        Assert.Equal(culture, await page.Locator("html").GetAttributeAsync("lang"));
    }

    private static async Task ExpectVisibleAsync(ILocator locator, string text, string label)
    {
        var match = locator.GetByText(text, new LocatorGetByTextOptions { Exact = true });
        await match.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 5_000 });
        Assert.True(await match.IsVisibleAsync(), $"{label} should be visible.");
    }

    private static async Task ExpectLocatorTextAsync(
        ILocator locator,
        string expected,
        string label)
    {
        await locator.WaitForAsync(new LocatorWaitForOptions
        {
            State = WaitForSelectorState.Visible,
            Timeout = 5_000,
        });
        Assert.Equal(expected, (await locator.InnerTextAsync()).Trim());
        Assert.True(await locator.IsVisibleAsync(), $"{label} should be visible.");
    }

    private static async Task CaptureAndAssertFitsAsync(IPage page, string state)
    {
        var fits = await page.EvaluateAsync<bool>("() => document.documentElement.scrollWidth <= window.innerWidth + 1");
        Assert.True(fits, $"{state} should not require horizontal scrolling.");

        var screenshotDirectory = Environment.GetEnvironmentVariable("BODYLIFE_UI_SCREENSHOT_DIR");
        if (!string.IsNullOrWhiteSpace(screenshotDirectory))
        {
            Directory.CreateDirectory(screenshotDirectory);
            await page.ScreenshotAsync(new PageScreenshotOptions
            {
                FullPage = true,
                Path = Path.Combine(screenshotDirectory, $"{state}.png"),
            });
        }
    }
}
