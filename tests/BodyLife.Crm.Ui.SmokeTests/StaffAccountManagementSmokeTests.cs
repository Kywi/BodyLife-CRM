using Microsoft.Playwright;

namespace BodyLife.Crm.Ui.SmokeTests;

public sealed class StaffAccountManagementSmokeTests : IClassFixture<ReceptionAppFixture>, IAsyncLifetime
{
    private readonly ReceptionAppFixture _app;
    private IPlaywright? _playwright;
    private IBrowser? _browser;

    public StaffAccountManagementSmokeTests(ReceptionAppFixture app)
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
    public async Task OwnerManagesStaffAccountOnTargetViewport(
        string viewportName,
        int width,
        int height)
    {
        Assert.NotNull(_browser);
        var context = await CreateContextAsync(width, height);

        try
        {
            var page = await context.NewPageAsync();
            await LoginAsync(
                page,
                _app.LoginName,
                _app.Password,
                $"{viewportName} owner");
            await page.GetByRole(AriaRole.Link, new() { Name = "Staff accounts" }).ClickAsync();
            await page.WaitForURLAsync("**/Owner/StaffAccounts");

            Assert.Equal("Staff accounts - BodyLife CRM", await page.TitleAsync());
            await ExpectVisibleAsync(
                page.GetByRole(AriaRole.Heading, new() { Name = "Staff accounts" }),
                viewportName,
                "staff account heading");

            var displayName = $"Managed {viewportName} desk";
            var updatedDisplayName = $"Updated {viewportName} desk";
            var loginName = $"managed.{viewportName}";
            var createForm = page.Locator("#create-account-form");
            await createForm.GetByLabel("Account type").SelectOptionAsync("SharedReceptionAdmin");
            await createForm.GetByLabel("Display name").FillAsync(displayName);
            await createForm.GetByRole(AriaRole.Button, new() { Name = "Add account" }).ClickAsync();

            await ExpectVisibleAsync(
                page.GetByText("Staff account created.", new() { Exact = false }),
                viewportName,
                "create result");
            var accountRow = FindAccountRow(page, displayName);
            await ExpectVisibleAsync(accountRow, viewportName, "created staff account");
            Assert.Contains(
                "Shared Reception/Admin",
                await accountRow.InnerTextAsync(),
                StringComparison.Ordinal);

            await accountRow.GetByLabel("Display name").FillAsync(updatedDisplayName);
            await accountRow.GetByRole(AriaRole.Button, new() { Name = "Save name" }).ClickAsync();
            await ExpectVisibleAsync(
                page.GetByText("Staff account display name updated.", new() { Exact = false }),
                viewportName,
                "display-name result");

            accountRow = FindAccountRow(page, updatedDisplayName);
            await accountRow.GetByLabel("Login", new() { Exact = true }).FillAsync(loginName);
            await accountRow.GetByLabel("New password", new() { Exact = true })
                .FillAsync($"managed {viewportName} password");
            await accountRow.GetByRole(AriaRole.Button, new() { Name = "Set credentials" }).ClickAsync();
            await ExpectVisibleAsync(
                page.GetByText("Staff credentials configured.", new() { Exact = false }),
                viewportName,
                "credential setup result");

            accountRow = FindAccountRow(page, updatedDisplayName);
            await accountRow.GetByLabel("New password", new() { Exact = true })
                .FillAsync($"rotated {viewportName} password");
            await accountRow.GetByLabel("Reason for reset", new() { Exact = true })
                .FillAsync("Scheduled credential rotation");
            await accountRow.GetByRole(AriaRole.Button, new() { Name = "Reset credentials" }).ClickAsync();
            await ExpectVisibleAsync(
                page.GetByText("Staff credentials reset.", new() { Exact = false }),
                viewportName,
                "credential reset result");

            accountRow = FindAccountRow(page, updatedDisplayName);
            await accountRow.GetByLabel("Reason for deactivation", new() { Exact = true })
                .FillAsync("Reception access ended");
            page.Dialog += (_, dialog) => _ = dialog.AcceptAsync();
            await accountRow.GetByRole(AriaRole.Button, new() { Name = "Deactivate" }).ClickAsync();
            await ExpectVisibleAsync(
                page.GetByText("Staff account deactivated.", new() { Exact = false }),
                viewportName,
                "deactivation result");

            accountRow = FindAccountRow(page, updatedDisplayName);
            await ExpectVisibleAsync(
                accountRow.GetByText("Inactive", new() { Exact = true }),
                viewportName,
                "inactive status");
            await accountRow.GetByRole(AriaRole.Button, new() { Name = "Activate" }).ClickAsync();
            await ExpectVisibleAsync(
                page.GetByText("Staff account activated.", new() { Exact = false }),
                viewportName,
                "activation result");

            Assert.True(
                await page.Locator("form[data-busy-form]").CountAsync() >= 4,
                "State-changing forms should opt into duplicate-submit protection.");
            var fitsViewport = await page.EvaluateAsync<bool>(
                "() => document.documentElement.scrollWidth <= window.innerWidth + 1");
            Assert.True(fitsViewport, $"{viewportName} layout should not require horizontal scrolling.");
        }
        finally
        {
            await context.CloseAsync();
        }
    }

    [Fact]
    public async Task NamedAdminCannotOpenOwnerStaffAccountPage()
    {
        Assert.NotNull(_browser);
        var context = await CreateContextAsync(1024, 768);

        try
        {
            var page = await context.NewPageAsync();
            await LoginAsync(page, _app.AdminLoginName, _app.AdminPassword, "admin tablet");

            await page.GotoAsync(new Uri(_app.BaseAddress, "/Owner/StaffAccounts").ToString());

            Assert.Contains("/AccessDenied", page.Url, StringComparison.Ordinal);
            await ExpectVisibleAsync(
                page.GetByRole(AriaRole.Heading, new() { Name = "Owner access required" }),
                "tablet",
                "owner-only access denied state");
        }
        finally
        {
            await context.CloseAsync();
        }
    }

    private async Task<IBrowserContext> CreateContextAsync(int width, int height)
    {
        return await _browser!.NewContextAsync(new BrowserNewContextOptions
        {
            ViewportSize = new ViewportSize
            {
                Width = width,
                Height = height,
            },
        });
    }

    private async Task LoginAsync(
        IPage page,
        string loginName,
        string password,
        string deviceLabel)
    {
        await page.GotoAsync(_app.BaseAddress.ToString(), new PageGotoOptions
        {
            WaitUntil = WaitUntilState.NetworkIdle,
        });
        await page.GetByRole(AriaRole.Textbox, new() { Name = "Login" }).FillAsync(loginName);
        await page.GetByLabel("Password", new() { Exact = true }).FillAsync(password);
        await page.GetByLabel("Device", new() { Exact = true }).FillAsync(deviceLabel);
        await page.GetByRole(AriaRole.Button, new() { Name = "Login" }).ClickAsync();
        await page.WaitForURLAsync("**/");
    }

    private static ILocator FindAccountRow(IPage page, string displayName)
    {
        return page.Locator(".staff-account-row")
            .Filter(new LocatorFilterOptions { HasText = displayName });
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

        Assert.True(await locator.IsVisibleAsync(), $"{label} should be visible on {viewportName} viewport.");
    }
}
