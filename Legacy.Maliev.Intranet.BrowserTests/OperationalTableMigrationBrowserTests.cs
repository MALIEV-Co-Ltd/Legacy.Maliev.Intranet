using System.Text.Json;
using Legacy.Maliev.Intranet.BrowserTests.Infrastructure;
using Microsoft.Playwright;

namespace Legacy.Maliev.Intranet.BrowserTests;

[Collection(CustomerBrowserCollection.Name)]
public sealed class OperationalTableMigrationBrowserTests(
    IntranetClientServerFixture server,
    PlaywrightFixture playwright)
{
    [Fact]
    public async Task EmployeeListUsesTheReleasedDataTableContract()
    {
        await using var context = await playwright.Browser.NewContextAsync(new()
        {
            ViewportSize = new() { Width = 1280, Height = 900 },
            ReducedMotion = ReducedMotion.Reduce,
        });
        var page = await context.NewPageAsync();
        var errors = CaptureErrors(page);
        await StubSalesBoundariesAsync(page);

        await page.GotoAsync(new Uri(server.BaseUri, "Employees/Index").AbsoluteUri);

        var dataTable = page.Locator("[data-slot='data-table']");
        await dataTable.WaitForAsync();
        Assert.Equal(1, await dataTable.CountAsync());
        Assert.Equal(0, await page.Locator("table.operational-table, .list-toolbar").CountAsync());
        Assert.Equal("Search", await dataTable.Locator("[data-slot='data-table-filter']").GetAttributeAsync("aria-label"));
        Assert.Equal("25", await dataTable.Locator("[data-slot='data-table-page-size']").InputValueAsync());
        Assert.Contains("Page 1 of 2", await dataTable.Locator("[data-slot='data-table-page-summary']").InnerTextAsync());
        Assert.Equal("/Employees/View?id=201", await page.GetByRole(AriaRole.Link, new() { Name = "View employee 201" }).GetAttributeAsync("href"));

        await page.GetByRole(AriaRole.Button, new() { Name = "Expand employee 201" }).ClickAsync();
        var detail = page.Locator("[data-slot='popover-content']");
        await detail.WaitForAsync();
        Assert.Contains("มาลี ดี ผู้เชี่ยวชาญงานขายอุตสาหกรรม", await detail.InnerTextAsync());
        Assert.Empty(errors);
    }

    [Fact]
    public async Task EmployeeSortingUpdatesRowsWithoutRefreshingTheBlazorPage()
    {
        await using var context = await playwright.Browser.NewContextAsync(new()
        {
            ViewportSize = new() { Width = 1280, Height = 900 },
            ReducedMotion = ReducedMotion.Reduce,
        });
        var page = await context.NewPageAsync();
        await StubSalesBoundariesAsync(page);
        await page.UnrouteAsync("**/bff/employees?*");
        var sortedRequest = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        await page.RouteAsync("**/bff/employees?*", route =>
        {
            var ascending = route.Request.Url.Contains("sort=EmployeeId_Ascending", StringComparison.Ordinal);
            if (ascending)
            {
                sortedRequest.TrySetResult(route.Request.Url);
            }
            var items = ascending
                ? "[{\"id\":201,\"firstName\":\"Mali\",\"lastName\":\"Dee\",\"fullName\":\"Mali Dee\",\"email\":\"mali@maliev.com\",\"role\":null},{\"id\":202,\"firstName\":\"Niran\",\"lastName\":\"Chai\",\"fullName\":\"Niran Chai\",\"email\":\"niran@maliev.com\",\"role\":null}]"
                : "[{\"id\":202,\"firstName\":\"Niran\",\"lastName\":\"Chai\",\"fullName\":\"Niran Chai\",\"email\":\"niran@maliev.com\",\"role\":null},{\"id\":201,\"firstName\":\"Mali\",\"lastName\":\"Dee\",\"fullName\":\"Mali Dee\",\"email\":\"mali@maliev.com\",\"role\":null}]";
            return route.FulfillAsync(new()
            {
                Status = 200,
                ContentType = "application/json",
                Body = $"{{\"items\":{items},\"pageIndex\":1,\"totalPages\":1,\"totalRecords\":2,\"hasNextPage\":false,\"hasPreviousPage\":false}}",
            });
        });

        await page.GotoAsync(new Uri(server.BaseUri, "Employees/Index").AbsoluteUri);
        var firstId = page.Locator("[data-slot='data-table'] tbody tr td").First;
        await firstId.WaitForAsync();
        Assert.Equal("202", (await firstId.InnerTextAsync()).Trim());

        var documentRequests = 0;
        page.Request += (_, request) =>
        {
            if (request.ResourceType == "document")
            {
                Interlocked.Increment(ref documentRequests);
            }
        };
        var idSort = page.Locator(".shadcn-data-table-sort").First;
        await idSort.ClickAsync();
        var sortedUrl = await sortedRequest.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Contains("sort=EmployeeId_Ascending", sortedUrl, StringComparison.Ordinal);
        await Assertions.Expect(firstId).ToHaveTextAsync("201", new() { Timeout = 5_000 });
        Assert.Contains("sort=EmployeeId_Ascending", page.Url, StringComparison.Ordinal);
        Assert.True(await idSort.EvaluateAsync<bool>("node => document.activeElement === node"));
        Assert.Equal(0, documentRequests);
    }

    [Fact]
    public async Task DashboardRefreshesItsSnapshotWithoutManualInteraction()
    {
        await using var context = await playwright.Browser.NewContextAsync(new()
        {
            ViewportSize = new() { Width = 1280, Height = 900 },
            ReducedMotion = ReducedMotion.Reduce,
        });
        var page = await context.NewPageAsync();
        var refreshErrors = new List<string>();
        page.Console += (_, message) => { if (message.Type == "error") refreshErrors.Add(message.Text); };
        page.PageError += (_, error) => refreshErrors.Add(error);
        await StubSpecializedTableBoundariesAsync(page);
        await page.UnrouteAsync("**/bff/dashboard");
        var requestCount = 0;
        await page.RouteAsync("**/bff/dashboard", route =>
        {
            var generatedAt = Interlocked.Increment(ref requestCount) == 1
                ? "2030-08-13T10:00:00+07:00"
                : "2030-08-13T10:01:00+07:00";
            return route.FulfillAsync(new()
            {
                Status = 200,
                ContentType = "application/json",
                Body = JsonSerializer.Serialize(new
                {
                    generatedAt,
                    cards = Array.Empty<object>(),
                    degradedSources = Array.Empty<string>(),
                    recentOrders = Array.Empty<object>(),
                    recentQuotations = Array.Empty<object>(),
                    recentCustomers = Array.Empty<object>(),
                    recentPayments = Array.Empty<object>(),
                    recentActivity = Array.Empty<object>(),
                    monthlyFinance = Array.Empty<object>(),
                    quotationSummary = (object?)null,
                    currencyCodes = new Dictionary<int, string>(),
                }),
            });
        });

        await page.GotoAsync(new Uri(server.BaseUri, "Dashboard").AbsoluteUri);
        var timestamp = page.Locator(".dashboard-freshness time");
        await timestamp.WaitForAsync();
        Assert.Equal("2030-08-13T10:00:00.0000000+07:00", await timestamp.GetAttributeAsync("datetime"));

        await page.WaitForTimeoutAsync(17_000);
        Assert.True(requestCount >= 2, $"Dashboard requests: {requestCount}; browser errors: {string.Join(" | ", refreshErrors)}");
        Assert.Equal("2030-08-13T10:01:00.0000000+07:00", await timestamp.GetAttributeAsync("datetime"));
        Assert.Empty(refreshErrors);
    }

    [Fact]
    public async Task QuotationDecisionChartRendersDistinctVisibleDonutSegments()
    {
        await using var context = await playwright.Browser.NewContextAsync(new()
        {
            ViewportSize = new() { Width = 1280, Height = 900 },
            ReducedMotion = ReducedMotion.Reduce,
        });
        var page = await context.NewPageAsync();
        await StubSalesBoundariesAsync(page);
        await page.GotoAsync(new Uri(server.BaseUri, "Quotations/Index").AbsoluteUri);

        var chart = page.Locator("[data-chart='chart-quotation-decisions']");
        await chart.WaitForAsync();
        var arcs = chart.Locator("[data-chart-shape='arc']");
        Assert.Equal(2, await arcs.CountAsync());
        var evidence = await arcs.EvaluateAllAsync<JsonElement>("""
            elements => elements.map(element => ({
                fill: getComputedStyle(element).fill,
                path: element.getAttribute('d'),
                width: element.getBoundingClientRect().width,
                height: element.getBoundingClientRect().height
            }))
            """);
        var segments = evidence.EnumerateArray().ToArray();
        Assert.All(segments, segment =>
        {
            Assert.False(string.IsNullOrWhiteSpace(segment.GetProperty("path").GetString()));
            Assert.True(segment.GetProperty("width").GetDouble() > 20, evidence.ToString());
            Assert.True(segment.GetProperty("height").GetDouble() > 20, evidence.ToString());
        });
        Assert.NotEqual(segments[0].GetProperty("fill").GetString(), segments[1].GetProperty("fill").GetString());
    }

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
        await page.Locator("[data-slot='data-table']").First.WaitForAsync();

        Assert.Equal("/Dashboard", await page.Locator("nav.page-breadcrumbs").GetByRole(AriaRole.Link).First.GetAttributeAsync("href"));
        var actionLinks = page.Locator(".operational-data-table__actions a");
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
            Assert.All(await page.Locator(".shadcn-data-table-frame .shadcn-table-container").AllAsync(), async table =>
                Assert.Contains(await table.EvaluateAsync<string>("node => getComputedStyle(node).overflowX"), new[] { "auto", "scroll" }));
            await AssertFocusDecorationIsNotClippedAsync(
                page.Locator("[data-slot='data-table-filter']").First,
                $"{route} at {width}px");
            if (width <= 720)
            {
                foreach (var action in await page.Locator(".operational-data-table__actions a, .operational-data-table__actions button").AllAsync())
                {
                    var size = await action.EvaluateAsync<JsonElement>("node => ({ width: node.getBoundingClientRect().width, height: node.getBoundingClientRect().height })");
                    Assert.True(size.GetProperty("width").GetDouble() >= 36 && size.GetProperty("height").GetDouble() >= 36, size.ToString());
                }
            }
        }

        await page.GetByRole(AriaRole.Button, new() { Name = expandName }).First.ClickAsync();
        Assert.Equal(1, await page.Locator("[data-slot='popover-content']").CountAsync());
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
        await page.Locator("[data-slot='data-table']").WaitForAsync();
        await page.GetByRole(AriaRole.Button, new() { Name = "ขยายเหตุการณ์ 801" }).ClickAsync();
        Assert.Equal(1, await page.Locator("[data-slot='popover-content']").CountAsync());
        Assert.Equal(0, await page.Locator(".operational-data-table__actions a").CountAsync());
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
        await page.Locator("[data-slot='data-table']").WaitForAsync();

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
                    tableContainers: Array.from(document.querySelectorAll('.shadcn-data-table-frame .shadcn-table-container')).map(node => ({
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

            await AssertFocusDecorationIsNotClippedAsync(
                page.Locator("[data-slot='data-table-filter']"),
                $"{route} at {width}px");

            if (width == 1280)
            {
                var rowHeights = await page.Locator("[data-slot='data-table'] tbody tr")
                    .EvaluateAllAsync<double[]>("rows => rows.map(row => row.getBoundingClientRect().height)");
                Assert.NotEmpty(rowHeights);
                Assert.All(rowHeights, height => Assert.InRange(height, 36, 64));
            }

            if (width <= 720)
            {
                foreach (var action in await page.Locator(".operational-data-table__actions a, .operational-data-table__actions button").AllAsync())
                {
                    var actionGeometry = await action.EvaluateAsync<JsonElement>(
                        "node => ({ width: node.getBoundingClientRect().width, height: node.getBoundingClientRect().height, tag: node.tagName, classes: node.className?.baseVal ?? node.className ?? '' })");
                    Assert.True(
                        actionGeometry.GetProperty("width").GetDouble() >= 36 && actionGeometry.GetProperty("height").GetDouble() >= 36,
                        actionGeometry.ToString());
                }
            }
        }

        await page.GetByRole(AriaRole.Button, new() { Name = expandName }).ClickAsync();
        Assert.Equal(1, await page.Locator("[data-slot='popover-content']").CountAsync());
        Assert.True(errors.Count == 0, string.Join(Environment.NewLine, errors));
    }

    private static async Task AssertFocusDecorationIsNotClippedAsync(ILocator control, string context)
    {
        await control.FocusAsync();
        var evidence = await control.EvaluateAsync<JsonElement>("""
            node => {
                const style = getComputedStyle(node);
                const outlineWidth = style.outlineStyle === 'none' ? 0 : Number.parseFloat(style.outlineWidth) || 0;
                const outlineOffset = Number.parseFloat(style.outlineOffset) || 0;
                const extent = outlineWidth + Math.max(0, outlineOffset);
                const rect = node.getBoundingClientRect();
                const decoration = {
                    left: rect.left - extent,
                    top: rect.top - extent,
                    right: rect.right + extent,
                    bottom: rect.bottom + extent
                };
                const clips = value => ['auto', 'scroll', 'hidden', 'clip'].includes(value);
                const offenders = [];
                for (let ancestor = node.parentElement; ancestor; ancestor = ancestor.parentElement) {
                    const ancestorStyle = getComputedStyle(ancestor);
                    const bounds = ancestor.getBoundingClientRect();
                    const intersectsInline = rect.right > bounds.left && rect.left < bounds.right;
                    const intersectsBlock = rect.bottom > bounds.top && rect.top < bounds.bottom;
                    const clipsInline = intersectsInline && clips(ancestorStyle.overflowX)
                        && (decoration.left < bounds.left - 0.5 || decoration.right > bounds.right + 0.5);
                    const clipsBlock = intersectsBlock && clips(ancestorStyle.overflowY)
                        && (decoration.top < bounds.top - 0.5 || decoration.bottom > bounds.bottom + 0.5);
                    if (clipsInline || clipsBlock) {
                        offenders.push({
                            tag: ancestor.tagName,
                            classes: ancestor.className?.baseVal ?? ancestor.className ?? '',
                            overflowX: ancestorStyle.overflowX,
                            overflowY: ancestorStyle.overflowY,
                            bounds,
                            clipsInline,
                            clipsBlock
                        });
                    }
                }
                return { extent, rect, decoration, offenders };
            }
            """);

        Assert.True(
            evidence.GetProperty("offenders").GetArrayLength() == 0,
            $"{context}: {evidence}");
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
        await page.Locator("[data-slot='data-table']").WaitForAsync();

        Assert.Equal("/Quotations/View?id=401", await page.GetByRole(AriaRole.Link, new() { Name = "เปิดใบเสนอราคา 401" }).GetAttributeAsync("href"));
        await page.GetByRole(AriaRole.Button, new() { Name = "ขยายรายละเอียดใบเสนอราคา 401" }).ClickAsync();
        var quotationPopover = page.Locator(".operational-data-table__popover");
        await quotationPopover.WaitForAsync();
        Assert.Contains("เงื่อนไขการจัดส่งและชำระเงินสำหรับลูกค้าอุตสาหกรรม", await quotationPopover.InnerTextAsync());
        Assert.Equal(
            await page.EvaluateAsync<int>("() => document.documentElement.clientWidth"),
            await page.EvaluateAsync<int>("() => document.documentElement.scrollWidth"));
        Assert.Empty(errors);
    }

    [Fact]
    public async Task ThaiSalesQuickViewsRecoverClippedEmployeeAndQuotationRequestIdentityAt320()
    {
        await using var context = await playwright.Browser.NewContextAsync(new()
        {
            ViewportSize = new() { Width = 320, Height = 844 },
            HasTouch = true,
            ReducedMotion = ReducedMotion.Reduce,
        });
        await context.AddInitScriptAsync("localStorage.setItem('maliev_culture', 'th-TH')");
        var page = await context.NewPageAsync();
        await StubSalesBoundariesAsync(page);

        await page.GotoAsync(new Uri(server.BaseUri, "Employees/Index").AbsoluteUri);
        await page.Locator("[data-slot='data-table']").WaitForAsync();
        Assert.Equal(1, await page.Locator("[data-slot='data-table-toolbar'] input[type='search']").CountAsync());
        Assert.Equal(0, await page.Locator("[data-slot='data-table-toolbar'] input[data-column-filter]").CountAsync());
        Assert.Equal(2, await page.Locator(".operational-data-table__actions").First.Locator("svg").CountAsync());
        await page.Locator("[data-slot='popover-trigger']").First.ClickAsync();
        var employeeQuickView = page.Locator(".employee-quick-view");
        await employeeQuickView.WaitForAsync();
        Assert.Contains("มาลี ดี ผู้เชี่ยวชาญงานขายอุตสาหกรรม", await employeeQuickView.InnerTextAsync());
        Assert.Contains("วิศวกรฝ่ายขายอาวุโสและผู้ประสานงานโครงการ", await employeeQuickView.InnerTextAsync());

        await page.GotoAsync(new Uri(server.BaseUri, "QuotationRequests/Index").AbsoluteUri);
        await page.Locator("[data-slot='data-table']").WaitForAsync();
        await page.Locator("[data-slot='popover-trigger']").First.ClickAsync();
        var requestQuickView = page.Locator(".quotation-request-quick-view");
        await requestQuickView.WaitForAsync();
        Assert.Contains("สุดา แก้ว ผู้ประสานงานโครงการอุตสาหกรรม", await requestQuickView.InnerTextAsync());
        Assert.Equal(
            await page.EvaluateAsync<int>("() => document.documentElement.clientWidth"),
            await page.EvaluateAsync<int>("() => document.documentElement.scrollWidth"));
    }

    [Theory]
    [InlineData("QuotationRequests/Index")]
    [InlineData("Quotations/Index")]
    public async Task QuotationRecordSummariesDescribeTheNativeOperationalTable(string route)
    {
        await using var context = await playwright.Browser.NewContextAsync();
        var page = await context.NewPageAsync();
        await StubSalesBoundariesAsync(page);
        await page.GotoAsync(new Uri(server.BaseUri, route).AbsoluteUri);

        var table = page.Locator("[data-slot='data-table']");
        await table.WaitForAsync();
        Assert.Equal(1, await table.Locator("[data-slot='data-table-selection-summary']").CountAsync());
        Assert.Equal(1, await table.Locator("[data-slot='data-table-page-summary']").CountAsync());
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
        var tables = page.Locator("table.dashboard-table");
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
            var scroller = table.Locator("xpath=ancestor::div[contains(@class,'shadcn-table-container')]");
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
        var moneyValues = await page.Locator(".dashboard-money").AllInnerTextsAsync();
        Assert.All(moneyValues, value => Assert.Contains("THB", value, StringComparison.Ordinal));
        Assert.DoesNotContain(moneyValues, value => value.Contains("Currency ", StringComparison.Ordinal));
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
        var fullValues = new[]
        {
            (Projection: "dashboard-orders", Value: "ชิ้นส่วนประกอบความเที่ยงตรงสูงสำหรับสายการผลิต"),
            (Projection: "dashboard-payments", Value: "ผู้รับชำระเงินสำหรับโครงการอุตสาหกรรมระยะยาว"),
            (Projection: "dashboard-customers", Value: "บริษัท มาลีฟ พรีซิชั่น แมนูแฟคเจอริ่ง จำกัด"),
        };
        foreach (var expected in fullValues)
        {
            var cell = page.Locator($"[data-projection='{expected.Projection}'] .dashboard-table-primary");
            Assert.Equal(expected.Value, (await cell.InnerTextAsync()).Trim());
            Assert.Equal("nowrap", await cell.EvaluateAsync<string>("node => getComputedStyle(node).whiteSpace"));
            Assert.NotEqual("hidden", await cell.EvaluateAsync<string>("node => getComputedStyle(node).overflow"));
            Assert.NotEqual("ellipsis", await cell.EvaluateAsync<string>("node => getComputedStyle(node).textOverflow"));
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
        var table = page.Locator($"table[data-projection='{projection}']");
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
        var scroller = table.Locator("xpath=..");
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
        Assert.Equal("none", await table.Locator("tbody td").First.EvaluateAsync<string>("node => getComputedStyle(node, '::before').content"));
        var scroller = page.Locator(".customer-history .shadcn-table-container");
        Assert.Contains(await scroller.EvaluateAsync<string>("node => getComputedStyle(node).overflowX"), new[] { "auto", "scroll" });
        var nameCell = page.Locator("[data-projection='history-orders'] td[data-field='Name']");
        var nameGeometry = await nameCell.EvaluateAsync<JsonElement>("""
            node => ({
                width: node.getBoundingClientRect().width,
                height: node.getBoundingClientRect().height,
                contentHeight: (() => {
                    const range = document.createRange();
                    range.selectNodeContents(node);
                    return range.getBoundingClientRect().height;
                })(),
                lineHeight: parseFloat(getComputedStyle(node).lineHeight),
                whiteSpace: getComputedStyle(node).whiteSpace,
                textOverflow: getComputedStyle(node).textOverflow,
                overflow: getComputedStyle(node).overflow,
                maxWidth: getComputedStyle(node).maxWidth
            })
            """);
        Assert.Equal("nowrap", nameGeometry.GetProperty("whiteSpace").GetString());
        Assert.True(nameGeometry.GetProperty("width").GetDouble() >= 160, nameGeometry.ToString());
        Assert.Equal("clip", nameGeometry.GetProperty("textOverflow").GetString());
        Assert.NotEqual("hidden", nameGeometry.GetProperty("overflow").GetString());
        Assert.Equal("none", nameGeometry.GetProperty("maxWidth").GetString());
        Assert.Equal("High precision aerospace fixture with inspection datum references", (await nameCell.InnerTextAsync()).Trim());
        Assert.True(
            nameGeometry.GetProperty("contentHeight").GetDouble() <= nameGeometry.GetProperty("lineHeight").GetDouble() * 2,
            nameGeometry.ToString());
        await scroller.FocusAsync();
        Assert.True(await scroller.EvaluateAsync<bool>("node => document.activeElement === node"));
        var scrollState = await scroller.EvaluateAsync<JsonElement>("node => ({ clientWidth: node.clientWidth, scrollWidth: node.scrollWidth, overflowX: getComputedStyle(node).overflowX, tabIndex: node.tabIndex })");
        Assert.True(scrollState.GetProperty("scrollWidth").GetInt32() > scrollState.GetProperty("clientWidth").GetInt32(), scrollState.ToString());
        await scroller.PressAsync("ArrowRight");
        await page.WaitForTimeoutAsync(100);
        Assert.True(await scroller.EvaluateAsync<double>("node => node.scrollLeft") > 0, scrollState.ToString());
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

    [Fact]
    public async Task SupplierEditorsUseNativeFieldsAndFocusManagedDeleteConfirmation()
    {
        await using var context = await playwright.Browser.NewContextAsync(new()
        {
            ViewportSize = new() { Width = 390, Height = 844 },
            HasTouch = true,
            ReducedMotion = ReducedMotion.Reduce,
        });
        var page = await context.NewPageAsync();
        var errors = CaptureErrors(page);
        await page.RouteAsync("**/bff/session", route => route.FulfillAsync(new()
        {
            Status = 200,
            ContentType = "application/json",
            Body = JsonSerializer.Serialize(new
            {
                isAuthenticated = true,
                employeeId = "supplier-editor-browser",
                displayName = "Supplier Editor Browser",
                roles = new[] { "Employee" },
                csrfToken = "supplier-editor-csrf",
                legacyDatabaseId = 1,
                permissions = new[] { "legacy-procurement.suppliers.read", "legacy-procurement.suppliers.create", "legacy-procurement.suppliers.update", "legacy-procurement.suppliers.delete" },
            }),
        }));
        await page.RouteAsync("**/bff/suppliers/42", route => route.FulfillAsync(new()
        {
            Status = 200,
            ContentType = "application/json",
            Body = JsonSerializer.Serialize(new
            {
                id = 42,
                name = "Thai Precision Supplier",
                website = "https://supplier.example",
                taxNumber = "0100000000000",
                email = "contact@supplier.example",
                note = "Approved supplier",
                telephone = "02-123-4567",
                mobile = "081-234-5678",
                fax = "02-123-4568",
                building = "MALIEV Industrial Park",
                address1 = "1 Manufacturing Road",
                address2 = (string?)null,
                city = "Bangkok",
                state = "Bangkok",
                postalCode = "10110",
                countryId = 66,
            }),
        }));

        await page.GotoAsync(new Uri(server.BaseUri, "Suppliers/Create").AbsoluteUri);
        await page.Locator("#supplier-name").WaitForAsync();
        Assert.Equal("url", await page.Locator("#supplier-website").GetAttributeAsync("type"));
        Assert.Equal("email", await page.Locator("#supplier-email").GetAttributeAsync("type"));
        Assert.Equal("tel", await page.Locator("#supplier-mobile").GetAttributeAsync("type"));
        Assert.Equal("number", await page.Locator("#supplier-country-id").GetAttributeAsync("type"));
        Assert.Equal(
            await page.EvaluateAsync<int>("() => document.documentElement.clientWidth"),
            await page.EvaluateAsync<int>("() => document.documentElement.scrollWidth"));

        await page.GotoAsync(new Uri(server.BaseUri, "Suppliers/View?id=42").AbsoluteUri);
        await page.Locator("#supplier-edit-name").WaitForAsync();
        Assert.Equal("Thai Precision Supplier", await page.Locator("#supplier-edit-name").InputValueAsync());
        var deleteTrigger = page.Locator("[data-slot='alert-dialog-trigger']");
        await deleteTrigger.ClickAsync();
        var dialog = page.GetByRole(AriaRole.Alertdialog);
        await dialog.WaitForAsync();
        Assert.Contains("Delete this supplier?", await dialog.InnerTextAsync());
        await dialog.GetByRole(AriaRole.Button, new() { Name = "Cancel", Exact = true }).ClickAsync();
        await Assertions.Expect(dialog).ToBeHiddenAsync();
        await Assertions.Expect(deleteTrigger).ToBeFocusedAsync();
        Assert.True(errors.Count == 0, string.Join(Environment.NewLine, errors));
    }

    [Fact]
    public async Task PurchaseOrderEditorsUseNativeSelectionsLinesTableAndDeleteDialog()
    {
        await using var context = await playwright.Browser.NewContextAsync(new()
        {
            ViewportSize = new() { Width = 390, Height = 844 },
            HasTouch = true,
            ReducedMotion = ReducedMotion.Reduce,
        });
        var page = await context.NewPageAsync();
        var errors = CaptureErrors(page);
        await page.RouteAsync("**/bff/session", route => route.FulfillAsync(new()
        {
            Status = 200,
            ContentType = "application/json",
            Body = JsonSerializer.Serialize(new
            {
                isAuthenticated = true,
                employeeId = "purchase-order-browser",
                displayName = "Purchase Order Browser",
                roles = new[] { "Employee" },
                csrfToken = "purchase-order-csrf",
                legacyDatabaseId = 1,
                permissions = new[] { "legacy-procurement.purchase-orders.read", "legacy-procurement.purchase-orders.create", "legacy-procurement.purchase-orders.delete" },
            }),
        }));
        await page.RouteAsync("**/bff/purchase-orders/create-options", route => route.FulfillAsync(new()
        {
            Status = 200,
            ContentType = "application/json",
            Body = JsonSerializer.Serialize(new
            {
                suppliers = new[] { new { id = 7, name = "Thai Precision Supplier" } },
                employees = new[] { new { id = 9, fullName = "Mali Dee" } },
                addresses = new[] { new { id = 11, addressLine1 = "1 Manufacturing Road", city = "Bangkok" } },
            }),
        }));
        await page.RouteAsync("**/bff/purchase-orders/84", route => route.FulfillAsync(new()
        {
            Status = 200,
            ContentType = "application/json",
            Body = JsonSerializer.Serialize(new
            {
                id = 84,
                supplierName = "Thai Precision Supplier",
                supplierContactPerson = "Somchai",
                orderedBy = "Mali Dee",
                shippingMethod = "Dedicated courier",
                fob = "Bangkok",
                terms = "Net 30",
                notes = "Inspection certificate required",
                createdDate = "2030-08-01T00:00:00Z",
                items = new[] { new { partNumber = "PO-84-A", description = "Precision fixture", quantity = 2, unitPrice = 1500m, subtotal = 3000m } },
                downloads = new[] { new { name = "purchase-order-84.pdf", url = "https://downloads.example/purchase-order-84.pdf" } },
            }),
        }));

        await page.GotoAsync(new Uri(server.BaseUri, "PurchaseOrders/Create").AbsoluteUri);
        await page.Locator("#purchase-order-supplier").WaitForAsync();
        Assert.Equal(4, await page.Locator(".purchase-order-create-page [data-slot='select-trigger']").CountAsync());
        Assert.Equal(1, await page.Locator("[id$='-description']").CountAsync());
        await page.GetByRole(AriaRole.Button, new() { Name = "Add line item", Exact = true }).ClickAsync();
        Assert.Equal(2, await page.Locator("[id$='-description']").CountAsync());
        await page.GetByRole(AriaRole.Button, new() { Name = "Remove line item", Exact = true }).Last.ClickAsync();
        Assert.Equal(1, await page.Locator("[id$='-description']").CountAsync());
        Assert.Equal(
            await page.EvaluateAsync<int>("() => document.documentElement.clientWidth"),
            await page.EvaluateAsync<int>("() => document.documentElement.scrollWidth"));

        await page.GotoAsync(new Uri(server.BaseUri, "PurchaseOrders/View?id=84").AbsoluteUri);
        var table = page.GetByRole(AriaRole.Table, new() { Name = "Purchase order line items", Exact = true });
        await table.WaitForAsync();
        Assert.Contains("Precision fixture", await table.InnerTextAsync());
        Assert.Equal("https://downloads.example/purchase-order-84.pdf", await page.GetByRole(AriaRole.Link, new() { Name = "Download PDF", Exact = true }).GetAttributeAsync("href"));
        var deleteTrigger = page.Locator("[data-slot='alert-dialog-trigger']");
        await deleteTrigger.ClickAsync();
        var dialog = page.GetByRole(AriaRole.Alertdialog);
        await dialog.WaitForAsync();
        Assert.Contains("Delete this purchase order?", await dialog.InnerTextAsync());
        await dialog.GetByRole(AriaRole.Button, new() { Name = "Cancel", Exact = true }).ClickAsync();
        await Assertions.Expect(dialog).ToBeHiddenAsync();
        Assert.True(await deleteTrigger.EvaluateAsync<bool>("node => document.activeElement === node"));
        Assert.True(errors.Count == 0, string.Join(Environment.NewLine, errors));
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
            Body = "{\"items\":[{\"id\":201,\"firstName\":\"มาลี\",\"lastName\":\"ดี ผู้เชี่ยวชาญงานขายอุตสาหกรรม\",\"fullName\":\"มาลี ดี ผู้เชี่ยวชาญงานขายอุตสาหกรรม\",\"email\":\"mali.employee@maliev.com\",\"role\":{\"id\":8,\"name\":\"วิศวกรฝ่ายขายอาวุโสและผู้ประสานงานโครงการ\"}},{\"id\":202,\"firstName\":\"Niran\",\"lastName\":\"Chai\",\"fullName\":\"Niran Chai\",\"email\":\"niran@maliev.com\",\"role\":null}],\"pageIndex\":1,\"totalPages\":2,\"totalRecords\":26,\"hasNextPage\":true,\"hasPreviousPage\":false}",
        }));
        await page.RouteAsync("**/bff/quotation-requests?*", route => route.FulfillAsync(new()
        {
            Status = 200,
            ContentType = "application/json",
            Body = "{\"items\":[{\"id\":301,\"firstName\":\"สุดา\",\"lastName\":\"แก้ว ผู้ประสานงานโครงการอุตสาหกรรม\",\"email\":\"suda@example.com\",\"telephoneNumber\":\"0812345678\",\"country\":\"Thailand\",\"companyName\":\"Thai Industrial Fixture Company\",\"taxIdentification\":\"0100000000001\",\"message\":\"Precision fixture request\",\"internalComment\":\"Priority\",\"done\":false,\"createdDate\":\"2030-08-01T00:00:00Z\",\"modifiedDate\":\"2030-08-02T00:00:00Z\"},{\"id\":302,\"firstName\":\"Somchai\",\"lastName\":\"Dee\",\"email\":null,\"telephoneNumber\":null,\"country\":null,\"companyName\":null,\"taxIdentification\":null,\"message\":null,\"internalComment\":null,\"done\":true,\"createdDate\":\"2030-08-03T00:00:00Z\",\"modifiedDate\":null}],\"pageIndex\":1,\"totalPages\":2,\"totalRecords\":26,\"hasNextPage\":true,\"hasPreviousPage\":false}",
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
                recentOrders = new[] { new { id = 901, name = "ชิ้นส่วนประกอบความเที่ยงตรงสูงสำหรับสายการผลิต", quantity = 4, manufactured = 1, remaining = 3, promisedDate = "2030-08-20T00:00:00Z", navigateTo = "/sales/orders/901" } },
                recentQuotations = new[] { new { id = 902, total = 1200m, quotedAmount = 1100m, currencyId = 764, expirationDate = "2030-09-01T00:00:00Z", accepted = (bool?)null, createdDate = "2030-08-02T00:00:00Z", navigateTo = "/Quotations/View?id=902" } },
                recentCustomers = new[] { new { id = 69738, fullName = "Mali Dee", email = "mali@maliev.com", company = "บริษัท มาลีฟ พรีซิชั่น แมนูแฟคเจอริ่ง จำกัด", navigateTo = "/Customers/View?id=69738" } },
                recentPayments = new[] { new { id = 903, amount = 900m, currencyId = (int?)764, recipient = "ผู้รับชำระเงินสำหรับโครงการอุตสาหกรรมระยะยาว", paymentDate = "2030-08-04T00:00:00Z", createdDate = "2030-08-03T00:00:00Z", navigateTo = "/Finances/View?id=903" } },
                recentActivity = Array.Empty<object>(),
                monthlyFinance = Array.Empty<object>(),
                quotationSummary = (object?)null,
                currencyCodes = new Dictionary<int, string> { [764] = "THB" },
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
                items = new[] { new { id = 901, customerId = 69738, employeeId = 1, name = "High precision aerospace fixture with inspection datum references", processId = 1, quantity = 4, manufactured = 1, remaining = 3, subtotal = (decimal?)null, promisedDate = "2030-08-20T00:00:00Z", allowSocialMedia = false, createdDate = "2030-08-01T00:00:00Z", modifiedDate = (string?)null } },
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
