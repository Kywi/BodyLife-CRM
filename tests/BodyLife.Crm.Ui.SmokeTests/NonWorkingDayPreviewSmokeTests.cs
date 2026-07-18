using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.Playwright;

namespace BodyLife.Crm.Ui.SmokeTests;

[Collection("Owner UI smoke")]
public sealed class NonWorkingDayPreviewSmokeTests : IClassFixture<ReceptionAppFixture>, IAsyncLifetime
{
    private readonly ReceptionAppFixture _app;
    private IPlaywright? _playwright;
    private IBrowser? _browser;

    public NonWorkingDayPreviewSmokeTests(ReceptionAppFixture app)
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

    [Theory]
    [InlineData("tablet", 1024, 768)]
    [InlineData("phone", 390, 844)]
    public async Task OwnerPreviewsExactNonWorkingDayImpactOnTargetViewport(
        string viewportName,
        int width,
        int height)
    {
        var mutationCountsBefore = await _app.ReadNonWorkingDayMutationCountsAsync();
        var context = await CreateContextAsync(width, height);

        try
        {
            var page = await LoginAsync(
                context,
                _app.LoginName,
                _app.Password,
                $"{viewportName} NonWorkingDay preview");
            await page.GetByRole(
                    AriaRole.Link,
                    new() { Name = "Non-working days", Exact = true })
                .ClickAsync();
            await page.WaitForURLAsync("**/Owner/NonWorkingDays");

            Assert.Equal("Non-working days - BodyLife CRM", await page.TitleAsync());
            await ExpectVisibleAsync(
                page.GetByRole(
                    AriaRole.Heading,
                    new() { Name = "Non-working days", Exact = true }),
                viewportName,
                "NonWorkingDay page heading");
            var form = page.Locator("#non-working-day-preview-form");
            await ExpectVisibleAsync(form, viewportName, "NonWorkingDay preview form");
            Assert.Equal(
                1,
                await form.Locator("input[name='__RequestVerificationToken']").CountAsync());
            Assert.Equal("this:replace", await form.GetAttributeAsync("hx-sync"));
            Assert.NotNull(await form.GetAttributeAsync("data-busy-form"));
            Assert.Equal(
                0,
                await page.Locator("[data-non-working-day-impact-preview]").CountAsync());

            await form.GetByLabel("Start date", new() { Exact = true })
                .FillAsync(FormatDate(_app.NonWorkingDayPreviewEndDate));
            await form.GetByLabel("End date", new() { Exact = true })
                .FillAsync(FormatDate(_app.NonWorkingDayPreviewStartDate));
            await form.GetByLabel("Reason code", new() { Exact = true })
                .FillAsync("weather_closure");
            await form.GetByLabel("Reason comment (optional)", new() { Exact = true })
                .FillAsync("Severe weather review");
            await SubmitHtmxPreviewAsync(page, verifyBusy: false);

            await ExpectVisibleAsync(
                page.GetByText(
                    "Proposed end date must be on or after the start date.",
                    new() { Exact = true }),
                viewportName,
                "invalid range error");
            Assert.Equal(
                0,
                await page.Locator("[data-non-working-day-impact-preview]").CountAsync());
            Assert.Equal(
                mutationCountsBefore,
                await _app.ReadNonWorkingDayMutationCountsAsync());

            form = page.Locator("#non-working-day-preview-form");
            await form.GetByLabel("Start date", new() { Exact = true })
                .FillAsync(FormatDate(_app.NonWorkingDayPreviewStartDate));
            await form.GetByLabel("End date", new() { Exact = true })
                .FillAsync(FormatDate(_app.NonWorkingDayPreviewEndDate));
            await DelayPreviewRequestsAsync(page);
            await AssertMinimumTouchTargetAsync(
                form.GetByRole(AriaRole.Button, new() { Name = "Preview impact" }),
                viewportName,
                "Preview impact button");
            await AssertFitsViewportAsync(page, viewportName, "NonWorkingDay form");

            await SubmitHtmxPreviewAsync(page, verifyBusy: true);

            var preview = page.Locator("[data-non-working-day-impact-preview]");
            await ExpectVisibleAsync(preview, viewportName, "NonWorkingDay impact preview");
            Assert.Equal("2", await preview.GetAttributeAsync("data-preview-affected-count"));
            Assert.Equal(
                "1",
                await preview.GetAttributeAsync("data-preview-overlap-warning-count"));
            Assert.Equal(
                FormatDate(_app.NonWorkingDayPreviewStartDate),
                await preview.GetAttributeAsync("data-preview-period-start"));
            Assert.Equal(
                FormatDate(_app.NonWorkingDayPreviewEndDate),
                await preview.GetAttributeAsync("data-preview-period-end"));
            await ExpectVisibleAsync(
                preview.GetByText(
                    "Every affected Membership receives 2040-02-01 to 2040-02-03, even when it overlaps only one endpoint. Existing extension sources remain union-counted.",
                    new() { Exact = true }),
                viewportName,
                "full-period endpoint disclosure");

            var confirmationToken = await preview
                .Locator("[data-preview-confirmation-token]")
                .InputValueAsync();
            Assert.False(string.IsNullOrWhiteSpace(confirmationToken));
            Assert.True(confirmationToken.Length <= 4096);
            var scopeFingerprint = await preview
                .Locator("[data-preview-scope-fingerprint]")
                .InputValueAsync();
            Assert.Matches(new Regex("^[0-9A-F]{64}$", RegexOptions.CultureInvariant), scopeFingerprint);
            var expiresAtValue = await preview.GetAttributeAsync("data-preview-expires-at");
            Assert.NotNull(expiresAtValue);
            var expiresAt = DateTimeOffset.Parse(
                expiresAtValue,
                CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind);
            Assert.True(expiresAt > DateTimeOffset.UtcNow);

            var affectedRows = preview.Locator("[data-impact-membership-id]");
            Assert.Equal(2, await affectedRows.CountAsync());
            Assert.Equal(
                0,
                await preview.Locator(
                    $"[data-impact-membership-id='{_app.NonWorkingDayNoOverlapMembershipId}']")
                    .CountAsync());

            var endBoundaryRow = preview.Locator(
                $"[data-impact-membership-id='{_app.NonWorkingDayEndBoundaryMembershipId}']");
            await ExpectVisibleAsync(
                endBoundaryRow.GetByRole(
                    AriaRole.Link,
                    new() { Name = "Closure End Boundary", Exact = true }),
                viewportName,
                "end-boundary Client link");
            Assert.Equal(
                "2040-02-01",
                await endBoundaryRow.GetAttributeAsync("data-impact-applied-start"));
            Assert.Equal(
                "2040-02-03",
                await endBoundaryRow.GetAttributeAsync("data-impact-applied-end"));
            await ExpectVisibleAsync(
                endBoundaryRow.GetByText("2040-02-02 to 2040-02-04", new() { Exact = true }),
                viewportName,
                "end-boundary effective-end estimate");
            await ExpectVisibleAsync(
                endBoundaryRow.GetByText("1 to 3", new() { Exact = true }),
                viewportName,
                "end-boundary extension estimate");
            await ExpectVisibleAsync(
                endBoundaryRow.GetByText("+2 unique days", new() { Exact = true }),
                viewportName,
                "union-counted added days");
            var overlapWarning = endBoundaryRow.Locator("[data-overlap-source-type='freeze']");
            await ExpectVisibleAsync(overlapWarning, viewportName, "Freeze overlap warning");
            Assert.Contains(
                "Freeze 2040-02-02..2040-02-02: Scheduled equipment pause",
                await overlapWarning.InnerTextAsync(),
                StringComparison.Ordinal);
            Assert.Contains("2040-02-02 to 2040-02-02, 1 days", await overlapWarning.InnerTextAsync(), StringComparison.Ordinal);

            var startBoundaryRow = preview.Locator(
                $"[data-impact-membership-id='{_app.NonWorkingDayStartBoundaryMembershipId}']");
            await ExpectVisibleAsync(
                startBoundaryRow.GetByRole(
                    AriaRole.Link,
                    new() { Name = "Closure Start Boundary", Exact = true }),
                viewportName,
                "start-boundary Client link");
            Assert.Equal(
                "2040-02-01",
                await startBoundaryRow.GetAttributeAsync("data-impact-applied-start"));
            Assert.Equal(
                "2040-02-03",
                await startBoundaryRow.GetAttributeAsync("data-impact-applied-end"));
            await ExpectVisibleAsync(
                startBoundaryRow.GetByText("2040-02-12 to 2040-02-15", new() { Exact = true }),
                viewportName,
                "start-boundary effective-end estimate");
            await ExpectVisibleAsync(
                startBoundaryRow.GetByText("0 to 3", new() { Exact = true }),
                viewportName,
                "start-boundary extension estimate");
            await ExpectVisibleAsync(
                startBoundaryRow.GetByText("+3 unique days", new() { Exact = true }),
                viewportName,
                "full-period added days");
            Assert.Equal(
                0,
                await startBoundaryRow.Locator("[data-overlap-source-type]").CountAsync());

            Assert.Equal(
                mutationCountsBefore,
                await _app.ReadNonWorkingDayMutationCountsAsync());
            await AssertFitsViewportAsync(page, viewportName, "NonWorkingDay impact preview");
            await CaptureVisualAsync(page, viewportName, "non-working-day-preview");
        }
        finally
        {
            await context.CloseAsync();
        }
    }

