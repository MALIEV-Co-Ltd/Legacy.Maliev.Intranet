using Maliev.ShadcnBlazor.BrowserTests.Infrastructure;
using Microsoft.Playwright;

namespace Maliev.ShadcnBlazor.BrowserTests;

[Collection(BrowserCollection.Name)]
public sealed class MudInventoryBrowserTests(ShowcaseServerFixture server, PlaywrightFixture playwright)
{
    [Fact]
    public async Task InventoryUsesVegaGeometryAndHealthyInteractions()
    {
        var errors = new List<string>();
        await using var context = await playwright.Browser.NewContextAsync(new()
        {
            ViewportSize = new() { Width = 1440, Height = 1000 },
            DeviceScaleFactor = 1,
            ReducedMotion = ReducedMotion.NoPreference
        });
        var page = await context.NewPageAsync();
        page.Console += (_, message) => { if (message.Type == "error") errors.Add(message.Text); };
        page.PageError += (_, error) => errors.Add(error);
        await page.GotoAsync(new Uri(server.BaseUri, "/components/mud-inventory").ToString());
        await page.GetByTestId("mud-inventory-fixture").WaitForAsync();

        Assert.Equal("36px", await page.GetByTestId("button-default")
            .EvaluateAsync<string>("element => getComputedStyle(element).height"));
        Assert.Equal("14px", await page.GetByTestId("button-default")
            .EvaluateAsync<string>("element => getComputedStyle(element).fontSize"));
        Assert.True(await page.Locator("[data-mud-type]").CountAsync() >= 41);

        var defaultButton = page.GetByTestId("button-default");
        await defaultButton.HoverAsync();
        Assert.NotEqual("rgba(0, 0, 0, 0)", await defaultButton.EvaluateAsync<string>(
            "element => getComputedStyle(element).backgroundColor"));

        var disabled = page.GetByTestId("button-disabled");
        Assert.False(await disabled.IsEnabledAsync());
        Assert.Equal("0", await page.GetByTestId("disabled-callback-count").InnerTextAsync());
        await disabled.ClickAsync(new() { Force = true });
        Assert.Equal("0", await page.GetByTestId("disabled-callback-count").InnerTextAsync());

        Assert.True(await page.Locator(".mud-input-error").CountAsync() >= 1);
        Assert.Equal(1, await page.Locator(".mud-tab.mud-tab-active").CountAsync());
        Assert.Equal(1, await page.Locator(".mud-table-row-selected").CountAsync());
        Assert.Equal(1, await page.Locator(".mud-expand-panel.mud-panel-expanded").CountAsync());
        Assert.NotEqual("none", await page.Locator(".mud-expand-panel.mud-panel-expanded .mud-expand-panel-header .mud-expand-panel-icon")
            .EvaluateAsync<string>("element => getComputedStyle(element).transform"));

        var root = page.Locator("[data-shadcn-scope]");
        Assert.Equal(5, await root.EvaluateAsync<int>("element => [1, 2, 3, 4, 5].filter(index => getComputedStyle(element).getPropertyValue(`--shadcn-chart-${index}`).trim()).length"));

        var evidence = Path.Combine(Path.GetTempPath(), $"maliev-mud-inventory-desktop-{Guid.NewGuid():N}.png");
        await page.ScreenshotAsync(new() { Path = evidence, FullPage = true });
        Assert.True(new FileInfo(evidence).Length > 0);
        Assert.Empty(errors);
    }

    [Fact]
    public async Task InventoryExposesCompactDesktopControlGeometry()
    {
        await using var context = await playwright.Browser.NewContextAsync(new()
        {
            ViewportSize = new() { Width = 1440, Height = 1000 },
            DeviceScaleFactor = 1
        });
        var page = await context.NewPageAsync();
        await page.GotoAsync(new Uri(server.BaseUri, "/components/mud-inventory").ToString());
        await page.GetByTestId("mud-inventory-fixture").WaitForAsync();

        Assert.Equal("32px", await page.GetByTestId("button-small")
            .EvaluateAsync<string>("element => getComputedStyle(element).height"));
    }

    [Fact]
    public async Task InventoryProvidesCoarsePointerMobileHitAreasAndNoPageOverflow()
    {
        await using var context = await playwright.Browser.NewContextAsync(new()
        {
            ViewportSize = new() { Width = 390, Height = 844 },
            DeviceScaleFactor = 1,
            IsMobile = true,
            HasTouch = true
        });
        var page = await context.NewPageAsync();
        await page.GotoAsync(new Uri(server.BaseUri, "/components/mud-inventory").ToString());
        await page.GetByTestId("mud-inventory-fixture").WaitForAsync();

        Assert.Equal("44px", await page.GetByTestId("button-default")
            .EvaluateAsync<string>("element => getComputedStyle(element).minHeight"));
        Assert.True(await page.EvaluateAsync<bool>("() => document.documentElement.scrollWidth <= document.documentElement.clientWidth"));

        var evidence = Path.Combine(Path.GetTempPath(), $"maliev-mud-inventory-mobile-{Guid.NewGuid():N}.png");
        await page.ScreenshotAsync(new() { Path = evidence, FullPage = true });
        Assert.True(new FileInfo(evidence).Length > 0);
    }

