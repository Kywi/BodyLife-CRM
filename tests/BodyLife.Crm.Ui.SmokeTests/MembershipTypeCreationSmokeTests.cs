using Microsoft.Playwright;

namespace BodyLife.Crm.Ui.SmokeTests;

[Collection("Owner UI smoke")]
public sealed class MembershipTypeCreationSmokeTests : IClassFixture<ReceptionAppFixture>, IAsyncLifetime
{
    private readonly ReceptionAppFixture _app;
    private IPlaywright? _playwright;
    private IBrowser? _browser;

    public MembershipTypeCreationSmokeTests(ReceptionAppFixture app)
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
    public async Task OwnerCreatesMembershipTypeWithValidationAndCanonicalReread(
        string viewportName,
        int width,
        int height)
    {
        Assert.NotNull(_browser);
        var context = await CreateContextAsync(width, height);
        var canonicalName = $"Created {viewportName} plan";

        try
        {
            var page = await context.NewPageAsync();
            await LoginAsync(page, $"{viewportName} catalog creation");
            await page.GetByRole(AriaRole.Link, new() { Name = "Membership types" }).ClickAsync();
            await page.WaitForURLAsync("**/Owner/MembershipTypes");

            var form = page.Locator("#create-membership-type-form");
            await FillCreateFormAsync(form, "Busy state probe");
            await form.EvaluateAsync(
                "form => form.addEventListener('submit', event => event.preventDefault(), { once: true })");
            var submitButton = form.Locator("button[type='submit']");
            await submitButton.ClickAsync();

            Assert.True(await submitButton.IsDisabledAsync());
            Assert.Equal("Adding...", await submitButton.InnerTextAsync());
            Assert.Equal(0, await _app.CountMembershipTypesByNameAsync("Busy state probe"));

            await page.ReloadAsync(new PageReloadOptions { WaitUntil = WaitUntilState.NetworkIdle });
            form = page.Locator("#create-membership-type-form");
            var postedIdempotencyKey = await ReadIdempotencyKeyAsync(form);
            var auditCountBefore = await _app.CountMembershipTypeCreateAuditEntriesAsync();
            var idempotencyCountBefore = await _app.CountCreateMembershipTypeIdempotencyKeysAsync();

            await FillCreateFormAsync(form, "   ");
            await form.GetByRole(AriaRole.Button, new() { Name = "Add membership type" }).ClickAsync();

            await ExpectVisibleAsync(
                page.GetByText("Membership type name is required.", new() { Exact = true }),
                viewportName,
                "server validation error");
            form = page.Locator("#create-membership-type-form");
            Assert.Equal(postedIdempotencyKey, await ReadIdempotencyKeyAsync(form));
            Assert.Equal(auditCountBefore, await _app.CountMembershipTypeCreateAuditEntriesAsync());
            Assert.Equal(idempotencyCountBefore, await _app.CountCreateMembershipTypeIdempotencyKeysAsync());
            Assert.Equal(0, await _app.CountMembershipTypesByNameAsync(canonicalName));

            await form.GetByLabel("Name", new() { Exact = true })
                .FillAsync($"  Created   {viewportName}   plan  ");
            await form.GetByLabel("Currency", new() { Exact = true }).FillAsync("uah");
            await form.GetByLabel("Comment", new() { Exact = true })
                .FillAsync("  Front desk launch.  ");
            await form.GetByRole(AriaRole.Button, new() { Name = "Add membership type" }).ClickAsync();

            Assert.DoesNotContain("handler=Create", page.Url, StringComparison.Ordinal);
            await ExpectVisibleAsync(
                page.GetByText("Membership type created.", new() { Exact = false }),
                viewportName,
                "create result");
            Assert.Contains(
                "Audit reference",
                await page.Locator(".operation-message").InnerTextAsync(),
                StringComparison.Ordinal);

            var createdRow = FindCatalogRow(page, canonicalName);
            await ExpectVisibleAsync(createdRow, viewportName, "created catalog row");
            await ExpectVisibleAsync(
                createdRow.GetByText("Active", new() { Exact = true }),
                viewportName,
                "created active state");
            var createdRowText = await createdRow.InnerTextAsync();
            Assert.Contains("60 days", createdRowText, StringComparison.Ordinal);
            Assert.Contains("16", createdRowText, StringComparison.Ordinal);
            Assert.Contains("1450.50 UAH", createdRowText, StringComparison.Ordinal);
            Assert.Contains("Front desk launch.", createdRowText, StringComparison.Ordinal);

            var freshIdempotencyKey = await ReadIdempotencyKeyAsync(
                page.Locator("#create-membership-type-form"));
            Assert.NotEqual(postedIdempotencyKey, freshIdempotencyKey);
            Assert.Equal(1, await _app.CountMembershipTypesByNameAsync(canonicalName));
            Assert.NotNull(await _app.FindMembershipTypeIdByNameAsync(canonicalName));
            Assert.Equal(auditCountBefore + 1, await _app.CountMembershipTypeCreateAuditEntriesAsync());
            Assert.Equal(
                idempotencyCountBefore + 1,
                await _app.CountCreateMembershipTypeIdempotencyKeysAsync());

            var fitsViewport = await page.EvaluateAsync<bool>(
                "() => document.documentElement.scrollWidth <= window.innerWidth + 1");
            Assert.True(fitsViewport, $"{viewportName} create workflow should not require horizontal scrolling.");
        }
        finally
        {
            await context.CloseAsync();
        }
    }

    private static async Task FillCreateFormAsync(ILocator form, string name)
    {
        await form.GetByLabel("Name", new() { Exact = true }).FillAsync(name);
        await form.GetByLabel("Duration (days)", new() { Exact = true }).FillAsync("60");
        await form.GetByLabel("Visit limit", new() { Exact = true }).FillAsync("16");
        await form.GetByLabel("Price amount", new() { Exact = true }).FillAsync("1450.50");
        await form.GetByLabel("Currency", new() { Exact = true }).FillAsync("UAH");
        await form.GetByLabel("Comment", new() { Exact = true }).FillAsync("Initial catalog value.");
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

    private async Task LoginAsync(IPage page, string deviceLabel)
    {
        await page.GotoAsync(_app.BaseAddress.ToString(), new PageGotoOptions
        {
            WaitUntil = WaitUntilState.NetworkIdle,
        });
        await page.GetByRole(AriaRole.Textbox, new() { Name = "Login" }).FillAsync(_app.LoginName);
        await page.GetByLabel("Password", new() { Exact = true }).FillAsync(_app.Password);
        await page.GetByLabel("Device", new() { Exact = true }).FillAsync(deviceLabel);
        await page.GetByRole(AriaRole.Button, new() { Name = "Login" }).ClickAsync();
        await page.WaitForURLAsync("**/");
    }

    private static async Task<string> ReadIdempotencyKeyAsync(ILocator form)
    {
        var value = await form.Locator("input[name='form.IdempotencyKey']").GetAttributeAsync("value");
        Assert.True(Guid.TryParseExact(value, "N", out _), "The create form should carry a valid idempotency key.");
        return value!;
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