    [Fact]
    public async Task NamedAdminCannotNavigateToOrOpenNonWorkingDayPreview()
    {
        var context = await CreateContextAsync(1024, 768);

        try
        {
            var page = await LoginAsync(
                context,
                _app.AdminLoginName,
                _app.AdminPassword,
                "admin NonWorkingDay denial");
            Assert.Equal(
                0,
                await page.GetByRole(
                        AriaRole.Link,
                        new() { Name = "Non-working days", Exact = true })
                    .CountAsync());

            await page.GotoAsync(
                new Uri(_app.BaseAddress, "/Owner/NonWorkingDays").ToString());

            Assert.Contains("/AccessDenied", page.Url, StringComparison.Ordinal);
            await ExpectVisibleAsync(
                page.GetByRole(
                    AriaRole.Heading,
                    new() { Name = "Owner access required", Exact = true }),
                "tablet",
                "Owner-only NonWorkingDay denial");
        }
        finally
        {
            await context.CloseAsync();
        }
    }

    private async Task<IBrowserContext> CreateContextAsync(int width, int height)
    {
        Assert.NotNull(_browser);
        return await _browser.NewContextAsync(new BrowserNewContextOptions
        {
            ViewportSize = new ViewportSize
            {
                Width = width,
                Height = height,
            },
        });
    }

