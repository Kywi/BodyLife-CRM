using Microsoft.Playwright;

namespace BodyLife.Crm.Ui.SmokeTests;

internal static class OperationStatusTestHelper
{
    public static ILocator Success(IPage page)
    {
        return page.Locator(
            "#global-operation-status.global-operation-status-success");
    }

    public static async Task CaptureViewportAsync(
        IPage page,
        string viewportName,
        string state)
    {
        var screenshotDirectory = Environment.GetEnvironmentVariable(
            "BODYLIFE_UI_SCREENSHOT_DIR");
        if (string.IsNullOrWhiteSpace(screenshotDirectory))
        {
            return;
        }

        Directory.CreateDirectory(screenshotDirectory);
        await page.EvaluateAsync("document.activeElement?.blur()");
        await page.ScreenshotAsync(new PageScreenshotOptions
        {
            FullPage = false,
            Path = Path.Combine(
                screenshotDirectory,
                $"{viewportName}-{state}.png"),
        });
    }
}
