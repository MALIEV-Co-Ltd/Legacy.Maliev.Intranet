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
        Assert.Equal(1, await page.Locator("[data-mud-type=\"MudLayout\"]").CountAsync());
        Assert.Equal(1, await page.Locator("[data-mud-type=\"MudMainContent\"]").CountAsync());

        var hoverButton = page.GetByTestId("button-hover");
        var hoverBefore = await hoverButton.EvaluateAsync<string>(
            "element => `${getComputedStyle(element).backgroundColor}|${getComputedStyle(element).borderColor}`");
        await hoverButton.HoverAsync();
        var hoverAfter = await hoverButton.EvaluateAsync<string>(
            "element => `${getComputedStyle(element).backgroundColor}|${getComputedStyle(element).borderColor}`");
        Assert.NotEqual(hoverBefore, hoverAfter);

        await page.GetByTestId("button-default").FocusAsync();
        await page.Keyboard.PressAsync("Tab");
        Assert.NotEqual("none", await page.GetByTestId("button-small").EvaluateAsync<string>(
            "element => getComputedStyle(element).boxShadow"));

        await page.GetByLabel("Invalid", new() { Exact = true }).FocusAsync();
        var invalidInput = page.GetByLabel("Invalid", new() { Exact = true });
        Assert.NotEqual("none", await invalidInput.EvaluateAsync<string>("element => getComputedStyle(element.closest('.mud-input-control')).boxShadow"));
        Assert.NotEqual("rgba(0, 0, 0, 0)", await invalidInput.EvaluateAsync<string>("element => getComputedStyle(element.closest('.mud-input-control')).borderColor"));

        var disabled = page.GetByTestId("button-disabled");
        Assert.False(await disabled.IsEnabledAsync());
        Assert.Equal("0", await page.GetByTestId("disabled-callback-count").InnerTextAsync());
        await disabled.ClickAsync(new() { Force = true });
        Assert.Equal("0", await page.GetByTestId("disabled-callback-count").InnerTextAsync());

        var approved = page.GetByRole(AriaRole.Checkbox, new() { Name = "Approved", Exact = true });
        Assert.True(await approved.IsCheckedAsync());
        await approved.ClickAsync();
        Assert.False(await approved.IsCheckedAsync());

        var material = page.GetByRole(AriaRole.Combobox, new() { Name = "Material", Exact = true }).Last;
        await material.ClickAsync();
        await page.GetByRole(AriaRole.Option, new() { Name = "Aluminium", Exact = true }).First.ClickAsync();
        Assert.Equal("Aluminium", await material.InnerTextAsync());

        Assert.True(await page.Locator(".mud-input-error").CountAsync() >= 1);
        Assert.Equal(1, await page.Locator(".mud-tab.mud-tab-active").CountAsync());
        Assert.Equal(1, await page.Locator(".mud-table-row-selected").CountAsync());
        var expansion = page.GetByTestId("material-readiness");
        var expansionContent = page.GetByText("Inspection data is retained in the expanded content.", new() { Exact = true });
        Assert.True(await expansionContent.IsVisibleAsync());
        var expandedTransform = await expansion.Locator(".mud-expand-panel-icon").EvaluateAsync<string>("element => getComputedStyle(element).transform");
        await expansion.Locator(".mud-expand-panel-header").ClickAsync();
        await page.WaitForTimeoutAsync(400);
        Assert.DoesNotContain("mud-panel-expanded", await expansion.GetAttributeAsync("class") ?? string.Empty, StringComparison.Ordinal);
        var collapsedTransform = await expansion.Locator(".mud-expand-panel-icon").EvaluateAsync<string>("element => getComputedStyle(element).transform");
        Assert.NotEqual(expandedTransform, collapsedTransform);
        await expansion.Locator(".mud-expand-panel-header").ClickAsync();
        await page.WaitForTimeoutAsync(400);
        Assert.Contains("mud-panel-expanded", await expansion.GetAttributeAsync("class") ?? string.Empty, StringComparison.Ordinal);
        Assert.True(await expansionContent.IsVisibleAsync());

        var expectedChartColors = await page.EvaluateAsync<string[]>("""
            () => {
                const root = document.querySelector('[data-shadcn-scope]');
                return [1, 2, 3, 4, 5].map(index => {
                    const probe = document.createElement('span');
                    probe.style.color = `var(--shadcn-chart-${index})`;
                    root.append(probe);
                    const color = getComputedStyle(probe).color;
                    probe.remove();
                    return color;
                });
            }
            """);
        var renderedChartColors = await page.EvaluateAsync<string[]>("""
            () => Array.from(document.querySelectorAll('.mud-chart svg path, .mud-chart svg rect, .mud-chart svg circle, .mud-chart svg polygon'))
                .flatMap(element => [getComputedStyle(element).fill, getComputedStyle(element).stroke])
            """);
        Assert.Equal(5, expectedChartColors.Distinct().Count());
        Assert.All(expectedChartColors, color => Assert.Contains(color, renderedChartColors));

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
        Assert.Equal(390, await page.EvaluateAsync<int>("() => document.documentElement.scrollWidth"));
        Assert.True(await page.EvaluateAsync<bool>("""
            () => {
                const table = document.querySelector('[aria-label="Responsive selected inventory table"]');
                const rows = Array.from(table.querySelectorAll('tbody tr'));
                const viewport = document.documentElement.clientWidth;
                return table.scrollWidth <= table.clientWidth
                    && rows.length > 0
                    && rows.every(row => {
                        const rect = row.getBoundingClientRect();
                        return getComputedStyle(row).display !== 'table-row'
                            && rect.left >= 0
                            && rect.right <= viewport
                            && row.scrollWidth <= row.clientWidth;
                    });
            }
            """));

        var touchTargetSizes = await page.EvaluateAsync<double[]>("""
            () => [
                document.querySelector('[data-testid="button-default"]'),
                document.querySelector('[data-testid="open-dialog"]'),
                document.querySelector('[data-testid="open-select"] .mud-input-control'),
                document.querySelector('[data-mud-type="MudCheckBox"]')
            ].filter(Boolean).flatMap(element => {
                const rect = element.getBoundingClientRect();
                return [rect.width, rect.height];
            })
            """);
        Assert.True(touchTargetSizes.Length >= 6);
        Assert.All(touchTargetSizes, size => Assert.True(size >= 44d, $"Expected a touch target of at least 44px but found {size}px."));

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

        await page.GetByTestId("theme-toggle").ClickAsync();
        await page.GetByTestId("direction-toggle").ClickAsync();
        var root = page.Locator("[data-shadcn-scope]");
        await Assertions.Expect(root).ToHaveAttributeAsync("data-shadcn-theme", "dark");
        await Assertions.Expect(root).ToHaveAttributeAsync("dir", "rtl");

        var trigger = page.GetByTestId("open-dialog");
        await trigger.ClickAsync();
        var dialog = page.Locator(".mud-dialog");
        await dialog.WaitForAsync();
        Assert.NotEqual("rgba(0, 0, 0, 0)", await dialog.EvaluateAsync<string>(
            "element => getComputedStyle(element).backgroundColor"));
        Assert.NotEqual("rgba(0, 0, 0, 0)", await dialog.EvaluateAsync<string>(
            "element => getComputedStyle(element).color"));
        Assert.NotEqual("0px", await dialog.EvaluateAsync<string>("element => getComputedStyle(element).borderRadius"));
        await AssertOverlayUsesDarkRtlContextAsync(dialog);
        await page.Keyboard.PressAsync("Escape");
        await Assertions.Expect(dialog).ToBeHiddenAsync();
        Assert.True(await trigger.EvaluateAsync<bool>("element => document.activeElement === element"));

        await page.GetByRole(AriaRole.Button, new() { Name = "Open menu" }).ClickAsync();
        var menu = page.Locator(".mud-popover-open").Last;
        await menu.WaitForAsync();
        Assert.NotEqual("rgba(0, 0, 0, 0)", await menu.EvaluateAsync<string>(
            "element => getComputedStyle(element).backgroundColor"));
        Assert.NotEqual("rgba(0, 0, 0, 0)", await menu.EvaluateAsync<string>(
            "element => getComputedStyle(element).color"));
        Assert.NotEqual("0px", await menu.EvaluateAsync<string>("element => getComputedStyle(element).borderRadius"));
        await AssertOverlayUsesDarkRtlContextAsync(menu);
        await page.Keyboard.PressAsync("Escape");

        var selectTrigger = page.GetByRole(AriaRole.Combobox, new() { Name = "Portal select", Exact = true }).Last;
        await selectTrigger.ClickAsync();
        var selectPopover = page.Locator(".mud-popover-open").Last;
        await selectPopover.WaitForAsync();
        Assert.NotEqual("rgba(0, 0, 0, 0)", await selectPopover.EvaluateAsync<string>(
            "element => getComputedStyle(element).backgroundColor"));
        Assert.NotEqual("0px", await selectPopover.EvaluateAsync<string>("element => getComputedStyle(element).borderRadius"));
        await AssertOverlayUsesDarkRtlContextAsync(selectPopover);
        await page.Keyboard.PressAsync("Escape");
        Assert.True(await selectTrigger.EvaluateAsync<bool>("element => document.activeElement === element"));

        var dateTrigger = page.GetByLabel("Open date picker", new() { Exact = true });
        await dateTrigger.ClickAsync();
        var datePopover = page.Locator(".mud-picker-open").Last;
        await datePopover.WaitForAsync();
        Assert.NotEqual("rgba(0, 0, 0, 0)", await datePopover.EvaluateAsync<string>(
            "element => getComputedStyle(element).backgroundColor"));
        Assert.NotEqual("0px", await datePopover.EvaluateAsync<string>("element => getComputedStyle(element).borderRadius"));
        await AssertOverlayUsesDarkRtlContextAsync(datePopover);
        await page.Keyboard.PressAsync("Escape");
        Assert.True(await dateTrigger.EvaluateAsync<bool>("element => document.activeElement === element"));

        await page.GetByTestId("open-snackbar").ClickAsync();
        await page.Locator(".mud-snackbar").WaitForAsync();
        Assert.NotEqual("rgba(0, 0, 0, 0)", await page.Locator(".mud-snackbar")
            .EvaluateAsync<string>("element => getComputedStyle(element).backgroundColor"));
        Assert.NotEqual("rgba(0, 0, 0, 0)", await page.Locator(".mud-snackbar")
            .EvaluateAsync<string>("element => getComputedStyle(element).color"));
        Assert.NotEqual("0px", await page.Locator(".mud-snackbar")
            .EvaluateAsync<string>("element => getComputedStyle(element).borderRadius"));
        await AssertOverlayUsesDarkRtlContextAsync(page.Locator(".mud-snackbar"));
        Assert.Empty(errors);
    }

    private static async Task AssertOverlayUsesDarkRtlContextAsync(ILocator overlay)
    {
        Assert.True(await overlay.EvaluateAsync<bool>("""
            element => {
                const scope = element.closest('[data-shadcn-theme][dir]');
                return scope?.getAttribute('data-shadcn-theme') === 'dark'
                    && scope.getAttribute('dir') === 'rtl';
            }
            """));
    }
}
