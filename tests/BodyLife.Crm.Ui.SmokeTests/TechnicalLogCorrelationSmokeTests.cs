using System.Text.Json;
using BodyLife.Crm.Infrastructure.Persistence.UsersRoles;
using BodyLife.Crm.Web.Operations;
using Microsoft.Playwright;

namespace BodyLife.Crm.Ui.SmokeTests;

[Collection("Owner UI smoke")]
public sealed class TechnicalLogCorrelationSmokeTests :
    IClassFixture<ReceptionAppFixture>,
    IAsyncLifetime
{
    private readonly ReceptionAppFixture _app;
    private IPlaywright? _playwright;
    private IBrowser? _browser;

    public TechnicalLogCorrelationSmokeTests(ReceptionAppFixture app)
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
    public async Task AuditedCommandAndTechnicalRequestLogShareCorrelationId()
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
            await LoginAsync(page);
            await page.GetByRole(AriaRole.Link, new() { Name = "Staff accounts" })
                .ClickAsync();
            await page.WaitForURLAsync("**/Owner/StaffAccounts");

            var suffix = Guid.NewGuid().ToString("N")[..8];
            var displayName = $"Private correlation admin {suffix}";
            var createForm = page.Locator("#create-account-form");
            await createForm.GetByLabel("Account type").SelectOptionAsync("NamedAdmin");
            await createForm.GetByLabel("Display name").FillAsync(displayName);
            await createForm.GetByRole(AriaRole.Button, new() { Name = "Add account" })
                .ClickAsync();
            await page.GetByText("Staff account created.", new() { Exact = false })
                .WaitForAsync();

            var requestCorrelationId = $"support-correlation-{Guid.NewGuid():N}";
            var privateReason = $"Private support reason {suffix}";
            await page.SetExtraHTTPHeadersAsync(
                new Dictionary<string, string>
                {
                    [RequestCorrelationMiddleware.HeaderName] = requestCorrelationId,
                });

            var accountRow = page.Locator(".staff-account-row")
                .Filter(new LocatorFilterOptions { HasText = displayName });
            await accountRow.GetByLabel("Reason for deactivation", new() { Exact = true })
                .FillAsync(privateReason);
            page.Dialog += (_, dialog) => _ = dialog.AcceptAsync();
            await accountRow.GetByRole(AriaRole.Button, new() { Name = "Deactivate" })
                .ClickAsync();
            await page.GetByText("Staff account deactivated.", new() { Exact = false })
                .WaitForAsync();

            var audit = Assert.IsType<AuditCorrelationSmokeSnapshot>(
                await _app.ReadAuditByCorrelationAsync(
                    StaffAccountAuditActions.Deactivated,
                    requestCorrelationId));
            Assert.Equal(StaffAccountAuditActions.Deactivated, audit.ActionType);
            Assert.Equal(StaffAccountAuditActions.EntityType, audit.EntityType);
            Assert.NotEqual(Guid.Empty, audit.EntityId);
            Assert.Equal(requestCorrelationId, audit.RequestCorrelationId);
            Assert.Equal(privateReason, audit.Reason);
            Assert.Null(audit.Comment);

            using (var before = JsonDocument.Parse(audit.BeforeSummaryJson))
            using (var after = JsonDocument.Parse(audit.AfterSummaryJson))
            {
                Assert.True(before.RootElement.GetProperty("isActive").GetBoolean());
                Assert.False(after.RootElement.GetProperty("isActive").GetBoolean());
            }

            var requestLog = await _app.WaitForRequestOutcomeLogAsync(
                requestCorrelationId,
                "POST",
                "StaffAccounts");
            Assert.Equal(requestCorrelationId, requestLog.RequestCorrelationId);
            Assert.Equal("POST", requestLog.Method);
            Assert.Contains("StaffAccounts", requestLog.RouteOrCommand, StringComparison.Ordinal);
            Assert.InRange(requestLog.StatusCode, 200, 399);
            Assert.Equal("success", requestLog.Outcome);
            Assert.Equal("none", requestLog.ErrorClass);
            Assert.DoesNotContain(displayName, requestLog.RawJson, StringComparison.Ordinal);
            Assert.DoesNotContain(privateReason, requestLog.RawJson, StringComparison.Ordinal);
            Assert.DoesNotContain(_app.Password, requestLog.RawJson, StringComparison.Ordinal);

            using var requestLogDocument = JsonDocument.Parse(requestLog.RawJson);
            var requestLogState = requestLogDocument.RootElement.GetProperty("State");
            Assert.False(requestLogState.TryGetProperty("display_name", out _));
            Assert.False(requestLogState.TryGetProperty("reason", out _));
            Assert.False(requestLogState.TryGetProperty("comment", out _));
            Assert.False(requestLogState.TryGetProperty("password", out _));
            Assert.False(requestLogState.TryGetProperty("token", out _));
        }
        finally
        {
            await context.CloseAsync();
        }
    }

    private async Task LoginAsync(IPage page)
    {
        await page.GotoAsync(_app.BaseAddress.ToString(), new PageGotoOptions
        {
            WaitUntil = WaitUntilState.NetworkIdle,
        });
        await page.GetByRole(AriaRole.Textbox, new() { Name = "Login" })
            .FillAsync(_app.LoginName);
        await page.GetByLabel("Password", new() { Exact = true })
            .FillAsync(_app.Password);
        await page.GetByLabel("Device", new() { Exact = true })
            .FillAsync("support correlation tablet");
        await page.GetByRole(AriaRole.Button, new() { Name = "Login" }).ClickAsync();
        await page.WaitForURLAsync("**/");
    }
}
