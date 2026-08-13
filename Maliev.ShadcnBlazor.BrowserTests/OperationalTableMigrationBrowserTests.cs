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

    public static TheoryData<string, string?, string> OperationalWavePages => new()
    {
        { "Invoices/Index", "/Invoices/View?id=501", "Expand invoice 501" },
        { "PurchaseOrders/Index", "/PurchaseOrders/View?id=601", "Expand purchase order 601" },
        { "Materials/Index", "/Materials/View?id=701", "Expand material 701" },
        { "Server/ErrorReport", null, "Expand diagnostic 801" },
    };

    public static TheoryData<string, string> AliasLandingBreadcrumbs => new()
    {
        { "Invoices/Index", "Invoices" },
        { "PurchaseOrders/Index", "Purchase orders" },
        { "Materials/Index", "Materials" },
    };

    [Theory]
    [MemberData(nameof(AliasLandingBreadcrumbs))]
    public async Task AliasLandingPagesRenderDashboardThenCurrentPageOnly(string route, string currentLabel)
    {
        await using var context = await playwright.Browser.NewContextAsync();
        var page = await context.NewPageAsync();
        await StubOperationalWaveBoundariesAsync(page);
        await page.GotoAsync(new Uri(server.BaseUri, route).AbsoluteUri);
        var breadcrumbs = page.Locator("nav.page-breadcrumbs");
        await breadcrumbs.WaitForAsync();

        Assert.Equal(2, await breadcrumbs.Locator("li").CountAsync());
        Assert.Equal("/Dashboard", await breadcrumbs.GetByRole(AriaRole.Link).GetAttributeAsync("href"));
        Assert.Equal(currentLabel, (await breadcrumbs.Locator("li[aria-current='page']").InnerTextAsync()).Trim());
    }

    [Theory]
    [MemberData(nameof(OperationalWavePages))]
    public async Task OperationalWaveUsesContainedTablesExactRoutesAndSingleQuickView(
        string route, string? detailHref, string expandName)
    {
        await using var context = await playwright.Browser.NewContextAsync(new()
        {
            ViewportSize = new() { Width = 1280, Height = 900 },
            ReducedMotion = ReducedMotion.Reduce,
        });
        var page = await context.NewPageAsync();
        var errors = CaptureErrors(page);
        await StubOperationalWaveBoundariesAsync(page);
        await page.GotoAsync(new Uri(server.BaseUri, route).AbsoluteUri);
        await page.Locator("table.operational-table").First.WaitForAsync();

        Assert.Equal("/Dashboard", await page.Locator("nav.page-breadcrumbs").GetByRole(AriaRole.Link).First.GetAttributeAsync("href"));
        var actionLinks = page.Locator(".operational-table__actions a");
        if (detailHref is null)
        {
            Assert.Equal(0, await actionLinks.CountAsync());
        }
        else
        {
            Assert.Equal(detailHref, await actionLinks.First.GetAttributeAsync("href"));
        }

        foreach (var width in new[] { 1280, 768, 390, 320 })
        {
            await page.SetViewportSizeAsync(width, 844);
            await page.WaitForFunctionAsync("width => document.documentElement.clientWidth === width", width);
            await page.WaitForFunctionAsync("() => document.documentElement.scrollWidth === document.documentElement.clientWidth");
            Assert.Equal(
                await page.EvaluateAsync<int>("() => document.documentElement.clientWidth"),
                await page.EvaluateAsync<int>("() => document.documentElement.scrollWidth"));
            Assert.All(await page.Locator(".operational-table__scroll").AllAsync(), async table =>
                Assert.Contains(await table.EvaluateAsync<string>("node => getComputedStyle(node).overflowX"), new[] { "auto", "scroll" }));
            if (width <= 720)
            {
                foreach (var action in await page.Locator(".operational-table__actions a, .operational-table__actions button").AllAsync())
                {
                    var size = await action.EvaluateAsync<JsonElement>("node => ({ width: node.getBoundingClientRect().width, height: node.getBoundingClientRect().height })");
                    Assert.True(size.GetProperty("width").GetDouble() >= 44 && size.GetProperty("height").GetDouble() >= 44, size.ToString());
                }
            }
        }

        await page.GetByRole(AriaRole.Button, new() { Name = expandName }).First.ClickAsync();
        Assert.Equal(1, await page.Locator(".operational-table__quick-view").CountAsync());
        var secondToggle = page.Locator(".operational-table__toggle").Nth(1);
        if (await secondToggle.CountAsync() == 1)
        {
            await secondToggle.ClickAsync();
            Assert.Equal(1, await page.Locator(".operational-table__quick-view").CountAsync());
        }
        foreach (var atomic in await page.Locator(".operational-table .mlv-mono").AllAsync())
        {
            Assert.Equal("nowrap", await atomic.EvaluateAsync<string>("node => getComputedStyle(node).whiteSpace"));
        }
        Assert.Empty(errors);
    }

    [Fact]
    public async Task ThaiOperationalWaveKeepsLongDiagnosticActionsReachableAt320()
    {
        await using var context = await playwright.Browser.NewContextAsync(new()
        {
            ViewportSize = new() { Width = 320, Height = 844 },
            HasTouch = true,
        });
        await context.AddInitScriptAsync("localStorage.setItem('maliev_culture', 'th-TH')");
        var page = await context.NewPageAsync();
        var errors = CaptureErrors(page);
        await StubOperationalWaveBoundariesAsync(page);
        await page.GotoAsync(new Uri(server.BaseUri, "Server/ErrorReport").AbsoluteUri);
        await page.Locator("table.operational-table").WaitForAsync();
        await page.GetByRole(AriaRole.Button, new() { Name = "ขยายเหตุการณ์ 801" }).ClickAsync();
        Assert.Equal(1, await page.Locator(".operational-table__quick-view").CountAsync());
        Assert.Equal(0, await page.Locator(".operational-table__actions a").CountAsync());
        Assert.Equal(await page.EvaluateAsync<int>("() => document.documentElement.clientWidth"), await page.EvaluateAsync<int>("() => document.documentElement.scrollWidth"));
        Assert.Empty(errors);
    }

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

    [Fact]
    public async Task CompactDashboardTableRemainsSemanticContainedAtomicAndKeyboardReachableAt320()
    {
        await using var context = await playwright.Browser.NewContextAsync(new()
        {
            ViewportSize = new() { Width = 320, Height = 844 },
            HasTouch = true,
            ReducedMotion = ReducedMotion.Reduce,
        });
        var page = await context.NewPageAsync();
        var errors = CaptureErrors(page);
        await StubSpecializedTableBoundariesAsync(page);
        await page.GotoAsync(new Uri(server.BaseUri, "Dashboard").AbsoluteUri);
        var tables = page.Locator(".dashboard-table table");
        await tables.First.WaitForAsync();
        var expectedRecords = new[]
        {
            (Projection: "dashboard-orders", AccessibleName: "Order #901", Href: "/sales/orders/901"),
            (Projection: "dashboard-quotations", AccessibleName: "Quote #902", Href: "/Quotations/View?id=902"),
            (Projection: "dashboard-payments", AccessibleName: "Payment #903", Href: "/Finances/View?id=903"),
            (Projection: "dashboard-customers", AccessibleName: "Mali Dee", Href: "/Customers/View?id=69738"),
        };

        var breadcrumbs = page.Locator("nav.page-breadcrumbs");
        Assert.Equal(1, await breadcrumbs.Locator("li").CountAsync());
        Assert.Equal("Dashboard", (await breadcrumbs.Locator("[aria-current='page']").InnerTextAsync()).Trim());
        Assert.Equal(4, await tables.CountAsync());
        foreach (var table in await tables.AllAsync())
        {
            Assert.Equal("table", await table.EvaluateAsync<string>("node => getComputedStyle(node).display"));
            Assert.NotEqual("none", await table.Locator("thead").EvaluateAsync<string>("node => getComputedStyle(node).display"));
            var scroller = table.Locator("xpath=ancestor::div[contains(@class,'dashboard-table-scroll')]");
            Assert.Contains(await scroller.EvaluateAsync<string>("node => getComputedStyle(node).overflowX"), new[] { "auto", "scroll" });
            Assert.True(await scroller.EvaluateAsync<bool>("node => node.scrollWidth > node.clientWidth"));
        }
        Assert.Equal(
            await page.EvaluateAsync<int>("() => document.documentElement.clientWidth"),
            await page.EvaluateAsync<int>("() => document.documentElement.scrollWidth"));
        foreach (var atomic in await tables.Locator(".mlv-mono").AllAsync())
        {
            Assert.Equal("nowrap", await atomic.EvaluateAsync<string>("node => getComputedStyle(node).whiteSpace"));
        }
        foreach (var expected in expectedRecords)
        {
            var table = page.Locator($"[data-projection='{expected.Projection}']:is(table), [data-projection='{expected.Projection}'] table");
            Assert.Equal(1, await table.CountAsync());
            var record = table.GetByRole(AriaRole.Link, new() { Name = expected.AccessibleName, Exact = true });
            Assert.Equal(expected.Href, await record.GetAttributeAsync("href"));
            var bounds = await record.BoundingBoxAsync();
            Assert.NotNull(bounds);
            Assert.True(bounds!.Width >= 44, $"{expected.Projection} record link width was {bounds.Width}px.");
            Assert.True(bounds.Height >= 44, $"{expected.Projection} record link height was {bounds.Height}px.");
            await record.FocusAsync();
            Assert.True(await record.EvaluateAsync<bool>("node => document.activeElement === node"));
        }
        Assert.Equal(0, await page.Locator(".operational-table__toggle, .operational-table__quick-view").CountAsync());
        Assert.Empty(errors);
    }

    [Theory]
    [InlineData("orders", "history-orders", "/Orders/View?id=901")]
    [InlineData("quotations", "history-quotations", "/Quotations/View?id=902")]
    [InlineData("invoices", "history-invoices", "/Invoices/View?id=903")]
    public async Task SpecializedCustomerHistoryMapsCompleteDtoInThaiAt320(
        string tab,
        string projection,
        string expectedHref)
    {
        await using var context = await playwright.Browser.NewContextAsync(new()
        {
            ViewportSize = new() { Width = 320, Height = 844 },
            HasTouch = true,
            ReducedMotion = ReducedMotion.Reduce,
        });
        await context.AddInitScriptAsync("localStorage.setItem('maliev_culture', 'th-TH')");
        var page = await context.NewPageAsync();
        var errors = CaptureErrors(page);
        await StubSpecializedTableBoundariesAsync(page);
        await page.GotoAsync(new Uri(server.BaseUri, $"Customers/View?id=69738&tab={tab}").AbsoluteUri);
        var table = page.Locator($"[data-projection='{projection}'] table");
        await table.WaitForAsync();

        var mapped = await table.Locator("[data-field]").EvaluateAllAsync<string[]>(
            "nodes => [...new Set(nodes.flatMap(node => node.dataset.field.split(/\\s+/).filter(Boolean)))]");
        var expectedFields = tab switch
        {
            "orders" => new[] { "Id", "CustomerId", "EmployeeId", "Name", "ProcessId", "Quantity", "Manufactured", "Remaining", "Subtotal", "PromisedDate", "AllowSocialMedia", "CreatedDate", "ModifiedDate" },
            "quotations" => new[] { "Id", "CustomerId", "EmployeeId", "InvoiceId", "Period", "ExpirationDate", "Subtotal", "Vat", "Total", "WithholdingTax", "QuotedAmount", "CurrencyId", "Comment", "Fob", "ShippedVia", "Terms", "Accepted", "CreatedDate", "ModifiedDate" },
            "invoices" => new[] { "Id", "CustomerId", "Number", "Currency", "PurchaseOrderNumber", "Subtotal", "Vat", "Total", "WithholdingTax", "Outstanding", "IsPaid", "ReceiptId", "PaymentDate", "CreatedDate" },
            _ => throw new InvalidOperationException($"Unsupported history tab '{tab}'."),
        };
        Assert.Equal(expectedFields.Order(StringComparer.Ordinal), mapped.Order(StringComparer.Ordinal));
        var scroller = page.Locator($"[data-projection='{projection}'] .mud-table-container");
        Assert.True(await scroller.EvaluateAsync<bool>("node => node.scrollWidth > node.clientWidth"));
        Assert.Equal(
            await page.EvaluateAsync<int>("() => document.documentElement.clientWidth"),
            await page.EvaluateAsync<int>("() => document.documentElement.scrollWidth"));
        foreach (var status in await table.Locator(".history-status").AllAsync())
        {
            Assert.Equal("nowrap", await status.EvaluateAsync<string>("node => getComputedStyle(node).whiteSpace"));
        }
        var record = table.Locator($"a[href='{expectedHref}']");
        var box = await record.BoundingBoxAsync();
        Assert.NotNull(box);
        Assert.True(box!.Width >= 44 && box.Height >= 44, $"Expected 44x44 record target, found {box.Width:F2}x{box.Height:F2}.");
        await record.FocusAsync();
        Assert.True(await record.EvaluateAsync<bool>("node => document.activeElement === node"));
        Assert.Equal(0, await page.Locator(".operational-table__toggle, .operational-table__quick-view").CountAsync());
        Assert.Empty(errors);
    }

    [Fact]
    public async Task SpecializedCustomerHistoryRemainsSemanticContainedAndUsesExactBreadcrumbsAt320()
    {
        await using var context = await playwright.Browser.NewContextAsync(new()
        {
            ViewportSize = new() { Width = 320, Height = 844 },
            HasTouch = true,
            ReducedMotion = ReducedMotion.Reduce,
        });
        var page = await context.NewPageAsync();
        var errors = CaptureErrors(page);
        await StubSpecializedTableBoundariesAsync(page);
        await page.GotoAsync(new Uri(server.BaseUri, "Customers/View?id=69738&tab=orders").AbsoluteUri);
        var table = page.Locator(".customer-history table");
        await table.WaitForAsync();

        var breadcrumbs = page.Locator("nav.page-breadcrumbs");
        Assert.Equal(2, await breadcrumbs.Locator("li").CountAsync());
        Assert.Equal("/customers", await breadcrumbs.GetByRole(AriaRole.Link, new() { Name = "Customers" }).GetAttributeAsync("href"));
        Assert.Equal("Mali Dee", (await breadcrumbs.Locator("[aria-current='page']").InnerTextAsync()).Trim());
        Assert.Equal("table", await table.EvaluateAsync<string>("node => getComputedStyle(node).display"));
        Assert.NotEqual("none", await table.Locator("thead").EvaluateAsync<string>("node => getComputedStyle(node).display"));
        var scroller = page.Locator(".customer-history .mud-table-container");
        Assert.Contains(await scroller.EvaluateAsync<string>("node => getComputedStyle(node).overflowX"), new[] { "auto", "scroll" });
        Assert.Equal(
            await page.EvaluateAsync<int>("() => document.documentElement.clientWidth"),
            await page.EvaluateAsync<int>("() => document.documentElement.scrollWidth"));
        var record = page.GetByRole(AriaRole.Link, new() { Name = "View order 901" });
        Assert.Equal("/Orders/View?id=901", await record.GetAttributeAsync("href"));
        Assert.True(await record.EvaluateAsync<double>("node => node.getBoundingClientRect().height") >= 44);
        await record.FocusAsync();
        Assert.True(await record.EvaluateAsync<bool>("node => document.activeElement === node"));
        Assert.Equal(0, await page.Locator(".operational-table__toggle, .operational-table__quick-view").CountAsync());
        Assert.Empty(errors);
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

    private static async Task StubOperationalWaveBoundariesAsync(IPage page)
    {
        var session = JsonSerializer.Serialize(new
        {
            isAuthenticated = true,
            employeeId = "operations-browser-employee",
            email = "operations.browser@maliev.com",
            displayName = "Operations Browser Employee",
            roles = new[] { "Employee" },
            csrfToken = "operations-browser-csrf",
            legacyDatabaseId = 201,
            permissions = new[] { "accounting.read", "purchase-orders.read", "suppliers.read", "materials.read", "diagnostics.read" },
        });
        await page.RouteAsync("**/bff/session", route => route.FulfillAsync(new() { Status = 200, ContentType = "application/json", Body = session }));
        await page.RouteAsync("**/bff/invoices?*", route => route.FulfillAsync(new()
        {
            Status = 200,
            ContentType = "application/json",
            Body = route.Request.Url.Contains("paid=true", StringComparison.Ordinal)
                ? "{\"items\":[{\"id\":502,\"customerId\":102,\"number\":\"INV-502\",\"currency\":\"THB\",\"purchaseOrderNumber\":\"PO-502\",\"subtotal\":2000,\"vat\":140,\"total\":2140,\"withholdingTax\":60,\"outstanding\":0,\"isPaid\":true,\"receiptId\":902,\"paymentDate\":\"2030-08-02T00:00:00Z\",\"createdDate\":\"2030-08-01T00:00:00Z\"}],\"pageIndex\":1,\"totalPages\":1,\"totalRecords\":1,\"hasNextPage\":false,\"hasPreviousPage\":false}"
                : "{\"items\":[{\"id\":501,\"customerId\":101,\"number\":\"INV-501\",\"currency\":\"THB\",\"purchaseOrderNumber\":\"PO-501-LONG\",\"subtotal\":1000,\"vat\":70,\"total\":1070,\"withholdingTax\":30,\"outstanding\":1040,\"isPaid\":false,\"receiptId\":null,\"paymentDate\":null,\"createdDate\":\"2030-08-01T00:00:00Z\"}],\"pageIndex\":1,\"totalPages\":1,\"totalRecords\":1,\"hasNextPage\":false,\"hasPreviousPage\":false}",
        }));
        await page.RouteAsync("**/bff/purchase-orders?*", route => route.FulfillAsync(new() { Status = 200, ContentType = "application/json", Body = "{\"items\":[{\"id\":601,\"employeeId\":201,\"fob\":\"Bangkok\",\"terms\":\"Net 30 industrial procurement terms\",\"shippingMethod\":\"Dedicated courier\",\"createdDate\":\"2030-08-01T00:00:00Z\"},{\"id\":602,\"employeeId\":202,\"fob\":\"Rayong\",\"terms\":\"Net 15\",\"shippingMethod\":\"Freight\",\"createdDate\":\"2030-08-02T00:00:00Z\"}],\"pageIndex\":1,\"totalPages\":1,\"totalRecords\":2,\"hasNextPage\":false,\"hasPreviousPage\":false}" }));
        await page.RouteAsync("**/bff/employees?*", route => route.FulfillAsync(new() { Status = 200, ContentType = "application/json", Body = "{\"items\":[{\"id\":201,\"fullName\":\"Mali Dee\"},{\"id\":202,\"fullName\":\"Somchai Chai\"}],\"pageIndex\":1,\"totalPages\":1,\"totalRecords\":2,\"hasNextPage\":false,\"hasPreviousPage\":false}" }));
        await page.RouteAsync("**/bff/catalog/materials?*", route => route.FulfillAsync(new() { Status = 200, ContentType = "application/json", Body = "{\"items\":[{\"id\":701,\"materialNumber\":\"AL-6061-T6\",\"name\":\"Aluminium 6061-T6\",\"densityKilogramPerCubicMeter\":2700,\"machinable\":true,\"printable\":false,\"materialGroup\":{\"id\":1,\"name\":\"Metals\"}},{\"id\":702,\"materialNumber\":\"PA12\",\"name\":\"Nylon PA12\",\"densityKilogramPerCubicMeter\":1020,\"machinable\":false,\"printable\":true,\"materialGroup\":{\"id\":2,\"name\":\"Polymers\"}}],\"pageIndex\":1,\"totalPages\":1,\"totalRecords\":2,\"hasNextPage\":false,\"hasPreviousPage\":false}" }));
        await page.RouteAsync("**/bff/diagnostics/events?*", route => route.FulfillAsync(new() { Status = 200, ContentType = "application/json", Body = "{\"items\":[{\"id\":801,\"level\":\"Error\",\"code\":\"BFF_TIMEOUT\",\"category\":\"Integration\",\"path\":\"/bff/operations/long-diagnostic-path\",\"correlationId\":\"corr-801-atomic\",\"timestamp\":\"2030-08-01T10:30:00Z\"},{\"id\":802,\"level\":\"Warning\",\"code\":\"RETRY\",\"category\":\"Integration\",\"path\":\"/bff/operations\",\"correlationId\":\"corr-802\",\"timestamp\":\"2030-08-01T10:31:00Z\"}],\"pageIndex\":1,\"totalPages\":1,\"totalRecords\":2,\"hasNextPage\":false,\"hasPreviousPage\":false}" }));
    }

    private static async Task StubSpecializedTableBoundariesAsync(IPage page)
    {
        var session = JsonSerializer.Serialize(new
        {
            isAuthenticated = true,
            employeeId = "specialized-table-browser-employee",
            displayName = "Specialized Table Browser Employee",
            roles = new[] { "Employee" },
            csrfToken = "specialized-table-browser-csrf",
            legacyDatabaseId = 1,
            permissions = new[] { "legacy-customer.customers.read", "legacy.orders.read", "legacy.quotations.read", "legacy.accounting.read" },
        });
        await page.RouteAsync("**/bff/session", route => route.FulfillAsync(new() { Status = 200, ContentType = "application/json", Body = session }));
        await page.RouteAsync("**/bff/dashboard", route => route.FulfillAsync(new()
        {
            Status = 200,
            ContentType = "application/json",
            Body = JsonSerializer.Serialize(new
            {
                generatedAt = "2030-08-13T10:00:00+07:00",
                cards = Array.Empty<object>(),
                degradedSources = Array.Empty<string>(),
                recentOrders = new[] { new { id = 901, name = "Precision fixture", quantity = 4, manufactured = 1, remaining = 3, promisedDate = "2030-08-20T00:00:00Z", navigateTo = "/sales/orders/901" } },
                recentQuotations = new[] { new { id = 902, total = 1200m, quotedAmount = 1100m, currencyId = 1, expirationDate = "2030-09-01T00:00:00Z", accepted = (bool?)null, createdDate = "2030-08-02T00:00:00Z", navigateTo = "/Quotations/View?id=902" } },
                recentCustomers = new[] { new { id = 69738, fullName = "Mali Dee", email = "mali@maliev.com", company = "MALIEV", navigateTo = "/Customers/View?id=69738" } },
                recentPayments = new[] { new { id = 903, amount = 900m, currencyId = (int?)1, recipient = "Mali Dee", paymentDate = "2030-08-04T00:00:00Z", createdDate = "2030-08-03T00:00:00Z", navigateTo = "/Finances/View?id=903" } },
                recentActivity = Array.Empty<object>(),
                monthlyFinance = Array.Empty<object>(),
                quotationSummary = (object?)null,
            }),
        }));
        await page.RouteAsync("**/bff/customers/69738", route => route.FulfillAsync(new()
        {
            Status = 200,
            ContentType = "application/json",
            Body = JsonSerializer.Serialize(new
            {
                id = 69738,
                firstName = "Mali",
                lastName = "Dee",
                fullName = "Mali Dee",
                email = "mali@maliev.com",
                telephone = (string?)null,
                mobile = "0812345678",
                fax = (string?)null,
                dateOfBirth = (string?)null,
                companyId = (int?)null,
                billingAddressId = (int?)null,
                shippingAddressId = (int?)null,
                createdDate = "2030-08-01T00:00:00Z",
                modifiedDate = "2030-08-02T00:00:00Z",
                company = (object?)null,
                billingAddress = (object?)null,
                shippingAddress = (object?)null,
            }),
        }));
        await page.RouteAsync("**/bff/customers/69738/orders*", route => route.FulfillAsync(new()
        {
            Status = 200,
            ContentType = "application/json",
            Body = JsonSerializer.Serialize(new
            {
                items = new[] { new { id = 901, customerId = 69738, employeeId = 1, name = "Precision fixture", processId = 1, quantity = 4, manufactured = 1, remaining = 3, subtotal = (decimal?)null, promisedDate = "2030-08-20T00:00:00Z", allowSocialMedia = false, createdDate = "2030-08-01T00:00:00Z", modifiedDate = (string?)null } },
                pageIndex = 1,
                totalPages = 1,
                totalRecords = 1,
                hasNextPage = false,
                hasPreviousPage = false,
            }),
        }));
        await page.RouteAsync("**/bff/customers/69738/quotations*", route => route.FulfillAsync(new()
        {
            Status = 200,
            ContentType = "application/json",
            Body = JsonSerializer.Serialize(new
            {
                items = new[] { new { id = 902, customerId = 69738, employeeId = 1, invoiceId = (int?)null, period = 30, expirationDate = "2030-09-01T00:00:00Z", subtotal = 1000m, vat = 70m, total = 1070m, withholdingTax = (decimal?)null, quotedAmount = 1050m, currencyId = 1, comment = "Precision quote", fob = "Bangkok", shippedVia = "Courier", terms = "Net 30", accepted = (bool?)null, createdDate = "2030-08-01T00:00:00Z", modifiedDate = "2030-08-02T00:00:00Z" } },
                pageIndex = 1,
                totalPages = 1,
                totalRecords = 1,
                hasNextPage = false,
                hasPreviousPage = false,
            }),
        }));
        await page.RouteAsync("**/bff/customers/69738/invoices*", route => route.FulfillAsync(new()
        {
            Status = 200,
            ContentType = "application/json",
            Body = JsonSerializer.Serialize(new
            {
                items = new[] { new { id = 903, customerId = 69738, number = "INV-903", currency = "THB", purchaseOrderNumber = "PO-1", subtotal = 1000m, vat = 70m, total = 1070m, withholdingTax = (decimal?)null, outstanding = 1070m, isPaid = false, receiptId = (int?)null, paymentDate = (string?)null, createdDate = "2030-08-01T00:00:00Z" } },
                pageIndex = 1,
                totalPages = 1,
                totalRecords = 1,
                hasNextPage = false,
                hasPreviousPage = false,
            }),
        }));
    }
}
