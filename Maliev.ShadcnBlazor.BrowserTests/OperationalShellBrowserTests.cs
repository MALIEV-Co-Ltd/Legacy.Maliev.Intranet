using System.Text.Json;
using Maliev.ShadcnBlazor.BrowserTests.Infrastructure;
using Microsoft.Playwright;

namespace Maliev.ShadcnBlazor.BrowserTests;

[Collection(CustomerBrowserCollection.Name)]
public sealed class OperationalShellBrowserTests(
    IntranetClientServerFixture server,
    PlaywrightFixture playwright)
{
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
                Assert.Equal(1, await page.Locator($".legacy-topbar__actions a[href='{href}']").CountAsync());
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