    [Fact]
    public async Task InventoryPropagatesDarkThemeAndRtlToTheFixtureRoot()
    {
        await using var context = await playwright.Browser.NewContextAsync(new()
        {
            ViewportSize = new() { Width = 1440, Height = 1000 },
            DeviceScaleFactor = 1,
            ColorScheme = ColorScheme.Dark
        });
        var page = await context.NewPageAsync();
        await page.GotoAsync(new Uri(server.BaseUri, "/components/mud-inventory").ToString());
        await page.GetByTestId("mud-inventory-fixture").WaitForAsync();

        var root = page.Locator("[data-shadcn-scope]");
        await page.GetByTestId("theme-toggle").ClickAsync();
        await Assertions.Expect(root).ToHaveAttributeAsync("data-shadcn-theme", "dark");
        Assert.NotEqual("rgba(0, 0, 0, 0)", await page.GetByTestId("mud-data-feedback")
            .EvaluateAsync<string>("element => getComputedStyle(element).backgroundColor"));

        await page.GetByTestId("direction-toggle").ClickAsync();
        await Assertions.Expect(root).ToHaveAttributeAsync("dir", "rtl");
        Assert.Equal("rtl", await root.GetAttributeAsync("dir"));
    }

    [Fact]
    public async Task InventorySuppressesMotionWhenReducedMotionIsRequested()
    {
        await using var context = await playwright.Browser.NewContextAsync(new()
        {
            ViewportSize = new() { Width = 1440, Height = 1000 },
            DeviceScaleFactor = 1,
            ReducedMotion = ReducedMotion.Reduce
        });
        var page = await context.NewPageAsync();
        await page.GotoAsync(new Uri(server.BaseUri, "/components/mud-inventory").ToString());
        await page.GetByTestId("mud-inventory-fixture").WaitForAsync();

        Assert.Equal(0.00001d, await page.Locator(".mud-progress-linear").Nth(1)
            .EvaluateAsync<double>("element => parseFloat(getComputedStyle(element).animationDuration)"));
        Assert.Equal(0.00001d, await page.Locator(".mud-skeleton-wave").First
            .EvaluateAsync<double>("element => parseFloat(getComputedStyle(element).animationDuration)"));
    }

    [Fact]
    public async Task InventoryPortalSurfacesAreVisibleSemanticAndRestoreFocus()
    {
        var errors = new List<string>();
        await using var context = await playwright.Browser.NewContextAsync(new()
        {
            ViewportSize = new() { Width = 1440, Height = 1000 },
            DeviceScaleFactor = 1
        });
        var page = await context.NewPageAsync();
        page.Console += (_, message) => { if (message.Type == "error") errors.Add(message.Text); };
        page.PageError += (_, error) => errors.Add(error);
        await page.GotoAsync(new Uri(server.BaseUri, "/components/mud-inventory").ToString());
        await page.GetByTestId("mud-inventory-fixture").WaitForAsync();

        var trigger = page.GetByTestId("open-dialog");
        await trigger.ClickAsync();
        var dialog = page.GetByRole(AriaRole.Dialog);
        await dialog.WaitForAsync();
        Assert.NotEqual("rgba(0, 0, 0, 0)", await dialog.EvaluateAsync<string>(
            "element => getComputedStyle(element).backgroundColor"));
        Assert.NotEqual("0px", await dialog.EvaluateAsync<string>("element => getComputedStyle(element).borderRadius"));
        await page.GetByRole(AriaRole.Button, new() { Name = "Close" }).ClickAsync(new() { Force = true });
        Assert.True(await trigger.EvaluateAsync<bool>("element => document.activeElement === element"));

        await page.GetByRole(AriaRole.Button, new() { Name = "Open menu" }).ClickAsync();
        var menu = page.Locator(".mud-popover-open").Last;
        await menu.WaitForAsync();
        Assert.NotEqual("rgba(0, 0, 0, 0)", await menu.EvaluateAsync<string>(
            "element => getComputedStyle(element).backgroundColor"));

        await page.GetByTestId("open-snackbar").ClickAsync();
        await page.Locator(".mud-snackbar").WaitForAsync();
        Assert.NotEqual("rgba(0, 0, 0, 0)", await page.Locator(".mud-snackbar")
            .EvaluateAsync<string>("element => getComputedStyle(element).backgroundColor"));
        Assert.Empty(errors);
    }
}
