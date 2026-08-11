using Microsoft.Playwright;

namespace BodyLife.Crm.Ui.SmokeTests;

internal static class AppNavigationTestHelper
{
    public static async Task<ILocator> OpenDrawerAsync(IPage page)
    {
        var drawer = page.Locator("#app-navigation-drawer");
        var drawerToggle = page.Locator("[data-drawer-toggle]");
        if (await drawerToggle.IsVisibleAsync()
            && await drawerToggle.GetAttributeAsync("aria-expanded") != "true")
        {
            await drawerToggle.ClickAsync();
        }

        await drawer.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });
        return drawer;
    }

    public static async Task<ILocator> OpenOwnerToolsAsync(IPage page)
    {
        var drawer = await OpenDrawerAsync(page);
        var ownerTools = drawer.Locator("details.owner-tools");
        if (await ownerTools.GetAttributeAsync("open") is null)
        {
            await ownerTools.Locator("summary").ClickAsync();
        }

        return drawer;
    }
}
