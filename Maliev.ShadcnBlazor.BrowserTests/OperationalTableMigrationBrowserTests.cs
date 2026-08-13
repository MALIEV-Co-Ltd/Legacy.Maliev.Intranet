using System.Text.Json;
using Maliev.ShadcnBlazor.BrowserTests.Infrastructure;
using Microsoft.Playwright;

namespace Maliev.ShadcnBlazor.BrowserTests;

[Collection(CustomerBrowserCollection.Name)]
public sealed class OperationalTableMigrationBrowserTests(
    IntranetClientServerFixture server,
    PlaywrightFixture playwright)
{
    public static TheoryData<string, string, string, string> SalesPages => new()
    {
        { "customers", "View customer 101", "/Customers/View?id=101", "Expand customer 101" },
        { "Employees/Index", "View employee 201", "/Employees/View?id=201", "Expand employee 201" },
        { "QuotationRequests/Index", "View quotation request 301", "/QuotationRequests/View?id=301", "Expand quotation request 301" },
        { "Quotations/Index", "View quotation 401", "/Quotations/View?id=401", "Expand quotation 401" },
    };

    [Theory]
    [MemberData(nameof(SalesPages))]
    public async Task SalesPagesUseContainedSemanticOperationalTablesAcrossSupportedWidths(
        string route,
        string detailName,
        string detailHref,
        string expandName)
    {
        await using var context = await playwright.Browser.NewContextAsync(new()
        {
            ViewportSize = new() { Width = 1280, Height = 900 },
            DeviceScaleFactor = 1,
            ReducedMotion = ReducedMotion.Reduce,
        });
        var page = await context.NewPageAsync();
        var errors = CaptureErrors(page);
        await StubSalesBoundariesAsync(page);
        await page.GotoAsync(new Uri(server.BaseUri, route).AbsoluteUri);
        await page.Locator("table.operational-table").WaitForAsync();

        var breadcrumbs = page.Locator("nav.page-breadcrumbs");
        Assert.Equal("/Dashboard", await breadcrumbs.GetByRole(AriaRole.Link).First.GetAttributeAsync("href"));
        Assert.Equal(detailHref, await page.GetByRole(AriaRole.Link, new() { Name = detailName }).GetAttributeAsync("href"));

        foreach (var width in new[] { 1280, 768, 390, 320 })
        {
            await page.SetViewportSizeAsync(width, 844);
            await page.WaitForFunctionAsync("width => document.documentElement.clientWidth === width", width);
            var geometry = await page.EvaluateAsync<JsonElement>("""
                () => ({
                    clientWidth: document.documentElement.clientWidth,
                    scrollWidth: document.documentElement.scrollWidth,
                    offenders: Array.from(document.querySelectorAll('body *')).map(node => {
                        const rect = node.getBoundingClientRect();
                        return { tag: node.tagName, classes: node.className?.baseVal ?? node.className ?? '', left: rect.left, right: rect.right, width: rect.width };
                    }).filter(node => node.left < -0.5 || node.right > document.documentElement.clientWidth + 0.5).slice(0, 12),
                    tableContainers: Array.from(document.querySelectorAll('.operational-table__scroll')).map(node => ({
                        clientWidth: node.clientWidth,
                        scrollWidth: node.scrollWidth,
                        overflowX: getComputedStyle(node).overflowX
                    }))
                })
                """);
            Assert.True(
                geometry.GetProperty("clientWidth").GetInt32() == geometry.GetProperty("scrollWidth").GetInt32(),
                geometry.ToString());
            Assert.All(geometry.GetProperty("tableContainers").EnumerateArray(), container =>
                Assert.Contains(container.GetProperty("overflowX").GetString(), new[] { "auto", "scroll" }));

            Assert.Equal(width > 720, await page.Locator(".operational-table [data-priority='supporting']").First.IsVisibleAsync());
            if (width <= 720)
            {
                foreach (var action in await page.Locator(".operational-table__actions a, .operational-table__actions button").AllAsync())
                {
                    var actionGeometry = await action.EvaluateAsync<JsonElement>(
                        "node => ({ width: node.getBoundingClientRect().width, height: node.getBoundingClientRect().height, tag: node.tagName, classes: node.className?.baseVal ?? node.className ?? '' })");
                    Assert.True(
                        actionGeometry.GetProperty("width").GetDouble() >= 44 && actionGeometry.GetProperty("height").GetDouble() >= 44,
                        actionGeometry.ToString());
                }
            }
        }

        await page.GetByRole(AriaRole.Button, new() { Name = expandName }).ClickAsync();
        Assert.Equal(1, await page.Locator(".operational-table__quick-view").CountAsync());
        await page.Locator(".list-toolbar__refresh").ClickAsync();
        await page.WaitForFunctionAsync("() => document.querySelectorAll('.operational-table__quick-view').length === 0");
        Assert.Empty(errors);
    }

    [Fact]
    public async Task ThaiQuotationKeepsLongLabelsAndBothActionsReachableAt320()
    {
        await using var context = await playwright.Browser.NewContextAsync(new()
        {
            ViewportSize = new() { Width = 320, Height = 844 },
            ReducedMotion = ReducedMotion.Reduce,
            HasTouch = true,
        });
        await context.AddInitScriptAsync("localStorage.setItem('maliev_culture', 'th-TH')");
        var page = await context.NewPageAsync();
        var errors = CaptureErrors(page);
        await StubSalesBoundariesAsync(page);
        await page.GotoAsync(new Uri(server.BaseUri, "Quotations/Index").AbsoluteUri);
        await page.Locator("table.operational-table").WaitForAsync();

        Assert.Equal("/Quotations/View?id=401", await page.GetByRole(AriaRole.Link, new() { Name = "เปิดใบเสนอราคา 401" }).GetAttributeAsync("href"));
        await page.GetByRole(AriaRole.Button, new() { Name = "ขยายรายละเอียดใบเสนอราคา 401" }).ClickAsync();
        Assert.Equal(1, await page.Locator(".operational-table__quick-view").CountAsync());
        Assert.Contains("เงื่อนไขการจัดส่งและชำระเงินสำหรับลูกค้าอุตสาหกรรม", await page.Locator(".operational-table__quick-view").InnerTextAsync());
        Assert.Equal(
            await page.EvaluateAsync<int>("() => document.documentElement.clientWidth"),
            await page.EvaluateAsync<int>("() => document.documentElement.scrollWidth"));
        Assert.Empty(errors);
    }

    [Theory]
    [InlineData("QuotationRequests/Index", "quotation-request-table-summary")]
    [InlineData("Quotations/Index", "quotation-table-caption")]
    public async Task QuotationRecordSummariesDescribeTheNativeOperationalTable(string route, string summaryId)
    {
        await using var context = await playwright.Browser.NewContextAsync();
        var page = await context.NewPageAsync();
        await StubSalesBoundariesAsync(page);
        await page.GotoAsync(new Uri(server.BaseUri, route).AbsoluteUri);

        var table = page.Locator("table.operational-table");
        await table.WaitForAsync();
        Assert.Equal(summaryId, await table.GetAttributeAsync("aria-describedby"));
        Assert.Equal(1, await page.Locator($"#{summaryId}").CountAsync());
        Assert.Equal(0, await page.Locator($"section[aria-describedby='{summaryId}']").CountAsync());
    }

    private static List<string> CaptureErrors(IPage page)
    {
        var errors = new List<string>();
        page.PageError += (_, error) => errors.Add(error);
        page.Console += (_, message) =>
        {
            if (message.Type == "error")
            {
                errors.Add(message.Text);
            }
        };
        return errors;
    }

    private static async Task StubSalesBoundariesAsync(IPage page)
    {
        var session = JsonSerializer.Serialize(new
        {
            isAuthenticated = true,
            employeeId = "sales-browser-employee",
            email = "sales.browser@maliev.com",
            displayName = "Sales Browser Employee",
            roles = new[] { "Employee" },
            csrfToken = "sales-browser-csrf",
            legacyDatabaseId = 201,
            permissions = new[] { "customers.read", "employees.read", "quotation-requests.read", "quotations.read" },
        });
        await page.RouteAsync("**/bff/session", route => route.FulfillAsync(new() { Status = 200, ContentType = "application/json", Body = session }));
        await page.RouteAsync("**/bff/customers?*", route => route.FulfillAsync(new()
        {
            Status = 200,
            ContentType = "application/json",
            Body = "{\"items\":[{\"id\":101,\"firstName\":\"Natthapol\",\"lastName\":\"Vanasrivilai\",\"fullName\":\"Natthapol Vanasrivilai with an intentionally complete customer name\",\"email\":\"natthapol.long.address@maliev.com\",\"company\":{\"id\":71,\"name\":\"MALIEV Precision Manufacturing Company Limited\"}},{\"id\":102,\"firstName\":\"Mali\",\"lastName\":\"Dee\",\"fullName\":\"Mali Dee\",\"email\":\"mali@maliev.com\",\"company\":null}],\"pageIndex\":1,\"totalPages\":2,\"totalRecords\":26,\"hasNextPage\":true,\"hasPreviousPage\":false}",
        }));
        await page.RouteAsync("**/bff/employees?*", route => route.FulfillAsync(new()
        {
            Status = 200,
            ContentType = "application/json",
            Body = "{\"items\":[{\"id\":201,\"firstName\":\"Mali\",\"lastName\":\"Dee\",\"fullName\":\"Mali Dee\",\"email\":\"mali.employee@maliev.com\",\"role\":{\"id\":8,\"name\":\"Sales Engineer\"}},{\"id\":202,\"firstName\":\"Niran\",\"lastName\":\"Chai\",\"fullName\":\"Niran Chai\",\"email\":\"niran@maliev.com\",\"role\":null}],\"pageIndex\":1,\"totalPages\":2,\"totalRecords\":26,\"hasNextPage\":true,\"hasPreviousPage\":false}",
        }));
        await page.RouteAsync("**/bff/quotation-requests?*", route => route.FulfillAsync(new()
        {
            Status = 200,
            ContentType = "application/json",
            Body = "{\"items\":[{\"id\":301,\"firstName\":\"Suda\",\"lastName\":\"Kaew\",\"email\":\"suda@example.com\",\"telephoneNumber\":\"0812345678\",\"country\":\"Thailand\",\"companyName\":\"Thai Industrial Fixture Company\",\"taxIdentification\":\"0100000000001\",\"message\":\"Precision fixture request\",\"internalComment\":\"Priority\",\"done\":false,\"createdDate\":\"2030-08-01T00:00:00Z\",\"modifiedDate\":\"2030-08-02T00:00:00Z\"},{\"id\":302,\"firstName\":\"Somchai\",\"lastName\":\"Dee\",\"email\":null,\"telephoneNumber\":null,\"country\":null,\"companyName\":null,\"taxIdentification\":null,\"message\":null,\"internalComment\":null,\"done\":true,\"createdDate\":\"2030-08-03T00:00:00Z\",\"modifiedDate\":null}],\"pageIndex\":1,\"totalPages\":2,\"totalRecords\":26,\"hasNextPage\":true,\"hasPreviousPage\":false}",
        }));
        await page.RouteAsync("**/bff/quotations/stats", route => route.FulfillAsync(new() { Status = 200, ContentType = "application/json", Body = "{\"accepted\":1,\"declined\":0,\"open\":1}" }));
        await page.RouteAsync("**/bff/catalog/currencies", route => route.FulfillAsync(new() { Status = 200, ContentType = "application/json", Body = "[{\"id\":1,\"shortName\":\"THB\",\"longName\":\"Thai baht\"}]" }));
        await page.RouteAsync("**/bff/quotations?*", route => route.FulfillAsync(new()
        {
            Status = 200,
            ContentType = "application/json",
            Body = "{\"items\":[{\"id\":401,\"customerId\":101,\"employeeId\":201,\"invoiceId\":null,\"period\":30,\"expirationDate\":\"2030-09-30T00:00:00Z\",\"subtotal\":100000.00,\"vat\":7000.00,\"total\":107000.00,\"withholdingTax\":3000.00,\"quotedAmount\":104000.00,\"currencyId\":1,\"comment\":\"fixture\",\"fob\":\"Bangkok\",\"shippedVia\":\"Dedicated industrial courier service\",\"terms\":\"เงื่อนไขการจัดส่งและชำระเงินสำหรับลูกค้าอุตสาหกรรม\",\"accepted\":null,\"createdDate\":\"2030-08-01T00:00:00Z\",\"modifiedDate\":\"2030-08-02T00:00:00Z\"},{\"id\":402,\"customerId\":102,\"employeeId\":202,\"invoiceId\":null,\"period\":14,\"expirationDate\":\"2030-09-15T00:00:00Z\",\"subtotal\":5000.00,\"vat\":350.00,\"total\":5350.00,\"withholdingTax\":null,\"quotedAmount\":5350.00,\"currencyId\":1,\"comment\":null,\"fob\":null,\"shippedVia\":null,\"terms\":null,\"accepted\":true,\"createdDate\":\"2030-08-03T00:00:00Z\",\"modifiedDate\":null}],\"pageIndex\":1,\"totalPages\":2,\"totalRecords\":12,\"hasNextPage\":true,\"hasPreviousPage\":false}",
        }));
    }
}
