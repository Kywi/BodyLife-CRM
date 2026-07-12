using Microsoft.Playwright;

namespace BodyLife.Crm.Ui.SmokeTests;

public sealed class ReceptionDashboardSmokeTests : IClassFixture<ReceptionAppFixture>, IAsyncLifetime
{
    private readonly ReceptionAppFixture _app;
    private IPlaywright? _playwright;
    private IBrowser? _browser;

    public ReceptionDashboardSmokeTests(ReceptionAppFixture app)
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
    public async Task ReceptionSearchAndProfileReadPathWorksOnTargetViewport(
        string viewportName,
        int width,
        int height)
    {
        Assert.NotNull(_browser);

        var context = await _browser.NewContextAsync(new BrowserNewContextOptions
        {
            ViewportSize = new ViewportSize
            {
                Width = width,
                Height = height,
            },
        });

        try
        {
            var page = await context.NewPageAsync();
            var response = await page.GotoAsync(_app.BaseAddress.ToString(), new PageGotoOptions
            {
                WaitUntil = WaitUntilState.NetworkIdle,
            });

            Assert.NotNull(response);
            Assert.True(response.Ok, $"{viewportName} request returned HTTP {response.Status}.");

            await ExpectVisibleAsync(page.GetByRole(AriaRole.Heading, new() { Name = "Login" }), viewportName, "login heading");
            var deviceLabel = $"{viewportName} smoke";
            await LoginAsync(page, _app.LoginName, _app.Password, deviceLabel);

            Assert.Equal("Reception - BodyLife CRM", await page.TitleAsync());

            await ExpectVisibleAsync(page.GetByLabel("Current session"), viewportName, "current session shell");
            await ExpectVisibleAsync(page.GetByText("BodyLife Owner"), viewportName, "current account display name");
            await ExpectVisibleAsync(page.GetByText("Owner account / Owner"), viewportName, "current account type and role");
            await ExpectVisibleAsync(page.GetByText(deviceLabel), viewportName, "current device label");
            await ExpectVisibleAsync(page.GetByText("Session"), viewportName, "current session id");
            await ExpectVisibleAsync(page.GetByRole(AriaRole.Button, new() { Name = "Log out" }), viewportName, "logout button");
            await ExpectVisibleAsync(page.GetByRole(AriaRole.Heading, new() { Name = "Reception" }), viewportName, "reception heading");
            await ExpectVisibleAsync(page.GetByRole(AriaRole.Searchbox, new() { Name = "Client search" }), viewportName, "client search input");
            await ExpectVisibleAsync(page.GetByRole(AriaRole.Button, new() { Name = "Search" }), viewportName, "search button");
            await ExpectVisibleAsync(page.GetByRole(AriaRole.Group, new() { Name = "Search mode" }), viewportName, "search mode control");
            await ExpectVisibleAsync(page.GetByRole(AriaRole.Checkbox, new() { Name = "Include inactive" }), viewportName, "inactive-client control");
            var searchResults = page.GetByRole(AriaRole.Region, new() { Name = "Search results" });
            var clientProfile = page.GetByRole(AriaRole.Region, new() { Name = "Client profile" });
            await ExpectVisibleAsync(searchResults, viewportName, "search results region");
            await ExpectVisibleAsync(clientProfile, viewportName, "client profile region");
            await ExpectVisibleAsync(clientProfile.GetByRole(AriaRole.Heading, new() { Name = "No client selected" }), viewportName, "initial profile state");
            await AssertFitsViewportAsync(page, viewportName, "initial dashboard");

            await SubmitHtmxSearchAsync(page, "BL-1001");

            Assert.Contains("q=BL-1001", page.Url, StringComparison.Ordinal);
            Assert.DoesNotContain("handler=Search", page.Url, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(
                "BL-1001",
                await page.GetByRole(AriaRole.Searchbox, new() { Name = "Client search" }).InputValueAsync());
            await ExpectVisibleAsync(clientProfile.GetByRole(AriaRole.Heading, new() { Name = "Kovalenko Olena" }), viewportName, "exact-card profile");
            await ExpectVisibleAsync(clientProfile.GetByText("BL-1001", new() { Exact = true }), viewportName, "exact-card profile number");
            await ExpectVisibleAsync(clientProfile.GetByText("No current membership", new() { Exact = true }), viewportName, "membership placeholder");
            await AssertFitsViewportAsync(page, viewportName, "exact-card profile");
            await CaptureVisualAsync(page, viewportName, "exact-profile");

            await SubmitHtmxSearchAsync(page, "Kovalenko");

            await ExpectVisibleAsync(clientProfile.GetByRole(AriaRole.Heading, new() { Name = "No client selected" }), viewportName, "ambiguous search profile state");
            Assert.Equal(3, await searchResults.Locator(".client-result-row").CountAsync());
            await ExpectVisibleAsync(searchResults.GetByRole(AriaRole.Link, new() { Name = "Open Kovalenko Marta", Exact = true }), viewportName, "Marta result");
            await ExpectVisibleAsync(searchResults.GetByRole(AriaRole.Link, new() { Name = "Open Kovalenko Olena", Exact = true }), viewportName, "Olena result");
            await ExpectVisibleAsync(searchResults.GetByRole(AriaRole.Link, new() { Name = "Open Kovalenko Taras", Exact = true }), viewportName, "Taras result");
            await AssertFitsViewportAsync(page, viewportName, "multiple results");
            await CaptureVisualAsync(page, viewportName, "multiple-results");

            await ClickHtmxProfileAsync(
                page,
                searchResults.GetByRole(AriaRole.Link, new() { Name = "Open Kovalenko Marta", Exact = true }));

            await ExpectVisibleAsync(clientProfile.GetByRole(AriaRole.Heading, new() { Name = "Kovalenko Marta" }), viewportName, "selected profile");
            Assert.Contains("clientId=", page.Url, StringComparison.Ordinal);
            Assert.DoesNotContain("handler=Profile", page.Url, StringComparison.OrdinalIgnoreCase);

            await ClickHtmxProfileAsync(
                page,
                searchResults.GetByRole(AriaRole.Link, new() { Name = "Open Kovalenko Taras", Exact = true }));

            await ExpectVisibleAsync(clientProfile.GetByRole(AriaRole.Heading, new() { Name = "Kovalenko Taras" }), viewportName, "no-card profile");
            await ExpectVisibleAsync(
                clientProfile.GetByLabel("Profile warnings").GetByText("No current card", new() { Exact = true }),
                viewportName,
                "no-card warning");

            await SubmitHtmxSearchAsync(page, "NO-SUCH-CLIENT");

            await ExpectVisibleAsync(searchResults.GetByText("No clients found", new() { Exact = true }), viewportName, "no-match state");
            await ExpectVisibleAsync(clientProfile.GetByRole(AriaRole.Heading, new() { Name = "No client selected" }), viewportName, "no-match profile state");
            await AssertFitsViewportAsync(page, viewportName, "no-match state");
        }
        finally
        {
            await context.CloseAsync();
        }
    }

    [Theory]
    [InlineData("tablet", 1024, 768, "BL-EDIT-TABLET")]
    [InlineData("phone", 390, 844, "BL-EDIT-PHONE")]
    public async Task UpdateClientRequiresExactDuplicateAcknowledgementsAndRereadsWorkspace(
        string viewportName,
        int width,
        int height,
        string cardNumber)
    {
        Assert.NotNull(_browser);
        var clientId = viewportName == "tablet"
            ? _app.TabletEditableClientId
            : _app.PhoneEditableClientId;
        var duplicateName = viewportName == "tablet"
            ? "TabletMatch"
            : "PhoneMatch";
        var duplicatePhone = viewportName == "tablet"
            ? "+380 67 777 88 91"
            : "+380 67 777 88 92";
        var context = await _browser.NewContextAsync(new BrowserNewContextOptions
        {
            ViewportSize = new ViewportSize
            {
                Width = width,
                Height = height,
            },
        });

        try
        {
            var page = await context.NewPageAsync();
            await DelayUpdateClientRequestsAsync(page);
            await page.GotoAsync(_app.BaseAddress.ToString(), new PageGotoOptions
            {
                WaitUntil = WaitUntilState.NetworkIdle,
            });
            await LoginAsync(
                page,
                _app.LoginName,
                _app.Password,
                $"{viewportName} update smoke");
            await SubmitHtmxSearchAsync(page, cardNumber);

            var profile = page.GetByRole(AriaRole.Region, new() { Name = "Client profile" });
            var actionPanel = profile.Locator("#profile-action-panel");
            await ExpectVisibleAsync(actionPanel.Locator("summary"), viewportName, "edit-client action");
            await actionPanel.Locator("summary").ClickAsync();
            await actionPanel.GetByLabel("Surname", new() { Exact = true }).FillAsync("Duplicate");
            await actionPanel.GetByLabel("Name", new() { Exact = true }).FillAsync(duplicateName);
            await actionPanel.GetByLabel("Phone", new() { Exact = true }).FillAsync(duplicatePhone);
            await actionPanel.GetByLabel("Reception note", new() { Exact = true })
                .FillAsync($"Updated from {viewportName}.");

            await SubmitHtmxUpdateAsync(page);

            await ExpectVisibleAsync(
                actionPanel.GetByRole(AriaRole.Alert),
                viewportName,
                "duplicate acknowledgement error");
            await ExpectVisibleAsync(
                actionPanel.GetByRole(AriaRole.Heading, new() { Name = "Duplicate review" }),
                viewportName,
                "duplicate review heading");
            Assert.Equal(2, await actionPanel.Locator(".duplicate-warning-item").CountAsync());
            Assert.Equal(0L, await _app.CountClientUpdateAuditEntriesAsync(clientId));
            Assert.Equal(0L, await _app.CountUpdateClientIdempotencyKeysAsync(clientId));
            Assert.Equal(0L, await _app.CountDuplicateAcknowledgementsAsync(clientId));
            await AssertFitsViewportAsync(page, viewportName, "duplicate review form");
            await CaptureVisualAsync(page, viewportName, "update-client-duplicate-review");

            var acknowledgementControls = actionPanel.GetByRole(
                AriaRole.Checkbox,
                new() { Name = "I verified this is the correct client update" });
            var reasonInputs = actionPanel.GetByLabel("Acknowledgement reason", new() { Exact = true });
            Assert.Equal(2, await acknowledgementControls.CountAsync());
            Assert.Equal(2, await reasonInputs.CountAsync());

            for (var index = 0; index < 2; index++)
            {
                await acknowledgementControls.Nth(index).CheckAsync();
                await reasonInputs.Nth(index).FillAsync($"Verified at reception {index + 1}");
            }

            await SubmitHtmxUpdateAsync(page);

            await ExpectVisibleAsync(
                profile.GetByRole(AriaRole.Heading, new() { Name = $"Duplicate {duplicateName}" }),
                viewportName,
                "canonical updated profile");
            await ExpectVisibleAsync(
                profile.GetByText("Client updated."),
                viewportName,
                "update success message");
            await ExpectVisibleAsync(
                page.GetByRole(AriaRole.Region, new() { Name = "Search results" })
                    .GetByRole(
                        AriaRole.Link,
                        new() { Name = $"Open Duplicate {duplicateName}", Exact = true }),
                viewportName,
                "canonical updated search row");
            Assert.Equal(1L, await _app.CountClientUpdateAuditEntriesAsync(clientId));
            Assert.Equal(1L, await _app.CountUpdateClientIdempotencyKeysAsync(clientId));
            Assert.Equal(2L, await _app.CountDuplicateAcknowledgementsAsync(clientId));
            await AssertFitsViewportAsync(page, viewportName, "updated client profile");
            await CaptureVisualAsync(page, viewportName, "update-client-success");
        }
        finally
        {
            await context.CloseAsync();
        }
    }

    [Fact]
    public async Task StaleUpdateRefreshesCanonicalFormBeforeRetry()
    {
        Assert.NotNull(_browser);
        var clientId = _app.StaleEditableClientId;
        var context = await _browser.NewContextAsync(new BrowserNewContextOptions
        {
            ViewportSize = new ViewportSize
            {
                Width = 1024,
                Height = 768,
            },
        });

        try
        {
            var page = await context.NewPageAsync();
            await DelayUpdateClientRequestsAsync(page);
            await page.GotoAsync(_app.BaseAddress.ToString(), new PageGotoOptions
            {
                WaitUntil = WaitUntilState.NetworkIdle,
            });
            await LoginAsync(page, _app.LoginName, _app.Password, "stale update smoke");
            await SubmitHtmxSearchAsync(page, "BL-EDIT-STALE");

            var profile = page.GetByRole(AriaRole.Region, new() { Name = "Client profile" });
            var actionPanel = profile.Locator("#profile-action-panel");
            await actionPanel.Locator("summary").ClickAsync();
            await _app.AdvanceClientUpdatedAtAsync(clientId);
            await actionPanel.GetByLabel("Reception note", new() { Exact = true })
                .FillAsync("Attempted stale edit.");

            await SubmitHtmxUpdateAsync(page);

            await ExpectVisibleAsync(
                actionPanel.GetByText(
                    "Client changed after the edit form was loaded. Refresh canonical state.",
                    new() { Exact = true }),
                "tablet",
                "stale-state error");
            Assert.NotNull(await actionPanel.GetAttributeAsync("open"));
            Assert.Equal(
                "Stale source.",
                await actionPanel.GetByLabel("Reception note", new() { Exact = true }).InputValueAsync());
            Assert.Equal(0L, await _app.CountClientUpdateAuditEntriesAsync(clientId));
            Assert.Equal(0L, await _app.CountUpdateClientIdempotencyKeysAsync(clientId));

            await actionPanel.GetByLabel("Reception note", new() { Exact = true })
                .FillAsync("Saved after canonical refresh.");
            await SubmitHtmxUpdateAsync(page);

            var canonicalNote = profile.Locator(".client-comment p");
            await ExpectVisibleAsync(canonicalNote, "tablet", "canonical retry result");
            Assert.Equal("Saved after canonical refresh.", await canonicalNote.TextContentAsync());
            Assert.Equal(1L, await _app.CountClientUpdateAuditEntriesAsync(clientId));
            Assert.Equal(1L, await _app.CountUpdateClientIdempotencyKeysAsync(clientId));
            Assert.Equal(0L, await _app.CountDuplicateAcknowledgementsAsync(clientId));
            await AssertFitsViewportAsync(page, "tablet", "stale-state retry");
        }
        finally
        {
            await context.CloseAsync();
        }
    }

    [Fact]
    public async Task ReceptionSearchFallsBackToFullPageWithoutJavascript()
    {
        Assert.NotNull(_browser);
        var context = await _browser.NewContextAsync(new BrowserNewContextOptions
        {
            JavaScriptEnabled = false,
            ViewportSize = new ViewportSize
            {
                Width = 1024,
                Height = 768,
            },
        });

        try
        {
            var page = await context.NewPageAsync();
            await page.GotoAsync(_app.BaseAddress.ToString(), new PageGotoOptions
            {
                WaitUntil = WaitUntilState.NetworkIdle,
            });
            await LoginAsync(page, _app.LoginName, _app.Password, "no-js smoke");
            await page.GetByRole(AriaRole.Searchbox, new() { Name = "Client search" }).FillAsync("BL-1001");
            await page.GetByRole(AriaRole.Button, new() { Name = "Search" }).ClickAsync();
            await page.WaitForURLAsync("**?q=BL-1001**");

            Assert.Equal("Reception - BodyLife CRM", await page.TitleAsync());
            await ExpectVisibleAsync(
                page.GetByRole(AriaRole.Region, new() { Name = "Client profile" })
                    .GetByRole(AriaRole.Heading, new() { Name = "Kovalenko Olena" }),
                "tablet",
                "full-page exact-card profile");
            await AssertFitsViewportAsync(page, "tablet", "no-JavaScript fallback");
        }
        finally
        {
            await context.CloseAsync();
        }
    }

    [Fact]
    public async Task ExpiredDatabaseSessionRequiresLoginAgain()
    {
        Assert.NotNull(_browser);
        var context = await _browser.NewContextAsync(new BrowserNewContextOptions
        {
            ViewportSize = new ViewportSize
            {
                Width = 1024,
                Height = 768,
            },
        });

        try
        {
            var page = await context.NewPageAsync();
            var deviceLabel = $"expiry-{Guid.NewGuid():N}";
            await page.GotoAsync(_app.BaseAddress.ToString(), new PageGotoOptions
            {
                WaitUntil = WaitUntilState.NetworkIdle,
            });
            await page.GetByRole(AriaRole.Textbox, new() { Name = "Login" }).FillAsync(_app.LoginName);
            await page.GetByLabel("Password", new() { Exact = true }).FillAsync(_app.Password);
            await page.GetByLabel("Device", new() { Exact = true }).FillAsync(deviceLabel);
            await page.GetByRole(AriaRole.Button, new() { Name = "Login" }).ClickAsync();
            await page.WaitForURLAsync("**/");
            await _app.ExpireSessionAsync(deviceLabel);

            await page.ReloadAsync();
            await page.WaitForURLAsync("**/Login**");

            await ExpectVisibleAsync(
                page.GetByRole(AriaRole.Heading, new() { Name = "Login" }),
                "tablet",
                "login after session expiry");
            Assert.Contains("ReturnUrl=%2F", page.Url, StringComparison.Ordinal);
            Assert.True(await _app.IsSessionEndedAsync(deviceLabel));
        }
        finally
        {
            await context.CloseAsync();
        }
    }

    private static async Task ExpectVisibleAsync(ILocator locator, string viewportName, string label)
    {
        await locator.WaitForAsync(new LocatorWaitForOptions
        {
            State = WaitForSelectorState.Visible,
            Timeout = 5_000,
        });

        Assert.True(await locator.IsVisibleAsync(), $"{label} should be visible on {viewportName} viewport.");
    }

    private static async Task LoginAsync(
        IPage page,
        string loginName,
        string password,
        string deviceLabel)
    {
        await page.GetByRole(AriaRole.Textbox, new() { Name = "Login" }).FillAsync(loginName);
        await page.GetByLabel("Password", new() { Exact = true }).FillAsync(password);
        await page.GetByLabel("Device", new() { Exact = true }).FillAsync(deviceLabel);
        await page.GetByRole(AriaRole.Button, new() { Name = "Login" }).ClickAsync();
        await page.WaitForURLAsync("**/");
        await page.WaitForLoadStateAsync(LoadState.NetworkIdle);
    }

    private static async Task SubmitHtmxSearchAsync(IPage page, string query)
    {
        await page.GetByRole(AriaRole.Searchbox, new() { Name = "Client search" }).FillAsync(query);
        var responseTask = page.WaitForResponseAsync(response =>
            response.Request.Method == "GET"
            && response.Url.Contains("handler=Search", StringComparison.OrdinalIgnoreCase));
        await page.GetByRole(AriaRole.Button, new() { Name = "Search" }).ClickAsync();
        AssertHtmxResponse(await responseTask);
        await WaitForHtmxSettleAsync(page);
    }

    private static async Task ClickHtmxProfileAsync(IPage page, ILocator profileLink)
    {
        var responseTask = page.WaitForResponseAsync(response =>
            response.Request.Method == "GET"
            && response.Url.Contains("handler=Profile", StringComparison.OrdinalIgnoreCase));
        await profileLink.ClickAsync();
        AssertHtmxResponse(await responseTask);
        await WaitForHtmxSettleAsync(page);
    }

    private static async Task SubmitHtmxUpdateAsync(IPage page)
    {
        var actionPanel = page.Locator("#profile-action-panel");
        var form = actionPanel.Locator("form");
        Assert.Equal("this:drop", await form.GetAttributeAsync("hx-sync"));
        Assert.NotNull(await form.GetAttributeAsync("data-busy-form"));
        var responseTask = page.WaitForResponseAsync(response =>
            response.Request.Method == "POST"
            && response.Url.Contains("handler=UpdateClient", StringComparison.OrdinalIgnoreCase));
        var disabledTask = page.WaitForFunctionAsync(
            "() => document.querySelector('#profile-action-panel button[type=\"submit\"]')?.disabled === true");
        var submitButton = actionPanel.GetByRole(
            AriaRole.Button,
            new() { Name = "Save changes" });
        await submitButton.ClickAsync();
        await disabledTask;
        AssertHtmxResponse(await responseTask);
        await WaitForHtmxSettleAsync(page);
    }

    private static Task DelayUpdateClientRequestsAsync(IPage page)
    {
        return page.RouteAsync(
            "**/*handler=UpdateClient*",
            async route =>
            {
                await Task.Delay(500);
                await route.ContinueAsync();
            });
    }

    private static void AssertHtmxResponse(IResponse response)
    {
        Assert.True(response.Ok, $"htmx request returned HTTP {response.Status}.");
        Assert.True(response.Request.Headers.TryGetValue("hx-request", out var htmxRequest));
        Assert.Equal("true", htmxRequest);
    }

    private static async Task WaitForHtmxSettleAsync(IPage page)
    {
        await page.WaitForFunctionAsync(
            "() => document.querySelector('.htmx-request') === null");
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
}
