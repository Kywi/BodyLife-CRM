using Microsoft.Playwright;

namespace BodyLife.Crm.Ui.SmokeTests;

[Collection("Owner UI smoke")]
public sealed class MembershipTypeEditingSmokeTests : IClassFixture<ReceptionAppFixture>, IAsyncLifetime
{
    private readonly ReceptionAppFixture _app;
    private IPlaywright? _playwright;
    private IBrowser? _browser;

    public MembershipTypeEditingSmokeTests(ReceptionAppFixture app)
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
    [InlineData("tablet", 1024, 768, "Eight visits / 30 days", true)]
    [InlineData("phone", 390, 844, "Legacy 12 visits / 45 days", false)]
    public async Task OwnerEditsCanonicalMembershipTypeAfterValidationAndStaleRefresh(
        string viewportName,
        int width,
        int height,
        string initialName,
        bool expectedActiveState)
    {
        Assert.NotNull(_browser);
        var context = await CreateContextAsync(width, height);
        var membershipTypeId = await _app.FindMembershipTypeIdByNameAsync(initialName)
            ?? throw new InvalidOperationException($"The {viewportName} edit target was not seeded.");
        var original = await _app.ReadMembershipTypeAsync(membershipTypeId);
        var canonicalStaleName = $"Canonical {viewportName} offer";
        var attemptedStaleName = $"Attempted {viewportName} overwrite";
        var finalName = $"Edited {viewportName} plan";

        try
        {
            var page = await context.NewPageAsync();
            await LoginAsync(page, $"{viewportName} catalog editing");
            await page.GetByRole(AriaRole.Link, new() { Name = "Membership types" }).ClickAsync();
            await page.WaitForURLAsync("**/Owner/MembershipTypes");

            var form = await OpenEditFormAsync(page, membershipTypeId);
            Assert.Equal(original.Name, await form.GetByLabel("Name", new() { Exact = true }).InputValueAsync());
            Assert.Equal(
                original.PriceAmount.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture),
                await form.GetByLabel("Price amount", new() { Exact = true }).InputValueAsync());

            await form.GetByLabel("Reason for change", new() { Exact = true })
                .FillAsync("Busy state probe.");
            await form.EvaluateAsync(
                "form => form.addEventListener('submit', event => event.preventDefault(), { once: true })");
            var submitButton = form.Locator("button[type='submit']");
            await submitButton.ClickAsync();

            Assert.True(await submitButton.IsDisabledAsync());
            Assert.Equal("Saving...", await submitButton.InnerTextAsync());

            await page.ReloadAsync(new PageReloadOptions { WaitUntil = WaitUntilState.NetworkIdle });
            form = await OpenEditFormAsync(page, membershipTypeId);
            var originalIdempotencyKey = await ReadIdempotencyKeyAsync(form);
            var originalExpectedUpdatedAt = await ReadExpectedUpdatedAtAsync(form);
            var auditCountBefore = await _app.CountMembershipTypeEditAuditEntriesAsync(membershipTypeId);
            var idempotencyCountBefore = await _app.CountEditMembershipTypeIdempotencyKeysAsync(
                membershipTypeId);

            await form.GetByLabel("Reason for change", new() { Exact = true })
                .FillAsync("Catalog review.");
            await form.GetByRole(AriaRole.Button, new() { Name = "Save changes" }).ClickAsync();

            await ExpectVisibleAsync(
                page.GetByText(
                    "Review the highlighted values.",
                    new() { Exact = true }),
                viewportName,
                "no-op validation error");
            form = page.Locator(EditFormSelector(membershipTypeId));
            Assert.Equal(originalIdempotencyKey, await ReadIdempotencyKeyAsync(form));
            Assert.Equal(
                auditCountBefore,
                await _app.CountMembershipTypeEditAuditEntriesAsync(membershipTypeId));
            Assert.Equal(
                idempotencyCountBefore,
                await _app.CountEditMembershipTypeIdempotencyKeysAsync(membershipTypeId));

            await _app.AdvanceMembershipTypeForStaleTestAsync(
                membershipTypeId,
                canonicalStaleName);
            var staleCanonical = await _app.ReadMembershipTypeAsync(membershipTypeId);
            await form.GetByLabel("Name", new() { Exact = true }).FillAsync(attemptedStaleName);
            await form.GetByLabel("Reason for change", new() { Exact = true })
                .FillAsync("Stale attempt.");
            await form.GetByRole(AriaRole.Button, new() { Name = "Save changes" }).ClickAsync();

            await ExpectVisibleAsync(
                page.GetByText(
                    "This membership type changed. Review the refreshed values.",
                    new() { Exact = true }),
                viewportName,
                "stale-state guidance");
            form = page.Locator(EditFormSelector(membershipTypeId));
            Assert.Equal(
                canonicalStaleName,
                await form.GetByLabel("Name", new() { Exact = true }).InputValueAsync());
            Assert.NotEqual(originalIdempotencyKey, await ReadIdempotencyKeyAsync(form));
            Assert.NotEqual(originalExpectedUpdatedAt, await ReadExpectedUpdatedAtAsync(form));
            Assert.Equal(
                auditCountBefore,
                await _app.CountMembershipTypeEditAuditEntriesAsync(membershipTypeId));
            Assert.Equal(
                idempotencyCountBefore,
                await _app.CountEditMembershipTypeIdempotencyKeysAsync(membershipTypeId));

            var successfulIdempotencyKey = await ReadIdempotencyKeyAsync(form);
            await FillEditFormAsync(form, viewportName);
            await form.GetByRole(AriaRole.Button, new() { Name = "Save changes" }).ClickAsync();

            Assert.DoesNotContain("handler=Edit", page.Url, StringComparison.Ordinal);
            await ExpectVisibleAsync(
                page.GetByText("Membership type updated.", new() { Exact = false }),
                viewportName,
                "edit result");
            Assert.Contains(
                "Audit reference",
                await page.Locator(".operation-message").InnerTextAsync(),
                StringComparison.Ordinal);

            var editedRow = FindCatalogRow(page, membershipTypeId);
            await ExpectVisibleAsync(editedRow, viewportName, "edited catalog row");
            var editedRowText = await editedRow.InnerTextAsync();
            Assert.Contains(finalName, editedRowText, StringComparison.Ordinal);
            Assert.Contains("75 days", editedRowText, StringComparison.Ordinal);
            Assert.Contains("18", editedRowText, StringComparison.Ordinal);
            Assert.Contains("1675.25 UAH", editedRowText, StringComparison.Ordinal);
            Assert.Contains("Reviewed future offer.", editedRowText, StringComparison.Ordinal);
            await ExpectVisibleAsync(
                editedRow.GetByText(expectedActiveState ? "Active" : "Inactive", new() { Exact = true }),
                viewportName,
                "preserved lifecycle state");

            var persisted = await _app.ReadMembershipTypeAsync(membershipTypeId);
            Assert.Equal(finalName, persisted.Name);
            Assert.Equal(75, persisted.DurationDays);
            Assert.Equal(18, persisted.VisitsLimit);
            Assert.Equal(1675.25m, persisted.PriceAmount);
            Assert.Equal("UAH", persisted.PriceCurrency);
            Assert.Equal(expectedActiveState, persisted.IsActive);
            Assert.Equal("Reviewed future offer.", persisted.Comment);
            Assert.Equal(original.CreatedAt, persisted.CreatedAt);
            Assert.Equal(original.DeactivatedAt, persisted.DeactivatedAt);
            Assert.True(persisted.UpdatedAt > staleCanonical.UpdatedAt);
            Assert.Equal(
                auditCountBefore + 1,
                await _app.CountMembershipTypeEditAuditEntriesAsync(membershipTypeId));
            Assert.Equal(
                idempotencyCountBefore + 1,
                await _app.CountEditMembershipTypeIdempotencyKeysAsync(membershipTypeId));
            Assert.Equal(
                "Annual catalog review.",
                await _app.ReadLatestMembershipTypeEditReasonAsync(membershipTypeId));

            var freshIdempotencyKey = await ReadIdempotencyKeyAsync(
                page.Locator(EditFormSelector(membershipTypeId)));
            Assert.NotEqual(successfulIdempotencyKey, freshIdempotencyKey);
            Assert.Equal(
                1,
                await page.Locator(
                    ".membership-type-deactivate-panel button[type='submit']")
                    .CountAsync());
            var fitsViewport = await page.EvaluateAsync<bool>(
                "() => document.documentElement.scrollWidth <= window.innerWidth + 1");
            Assert.True(fitsViewport, $"{viewportName} edit workflow should not require horizontal scrolling.");
        }
        finally
        {
            await context.CloseAsync();
        }
    }

    private static async Task FillEditFormAsync(ILocator form, string viewportName)
    {
        await form.GetByLabel("Name", new() { Exact = true })
            .FillAsync($"  Edited   {viewportName}   plan  ");
        await form.GetByLabel("Duration (days)", new() { Exact = true }).FillAsync("75");
        await form.GetByLabel("Visit limit", new() { Exact = true }).FillAsync("18");
        await form.GetByLabel("Price amount", new() { Exact = true }).FillAsync("1675.25");
        await form.GetByLabel("Currency", new() { Exact = true }).FillAsync("uah");
        await form.GetByLabel("Catalog comment", new() { Exact = true })
            .FillAsync("  Reviewed future offer.  ");
        await form.GetByLabel("Reason for change", new() { Exact = true })
            .FillAsync("  Annual catalog review.  ");
    }

    private static async Task<ILocator> OpenEditFormAsync(IPage page, Guid membershipTypeId)
    {
        var row = FindCatalogRow(page, membershipTypeId);
        var details = row.Locator(".membership-type-edit-panel");
        await details.Locator("summary").ClickAsync();
        var form = details.Locator(EditFormSelector(membershipTypeId));
        await form.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });
        return form;
    }

    private static ILocator FindCatalogRow(IPage page, Guid membershipTypeId)
    {
        return page.Locator(
            $".membership-type-row[data-membership-type-id='{membershipTypeId}']");
    }

    private static string EditFormSelector(Guid membershipTypeId)
    {
        return $"#edit-membership-type-form-{membershipTypeId:N}";
    }

    private static async Task<string> ReadIdempotencyKeyAsync(ILocator form)
    {
        var value = await form.Locator("input[name='form.IdempotencyKey']").GetAttributeAsync("value");
        Assert.True(Guid.TryParseExact(value, "N", out _), "The edit form should carry a valid idempotency key.");
        return value!;
    }

    private static async Task<string> ReadExpectedUpdatedAtAsync(ILocator form)
    {
        var value = await form.Locator("input[name='form.ExpectedUpdatedAt']").GetAttributeAsync("value");
        Assert.True(
            DateTimeOffset.TryParse(value, out _),
            "The edit form should carry a valid expected updated_at value.");
        return value!;
    }

    private async Task<IBrowserContext> CreateContextAsync(int width, int height)
    {
        return await _browser!.NewContextAsync(new BrowserNewContextOptions
        {
            Locale = ReceptionAppFixture.WorkflowCulture,
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
