using System.Text.Json;
using Maliev.ShadcnBlazor.BrowserTests.Infrastructure;
using Microsoft.Playwright;

namespace Maliev.ShadcnBlazor.BrowserTests;

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
    public async Task ThreeHundredTwentyPixelTopbarContainsNonOverlappingZonesControlsAndLogo()
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
        await page.Locator(".legacy-topbar__utilities").WaitForAsync();

        var desktopShell = await page.EvaluateAsync<JsonElement>("""
            () => {
                const bounds = selector => {
                    const rect = document.querySelector(selector).getBoundingClientRect();
                    return { left: rect.left, right: rect.right, top: rect.top, bottom: rect.bottom, width: rect.width, height: rect.height };
                };
                return {
                    viewportWidth: document.documentElement.clientWidth,
                    topbar: bounds('.legacy-topbar'),
                    brand: bounds('.legacy-topbar__brand'),
                    logo: bounds('.legacy-topbar-logo img:not([aria-hidden=true])'),
                    workspace: bounds('.legacy-workspace-shell')
                };
            }
            """);
        var desktopTopbar = desktopShell.GetProperty("topbar");
        var desktopBrand = desktopShell.GetProperty("brand");
        var desktopLogo = desktopShell.GetProperty("logo");
        var desktopWorkspace = desktopShell.GetProperty("workspace");
        Assert.InRange(desktopTopbar.GetProperty("left").GetDouble(), -0.5, 0.5);
        Assert.InRange(
            desktopTopbar.GetProperty("right").GetDouble(),
            desktopShell.GetProperty("viewportWidth").GetInt32() - 0.5,
            desktopShell.GetProperty("viewportWidth").GetInt32() + 0.5);
        Assert.InRange(
            Math.Abs(desktopBrand.GetProperty("right").GetDouble() - desktopWorkspace.GetProperty("left").GetDouble()),
            0,
            2);
        Assert.True(desktopLogo.GetProperty("left").GetDouble() < desktopWorkspace.GetProperty("left").GetDouble(), desktopShell.ToString());

        var parent = page.Locator("#legacy-navigation-rail a[href='/sales/orders']");
        var child = page.Locator("#legacy-navigation-rail a[href='/Orders/Create']");
        Assert.Equal("page", await parent.GetAttributeAsync("aria-current"));
        Assert.Contains("legacy-rail-link--child", await child.GetAttributeAsync("class"));
        Assert.True(await child.EvaluateAsync<bool>(
            "(node, parent) => Boolean(node.compareDocumentPosition(document.querySelector(parent)) & Node.DOCUMENT_POSITION_PRECEDING)",
            "#legacy-navigation-rail a[href='/sales/orders']"));

        var zones = await page.Locator(".legacy-topbar__brand, .legacy-topbar__search, .legacy-topbar__actions, .legacy-topbar__utilities")
            .EvaluateAllAsync<JsonElement>("""
                elements => elements.map(element => {
                    const rect = element.getBoundingClientRect();
                    return { className: element.className, center: rect.top + rect.height / 2 };
                })
                """);
        var centers = zones.EnumerateArray().Select(zone => zone.GetProperty("center").GetDouble()).ToArray();
        Assert.Equal(4, centers.Length);
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
        var menu = page.GetByRole(AriaRole.Button, new() { Name = "Open navigation" });
        await menu.FocusAsync();
        await menu.ClickAsync();

        var drawer = page.Locator("#legacy-navigation-rail-drawer");
        await drawer.WaitForAsync();
        Assert.True(await drawer.Locator("a[href='/Orders/Create']").EvaluateAsync<bool>(
            "element => element.getBoundingClientRect().height >= 44"));
        var close = drawer.GetByRole(AriaRole.Button, new() { Name = "Close navigation" });
        await page.WaitForFunctionAsync("element => element === document.activeElement", await close.ElementHandleAsync());
        await page.Keyboard.PressAsync("Shift+Tab");
        await page.Keyboard.PressAsync("Shift+Tab");
        Assert.True(await drawer.Locator(".legacy-rail-link").Last.EvaluateAsync<bool>("element => element === document.activeElement"));
        await page.Keyboard.PressAsync("Tab");
        await page.WaitForFunctionAsync("element => element === document.activeElement", await close.ElementHandleAsync());
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
            if (width <= 1180)
                await page.GetByRole(AriaRole.Button, new() { Name = thai ? "เปิดเมนูนำทาง" : "Open navigation" }).ClickAsync();

            var rail = page.Locator(width <= 1180 ? "#legacy-navigation-rail-drawer" : "#legacy-navigation-rail");
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
            Assert.True(await child.EvaluateAsync<bool>("node => node.getBoundingClientRect().height >= 44"));
            await parent.FocusAsync();
            await page.Keyboard.PressAsync("Tab");
            Assert.True(await child.EvaluateAsync<bool>("node => document.activeElement === node"));

            if (width <= 1180)
                await page.GetByRole(AriaRole.Button, new() { Name = thai ? "ปิดเมนูนำทาง" : "Close navigation" }).ClickAsync();
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
                    logo: rect(topbar.querySelector('.legacy-topbar-logo img:not([aria-hidden=true])')),
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

        var logo = geometry.GetProperty("logo");
        var brand = geometry.GetProperty("brand");
        Assert.True(logo.GetProperty("width").GetDouble() > 0 && logo.GetProperty("height").GetDouble() > 0, geometry.ToString());
        Assert.True(logo.GetProperty("left").GetDouble() >= brand.GetProperty("left").GetDouble(), geometry.ToString());
        Assert.True(logo.GetProperty("right").GetDouble() <= brand.GetProperty("right").GetDouble(), geometry.ToString());
        Assert.True(logo.GetProperty("top").GetDouble() >= brand.GetProperty("top").GetDouble(), geometry.ToString());
        Assert.True(logo.GetProperty("bottom").GetDouble() <= brand.GetProperty("bottom").GetDouble(), geometry.ToString());
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
