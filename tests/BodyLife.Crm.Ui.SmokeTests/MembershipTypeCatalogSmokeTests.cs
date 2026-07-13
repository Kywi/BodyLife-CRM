using Microsoft.Playwright;

namespace BodyLife.Crm.Ui.SmokeTests;

[Collection("Owner UI smoke")]
public sealed class MembershipTypeCatalogSmokeTests : IClassFixture<ReceptionAppFixture>, IAsyncLifetime
{
    private readonly ReceptionAppFixture _app;
    private IPlaywright? _playwright;
    private IBrowser? _browser;

    public MembershipTypeCatalogSmokeTests(ReceptionAppFixture app)
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
    public async Task OwnerReadsActiveAndInactiveCatalogOnTargetViewport(
        string viewportName,
        int width,
        int height)
    {
        Assert.NotNull(_browser);
        var context = await CreateContextAsync(width, height);

        try
        {
            var page = await context.NewPageAsync();
            await LoginAsync(page, _app.LoginName, _app.Password, $"{viewportName} catalog owner");
            await page.GetByRole(AriaRole.Link, new() { Name = "Membership types" }).ClickAsync();
            await page.WaitForURLAsync("**/Owner/MembershipTypes");

            Assert.Equal("Membership types - BodyLife CRM", await page.TitleAsync());
            await ExpectVisibleAsync(
                page.GetByRole(AriaRole.Heading, new() { Name = "Membership types" }),
                viewportName,
                "catalog heading");
            await ExpectVisibleAsync(
                page.GetByText("Owner managed", new() { Exact = true }),
                viewportName,
                "owner permission state");
            await ExpectVisibleAsync(
                page.GetByText("1 active", new() { Exact = true }),
                viewportName,
                "active count");
            await ExpectVisibleAsync(
                page.GetByText("1 inactive", new() { Exact = true }),
                viewportName,
                "inactive count");

            var rows = page.Locator(".membership-type-row");
            Assert.Equal(2, await rows.CountAsync());
            Assert.Equal("active", await rows.First.GetAttributeAsync("data-membership-type-status"));
            Assert.Equal("inactive", await rows.Last.GetAttributeAsync("data-membership-type-status"));

            var activeRow = FindCatalogRow(page, "Eight visits / 30 days");
            await ExpectVisibleAsync(
                activeRow.GetByText("Active", new() { Exact = true }),
                viewportName,
                "active lifecycle state");
            Assert.Contains("30 days", await activeRow.InnerTextAsync(), StringComparison.Ordinal);
            await ExpectVisibleAsync(
                activeRow.GetByText("8", new() { Exact = true }),
                viewportName,
                "active visit limit");
            Assert.Contains("950.00 UAH", await activeRow.InnerTextAsync(), StringComparison.Ordinal);
            Assert.Contains("Standard reception offer.", await activeRow.InnerTextAsync(), StringComparison.Ordinal);
            Assert.Contains("Not deactivated", await activeRow.InnerTextAsync(), StringComparison.Ordinal);

            var inactiveRow = FindCatalogRow(page, "Legacy 12 visits / 45 days");
            await ExpectVisibleAsync(
                inactiveRow.GetByText("Inactive", new() { Exact = true }),
                viewportName,
                "inactive lifecycle state");
            Assert.Contains("45 days", await inactiveRow.InnerTextAsync(), StringComparison.Ordinal);
            await ExpectVisibleAsync(
                inactiveRow.GetByText("12", new() { Exact = true }),
                viewportName,
                "inactive visit limit");
            Assert.Contains("1200.00 UAH", await inactiveRow.InnerTextAsync(), StringComparison.Ordinal);
            Assert.Contains("Retained for catalog history.", await inactiveRow.InnerTextAsync(), StringComparison.Ordinal);
            Assert.Contains("2026-07-05 11:00 UTC", await inactiveRow.InnerTextAsync(), StringComparison.Ordinal);

            Assert.Equal(3, await page.Locator("main form").CountAsync());
            await ExpectVisibleAsync(
                page.Locator("#create-membership-type-form"),
                viewportName,
                "owner create form");
            Assert.Equal(
                2,
                await page.Locator(".membership-type-edit-panel").CountAsync());
            Assert.Equal(
                0,
                await page.GetByRole(AriaRole.Button, new() { Name = "Deactivate" }).CountAsync());
            var fitsViewport = await page.EvaluateAsync<bool>(
                "() => document.documentElement.scrollWidth <= window.innerWidth + 1");
            Assert.True(fitsViewport, $"{viewportName} catalog should not require horizontal scrolling.");
        }
        finally
        {
            await context.CloseAsync();
        }
    }

    [Fact]
    public async Task NamedAdminCannotNavigateToOrOpenMembershipTypeCatalog()
    {
        Assert.NotNull(_browser);
        var context = await CreateContextAsync(1024, 768);

        try
        {
            var page = await context.NewPageAsync();
            await LoginAsync(page, _app.AdminLoginName, _app.AdminPassword, "admin catalog denial");

            Assert.Equal(
                0,
                await page.GetByRole(AriaRole.Link, new() { Name = "Membership types" }).CountAsync());

            await page.GotoAsync(new Uri(_app.BaseAddress, "/Owner/MembershipTypes").ToString());

            Assert.Contains("/AccessDenied", page.Url, StringComparison.Ordinal);
            await ExpectVisibleAsync(
                page.GetByRole(AriaRole.Heading, new() { Name = "Owner access required" }),
                "tablet",
                "owner-only catalog denial");
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

    private static ILocator FindCatalogRow(IPage page, string membershipTypeName)
    {
        return page.Locator(".membership-type-row")
            .Filter(new LocatorFilterOptions { HasText = membershipTypeName });
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
