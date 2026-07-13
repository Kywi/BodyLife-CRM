using Microsoft.Playwright;

namespace BodyLife.Crm.Ui.SmokeTests;

[Collection("Owner UI smoke")]
public sealed class MembershipTypeDeactivationSmokeTests : IClassFixture<ReceptionAppFixture>, IAsyncLifetime
{
    private readonly ReceptionAppFixture _app;
    private IPlaywright? _playwright;
    private IBrowser? _browser;

    public MembershipTypeDeactivationSmokeTests(ReceptionAppFixture app)
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
    public async Task OwnerDeactivatesMembershipTypeAfterValidationAndCanonicalRefresh(
        string viewportName,
        int width,
        int height)
    {
        Assert.NotNull(_browser);
        var context = await CreateContextAsync(width, height);
        var membershipTypeId = await _app.SeedActiveMembershipTypeForDeactivationAsync(
            $"Deactivate {viewportName} plan");
        var alreadyInactiveId = await _app.SeedActiveMembershipTypeForDeactivationAsync(
            $"Concurrent {viewportName} plan");
        var original = await _app.ReadMembershipTypeAsync(membershipTypeId);
        var canonicalStaleName = $"Canonical {viewportName} deactivation";

        try
        {
            var page = await context.NewPageAsync();
            page.Dialog += (_, dialog) => _ = dialog.AcceptAsync();
            await LoginAsync(page, $"{viewportName} catalog deactivation");
            await page.GetByRole(AriaRole.Link, new() { Name = "Membership types" }).ClickAsync();
            await page.WaitForURLAsync("**/Owner/MembershipTypes");

            var form = await OpenDeactivateFormAsync(page, membershipTypeId);
            await form.GetByLabel("Reason for deactivation", new() { Exact = true })
                .FillAsync("Busy state probe.");
            await form.EvaluateAsync(
                "form => form.addEventListener('submit', event => event.preventDefault(), { once: true })");
            var submitButton = form.Locator("button[type='submit']");
            await submitButton.ClickAsync();

            Assert.True(await submitButton.IsDisabledAsync());
            Assert.Equal("Deactivating...", await submitButton.InnerTextAsync());

            await page.ReloadAsync(new PageReloadOptions { WaitUntil = WaitUntilState.NetworkIdle });
            form = await OpenDeactivateFormAsync(page, membershipTypeId);
            var originalIdempotencyKey = await ReadIdempotencyKeyAsync(form);
            var originalExpectedUpdatedAt = await ReadExpectedUpdatedAtAsync(form);
            var auditCountBefore = await _app.CountMembershipTypeDeactivateAuditEntriesAsync(
                membershipTypeId);
            var idempotencyCountBefore = await _app.CountDeactivateMembershipTypeIdempotencyKeysAsync(
                membershipTypeId);

            var reasonField = form.GetByLabel("Reason for deactivation", new() { Exact = true });
            await reasonField.EvaluateAsync("element => element.removeAttribute('required')");
            await form.GetByRole(AriaRole.Button, new() { Name = "Deactivate" }).ClickAsync();

            await ExpectVisibleAsync(
                page.GetByText(
                    "Reason or command comment is required to deactivate a membership type.",
                    new() { Exact = true }),
                viewportName,
                "server reason validation");
            form = page.Locator(DeactivateFormSelector(membershipTypeId));
            Assert.Equal(originalIdempotencyKey, await ReadIdempotencyKeyAsync(form));
            Assert.Equal(
                auditCountBefore,
                await _app.CountMembershipTypeDeactivateAuditEntriesAsync(membershipTypeId));
            Assert.Equal(
                idempotencyCountBefore,
                await _app.CountDeactivateMembershipTypeIdempotencyKeysAsync(membershipTypeId));
            Assert.True((await _app.ReadMembershipTypeAsync(membershipTypeId)).IsActive);

            await _app.AdvanceMembershipTypeForStaleTestAsync(
                membershipTypeId,
                canonicalStaleName);
            var staleCanonical = await _app.ReadMembershipTypeAsync(membershipTypeId);
            await form.GetByLabel("Reason for deactivation", new() { Exact = true })
                .FillAsync("Stale lifecycle attempt.");
            await form.GetByRole(AriaRole.Button, new() { Name = "Deactivate" }).ClickAsync();

            await ExpectVisibleAsync(
                page.GetByText(
                    "This membership type changed after the deactivation form was opened. Canonical state was reloaded; review it before trying again.",
                    new() { Exact = true }),
                viewportName,
                "stale-state guidance");
            form = page.Locator(DeactivateFormSelector(membershipTypeId));
            Assert.Contains(
                canonicalStaleName,
                await FindCatalogRow(page, membershipTypeId).InnerTextAsync(),
                StringComparison.Ordinal);
            Assert.NotEqual(originalIdempotencyKey, await ReadIdempotencyKeyAsync(form));
            Assert.NotEqual(originalExpectedUpdatedAt, await ReadExpectedUpdatedAtAsync(form));
            Assert.Equal(
                auditCountBefore,
                await _app.CountMembershipTypeDeactivateAuditEntriesAsync(membershipTypeId));
            Assert.Equal(
                idempotencyCountBefore,
                await _app.CountDeactivateMembershipTypeIdempotencyKeysAsync(membershipTypeId));

            var otherKeyBeforeRedirect = await ReadIdempotencyKeyAsync(
                page.Locator(DeactivateFormSelector(alreadyInactiveId)));
            await form.GetByLabel("Reason for deactivation", new() { Exact = true })
                .FillAsync("  Retired from future sales.  ");
            await form.GetByRole(AriaRole.Button, new() { Name = "Deactivate" }).ClickAsync();

            Assert.DoesNotContain("handler=Deactivate", page.Url, StringComparison.Ordinal);
            await ExpectVisibleAsync(
                page.GetByText("Membership type deactivated.", new() { Exact = false }),
                viewportName,
                "deactivation result");
            Assert.Contains(
                "Audit reference",
                await page.Locator(".operation-message").InnerTextAsync(),
                StringComparison.Ordinal);

            var deactivatedRow = FindCatalogRow(page, membershipTypeId);
            await ExpectVisibleAsync(deactivatedRow, viewportName, "deactivated catalog row");
            await ExpectVisibleAsync(
                deactivatedRow.GetByText("Inactive", new() { Exact = true }),
                viewportName,
                "inactive lifecycle state");
            Assert.Equal(0, await deactivatedRow.Locator(".membership-type-deactivate-panel").CountAsync());
            Assert.Equal(1, await deactivatedRow.Locator(".membership-type-edit-panel").CountAsync());

            var persisted = await _app.ReadMembershipTypeAsync(membershipTypeId);
            Assert.Equal(canonicalStaleName, persisted.Name);
            Assert.Equal(original.DurationDays, persisted.DurationDays);
            Assert.Equal(original.VisitsLimit, persisted.VisitsLimit);
            Assert.Equal(original.PriceAmount, persisted.PriceAmount);
            Assert.Equal(original.PriceCurrency, persisted.PriceCurrency);
            Assert.Equal(original.Comment, persisted.Comment);
            Assert.Equal(original.CreatedAt, persisted.CreatedAt);
            Assert.False(persisted.IsActive);
            Assert.NotNull(persisted.DeactivatedAt);
            Assert.Equal(persisted.UpdatedAt, persisted.DeactivatedAt);
            Assert.True(persisted.UpdatedAt > staleCanonical.UpdatedAt);
            Assert.Equal(
                auditCountBefore + 1,
                await _app.CountMembershipTypeDeactivateAuditEntriesAsync(membershipTypeId));
            Assert.Equal(
                idempotencyCountBefore + 1,
                await _app.CountDeactivateMembershipTypeIdempotencyKeysAsync(membershipTypeId));
            Assert.Equal(
                "Retired from future sales.",
                await _app.ReadLatestMembershipTypeDeactivateReasonAsync(membershipTypeId));
            Assert.NotEqual(
                otherKeyBeforeRedirect,
                await ReadIdempotencyKeyAsync(
                    page.Locator(DeactivateFormSelector(alreadyInactiveId))));

            var alreadyInactiveAuditCount =
                await _app.CountMembershipTypeDeactivateAuditEntriesAsync(alreadyInactiveId);
            var alreadyInactiveIdempotencyCount =
                await _app.CountDeactivateMembershipTypeIdempotencyKeysAsync(alreadyInactiveId);
            var alreadyInactiveForm = await OpenDeactivateFormAsync(page, alreadyInactiveId);
            var concurrentUpdatedAt = await _app.DeactivateMembershipTypeForAlreadyInactiveTestAsync(
                alreadyInactiveId);
            var expectedUpdatedAt = new DateTimeOffset(
                DateTime.SpecifyKind(concurrentUpdatedAt, DateTimeKind.Utc));
            await alreadyInactiveForm.Locator("input[name='form.ExpectedUpdatedAt']").EvaluateAsync(
                "(element, value) => element.value = value",
                expectedUpdatedAt.ToString("O"));
            await alreadyInactiveForm.GetByLabel("Reason for deactivation", new() { Exact = true })
                .FillAsync("Concurrent lifecycle attempt.");
            await alreadyInactiveForm.GetByRole(
                    AriaRole.Button,
                    new() { Name = "Deactivate" })
                .ClickAsync();

            await ExpectVisibleAsync(
                page.GetByText(
                    "This membership type is already inactive. The canonical catalog was refreshed.",
                    new() { Exact = true }),
                viewportName,
                "already-inactive guidance");
            var concurrentlyDeactivatedRow = FindCatalogRow(page, alreadyInactiveId);
            await ExpectVisibleAsync(
                concurrentlyDeactivatedRow.GetByText("Inactive", new() { Exact = true }),
                viewportName,
                "concurrent inactive state");
            Assert.Equal(
                0,
                await concurrentlyDeactivatedRow.Locator(".membership-type-deactivate-panel").CountAsync());
            Assert.Equal(
                alreadyInactiveAuditCount,
                await _app.CountMembershipTypeDeactivateAuditEntriesAsync(alreadyInactiveId));
            Assert.Equal(
                alreadyInactiveIdempotencyCount,
                await _app.CountDeactivateMembershipTypeIdempotencyKeysAsync(alreadyInactiveId));

            var fitsViewport = await page.EvaluateAsync<bool>(
                "() => document.documentElement.scrollWidth <= window.innerWidth + 1");
            Assert.True(
                fitsViewport,
                $"{viewportName} deactivation workflow should not require horizontal scrolling.");
        }
        finally
        {
            await context.CloseAsync();
        }
    }

