using System.Text.Json;
using Legacy.Maliev.Intranet.BrowserTests.Infrastructure;
using Microsoft.Playwright;

namespace Legacy.Maliev.Intranet.BrowserTests;

[Collection(CustomerBrowserCollection.Name)]
public sealed class OperationalShellBrowserTests(
    IntranetClientServerFixture server,
    PlaywrightFixture playwright)
{
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
    public async Task DesktopShellOwnsTheViewportAndKeepsNavigationAndWorkspaceScrollingIndependent()
    {
        await using var context = await playwright.Browser.NewContextAsync(new()
        {
            ViewportSize = new() { Width = 1280, Height = 900 },
            ReducedMotion = ReducedMotion.Reduce,
        });
        var page = await context.NewPageAsync();
        await StubProductionBoundariesAsync(page);
        await page.GotoAsync(new Uri(server.BaseUri, "sales/orders").AbsoluteUri);
        await page.Locator(".legacy-topbar").WaitForAsync();

        var geometry = await page.EvaluateAsync<JsonElement>("""
            () => {
                const main = document.querySelector('#main-content');
                const navigation = document.querySelector('.legacy-rail-groups');
                const breadcrumbTopBeforeScroll = document.querySelector('.page-breadcrumbs').getBoundingClientRect().top;
                main.insertAdjacentHTML('beforeend', '<div data-shell-test-filler style="height:1400px"></div>');
                navigation.insertAdjacentHTML('beforeend', '<div data-shell-test-filler style="height:900px"></div>');
                main.scrollTop = 240;
                navigation.scrollTop = 180;
                const bounds = element => {
                    const rect = element.getBoundingClientRect();
                    const style = getComputedStyle(element);
                    return {
                        top: rect.top,
                        bottom: rect.bottom,
                        clientWidth: element.clientWidth,
                        scrollWidth: element.scrollWidth,
                        clientHeight: element.clientHeight,
                        scrollHeight: element.scrollHeight,
                        scrollTop: element.scrollTop,
                        overflowX: style.overflowX,
                        overflowY: style.overflowY
                    };
                };
                return {
                    viewportHeight: innerHeight,
                    documentScrollY: scrollY,
                    body: bounds(document.body),
                    topbar: bounds(document.querySelector('.legacy-topbar')),
                    main: bounds(main),
                    navigation: bounds(navigation),
                    activeTag: document.activeElement?.tagName,
                    activeId: document.activeElement?.id,
                    headingFontSize: parseFloat(getComputedStyle(document.querySelector('h1')).fontSize),
                    headingMarginTop: parseFloat(getComputedStyle(document.querySelector('h1')).marginTop),
                    breadcrumbTopBeforeScroll
                };
            }
            """);

        Assert.Equal(900, geometry.GetProperty("body").GetProperty("clientHeight").GetInt32());
        Assert.Equal(900, geometry.GetProperty("body").GetProperty("scrollHeight").GetInt32());
        Assert.Equal(0, geometry.GetProperty("documentScrollY").GetDouble());
        Assert.Equal("hidden", geometry.GetProperty("main").GetProperty("overflowX").GetString());
        Assert.Contains(geometry.GetProperty("main").GetProperty("overflowY").GetString(), new[] { "auto", "scroll" });
        Assert.True(geometry.GetProperty("main").GetProperty("scrollTop").GetDouble() > 0, geometry.ToString());
        Assert.Equal("hidden", geometry.GetProperty("navigation").GetProperty("overflowX").GetString());
        Assert.Contains(geometry.GetProperty("navigation").GetProperty("overflowY").GetString(), new[] { "auto", "scroll" });
        Assert.True(geometry.GetProperty("navigation").GetProperty("scrollTop").GetDouble() > 0, geometry.ToString());
        Assert.True(
            geometry.GetProperty("main").GetProperty("top").GetDouble() >= geometry.GetProperty("topbar").GetProperty("bottom").GetDouble() - .5,
            geometry.ToString());
        Assert.Equal("MAIN", geometry.GetProperty("activeTag").GetString());
        Assert.Equal("main-content", geometry.GetProperty("activeId").GetString());
        Assert.InRange(geometry.GetProperty("headingFontSize").GetDouble(), 24, 30);
        Assert.Equal(0, geometry.GetProperty("headingMarginTop").GetDouble());
        Assert.True(
            geometry.GetProperty("breadcrumbTopBeforeScroll").GetDouble() >= geometry.GetProperty("topbar").GetProperty("bottom").GetDouble(),
            geometry.ToString());
    }

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

    [Fact]
    public async Task SidebarBrandHierarchyBadgeAndTopbarControlsMatchTheWorkspaceShellContract()
    {
        await using var context = await playwright.Browser.NewContextAsync(new()
        {
            ViewportSize = new() { Width = 1280, Height = 900 },
            ReducedMotion = ReducedMotion.Reduce,
        });
        var page = await context.NewPageAsync();
        await StubProductionBoundariesAsync(page);
        await page.GotoAsync(new Uri(server.BaseUri, "customers").AbsoluteUri);

        var logo = page.Locator(".legacy-rail-logo img[alt='MALIEV']");
        await logo.WaitForAsync();
        Assert.EndsWith("images/MALIEV_BLACK.svg", await logo.GetAttributeAsync("src"), StringComparison.Ordinal);
        Assert.Equal(0, await page.Locator(".legacy-rail-brand__mark").CountAsync());

        var quotationBadge = page.Locator("a[href='/QuotationRequests/Index'] + [data-slot='sidebar-menu-badge']");
        await quotationBadge.WaitForAsync();
        Assert.Equal("17", (await quotationBadge.InnerTextAsync()).Trim());

        var customers = page.Locator(".legacy-navigation-rail a[href='/customers']");
        var newCustomer = page.Locator(".legacy-navigation-rail a[href='/customers/new']");
        var childToggle = page.Locator(".legacy-navigation-rail [data-branch-href='/customers'] [data-slot='collapsible-trigger']");
        Assert.Equal("true", await childToggle.GetAttributeAsync("aria-expanded"));
        Assert.True(await newCustomer.IsVisibleAsync());
        Assert.NotEqual(
            await customers.EvaluateAsync<string>("element => getComputedStyle(element).color"),
            await newCustomer.EvaluateAsync<string>("element => getComputedStyle(element).color"));
        await childToggle.ClickAsync();
        Assert.Equal("false", await childToggle.GetAttributeAsync("aria-expanded"));
        Assert.False(await newCustomer.IsVisibleAsync());

        var topbarHeights = await page.Locator(".legacy-global-search input, .legacy-quick-action, .legacy-language-selector select, .legacy-theme-toggle, .legacy-profile")
            .EvaluateAllAsync<double[]>("elements => elements.filter(element => getComputedStyle(element).display !== 'none').map(element => element.getBoundingClientRect().height)");
        Assert.True(topbarHeights.Length >= 6, string.Join(", ", topbarHeights));
        Assert.True(topbarHeights.Max() - topbarHeights.Min() <= .5, string.Join(", ", topbarHeights));

        await page.Locator("#legacy-sidebar-collapse").ClickAsync();
        await page.Locator(".legacy-navigation-rail[data-state='collapsed']").WaitForAsync();
        Assert.Equal(0, await page.Locator(".legacy-navigation-rail[data-state='collapsed'] .legacy-rail-link--child:visible").CountAsync());
        var iconCenters = await page.Locator(".legacy-navigation-rail[data-state='collapsed'] .shadcn-sidebar-menu-button:visible")
            .EvaluateAllAsync<double[]>("elements => elements.map(element => { const rect = element.getBoundingClientRect(); return rect.left + rect.width / 2; })");
        Assert.True(iconCenters.Length > 5, string.Join(", ", iconCenters));
        Assert.True(iconCenters.Max() - iconCenters.Min() <= .5, string.Join(", ", iconCenters));
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
        Assert.Equal(1, await page.Locator(".legacy-theme-toggle:is(button)").CountAsync());
        Assert.Equal(1, await page.Locator(".legacy-profile:is(button)").CountAsync());
        Assert.Equal(2, await page.Locator(".legacy-quick-action:is(a)").CountAsync());
        Assert.Equal(1, await page.Locator(".legacy-global-search input").CountAsync());
        Assert.Equal(1, await page.Locator(".legacy-language-selector select").CountAsync());
        Assert.Equal("inset", await page.Locator(".legacy-navigation-rail[data-mobile='false']").GetAttributeAsync("data-variant"));

        var documentedStyle = await page.EvaluateAsync<JsonElement>("""
            () => {
                const sidebar = document.querySelector('.legacy-navigation-rail[data-mobile="false"]');
                const wrapper = document.querySelector('.shadcn-sidebar-wrapper');
                const inset = document.querySelector('.shadcn-sidebar-inset');
                const logo = document.querySelector('.legacy-rail-brand-logo');
                const active = document.querySelector('.legacy-rail-link[data-active="true"]');
                const table = document.querySelector('[data-slot="data-table"] table');
                const tabular = table?.querySelector('tbody td');
                return {
                    sidebarWidth: sidebar.getBoundingClientRect().width,
                    sidebarBackground: getComputedStyle(sidebar).backgroundColor,
                    wrapperBackground: getComputedStyle(wrapper).backgroundColor,
                    insetRadius: parseFloat(getComputedStyle(inset).borderRadius),
                    insetShadow: getComputedStyle(inset).boxShadow,
                    logoWidth: logo.getBoundingClientRect().width,
                    activeHeight: active.getBoundingClientRect().height,
                    tableFont: table ? getComputedStyle(table).fontFamily : '',
                    tabularFont: tabular ? getComputedStyle(tabular).fontFamily : ''
                };
            }
            """);
        Assert.InRange(documentedStyle.GetProperty("sidebarWidth").GetDouble(), 239.5, 240.5);
        Assert.Equal(documentedStyle.GetProperty("wrapperBackground").GetString(), documentedStyle.GetProperty("sidebarBackground").GetString());
        Assert.InRange(documentedStyle.GetProperty("insetRadius").GetDouble(), 13.5, 14.5);
        Assert.NotEqual("none", documentedStyle.GetProperty("insetShadow").GetString());
        Assert.InRange(documentedStyle.GetProperty("logoWidth").GetDouble(), 83.5, 84.5);
        Assert.InRange(documentedStyle.GetProperty("activeHeight").GetDouble(), 31.5, 32.5);
        if (!string.IsNullOrWhiteSpace(documentedStyle.GetProperty("tableFont").GetString()))
        {
            Assert.Equal(documentedStyle.GetProperty("tableFont").GetString(), documentedStyle.GetProperty("tabularFont").GetString());
            Assert.DoesNotContain("Mono", documentedStyle.GetProperty("tabularFont").GetString(), StringComparison.OrdinalIgnoreCase);
        }

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
                    logo: bounds('.legacy-rail-brand-logo'),
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
            7,
            9);
        Assert.InRange(
            desktopTopbar.GetProperty("right").GetDouble(),
            desktopShell.GetProperty("viewportWidth").GetInt32() - 8.5,
            desktopShell.GetProperty("viewportWidth").GetInt32() - 7.5);
        Assert.InRange(
            Math.Abs(desktopSidebar.GetProperty("right").GetDouble() - desktopWorkspace.GetProperty("left").GetDouble()),
            7,
            9);
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
        await page.WaitForFunctionAsync(
            "drawer => document.activeElement?.closest('.legacy-navigation-rail') === drawer",
            await drawer.ElementHandleAsync());
        Assert.True(await drawer.EvaluateAsync<bool>(
            "element => document.activeElement?.closest('.legacy-navigation-rail') === element"));
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
                "(node, parentSelector) => node.closest('.legacy-rail-branch')?.querySelector(parentSelector) !== null",
                $"a[href='{parentHref}']"));
            var minimumHeight = width <= 768 ? 44 : 28;
            Assert.True(await child.EvaluateAsync<bool>("(node, minimum) => node.getBoundingClientRect().height >= minimum", minimumHeight));
            await parent.FocusAsync();
            await page.Keyboard.PressAsync("Tab");
            var toggle = parent.Locator("xpath=ancestor::*[contains(@class, 'legacy-rail-branch')][1]//*[@data-slot='collapsible-trigger']");
            Assert.True(await toggle.EvaluateAsync<bool>("node => document.activeElement === node"));
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
                "legacy.quotation-requests.read",
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
        await page.RouteAsync("**/bff/quotation-requests?*", route => route.FulfillAsync(new()
        {
            Status = 200,
            ContentType = "application/json",
            Body = "{\"items\":[],\"pageIndex\":1,\"totalPages\":17,\"totalRecords\":17,\"hasNextPage\":true,\"hasPreviousPage\":false}",
        }));
    }
}
