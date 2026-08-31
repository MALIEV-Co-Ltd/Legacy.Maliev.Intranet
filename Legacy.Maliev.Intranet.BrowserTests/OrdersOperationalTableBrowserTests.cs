using System.Text.Json;
using Legacy.Maliev.Intranet.BrowserTests.Infrastructure;
using Microsoft.Playwright;

namespace Legacy.Maliev.Intranet.BrowserTests;

[Collection(CustomerBrowserCollection.Name)]
public sealed class OrdersOperationalTableBrowserTests(
    IntranetClientServerFixture server,
    PlaywrightFixture playwright)
{
    [Theory]
    [InlineData(1280)]
    [InlineData(768)]
    [InlineData(390)]
    [InlineData(320)]
    public async Task OrdersUseOneContainedReleasedDataTableAcrossSupportedWidths(int width)
    {
        await using var context = await playwright.Browser.NewContextAsync(new()
        {
            ViewportSize = new() { Width = width, Height = 844 },
            ReducedMotion = ReducedMotion.Reduce,
        });
        var page = await context.NewPageAsync();
        await StubOrdersBoundariesAsync(page);
        await page.GotoAsync(new Uri(server.BaseUri, "sales/orders").AbsoluteUri);
        await page.Locator("[data-slot='data-table']").WaitForAsync();

        Assert.Equal(1, await page.Locator("[data-slot='data-table']").CountAsync());
        Assert.Equal(0, await page.Locator("table.operational-table, .list-toolbar").CountAsync());
        Assert.Equal(
            await page.EvaluateAsync<int>("() => document.documentElement.clientWidth"),
            await page.EvaluateAsync<int>("() => document.documentElement.scrollWidth"));
        Assert.Contains(
            await page.Locator(".shadcn-table-container").EvaluateAsync<string>("node => getComputedStyle(node).overflowX"),
            new[] { "auto", "scroll" });
        Assert.Equal("/Orders/View?id=9101", await page.Locator(".operational-data-table__detail").First.GetAttributeAsync("href"));
    }

    [Fact]
    public async Task OrdersExposeWorkingSetSummariesAndAuthoritativePagedRows()
    {
        await using var context = await playwright.Browser.NewContextAsync(new() { ViewportSize = new() { Width = 1280, Height = 900 } });
        var page = await context.NewPageAsync();
        await StubOrdersBoundariesAsync(page);
        await page.GotoAsync(new Uri(server.BaseUri, "sales/orders").AbsoluteUri);
        await page.Locator("[data-slot='data-table']").WaitForAsync();

        Assert.Equal(2, await page.Locator(".orders-working-set").CountAsync());
        Assert.Contains("Page 1 of 2", await page.Locator("[data-slot='data-table-page-summary']").InnerTextAsync());
        Assert.Contains("13", await page.Locator("[data-slot='data-table-selection-summary']").InnerTextAsync());
    }

    [Fact]
    public async Task OrdersQuickViewAndControlsUseReleasedDataTableContracts()
    {
        await using var context = await playwright.Browser.NewContextAsync(new() { ViewportSize = new() { Width = 390, Height = 844 } });
        var page = await context.NewPageAsync();
        await StubOrdersBoundariesAsync(page);
        await page.GotoAsync(new Uri(server.BaseUri, "sales/orders").AbsoluteUri);
        await page.Locator("[data-slot='data-table']").WaitForAsync();

        await page.GetByRole(AriaRole.Button, new() { Name = "Expand order 9101" }).ClickAsync();
        var popover = page.Locator(".operational-data-table__popover");
        await popover.WaitForAsync();
        var details = await popover.InnerTextAsync();
        Assert.Contains("Confidential", details);
        Assert.Contains("CNC Milling", details);
        Assert.Contains("ชิ้นส่วนประกอบความเที่ยงตรงสูงสำหรับสายการผลิต", details);

        await page.Locator(".shadcn-data-table-sort").First.ClickAsync();
        await page.WaitForURLAsync(url => url.Contains("sort=OrderId_Ascending", StringComparison.Ordinal));
        await page.Locator("[data-slot='data-table-filter']").FillAsync("fixture");
        await page.WaitForURLAsync(url => url.Contains("search=fixture", StringComparison.Ordinal));
        await page.Locator("[data-slot='data-table-page-size']").SelectOptionAsync("25");
        await page.WaitForURLAsync(url => url.Contains("size=25", StringComparison.Ordinal));
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
        const string allOrders = """
            {"items":[
              {"id":9101,"customerId":4401,"employeeId":3,"name":"ชิ้นส่วนประกอบความเที่ยงตรงสูงสำหรับสายการผลิต","processId":2,"quantity":12,"manufactured":5,"remaining":7,"subtotal":125000.50,"promisedDate":"2030-09-30T00:00:00","allowSocialMedia":false,"createdDate":"2030-08-01T00:00:00","modifiedDate":"2030-08-02T00:00:00"},
              {"id":9102,"customerId":4402,"employeeId":null,"name":"Long English production fixture name","processId":1,"quantity":4,"manufactured":4,"remaining":0,"subtotal":8900.00,"promisedDate":null,"allowSocialMedia":true,"createdDate":"2030-08-03T00:00:00","modifiedDate":null},
              {"id":9103,"customerId":null,"employeeId":null,"name":null,"processId":2,"quantity":1,"manufactured":0,"remaining":1,"subtotal":null,"promisedDate":"2030-10-01T00:00:00","allowSocialMedia":true,"createdDate":"2030-08-04T00:00:00","modifiedDate":null}
            ],"pageIndex":1,"totalPages":2,"totalRecords":13,"hasNextPage":true,"hasPreviousPage":false}
            """;
        const string pendingOrders = """
            {"items":[
              {"id":9101,"customerId":4401,"employeeId":7,"name":"ชิ้นส่วนประกอบความเที่ยงตรงสูงสำหรับสายการผลิต","processId":2,"quantity":12,"manufactured":5,"remaining":7,"subtotal":125000.50,"promisedDate":"2030-09-30T00:00:00","allowSocialMedia":false,"createdDate":"2030-08-01T00:00:00","modifiedDate":"2030-08-02T00:00:00"},
              {"id":9202,"customerId":4502,"employeeId":null,"name":"Awaiting fixture","processId":2,"quantity":3,"manufactured":1,"remaining":2,"subtotal":2200.00,"promisedDate":null,"allowSocialMedia":false,"createdDate":"2030-08-01T00:00:00","modifiedDate":null}
            ],"pageIndex":1,"totalPages":1,"totalRecords":2,"hasNextPage":false,"hasPreviousPage":false}
            """;

        await page.RouteAsync("**/bff/session", route => route.FulfillAsync(new() { Status = 200, ContentType = "application/json", Body = session }));
        await page.RouteAsync("**/bff/orders/pending?*", route => route.FulfillAsync(new() { Status = 200, ContentType = "application/json", Body = pendingOrders }));
        await page.RouteAsync("**/bff/orders?*", route => route.FulfillAsync(new() { Status = 200, ContentType = "application/json", Body = allOrders }));
        await page.RouteAsync("**/bff/order-processes", route => route.FulfillAsync(new() { Status = 200, ContentType = "application/json", Body = "[{\"id\":1,\"name\":\"3D Design\"},{\"id\":2,\"name\":\"CNC Milling\"}]" }));
        await page.RouteAsync("**/bff/employees?*", route => route.FulfillAsync(new()
        {
            Status = 200,
            ContentType = "application/json",
            Body = "{\"items\":[{\"id\":3,\"firstName\":\"Niran\",\"lastName\":\"Chai\",\"fullName\":\"Niran Chai\",\"email\":\"niran@maliev.com\",\"role\":null},{\"id\":7,\"firstName\":\"Mali\",\"lastName\":\"Dee\",\"fullName\":\"Mali Dee\",\"email\":\"mali@maliev.com\",\"role\":null}],\"pageIndex\":1,\"totalPages\":1,\"totalRecords\":2,\"hasNextPage\":false,\"hasPreviousPage\":false}",
        }));
    }
}