    private static async Task<ILocator> OpenDeactivateFormAsync(
        IPage page,
        Guid membershipTypeId)
    {
        var row = FindCatalogRow(page, membershipTypeId);
        var details = row.Locator(".membership-type-deactivate-panel");
        await details.Locator("summary").ClickAsync();
        var form = details.Locator(DeactivateFormSelector(membershipTypeId));
        await form.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });
        return form;
    }

    private static ILocator FindCatalogRow(IPage page, Guid membershipTypeId)
    {
        return page.Locator(
            $".membership-type-row[data-membership-type-id='{membershipTypeId}']");
    }

    private static string DeactivateFormSelector(Guid membershipTypeId)
    {
        return $"#deactivate-membership-type-form-{membershipTypeId:N}";
    }

    private static async Task<string> ReadIdempotencyKeyAsync(ILocator form)
    {
        var value = await form.Locator("input[name='form.IdempotencyKey']").GetAttributeAsync("value");
        Assert.True(
            Guid.TryParseExact(value, "N", out _),
            "The deactivation form should carry a valid idempotency key.");
        return value!;
    }

    private static async Task<string> ReadExpectedUpdatedAtAsync(ILocator form)
    {
        var value = await form.Locator("input[name='form.ExpectedUpdatedAt']").GetAttributeAsync("value");
        Assert.True(
            DateTimeOffset.TryParse(value, out _),
            "The deactivation form should carry a valid expected updated_at value.");
        return value!;
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
