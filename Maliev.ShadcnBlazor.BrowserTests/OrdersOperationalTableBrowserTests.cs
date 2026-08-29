using System.Text.Json;
using Maliev.ShadcnBlazor.BrowserTests.Infrastructure;
using Microsoft.Playwright;

namespace Maliev.ShadcnBlazor.BrowserTests;

[Collection(CustomerBrowserCollection.Name)]
public sealed class OrdersOperationalTableBrowserTests(
    IntranetClientServerFixture server,
    PlaywrightFixture playwright)
{
    [Fact]
    public async Task OrdersKeepSemanticContainedPriorityTablesAcrossSupportedWidths()
    {
        await using var context = await playwright.Browser.NewContextAsync(new()
        {
            ViewportSize = new() { Width = 1280, Height = 900 },
            DeviceScaleFactor = 1,
            ReducedMotion = ReducedMotion.Reduce,
        });
        var page = await context.NewPageAsync();
        await StubOrdersBoundariesAsync(page);
        await page.GotoAsync(new Uri(server.BaseUri, "sales/orders").AbsoluteUri);
        await page.Locator(".operational-table").First.WaitForAsync();

        await page.EvaluateAsync("""
            () => {
                const decoy = document.createElement('a');
                decoy.href = '/unrelated-operations';
                decoy.setAttribute('aria-label', 'Operations');
                decoy.textContent = 'Operations';
                document.body.append(decoy);
            }
            """);
        var breadcrumbs = page.Locator("nav.page-breadcrumbs");
        Assert.Equal(1, await breadcrumbs.CountAsync());
        Assert.Equal(2, await breadcrumbs.Locator(":scope > ol > li").CountAsync());
        Assert.Equal(1, await breadcrumbs.GetByRole(AriaRole.Link).CountAsync());
        Assert.Equal(
            "/Dashboard",
            await breadcrumbs.GetByRole(AriaRole.Link, new() { Name = "Operations", Exact = true }).GetAttributeAsync("href"));
        var currentCrumb = breadcrumbs.Locator("li[aria-current='page']");
        Assert.Equal(1, await currentCrumb.CountAsync());
        Assert.Equal("Orders", (await currentCrumb.InnerTextAsync()).Trim());
        Assert.Equal(3, await page.Locator("table.operational-table").CountAsync());
        Assert.Equal(3, await page.Locator("table.operational-table caption").CountAsync());
        Assert.Equal(0, await page.Locator(".orders-table, [data-label]").CountAsync());
        var firstHeader = page.Locator(".operational-table thead th").First;
        Assert.Equal("nowrap", await firstHeader.EvaluateAsync<string>("node => getComputedStyle(node).whiteSpace"));
        Assert.True(await firstHeader.EvaluateAsync<double>("node => parseFloat(getComputedStyle(node).paddingInlineStart)") >= 10);
        Assert.True(await page.Locator(".operational-table").First.EvaluateAsync<double>("node => node.getBoundingClientRect().width") >= 1152);

        foreach (var width in new[] { 1280, 768, 390, 320 })
        {
            await page.SetViewportSizeAsync(width, 844);
            await page.WaitForFunctionAsync("width => document.documentElement.clientWidth === width", width);

            var geometry = await page.EvaluateAsync<JsonElement>("""
                () => ({
                    clientWidth: document.documentElement.clientWidth,
                    scrollWidth: document.documentElement.scrollWidth,
                    offenders: Array.from(document.querySelectorAll('body *')).map(element => {
                        const rect = element.getBoundingClientRect();
                        return { tag: element.tagName, classes: element.className?.baseVal ?? element.className ?? '', left: rect.left, right: rect.right, width: rect.width };
                    }).filter(element => element.left < -0.5 || element.right > document.documentElement.clientWidth + 0.5).slice(0, 12),
                    sections: Array.from(document.querySelectorAll('.orders-module-shell, .orders-section')).map(element => ({
                        classes: element.className, overflowX: getComputedStyle(element).overflowX,
                        clientWidth: element.clientWidth, scrollWidth: element.scrollWidth,
                        scope: Array.from(element.attributes).map(attribute => attribute.name).filter(name => name.startsWith('b-'))
                    })),
                    tables: Array.from(document.querySelectorAll('.operational-table__scroll')).map(container => ({
                        clientWidth: container.clientWidth,
                        scrollWidth: container.scrollWidth,
                        overflowX: getComputedStyle(container).overflowX
                    }))
                })
                """);
            Assert.True(
                geometry.GetProperty("clientWidth").GetInt32() == geometry.GetProperty("scrollWidth").GetInt32(),
                geometry.ToString());
            Assert.All(geometry.GetProperty("tables").EnumerateArray(), table =>
                Assert.Contains(table.GetProperty("overflowX").GetString(), new[] { "auto", "scroll" }));

            var supporting = page.Locator(".operational-table [data-priority='supporting']").First;
            Assert.Equal(width > 720, await supporting.IsVisibleAsync());

            foreach (var action in await page.Locator(".operational-table__actions a, .operational-table__actions button").AllAsync())
            {
                if (width <= 720)
                {
                    Assert.True(await action.EvaluateAsync<bool>(
                        "node => node.getBoundingClientRect().width >= 44 && node.getBoundingClientRect().height >= 44"));
                }
            }
        }

        foreach (var selector in new[] { ".orders-id", ".orders-money", ".orders-date", ".orders-quantity" })
        {
            Assert.Equal("nowrap", await page.Locator(selector).First.EvaluateAsync<string>("node => getComputedStyle(node).whiteSpace"));
        }
    }

    [Fact]
    public async Task OrdersToolbarKeepsEveryLabelReadableAtModeledTwoHundredPercentZoom()
    {
        await using var context = await playwright.Browser.NewContextAsync(new()
        {
            ViewportSize = new() { Width = 640, Height = 900 },
            ReducedMotion = ReducedMotion.Reduce,
        });
        var page = await context.NewPageAsync();
        await StubOrdersBoundariesAsync(page);
        await page.GotoAsync(new Uri(server.BaseUri, "sales/orders").AbsoluteUri);
        await page.Locator(".list-toolbar").WaitForAsync();
        await page.EvaluateAsync("document.documentElement.style.zoom = '2'");

        Assert.Equal(1, await page.Locator(".list-toolbar__grid").EvaluateAsync<int>(
            "node => getComputedStyle(node).gridTemplateColumns.split(' ').length"));
        foreach (var label in await page.Locator(".list-toolbar label").AllAsync())
        {
            Assert.True(await label.EvaluateAsync<bool>("node => node.scrollWidth <= node.clientWidth"), await label.InnerTextAsync());
        }
        Assert.Equal(
            await page.EvaluateAsync<int>("() => document.documentElement.clientWidth"),
            await page.EvaluateAsync<int>("() => document.documentElement.scrollWidth"));
    }

    [Fact]
    public async Task OrdersExposeSeparateDetailAndSingleRowQuickViewActionsAndClearOnControls()
    {
        await using var context = await playwright.Browser.NewContextAsync(new()
        {
            ViewportSize = new() { Width = 390, Height = 844 },
            DeviceScaleFactor = 1,
            ReducedMotion = ReducedMotion.Reduce,
        });
        var page = await context.NewPageAsync();
        await StubOrdersBoundariesAsync(page);
        await page.GotoAsync(new Uri(server.BaseUri, "sales/orders").AbsoluteUri);
        await page.Locator(".operational-table").First.WaitForAsync();

        var detail = page.GetByRole(AriaRole.Link, new() { Name = "View order 9101" }).First;
        Assert.Equal("/Orders/View?id=9101", await detail.GetAttributeAsync("href"));

        var expandA = page.GetByRole(AriaRole.Button, new() { Name = "Expand order 9101" }).First;
        await expandA.ClickAsync();
        Assert.Equal(1, await page.Locator(".operational-table__quick-view").CountAsync());
        Assert.Contains("Confidential", await page.Locator(".operational-table__quick-view").InnerTextAsync());
        Assert.Contains("CNC Milling", await page.Locator(".operational-table__quick-view").InnerTextAsync());
        Assert.Contains("ชิ้นส่วนประกอบความเที่ยงตรงสูงสำหรับสายการผลิต", await page.Locator(".operational-table__quick-view").InnerTextAsync());

        await page.GetByRole(AriaRole.Button, new() { Name = "Expand order 9102" }).First.ClickAsync();
        Assert.Equal(1, await page.Locator(".operational-table__quick-view").CountAsync());
        await page.GetByRole(AriaRole.Button, new() { Name = "Collapse order 9102" }).First.ClickAsync();
        Assert.Equal(0, await page.Locator(".operational-table__quick-view").CountAsync());

        await AssertControlClearsExpansionAsync(page, "refresh");
        await AssertControlClearsExpansionAsync(page, "search");
        await AssertControlClearsExpansionAsync(page, "sort");
        await AssertControlClearsExpansionAsync(page, "page");
    }

    private static async Task AssertControlClearsExpansionAsync(IPage page, string control)
    {
        await page.GetByRole(AriaRole.Button, new() { Name = "Expand order 9101" }).First.ClickAsync();
        Assert.Equal(1, await page.Locator(".operational-table__quick-view").CountAsync());

        switch (control)
        {
            case "refresh":
                await page.GetByRole(AriaRole.Button, new() { Name = "Refresh" }).ClickAsync();
                break;
            case "search":
                await page.Locator("#list-toolbar-search").FillAsync("fixture");
                await page.WaitForTimeoutAsync(500);
                break;
            case "sort":
                await page.Locator("#list-toolbar-sort").SelectOptionAsync("OrderId_Ascending");
                break;
            case "page":
                await page.GetByRole(AriaRole.Button, new() { Name = "Next" }).ClickAsync();
                break;
        }

        await page.WaitForFunctionAsync("() => document.querySelectorAll('.operational-table__quick-view').length === 0");
    }

    private static async Task StubOrdersBoundariesAsync(IPage page)
    {
        var session = JsonSerializer.Serialize(new
        {
            isAuthenticated = true,
            employeeId = "orders-browser-employee",
            email = "orders.browser@maliev.com",
            displayName = "Orders Browser Employee",
            roles = new[] { "Employee" },
            csrfToken = "orders-browser-csrf",
            legacyDatabaseId = 7,
            permissions = new[] { "legacy.orders.read", "legacy.orders.create", "legacy.order-catalog.read" },
        });
        var allOrders = """
            {"items":[
              {"id":9101,"customerId":4401,"employeeId":3,"name":"ชิ้นส่วนประกอบความเที่ยงตรงสูงสำหรับสายการผลิต","processId":2,"quantity":12,"manufactured":5,"remaining":7,"subtotal":125000.50,"promisedDate":"2030-09-30T00:00:00","allowSocialMedia":false,"createdDate":"2030-08-01T00:00:00","modifiedDate":"2030-08-02T00:00:00"},
              {"id":9102,"customerId":4402,"employeeId":null,"name":"Long English production fixture name","processId":1,"quantity":4,"manufactured":4,"remaining":0,"subtotal":8900.00,"promisedDate":null,"allowSocialMedia":true,"createdDate":"2030-08-03T00:00:00","modifiedDate":null},
              {"id":9103,"customerId":null,"employeeId":null,"name":null,"processId":2,"quantity":1,"manufactured":0,"remaining":1,"subtotal":null,"promisedDate":"2030-10-01T00:00:00","allowSocialMedia":true,"createdDate":"2030-08-04T00:00:00","modifiedDate":null}
            ],"pageIndex":1,"totalPages":2,"totalRecords":13,"hasNextPage":true,"hasPreviousPage":false}
            """;
        var pendingOrders = """
            {"items":[
              {"id":9101,"customerId":4401,"employeeId":7,"name":"ชิ้นส่วนประกอบความเที่ยงตรงสูงสำหรับสายการผลิต","processId":2,"quantity":12,"manufactured":5,"remaining":7,"subtotal":125000.50,"promisedDate":"2030-09-30T00:00:00","allowSocialMedia":false,"createdDate":"2030-08-01T00:00:00","modifiedDate":"2030-08-02T00:00:00"},
              {"id":9202,"customerId":4502,"employeeId":null,"name":"Awaiting fixture","processId":2,"quantity":3,"manufactured":1,"remaining":2,"subtotal":2200.00,"promisedDate":null,"allowSocialMedia":false,"createdDate":"2030-08-01T00:00:00","modifiedDate":null}
            ],"pageIndex":1,"totalPages":1,"totalRecords":2,"hasNextPage":false,"hasPreviousPage":false}
            """;

        await page.RouteAsync("**/bff/session", route => route.FulfillAsync(new() { Status = 200, ContentType = "application/json", Body = session }));
        await page.RouteAsync("**/bff/orders/pending?*", route => route.FulfillAsync(new() { Status = 200, ContentType = "application/json", Body = pendingOrders }));
        await page.RouteAsync("**/bff/orders?*", route => route.FulfillAsync(new() { Status = 200, ContentType = "application/json", Body = allOrders }));
        await page.RouteAsync("**/bff/order-processes", route => route.FulfillAsync(new()
        {
            Status = 200,
            ContentType = "application/json",
            Body = "[{\"id\":1,\"name\":\"3D Design\"},{\"id\":2,\"name\":\"CNC Milling\"}]",
        }));
        await page.RouteAsync("**/bff/employees?*", route => route.FulfillAsync(new()
        {
            Status = 200,
            ContentType = "application/json",
            Body = "{\"items\":[{\"id\":3,\"firstName\":\"Niran\",\"lastName\":\"Chai\",\"fullName\":\"Niran Chai\",\"email\":\"niran@maliev.com\",\"role\":null},{\"id\":7,\"firstName\":\"Mali\",\"lastName\":\"Dee\",\"fullName\":\"Mali Dee\",\"email\":\"mali@maliev.com\",\"role\":null}],\"pageIndex\":1,\"totalPages\":1,\"totalRecords\":2,\"hasNextPage\":false,\"hasPreviousPage\":false}",
        }));
    }
}
