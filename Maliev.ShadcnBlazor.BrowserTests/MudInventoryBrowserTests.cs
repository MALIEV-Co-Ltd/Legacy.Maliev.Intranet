using Maliev.ShadcnBlazor.BrowserTests.Infrastructure;
using Microsoft.Playwright;
using System.Text.Json;

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

        var emailInput = page.GetByLabel("Email", new() { Exact = true });
        await emailInput.FocusAsync();
        Assert.Equal("none", await emailInput.EvaluateAsync<string>(
            "element => getComputedStyle(element.closest('.mud-input-control')).boxShadow"));
        Assert.NotEqual("none", await emailInput.EvaluateAsync<string>(
            "element => getComputedStyle(element.closest('.mud-input')).boxShadow"));
        Assert.Equal("1px", await emailInput.EvaluateAsync<string>(
            "element => getComputedStyle(element.closest('.mud-input-control').querySelector('.mud-input-outlined-border')).borderWidth"));
        Assert.Equal("36px", await emailInput.EvaluateAsync<string>(
            "element => getComputedStyle(element.closest('.mud-input')).height"));
        Assert.Equal("36px", await emailInput.EvaluateAsync<string>(
            "element => getComputedStyle(element).height"));
        Assert.Equal(36, await emailInput.EvaluateAsync<double>(
            "element => element.getBoundingClientRect().height"));
        Assert.Equal("none", await emailInput.EvaluateAsync<string>("""
            element => getComputedStyle(
                element.closest('.mud-input-control').querySelector('.mud-input-label'))
                .transform
            """));
        Assert.Equal("0px", await emailInput.EvaluateAsync<string>("""
            element => getComputedStyle(
                element.closest('.mud-input-control').querySelector('.mud-input-label'))
                .paddingInlineStart
            """));
        Assert.True(await emailInput.EvaluateAsync<bool>("""
            element => {
                const control = element.closest('.mud-input-control');
                const input = element.closest('.mud-input').getBoundingClientRect();
                const label = control.querySelector('.mud-input-label').getBoundingClientRect();
                return label.bottom <= input.top - 6;
            }
            """));
        Assert.Equal(0, await emailInput.EvaluateAsync<double>("""
            element => element.closest('.mud-input-control')
                .querySelector('.mud-input-outlined-border > legend')
                .getBoundingClientRect().width
            """));

        await page.GetByLabel("Invalid", new() { Exact = true }).FocusAsync();
        var invalidInput = page.GetByLabel("Invalid", new() { Exact = true });
        var invalidControl = page.Locator(".mud-input-control.mud-input-error");
        var destructive = await NormalizeCssColorAsync(page, "var(--shadcn-destructive)");
        var destructiveFocusRing = await NormalizeCssColorAsync(page, "color-mix(in oklab, var(--shadcn-destructive) 20%, transparent)");
        Assert.Equal(destructive, await invalidInput.EvaluateAsync<string>("""
            element => {
                const control = element.closest('.mud-input-control');
                const border = control.querySelector('.mud-input-outlined-border');
                return window.__normalizeShadcnColor(getComputedStyle(border).borderColor);
            }
            """));
        Assert.Equal("none", await invalidInput.EvaluateAsync<string>(
            "element => getComputedStyle(element.closest('.mud-input-control')).boxShadow"));
        Assert.Equal(destructiveFocusRing, await invalidInput.EvaluateAsync<string>("""
            element => {
                const shadow = getComputedStyle(element.closest('.mud-input')).boxShadow;
                const color = shadow.match(/(?:rgba?|oklab|color)\([^)]*\)/g).at(-1);
                return window.__normalizeShadcnColor(color);
            }
            """));

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
        await Assertions.Expect(material).ToHaveTextAsync("Aluminium");

        Assert.True(await page.Locator(".mud-input-error").CountAsync() >= 1);
        var overviewTab = page.GetByRole(AriaRole.Tab, new() { Name = "Overview", Exact = true });
        var historyTab = page.GetByRole(AriaRole.Tab, new() { Name = "History", Exact = true });
        var historyBefore = await historyTab.EvaluateAsync<string>("element => `${getComputedStyle(element).backgroundColor}|${getComputedStyle(element).color}`");
        await historyTab.ClickAsync();
        await Assertions.Expect(historyTab).ToHaveAttributeAsync("aria-selected", "true");
        Assert.Equal("false", await overviewTab.GetAttributeAsync("aria-selected"));
        var historyAfter = await historyTab.EvaluateAsync<string>("element => `${getComputedStyle(element).backgroundColor}|${getComputedStyle(element).color}`");
        Assert.NotEqual(historyBefore, historyAfter);

        var clampRow = page.GetByText("Clamp plate", new() { Exact = true }).Locator("xpath=ancestor::tr");
        var rowBefore = await clampRow.EvaluateAsync<string>("element => `${getComputedStyle(element).backgroundColor}|${getComputedStyle(element).color}`");
        await clampRow.ClickAsync();
        Assert.Contains("mud-table-row-selected", await clampRow.GetAttributeAsync("class") ?? string.Empty, StringComparison.Ordinal);
        var rowAfter = await clampRow.EvaluateAsync<string>("element => `${getComputedStyle(element).backgroundColor}|${getComputedStyle(element).color}`");
        Assert.NotEqual(rowBefore, rowAfter);
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
    public async Task InventoryNormalizesEveryFormVariantToShadcnFieldGeometry()
    {
        await using var context = await playwright.Browser.NewContextAsync(new()
        {
            ViewportSize = new() { Width = 1440, Height = 1000 },
            DeviceScaleFactor = 1
        });
        var page = await context.NewPageAsync();
        await page.GotoAsync(new Uri(server.BaseUri, "/components/mud-inventory").ToString());
        await page.GetByTestId("mud-inventory-fixture").WaitForAsync();

        foreach (var label in new[] { "Email", "Quantity", "Material", "Delivery date" })
        {
            var control = page.Locator(".mud-input-control").Filter(new() { HasText = label }).First;
            var geometry = await control.EvaluateAsync<JsonElement>("""
                element => {
                    const label = element.querySelector('.mud-input-label');
                    const input = element.querySelector('.mud-input');
                    const labelRect = label.getBoundingClientRect();
                    const inputRect = input.getBoundingClientRect();
                    const style = getComputedStyle(input);
                    const visibleBorder = input.querySelector('.mud-input-outlined-border') ?? input;
                    const borderStyle = getComputedStyle(visibleBorder);
                    const before = getComputedStyle(input, '::before');
                    const after = getComputedStyle(input, '::after');
                    return {
                        labelPosition: getComputedStyle(label).position,
                        labelTransform: getComputedStyle(label).transform,
                        labelBottom: labelRect.bottom,
                        inputTop: inputRect.top,
                        inputHeight: inputRect.height,
                        borderTopWidth: borderStyle.borderTopWidth,
                        borderBottomWidth: borderStyle.borderBottomWidth,
                        borderRadius: style.borderRadius,
                        beforeBorderBottomWidth: before.borderBottomWidth,
                        afterBorderBottomWidth: after.borderBottomWidth
                    };
                }
                """);

            Assert.Equal("static", geometry.GetProperty("labelPosition").GetString());
            Assert.Equal("none", geometry.GetProperty("labelTransform").GetString());
            Assert.True(
                geometry.GetProperty("labelBottom").GetDouble() <= geometry.GetProperty("inputTop").GetDouble() - 6d,
                $"Expected the {label} label to sit above its control without overlap.");
            Assert.Equal(36d, geometry.GetProperty("inputHeight").GetDouble(), precision: 1);
            Assert.Equal("1px", geometry.GetProperty("borderTopWidth").GetString());
            Assert.Equal("1px", geometry.GetProperty("borderBottomWidth").GetString());
            Assert.NotEqual("0px", geometry.GetProperty("borderRadius").GetString());
            Assert.Equal("0px", geometry.GetProperty("beforeBorderBottomWidth").GetString());
            Assert.Equal("0px", geometry.GetProperty("afterBorderBottomWidth").GetString());
        }

        var dateInput = page.GetByLabel("Delivery date", new() { Exact = true });
        var dateAdornment = dateInput.Locator("xpath=ancestor::*[contains(@class,'mud-input')][1]")
            .Locator(".mud-input-adornment");
        Assert.True(await dateAdornment.EvaluateAsync<bool>("""
            element => {
                const input = element.closest('.mud-input').getBoundingClientRect();
                const adornment = element.getBoundingClientRect();
                return adornment.left >= input.left
                    && adornment.right <= input.right
                    && Math.abs((adornment.top + adornment.height / 2) - (input.top + input.height / 2)) <= 1;
            }
            """));
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
        Assert.True(await dialog.EvaluateAsync<bool>("element => element.contains(document.activeElement)"));
        await page.Keyboard.PressAsync("Tab");
        Assert.True(await dialog.EvaluateAsync<bool>("element => element.contains(document.activeElement)"));
        await page.Keyboard.PressAsync("Shift+Tab");
        Assert.True(await dialog.EvaluateAsync<bool>("element => element.contains(document.activeElement)"));
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
        Assert.Equal(await ResolveCssColorAsync(page, "var(--shadcn-popover-foreground)"), await selectPopover.EvaluateAsync<string>("element => getComputedStyle(element).color"));
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
        Assert.Equal(await ResolveCssColorAsync(page, "var(--shadcn-popover-foreground)"), await datePopover.EvaluateAsync<string>("element => getComputedStyle(element).color"));
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

    private static Task<string> ResolveCssColorAsync(IPage page, string cssValue) => page.EvaluateAsync<string>("""
        cssValue => {
            const probe = document.createElement('span');
            probe.style.color = cssValue;
            document.querySelector('[data-shadcn-scope]').append(probe);
            const color = getComputedStyle(probe).color;
            probe.remove();
            return color;
        }
        """, cssValue);

    private static async Task<string> NormalizeCssColorAsync(IPage page, string cssValue)
    {
        await page.EvaluateAsync("""
            () => {
                window.__normalizeShadcnColor ??= value => {
                    const canvas = document.createElement('canvas');
                    canvas.width = canvas.height = 1;
                    const context = canvas.getContext('2d');
                    context.clearRect(0, 0, 1, 1);
                    context.fillStyle = value;
                    context.fillRect(0, 0, 1, 1);
                    return Array.from(context.getImageData(0, 0, 1, 1).data).join(',');
                };
            }
            """);
        var resolved = await ResolveCssColorAsync(page, cssValue);
        return await page.EvaluateAsync<string>("value => window.__normalizeShadcnColor(value)", resolved);
    }

}
