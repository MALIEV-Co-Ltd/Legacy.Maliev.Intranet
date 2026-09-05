using System.Text.Json;
using Legacy.Maliev.Intranet.BrowserTests.Infrastructure;
using Microsoft.Playwright;

namespace Legacy.Maliev.Intranet.BrowserTests;

[Collection(CustomerBrowserCollection.Name)]
public sealed class OperationalShellBrowserTests(
    IntranetClientServerFixture server,
    PlaywrightFixture playwright)
{
    [Fact]
    public async Task DesktopShellKeepsNavigationScrollableWithoutHorizontalRailAndUsesOpaqueTopbar()
    {
        await using var context = await playwright.Browser.NewContextAsync(new()
        {
            ViewportSize = new() { Width = 1280, Height = 900 },
            ReducedMotion = ReducedMotion.Reduce,
        });
        var page = await context.NewPageAsync();
        await StubProductionBoundariesAsync(page);
        await page.GotoAsync(new Uri(server.BaseUri, "sales/orders").AbsoluteUri);

        var railGroups = page.Locator(".legacy-rail-groups");
        await railGroups.WaitForAsync();
        Assert.Equal("hidden", await railGroups.EvaluateAsync<string>("node => getComputedStyle(node).overflowX"));
        Assert.Contains(await railGroups.EvaluateAsync<string>("node => getComputedStyle(node).overflowY"), new[] { "auto", "scroll" });

        var topbar = page.Locator(".legacy-topbar");
        var topbarSurface = await topbar.EvaluateAsync<JsonElement>(
            "node => ({ background: getComputedStyle(node).backgroundColor, backdrop: getComputedStyle(node).backdropFilter })");
        Assert.DoesNotContain("rgba(0, 0, 0, 0)", topbarSurface.GetProperty("background").GetString(), StringComparison.Ordinal);
        Assert.Equal("none", topbarSurface.GetProperty("backdrop").GetString());

        var breadcrumbs = page.Locator("nav.page-breadcrumbs ol");
        await breadcrumbs.WaitForAsync();
        var breadcrumbBounds = await breadcrumbs.BoundingBoxAsync();
        Assert.NotNull(breadcrumbBounds);
        Assert.True(breadcrumbBounds!.X >= 16, $"Breadcrumb started at {breadcrumbBounds.X}px.");
        Assert.True(1280 - (breadcrumbBounds.X + breadcrumbBounds.Width) >= 16, $"Breadcrumb ended at {breadcrumbBounds.X + breadcrumbBounds.Width}px.");
    }

    [Fact]
    public async Task DesktopNavigationUsesCompactRowsWhileMobileDrawerKeepsTouchTargets()
    {
        await using var context = await playwright.Browser.NewContextAsync(new()
        {
            ViewportSize = new() { Width = 1280, Height = 900 },
            HasTouch = false,
            ReducedMotion = ReducedMotion.Reduce,
        });
        var page = await context.NewPageAsync();
        await StubProductionBoundariesAsync(page);
        await page.GotoAsync(new Uri(server.BaseUri, "sales/orders").AbsoluteUri);

        var desktopRail = page.Locator(".legacy-navigation-rail[data-mobile='false']");
        await desktopRail.WaitForAsync();
        Assert.True(await desktopRail.EvaluateAsync<bool>(
            "element => element.getBoundingClientRect().width <= 232"));
        Assert.True(await desktopRail.Locator(".legacy-rail-link").EvaluateAllAsync<bool>(
            "elements => elements.length > 0 && elements.every(element => { const height = element.getBoundingClientRect().height; return height >= 34 && height <= 38; })"));

        await page.SetViewportSizeAsync(768, 844);
        await page.Locator("#legacy-mobile-navigation-toggle").ClickAsync();

        var drawer = page.Locator(".legacy-navigation-rail[data-mobile='true']");
        await drawer.WaitForAsync();
        Assert.True(await drawer.Locator(".legacy-rail-link").EvaluateAllAsync<bool>(
            "elements => elements.length > 0 && elements.every(element => element.getBoundingClientRect().height >= 44)"));
        Assert.Equal("hidden", await drawer.Locator(".legacy-rail-groups").EvaluateAsync<string>(
            "element => getComputedStyle(element).overflowX"));
    }

    private static readonly (string ParentHref, string ChildHref)[] NavigationHierarchy =
    [
        ("/customers", "/customers/new"),
        ("/sales/orders", "/Orders/Create"),
        ("/Quotations/Index", "/Quotations/Create"),
        ("/finance/invoices", "/accounting/new"),
        ("/Finances/Index", "/Finances/Create"),
        ("/mfg/materials", "/Materials/Create"),
        ("/purchasing", "/purchasing/new"),
        ("/purchasing/suppliers", "/Suppliers/Create"),
    ];

    [Fact]
    public async Task NavigationAndProfileUseReleasedShadcnPrimitivesWithoutDefaultLinkDecoration()
    {
        await using var context = await playwright.Browser.NewContextAsync(new()
        {
            ViewportSize = new() { Width = 1280, Height = 900 },
            ReducedMotion = ReducedMotion.Reduce,
        });
        var page = await context.NewPageAsync();
        await StubProductionBoundariesAsync(page);
        await page.GotoAsync(new Uri(server.BaseUri, "customers/new").AbsoluteUri);

        var links = page.Locator(".legacy-navigation-rail .legacy-rail-link");
        await links.First.WaitForAsync();
        Assert.True(await links.CountAsync() > 1);
        Assert.True(await links.EvaluateAllAsync<bool>(
            "elements => elements.every(element => element.matches('.shadcn-sidebar-menu-button, .shadcn-sidebar-menu-sub-button') && getComputedStyle(element).textDecorationLine === 'none')"));

        var activeChild = page.Locator(".legacy-navigation-rail a[href='/customers/new']");
        Assert.Equal("page", await activeChild.GetAttributeAsync("aria-current"));
        Assert.True(await activeChild.EvaluateAsync<bool>(
            "element => { const style = getComputedStyle(element); return style.color !== style.backgroundColor; }"));

        var avatar = page.Locator(".legacy-profile [data-slot='avatar']");
        await avatar.WaitForAsync();
        Assert.Equal("BE", (await avatar.Locator("[data-slot='avatar-fallback']").InnerTextAsync()).Trim());
        Assert.Equal("1px", await avatar.EvaluateAsync<string>("element => getComputedStyle(element, '::after').borderTopWidth"));
    }

    [Theory]
    [InlineData(1280)]
    [InlineData(320)]
    public async Task EmployeeMenuOwnsLanguagePreferenceAndPersistsCultureSelection(int width)
    {
        await using var context = await playwright.Browser.NewContextAsync(new()
        {
            ViewportSize = new() { Width = width, Height = 900 },
            HasTouch = width <= 720,
            ReducedMotion = ReducedMotion.Reduce,
        });
        var page = await context.NewPageAsync();
        await StubProductionBoundariesAsync(page);
        await page.GotoAsync(new Uri(server.BaseUri, "sales/orders").AbsoluteUri);

        Assert.Equal(0, await page.Locator(".legacy-topbar__utilities > .legacy-language-selector").CountAsync());
        if (width <= 720)
        {
            var trigger = page.GetByRole(AriaRole.Button, new() { Name = "Employee menu" });
            Assert.False(await trigger.Locator(".legacy-profile-chevron").IsVisibleAsync());
            var bounds = await trigger.BoundingBoxAsync();
            Assert.NotNull(bounds);
            Assert.InRange(Math.Abs(bounds!.Width - bounds.Height), 0, 1);
        }

        await page.GetByRole(AriaRole.Button, new() { Name = "Employee menu" }).ClickAsync();
        var preference = page.Locator(".legacy-profile-popover .legacy-language-selector");
        await preference.WaitForAsync();
        Assert.True(await preference.Locator("select").EvaluateAsync<bool>(
            "element => element.getBoundingClientRect().width >= 44 && element.getBoundingClientRect().height >= 44"));

        await preference.Locator("select").SelectOptionAsync("th-TH");
        await page.WaitForFunctionAsync("() => localStorage.getItem('maliev_culture') === 'th-TH'");
        await page.WaitForLoadStateAsync(LoadState.DOMContentLoaded);
        Assert.Equal("th-TH", await page.EvaluateAsync<string>("localStorage.getItem('maliev_culture')"));

        await page.GetByRole(AriaRole.Button, new() { Name = "เมนูพนักงาน" }).ClickAsync();
        Assert.Equal("th-TH", await page.GetByLabel("ภาษา").InputValueAsync());
    }

    [Fact]
    public async Task EmployeeProfileTriggerIsAStandaloneTouchSizedCapsule()
    {
        await using var context = await playwright.Browser.NewContextAsync(new()
        {
            ViewportSize = new() { Width = 1280, Height = 900 },
            ReducedMotion = ReducedMotion.Reduce,
        });
        var page = await context.NewPageAsync();
        await StubProductionBoundariesAsync(page);
        await page.GotoAsync(new Uri(server.BaseUri, "sales/orders").AbsoluteUri);

        var visual = await page.GetByRole(AriaRole.Button, new() { Name = "Employee menu" })
            .EvaluateAsync<JsonElement>("""
                element => {
                    const bounds = element.getBoundingClientRect();
                    const style = getComputedStyle(element);
                    const utilitiesStyle = getComputedStyle(element.closest('.legacy-topbar__utilities'));
                    return {
                        height: bounds.height,
                        radius: Number.parseFloat(style.borderTopLeftRadius),
                        utilitiesBorder: utilitiesStyle.borderTopWidth,
                        utilitiesShadow: utilitiesStyle.boxShadow
                    };
                }
                """);

        var height = visual.GetProperty("height").GetDouble();
        Assert.True(height >= 44, visual.ToString());
        Assert.True(visual.GetProperty("radius").GetDouble() >= height / 2 - 1, visual.ToString());
        Assert.Equal("0px", visual.GetProperty("utilitiesBorder").GetString());
        Assert.Equal("none", visual.GetProperty("utilitiesShadow").GetString());
    }

    [Fact]
    public async Task EmployeeMenuOwnsThemePreferenceAndPersistsSelection()
    {
        await using var context = await playwright.Browser.NewContextAsync(new()
        {
            ViewportSize = new() { Width = 1280, Height = 900 },
            ReducedMotion = ReducedMotion.Reduce,
        });
        await context.AddInitScriptAsync("localStorage.setItem('maliev_theme', 'light')");
        var page = await context.NewPageAsync();
        await StubProductionBoundariesAsync(page);
        await page.GotoAsync(new Uri(server.BaseUri, "sales/orders").AbsoluteUri);

        Assert.Equal(0, await page.Locator(".legacy-topbar__utilities > .legacy-theme-toggle").CountAsync());
        await page.GetByRole(AriaRole.Button, new() { Name = "Employee menu" }).ClickAsync();

        var themePreference = page.Locator(".legacy-profile-popover .legacy-theme-toggle");
        await themePreference.WaitForAsync();
        Assert.True(await themePreference.EvaluateAsync<bool>(
            "element => element.getBoundingClientRect().width >= 44 && element.getBoundingClientRect().height >= 44"));
        await themePreference.ClickAsync();

        await page.WaitForFunctionAsync("() => document.documentElement.dataset.malievTheme === 'dark'");
        Assert.Equal("dark", await page.EvaluateAsync<string>("localStorage.getItem('maliev_theme')"));
    }

    [Fact]
    public async Task CollapsedNavigationCentersEveryVisibleIconInRail()
    {
        await using var context = await playwright.Browser.NewContextAsync(new()
        {
            ViewportSize = new() { Width = 1280, Height = 900 },
            ReducedMotion = ReducedMotion.Reduce,
        });
        var page = await context.NewPageAsync();
        await StubProductionBoundariesAsync(page);
        await page.GotoAsync(new Uri(server.BaseUri, "sales/orders").AbsoluteUri);

        await page.Locator("#legacy-sidebar-collapse").ClickAsync();
        var rail = page.Locator(".legacy-navigation-rail[data-mobile='false'][data-state='collapsed']");
        await rail.WaitForAsync();

        var maximumDeviation = await rail.EvaluateAsync<double>("""
            element => {
                const railBounds = element.getBoundingClientRect();
                const railCenter = railBounds.left + railBounds.width / 2;
                const icons = Array.from(element.querySelectorAll('.legacy-rail-link svg'))
                    .filter(icon => icon.getBoundingClientRect().height > 0);
                return Math.max(...icons.map(icon => {
                    const bounds = icon.getBoundingClientRect();
                    return Math.abs(bounds.left + bounds.width / 2 - railCenter);
                }));
            }
            """);

        Assert.InRange(maximumDeviation, 0, 1);
    }

    [Fact]
    public async Task SignOutActionUsesDestructiveColorWithAccessibleContrast()
    {
        await using var context = await playwright.Browser.NewContextAsync(new()
        {
            ViewportSize = new() { Width = 1280, Height = 900 },
            ReducedMotion = ReducedMotion.Reduce,
        });
        var page = await context.NewPageAsync();
        await StubProductionBoundariesAsync(page);
        await page.GotoAsync(new Uri(server.BaseUri, "sales/orders").AbsoluteUri);

        await page.GetByRole(AriaRole.Button, new() { Name = "Employee menu" }).ClickAsync();
        var signOut = page.GetByRole(AriaRole.Button, new() { Name = "Sign out" });
        var visual = await signOut.EvaluateAsync<JsonElement>("""
            element => {
                const style = getComputedStyle(element);
                const surface = getComputedStyle(element.closest('.legacy-profile-popover')).backgroundColor;
                const canvas = document.createElement('canvas');
                canvas.width = canvas.height = 1;
                const context = canvas.getContext('2d', { willReadFrequently: true });
                const rgba = value => {
                    context.clearRect(0, 0, 1, 1);
                    context.fillStyle = value;
                    context.fillRect(0, 0, 1, 1);
                    return Array.from(context.getImageData(0, 0, 1, 1).data);
                };
                const composite = (foreground, background) => {
                    const alpha = foreground[3] / 255;
                    return foreground.slice(0, 3).map((channel, index) =>
                        channel * alpha + background[index] * (1 - alpha));
                };
                const channel = value => {
                    const normalized = value / 255;
                    return normalized <= 0.04045
                        ? normalized / 12.92
                        : Math.pow((normalized + 0.055) / 1.055, 2.4);
                };
                const luminance = ([red, green, blue]) => {
                    return 0.2126 * channel(red) + 0.7152 * channel(green) + 0.0722 * channel(blue);
                };
                const foreground = luminance(rgba(style.color));
                const background = luminance(composite(rgba(style.backgroundColor), rgba(surface)));
                return {
                    color: style.color,
                    background: style.backgroundColor,
                    height: element.getBoundingClientRect().height,
                    contrast: (Math.max(foreground, background) + 0.05) / (Math.min(foreground, background) + 0.05)
                };
            }
            """);

        Assert.DoesNotContain("rgba(0, 0, 0, 0)", visual.GetProperty("background").GetString(), StringComparison.Ordinal);
        Assert.True(visual.GetProperty("height").GetDouble() >= 44, visual.ToString());
        Assert.True(visual.GetProperty("contrast").GetDouble() >= 4.5, visual.ToString());
    }

    [Fact]
    public async Task NarrowQuickCreateRoutesRemainVisibleFocusableAndTouchSized()
    {
        await using var context = await playwright.Browser.NewContextAsync(new()
        {
            ViewportSize = new() { Width = 768, Height = 844 },
            DeviceScaleFactor = 1,
            ReducedMotion = ReducedMotion.Reduce,
        });
        var page = await context.NewPageAsync();
        await StubProductionBoundariesAsync(page);
        await page.GotoAsync(new Uri(server.BaseUri, "sales/orders").AbsoluteUri);
        await page.Locator(".legacy-topbar__actions").WaitForAsync();

        foreach (var width in new[] { 768, 390, 320 })
        {
            await page.SetViewportSizeAsync(width, 844);
            foreach (var href in new[] { "/Quotations/Create", "/Orders/Create" })
            {
                var action = page.Locator($".legacy-topbar__actions a[href='{href}']");
                Assert.True(await action.IsVisibleAsync(), $"{href} was not visible at {width}px.");
                Assert.True(await action.EvaluateAsync<bool>(
                    "element => element.getBoundingClientRect().width >= 44 && element.getBoundingClientRect().height >= 44"));
                await action.FocusAsync();
                Assert.True(await action.EvaluateAsync<bool>("element => element === document.activeElement"));
            }
        }
    }

    [Fact]
    public async Task ThreeHundredTwentyPixelTopbarContainsNonOverlappingZonesAndControls()
    {
        await using var context = await playwright.Browser.NewContextAsync(new()
        {
            ViewportSize = new() { Width = 320, Height = 844 },
            DeviceScaleFactor = 1,
            ReducedMotion = ReducedMotion.Reduce,
        });
        var page = await context.NewPageAsync();
        await StubProductionBoundariesAsync(page);
        await page.GotoAsync(new Uri(server.BaseUri, "sales/orders").AbsoluteUri);
        await page.Locator(".legacy-topbar__actions").WaitForAsync();

        await AssertStrictNarrowTopbarGeometryAsync(page);
    }

    [Fact]
    public async Task ProductionShellKeepsHierarchyAlignmentContainmentAndDrawerFocusAcrossSupportedWidths()
    {
        await using var context = await playwright.Browser.NewContextAsync(new()
        {
            ViewportSize = new() { Width = 1280, Height = 900 },
            DeviceScaleFactor = 1,
            ReducedMotion = ReducedMotion.Reduce,
        });
        var page = await context.NewPageAsync();
        var errors = new List<string>();
        page.Console += (_, message) => { if (message.Type == "error") errors.Add(message.Text); };
        page.PageError += (_, error) => errors.Add(error);
        await StubProductionBoundariesAsync(page);

        await page.GotoAsync(new Uri(server.BaseUri, "sales/orders").AbsoluteUri);
        try
        {
            await page.Locator(".legacy-topbar__utilities").WaitForAsync(new() { Timeout = 10_000 });
        }
        catch (TimeoutException exception)
        {
            var errorUi = await page.Locator("#blazor-error-ui").TextContentAsync();
            throw new InvalidOperationException(
                $"The production shell did not render. URL: {page.Url}; browser errors: {string.Join(" | ", errors)}; error UI: {errorUi}",
                exception);
        }

        Assert.Equal(1, await page.Locator("#legacy-sidebar-collapse:is(button)").CountAsync());
        Assert.Equal(0, await page.Locator(".legacy-topbar__utilities > .legacy-theme-toggle:is(button)").CountAsync());
        Assert.Equal(1, await page.Locator(".legacy-profile:is(button)").CountAsync());
        Assert.Equal(2, await page.Locator(".legacy-quick-action:is(a)").CountAsync());
        Assert.Equal(1, await page.Locator(".legacy-global-search input").CountAsync());

        var desktopShell = await page.EvaluateAsync<JsonElement>("""
            () => {
                const bounds = selector => {
                    const rect = document.querySelector(selector).getBoundingClientRect();
                    return { left: rect.left, right: rect.right, top: rect.top, bottom: rect.bottom, width: rect.width, height: rect.height };
                };
                return {
                    viewportWidth: document.documentElement.clientWidth,
                    topbar: bounds('.legacy-topbar'),
                    sidebar: bounds('.legacy-navigation-rail'),
                    collapse: bounds('#legacy-sidebar-collapse'),
                    logo: bounds('.legacy-rail-logo img:not([aria-hidden=true])'),
                    workspace: bounds('.legacy-workspace-shell')
                };
            }
            """);
        var desktopTopbar = desktopShell.GetProperty("topbar");
        var desktopSidebar = desktopShell.GetProperty("sidebar");
        var desktopCollapse = desktopShell.GetProperty("collapse");
        var desktopLogo = desktopShell.GetProperty("logo");
        var desktopWorkspace = desktopShell.GetProperty("workspace");
        Assert.InRange(
            Math.Abs(desktopTopbar.GetProperty("left").GetDouble() - desktopSidebar.GetProperty("right").GetDouble()),
            0,
            2);
        Assert.InRange(
            desktopTopbar.GetProperty("right").GetDouble(),
            desktopShell.GetProperty("viewportWidth").GetInt32() - 0.5,
            desktopShell.GetProperty("viewportWidth").GetInt32() + 0.5);
        Assert.InRange(
            Math.Abs(desktopSidebar.GetProperty("right").GetDouble() - desktopWorkspace.GetProperty("left").GetDouble()),
            0,
            2);
        Assert.True(desktopLogo.GetProperty("left").GetDouble() < desktopWorkspace.GetProperty("left").GetDouble(), desktopShell.ToString());
        Assert.InRange(desktopCollapse.GetProperty("left").GetDouble(), desktopSidebar.GetProperty("left").GetDouble(), desktopSidebar.GetProperty("right").GetDouble());
        Assert.InRange(desktopCollapse.GetProperty("right").GetDouble(), desktopSidebar.GetProperty("left").GetDouble(), desktopSidebar.GetProperty("right").GetDouble());

        var parent = page.Locator(".legacy-navigation-rail a[href='/sales/orders']");
        var child = page.Locator(".legacy-navigation-rail a[href='/Orders/Create']");
        Assert.Equal("page", await parent.GetAttributeAsync("aria-current"));
        Assert.Contains("legacy-rail-link--child", await child.GetAttributeAsync("class"));
        Assert.True(await child.EvaluateAsync<bool>(
            "(node, parent) => Boolean(node.compareDocumentPosition(document.querySelector(parent)) & Node.DOCUMENT_POSITION_PRECEDING)",
            ".legacy-navigation-rail a[href='/sales/orders']"));

        var zones = await page.Locator(".legacy-topbar__search, .legacy-topbar__actions, .legacy-topbar__utilities")
            .EvaluateAllAsync<JsonElement>("""
                elements => elements.map(element => {
                    const rect = element.getBoundingClientRect();
                    return { className: element.className, center: rect.top + rect.height / 2 };
                })
                """);
        var centers = zones.EnumerateArray().Select(zone => zone.GetProperty("center").GetDouble()).ToArray();
        Assert.Equal(3, centers.Length);
        Assert.True(centers.Max() - centers.Min() <= 2, zones.ToString());

        foreach (var width in new[] { 1280, 768, 390, 320 })
        {
            await page.SetViewportSizeAsync(width, 844);
            await page.WaitForFunctionAsync("width => document.documentElement.clientWidth === width", width);
            var containment = await page.EvaluateAsync<JsonElement>("""
                () => ({
                    clientWidth: document.documentElement.clientWidth,
                    scrollWidth: document.documentElement.scrollWidth,
                    matches960: matchMedia('(max-width: 960px)').matches,
                    responsiveTopbarRules: Array.from(document.styleSheets).flatMap(sheet => {
                        try {
                            return Array.from(sheet.cssRules).flatMap(rule =>
                                rule instanceof CSSMediaRule && rule.conditionText.includes('960')
                                    ? Array.from(rule.cssRules).filter(inner => inner.cssText.includes('.legacy-topbar')).map(inner => `${sheet.href}: ${inner.cssText}`)
                                    : []);
                        } catch { return []; }
                    }),
                    topbar: (() => {
                        const topbar = document.querySelector('.legacy-topbar');
                        const rect = topbar.getBoundingClientRect();
                        return { left: rect.left, right: rect.right, width: rect.width, columns: getComputedStyle(topbar).gridTemplateColumns };
                    })(),
                    offenders: Array.from(document.querySelectorAll('body *'))
                        .map(element => ({
                            tag: element.tagName,
                            classes: element.className?.baseVal ?? element.className ?? '',
                            left: element.getBoundingClientRect().left,
                            right: element.getBoundingClientRect().right,
                            width: element.getBoundingClientRect().width
                        }))
                        .filter(element => element.left < -0.5 || element.right > document.documentElement.clientWidth + 0.5)
                        .slice(0, 12)
                })
                """);
            Assert.True(
                containment.GetProperty("scrollWidth").GetInt32() == containment.GetProperty("clientWidth").GetInt32(),
                containment.ToString());

            foreach (var href in new[] { "/Quotations/Create", "/Orders/Create" })
            {
                var action = page.Locator($".legacy-topbar__actions a[href='{href}']");
                Assert.Equal(1, await action.CountAsync());
                Assert.True(await action.IsVisibleAsync(), $"{href} was not visible at {width}px.");
                Assert.True(await action.EvaluateAsync<bool>(
                    "element => element.getBoundingClientRect().width >= 44 && element.getBoundingClientRect().height >= 44"),
                    $"{href} was not touch sized at {width}px.");
                await action.FocusAsync();
                Assert.True(await action.EvaluateAsync<bool>("element => element === document.activeElement"));
            }

            if (width == 320)
                await AssertStrictNarrowTopbarGeometryAsync(page);
        }

        await page.SetViewportSizeAsync(768, 844);
        var menu = page.Locator("#legacy-mobile-navigation-toggle");
        await menu.FocusAsync();
        await menu.ClickAsync();

        var drawer = page.Locator(".legacy-navigation-rail[data-mobile='true']");
        await drawer.WaitForAsync();
        Assert.True(await drawer.Locator("a[href='/Orders/Create']").EvaluateAsync<bool>(
            "element => element.getBoundingClientRect().height >= 44"));
        var close = drawer.Locator("#legacy-sidebar-collapse");
        await close.WaitForAsync();
        var firstFocusable = drawer.Locator(".legacy-rail-logo");
        await firstFocusable.FocusAsync();
        await page.Keyboard.PressAsync("Shift+Tab");
        var lastNavigationLink = drawer.Locator(".legacy-rail-link").Last;
        await page.WaitForFunctionAsync(
            "element => element === document.activeElement",
            await lastNavigationLink.ElementHandleAsync());
        Assert.True(await lastNavigationLink.EvaluateAsync<bool>("element => element === document.activeElement"));
        await page.Keyboard.PressAsync("Escape");
        await drawer.WaitForAsync(new() { State = WaitForSelectorState.Detached });
        Assert.True(await menu.EvaluateAsync<bool>("element => element === document.activeElement"));

        Assert.Empty(errors);
    }

    [Theory]
    [InlineData(1280, false)]
    [InlineData(320, true)]
    public async Task EveryCreateRouteHasOneMostSpecificCurrentLinkInsideItsAuthorizedParent(int width, bool thai)
    {
        await using var context = await playwright.Browser.NewContextAsync(new()
        {
            ViewportSize = new() { Width = width, Height = 844 },
            HasTouch = width <= 720,
            ReducedMotion = ReducedMotion.Reduce,
        });
        if (thai)
            await context.AddInitScriptAsync("localStorage.setItem('maliev_culture', 'th-TH')");
        var page = await context.NewPageAsync();
        await StubProductionBoundariesAsync(page);

        foreach (var (parentHref, childHref) in NavigationHierarchy)
        {
            await page.GotoAsync(new Uri(server.BaseUri, childHref.TrimStart('/')).AbsoluteUri);
            if (width <= 768)
                await page.Locator("#legacy-mobile-navigation-toggle").ClickAsync();

            var rail = page.Locator(width <= 768 ? ".legacy-navigation-rail[data-mobile='true']" : ".legacy-navigation-rail[data-mobile='false']");
            await rail.WaitForAsync();
            var parent = rail.Locator($"a[href='{parentHref}']");
            var child = rail.Locator($"a[href='{childHref}']");
            await parent.WaitForAsync();
            await child.WaitForAsync();
            Assert.Equal(1, await parent.CountAsync());
            Assert.Equal(1, await child.CountAsync());
            Assert.Equal(1, await rail.Locator("[aria-current='page']").CountAsync());
            Assert.Null(await parent.GetAttributeAsync("aria-current"));
            Assert.Equal("page", await child.GetAttributeAsync("aria-current"));
            Assert.True(await child.EvaluateAsync<bool>(
                "(node, parentSelector) => node.closest('ul.legacy-rail-children')?.parentElement?.querySelector(':scope > ' + parentSelector) !== null",
                $"a[href='{parentHref}']"));
            var minimumHeight = width <= 768 ? 44 : 28;
            Assert.True(await child.EvaluateAsync<bool>("(node, minimum) => node.getBoundingClientRect().height >= minimum", minimumHeight));
            await parent.FocusAsync();
            await page.Keyboard.PressAsync("Tab");
            Assert.True(await child.EvaluateAsync<bool>("node => document.activeElement === node"));

            if (width <= 768)
                await page.Keyboard.PressAsync("Escape");
        }
    }

    private static async Task AssertStrictNarrowTopbarGeometryAsync(IPage page)
    {
        var geometry = await page.EvaluateAsync<JsonElement>("""
            () => {
                const topbar = document.querySelector('.legacy-topbar');
                const headerRect = topbar.getBoundingClientRect();
                const rect = element => {
                    const bounds = element.getBoundingClientRect();
                    return {
                        name: element.getAttribute('aria-label') || element.getAttribute('href') || element.className?.baseVal || element.className || element.tagName,
                        left: bounds.left,
                        right: bounds.right,
                        top: bounds.top,
                        bottom: bounds.bottom,
                        width: bounds.width,
                        height: bounds.height
                    };
                };
                const visible = element => {
                    const style = getComputedStyle(element);
                    const bounds = element.getBoundingClientRect();
                    return style.display !== 'none' && style.visibility !== 'hidden' && bounds.width > 0 && bounds.height > 0;
                };
                return {
                    header: rect(topbar),
                    zones: Array.from(topbar.querySelectorAll(':scope > [class*="legacy-topbar__"]')).filter(visible).map(rect),
                    controls: Array.from(topbar.querySelectorAll('a, button, input:not([type="hidden"])')).filter(visible).map(rect),
                    brand: rect(topbar.querySelector('.legacy-topbar__brand'))
                };
            }
            """);

        var header = geometry.GetProperty("header");
        var zones = geometry.GetProperty("zones").EnumerateArray().ToArray();
        var controls = geometry.GetProperty("controls").EnumerateArray().ToArray();
        foreach (var element in zones.Concat(controls))
        {
            Assert.True(element.GetProperty("left").GetDouble() >= header.GetProperty("left").GetDouble() - 0.5, geometry.ToString());
            Assert.True(element.GetProperty("right").GetDouble() <= header.GetProperty("right").GetDouble() + 0.5, geometry.ToString());
            Assert.True(element.GetProperty("top").GetDouble() >= header.GetProperty("top").GetDouble() - 0.5, geometry.ToString());
            Assert.True(element.GetProperty("bottom").GetDouble() <= header.GetProperty("bottom").GetDouble() + 0.5, geometry.ToString());
        }

        for (var index = 0; index < zones.Length; index++)
        {
            for (var other = index + 1; other < zones.Length; other++)
                Assert.False(Overlaps(zones[index], zones[other]), geometry.ToString());
        }

        foreach (var control in controls)
        {
            Assert.True(control.GetProperty("width").GetDouble() >= 44, geometry.ToString());
            Assert.True(control.GetProperty("height").GetDouble() >= 44, geometry.ToString());
        }

        for (var index = 0; index < controls.Length; index++)
        {
            for (var other = index + 1; other < controls.Length; other++)
                Assert.False(Overlaps(controls[index], controls[other]), geometry.ToString());
        }

    }

    private static bool Overlaps(JsonElement first, JsonElement second) =>
        first.GetProperty("left").GetDouble() < second.GetProperty("right").GetDouble() - 0.5 &&
        first.GetProperty("right").GetDouble() > second.GetProperty("left").GetDouble() + 0.5 &&
        first.GetProperty("top").GetDouble() < second.GetProperty("bottom").GetDouble() - 0.5 &&
        first.GetProperty("bottom").GetDouble() > second.GetProperty("top").GetDouble() + 0.5;

    private static async Task StubProductionBoundariesAsync(IPage page)
    {
        var session = JsonSerializer.Serialize(new
        {
            isAuthenticated = true,
            employeeId = "browser-shell-employee",
            email = "browser.shell@maliev.com",
            displayName = "Browser Shell Employee",
            roles = new[] { "Employee" },
            csrfToken = "browser-shell-csrf",
            legacyDatabaseId = 1,
            permissions = new[]
            {
                "legacy.orders.read", "legacy.orders.create",
                "legacy.quotations.read", "legacy.quotations.create",
                "legacy-customer.customers.list", "legacy-customer.customers.create",
                "legacy.accounting.read", "legacy.accounting.create",
                "legacy-catalog.materials.read", "legacy-catalog.materials.create",
                "legacy-procurement.purchase-orders.read", "legacy-procurement.purchase-orders.create",
                "legacy-procurement.suppliers.read", "legacy-procurement.suppliers.create",
            },
        });

        await page.RouteAsync("**/bff/session", route => route.FulfillAsync(new()
        {
            Status = 200,
            ContentType = "application/json",
            Body = session,
        }));
        await page.RouteAsync("**/bff/orders?*", route => route.FulfillAsync(new()
        {
            Status = 200,
            ContentType = "application/json",
            Body = "{\"items\":[],\"pageIndex\":1,\"totalPages\":1,\"totalRecords\":0,\"hasNextPage\":false,\"hasPreviousPage\":false}",
        }));
        await page.RouteAsync("**/bff/orders/pending?*", route => route.FulfillAsync(new()
        {
            Status = 200,
            ContentType = "application/json",
            Body = "[]",
        }));
    }
}