    private async Task<IPage> LoginAsync(
        IBrowserContext context,
        string loginName,
        string password,
        string deviceLabel)
    {
        var page = await context.NewPageAsync();
        await page.GotoAsync(_app.BaseAddress.ToString(), new PageGotoOptions
        {
            WaitUntil = WaitUntilState.NetworkIdle,
        });
        await page.GetByRole(AriaRole.Textbox, new() { Name = "Login" })
            .FillAsync(loginName);
        await page.GetByLabel("Password", new() { Exact = true }).FillAsync(password);
        await page.GetByLabel("Device", new() { Exact = true }).FillAsync(deviceLabel);
        await page.GetByRole(AriaRole.Button, new() { Name = "Login" }).ClickAsync();
        await page.WaitForURLAsync("**/");
        return page;
    }

    private static async Task SubmitHtmxPreviewAsync(IPage page, bool verifyBusy)
    {
        var form = page.Locator("#non-working-day-preview-form");
        Assert.Equal("this:replace", await form.GetAttributeAsync("hx-sync"));
        var responseTask = page.WaitForResponseAsync(response =>
            response.Request.Method == "POST"
            && response.Url.Contains("handler=Preview", StringComparison.OrdinalIgnoreCase));
        Task<IJSHandle>? disabledTask = null;
        if (verifyBusy)
        {
            disabledTask = page.WaitForFunctionAsync(
                "() => document.querySelector('#non-working-day-preview-form button[type=\"submit\"]')?.disabled === true");
        }

        await form.GetByRole(AriaRole.Button, new() { Name = "Preview impact" }).ClickAsync();
        if (disabledTask is not null)
        {
            await disabledTask;
        }

        var response = await responseTask;
        Assert.True(response.Ok, $"htmx preview returned HTTP {response.Status}.");
        Assert.True(response.Request.Headers.TryGetValue("hx-request", out var htmxRequest));
        Assert.Equal("true", htmxRequest);
        Assert.DoesNotContain("weather_closure", response.Url, StringComparison.Ordinal);
        await WaitForHtmxSettleAsync(page);
    }

    private static Task DelayPreviewRequestsAsync(IPage page)
    {
        return page.RouteAsync(
            "**/*handler=Preview*",
            async route =>
            {
                await Task.Delay(500);
                await route.ContinueAsync();
            });
    }

    private static async Task WaitForHtmxSettleAsync(IPage page)
    {
        await page.WaitForFunctionAsync(
            "() => document.querySelector('.htmx-request') === null");
    }

    private static async Task ExpectVisibleAsync(
        ILocator locator,
        string viewportName,
        string label)
    {
        await locator.WaitForAsync(new LocatorWaitForOptions
        {
            State = WaitForSelectorState.Visible,
            Timeout = 5_000,
        });
        Assert.True(
            await locator.IsVisibleAsync(),
            $"{label} should be visible on {viewportName} viewport.");
    }

    private static async Task AssertFitsViewportAsync(
        IPage page,
        string viewportName,
        string state)
    {
        var fitsViewport = await page.EvaluateAsync<bool>(
            "() => document.documentElement.scrollWidth <= window.innerWidth + 1");
        Assert.True(
            fitsViewport,
            $"{viewportName} {state} should not require horizontal scrolling.");
    }

    private static async Task AssertMinimumTouchTargetAsync(
        ILocator locator,
        string viewportName,
        string label)
    {
        var bounds = await locator.BoundingBoxAsync();
        Assert.NotNull(bounds);
        Assert.True(
            bounds.Width >= 44 && bounds.Height >= 44,
            $"{label} should be at least 44px in both dimensions on {viewportName}.");
    }

    private static async Task CaptureVisualAsync(
        IPage page,
        string viewportName,
        string state)
    {
        var screenshotDirectory = Environment.GetEnvironmentVariable("BODYLIFE_UI_SCREENSHOT_DIR");
        if (string.IsNullOrWhiteSpace(screenshotDirectory))
        {
            return;
        }

        Directory.CreateDirectory(screenshotDirectory);
        await page.ScreenshotAsync(new PageScreenshotOptions
        {
            FullPage = true,
            Path = Path.Combine(screenshotDirectory, $"{viewportName}-{state}.png"),
        });
    }

    private static string FormatDate(DateOnly date)
    {
        return date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    }
}
