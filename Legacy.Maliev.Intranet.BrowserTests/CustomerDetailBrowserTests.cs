using System.Text.Json;
using Legacy.Maliev.Intranet.BrowserTests.Infrastructure;
using Microsoft.Playwright;

namespace Legacy.Maliev.Intranet.BrowserTests;

[Collection(CustomerBrowserCollection.Name)]
public sealed class CustomerDetailBrowserTests(
    IntranetClientServerFixture server,
    PlaywrightFixture playwright)
{
    [Theory]
    [InlineData(1280)]
    [InlineData(768)]
    [InlineData(390)]
    [InlineData(320)]
    public async Task ProductionCustomerDetailUsesAResponsiveRecordHierarchy(int width)
    {
        await using var context = await playwright.Browser.NewContextAsync(new()
        {
            ViewportSize = new() { Width = width, Height = 900 },
            DeviceScaleFactor = 1,
            ReducedMotion = ReducedMotion.Reduce
        });
        var page = await context.NewPageAsync();
        var errors = new List<string>();
        page.Console += (_, message) => { if (message.Type == "error") errors.Add(message.Text); };
        page.PageError += (_, error) => errors.Add(error);
        await StubCustomerDetailBoundariesAsync(page);

        await page.GotoAsync(new Uri(server.BaseUri, "Customers/View?id=69738").AbsoluteUri);
        await page.GetByRole(AriaRole.Heading, new() { Name = "ธันวรินต์ กวินภัทรลักษณ์", Level = 1 }).WaitForAsync();

        Assert.Equal(1, await page.Locator(".customer-detail").CountAsync());
        Assert.Equal(1, await page.Locator(".customer-detail__header").CountAsync());
        Assert.Equal(1, await page.Locator(".customer-overview__primary").CountAsync());
        Assert.Equal(1, await page.Locator(".customer-overview__secondary").CountAsync());
        Assert.Equal(1, await page.Locator(".customer-overview__addresses").CountAsync());

        var layout = await page.Locator(".customer-overview__layout").EvaluateAsync<JsonElement>("""
            element => {
                const style = getComputedStyle(element);
                const rect = element.getBoundingClientRect();
                return {
                    columns: style.gridTemplateColumns,
                    gap: style.gap,
                    width: rect.width,
                    left: rect.left,
                    right: rect.right,
                    viewport: document.documentElement.clientWidth,
                    scrollWidth: document.documentElement.scrollWidth
                };
            }
            """);
        Assert.Equal(layout.GetProperty("viewport").GetDouble(), layout.GetProperty("scrollWidth").GetDouble());
        Assert.True(layout.GetProperty("left").GetDouble() >= 0, layout.ToString());
        Assert.True(layout.GetProperty("right").GetDouble() <= layout.GetProperty("viewport").GetDouble() + 0.5, layout.ToString());
        var columns = layout.GetProperty("columns").GetString() ?? string.Empty;
        if (width >= 900)
            Assert.Contains(' ', columns);
        else
            Assert.DoesNotContain(' ', columns);

        var detailRows = page.Locator(".customer-overview__details > div");
        Assert.True(await detailRows.CountAsync() >= 5);
        Assert.All(await detailRows.EvaluateAllAsync<string[]>(
            "elements => elements.map(element => getComputedStyle(element).display)"),
            display => Assert.Equal("grid", display));

        Assert.Empty(errors);
    }

    [Fact]
    public async Task ProductionCustomerEditFormUsesShadcnFieldsAndASeparatedActionRow()
    {
        await using var context = await playwright.Browser.NewContextAsync(new()
        {
            ViewportSize = new() { Width = 1280, Height = 1000 },
            DeviceScaleFactor = 1,
            ReducedMotion = ReducedMotion.Reduce
        });
        var page = await context.NewPageAsync();
        await StubCustomerDetailBoundariesAsync(page);

        await page.GotoAsync(new Uri(server.BaseUri, "Customers/View?id=69738").AbsoluteUri);
        await page.GetByRole(AriaRole.Button, new() { Name = "Edit customer", Exact = true }).ClickAsync();

        var formGrid = page.Locator(".customer-overview__form-grid");
        Assert.Equal(1, await formGrid.CountAsync());
        Assert.Contains(' ', await formGrid.EvaluateAsync<string>("element => getComputedStyle(element).gridTemplateColumns"));

        var dateInput = page.GetByLabel("Date of birth", new() { Exact = true });
        var dateGeometry = await dateInput.EvaluateAsync<JsonElement>("""
            element => {
                const control = element.closest('.shadcn-field');
                const input = element.closest('.shadcn-date-picker-trigger') ?? element;
                const border = input;
                const label = control.querySelector('.shadcn-field-label');
                const inputRect = input.getBoundingClientRect();
                const labelRect = label.getBoundingClientRect();
                return {
                    height: inputRect.height,
                    borderWidth: getComputedStyle(border).borderTopWidth,
                    borderRadius: getComputedStyle(border).borderRadius,
                    labelPosition: getComputedStyle(label).position,
                    labelBottom: labelRect.bottom,
                    inputTop: inputRect.top
                };
            }
            """);
        Assert.Equal(36d, dateGeometry.GetProperty("height").GetDouble(), precision: 1);
        Assert.Equal("1px", dateGeometry.GetProperty("borderWidth").GetString());
        Assert.NotEqual("0px", dateGeometry.GetProperty("borderRadius").GetString());
        Assert.Equal("static", dateGeometry.GetProperty("labelPosition").GetString());
        Assert.True(dateGeometry.GetProperty("labelBottom").GetDouble() <= dateGeometry.GetProperty("inputTop").GetDouble() - 6d);

        var actionRow = page.Locator(".customer-overview__form-actions");
        Assert.Equal("1px", await actionRow.EvaluateAsync<string>("element => getComputedStyle(element).borderTopWidth"));
        Assert.True(await actionRow.GetByRole(AriaRole.Button, new() { Name = "Save changes" }).IsVisibleAsync());
        Assert.True(await actionRow.GetByRole(AriaRole.Button, new() { Name = "Cancel" }).IsVisibleAsync());

        await page.SetViewportSizeAsync(390, 844);
        Assert.DoesNotContain(' ', await formGrid.EvaluateAsync<string>("element => getComputedStyle(element).gridTemplateColumns"));
        var dateInputHandle = await dateInput.ElementHandleAsync();
        Assert.NotNull(dateInputHandle);
        await page.WaitForFunctionAsync(
            "element => element.getBoundingClientRect().height >= 44",
            dateInputHandle,
            new() { Timeout = 10_000 });
        var narrowDateGeometry = await dateInput.EvaluateAsync<JsonElement>("""
            element => {
                const input = element.closest('.shadcn-date-picker-trigger') ?? element;
                return {
                    height: input.getBoundingClientRect().height,
                    controlHeight: getComputedStyle(input).getPropertyValue('--shadcn-control-height').trim(),
                    className: input.className,
                    hasTextarea: input.querySelector('textarea') !== null,
                    viewportWidth: window.innerWidth
                };
            }
            """);
        var narrowDateHeight = narrowDateGeometry.GetProperty("height").GetDouble();
        Assert.True(
            narrowDateHeight >= 44d,
            $"Expected the narrow date input to be at least 44px high, but it measured {narrowDateHeight:F2}px at {narrowDateGeometry.GetProperty("viewportWidth").GetDouble():F0}px with --shadcn-control-height={narrowDateGeometry.GetProperty("controlHeight").GetString()}, class={narrowDateGeometry.GetProperty("className").GetString()}, hasTextarea={narrowDateGeometry.GetProperty("hasTextarea").GetBoolean()}.");
        Assert.Equal(
            await page.EvaluateAsync<double>("() => document.documentElement.clientWidth"),
            await page.EvaluateAsync<double>("() => document.documentElement.scrollWidth"));
    }

    [Fact]
    public async Task CustomerWorkspaceUsesUrlHistoryPermissionScopedTabsAndLazyLoadsEachFamilyOnce()
    {
        await using var context = await playwright.Browser.NewContextAsync(new()
        {
            ViewportSize = new() { Width = 1280, Height = 900 },
            ReducedMotion = ReducedMotion.Reduce
        });
        var page = await context.NewPageAsync();
        var state = new CustomerBoundaryState();
        await StubCustomerDetailBoundariesAsync(page,
            [
                "legacy-customer.customers.read",
                "legacy-customer.customers.update",
                "legacy.orders.read",
                "legacy.quotations.read",
                "legacy.accounting.read"
            ], state);

        await page.GotoAsync(new Uri(server.BaseUri, "Customers/View?id=69738").AbsoluteUri);
        await page.WaitForURLAsync(url => url.Contains("tab=overview", StringComparison.OrdinalIgnoreCase));

        var tabs = page.GetByRole(AriaRole.Tab);
        Assert.Equal(5, await tabs.CountAsync());
        Assert.Equal(1, state.CustomerLoads);
        Assert.Equal(0, state.ActivityLoads + state.OrderLoads + state.QuotationLoads + state.InvoiceLoads);

        await page.GetByRole(AriaRole.Tab, new() { Name = "Activity", Exact = true }).ClickAsync();
        await page.WaitForURLAsync(url => url.Contains("tab=activity", StringComparison.OrdinalIgnoreCase));
        await page.GetByRole(AriaRole.Link, new() { Name = "View order 901", Exact = true }).WaitForAsync();
        await page.GetByText("Quotations: Temporarily unavailable", new() { Exact = true }).WaitForAsync();
        Assert.Equal(0, await page.GetByText("Invoices: Not permitted", new() { Exact = true }).CountAsync());
        Assert.Equal(1, state.ActivityLoads);

        await page.GetByRole(AriaRole.Tab, new() { Name = "Orders", Exact = true }).ClickAsync();
        await page.WaitForURLAsync(url => url.Contains("tab=orders", StringComparison.OrdinalIgnoreCase));
        await page.GetByRole(AriaRole.Link, new() { Name = "View order 901", Exact = true }).WaitForAsync();
        Assert.Equal(1, state.OrderLoads);
        Assert.Equal(1, state.CustomerLoads);

        await page.GetByRole(AriaRole.Tab, new() { Name = "Overview", Exact = true }).ClickAsync();
        await page.GetByRole(AriaRole.Tab, new() { Name = "Orders", Exact = true }).ClickAsync();
        Assert.Equal(1, state.OrderLoads);

        await page.GetByRole(AriaRole.Tab, new() { Name = "Quotations", Exact = true }).ClickAsync();
        await page.GetByRole(AriaRole.Link, new() { Name = "View quotation 801", Exact = true }).WaitForAsync();
        await page.GetByRole(AriaRole.Tab, new() { Name = "Invoices", Exact = true }).ClickAsync();
        await page.GetByRole(AriaRole.Link, new() { Name = "View invoice INV-701", Exact = true }).WaitForAsync();
        Assert.Equal(1, state.QuotationLoads);
        Assert.Equal(1, state.InvoiceLoads);

        await page.GoBackAsync();
        await page.WaitForURLAsync(url => url.Contains("tab=quotations", StringComparison.OrdinalIgnoreCase));
        await Assertions.Expect(page.GetByRole(AriaRole.Tab, new() { Name = "Quotations", Exact = true })).ToHaveAttributeAsync("aria-selected", "true");
        await page.GoForwardAsync();
        await page.WaitForURLAsync(url => url.Contains("tab=invoices", StringComparison.OrdinalIgnoreCase));
        await Assertions.Expect(page.GetByRole(AriaRole.Tab, new() { Name = "Invoices", Exact = true })).ToHaveAttributeAsync("aria-selected", "true");
        Assert.Equal(1, state.CustomerLoads);
    }

    [Theory]
    [InlineData("orders", "Order history is temporarily unavailable.", "View order 902", "/Orders/View?id=902")]
    [InlineData("quotations", "Quotation history is temporarily unavailable.", "View quotation 802", "/Quotations/View?id=802")]
    [InlineData("invoices", "Invoice history is temporarily unavailable.", "View invoice INV-702", "/Invoices/View?id=702")]
    public async Task FailedSecondHistoryPageRetriesTheRequestedPage(string tab, string failureText, string accessibleName, string expectedHref)
    {
        await using var context = await playwright.Browser.NewContextAsync(new()
        {
            ViewportSize = new() { Width = 1280, Height = 900 },
            ReducedMotion = ReducedMotion.Reduce
        });
        var page = await context.NewPageAsync();
        var state = new CustomerBoundaryState { FailPageTwoFamily = tab };
        await StubCustomerDetailBoundariesAsync(page,
            ["legacy-customer.customers.read", "legacy.orders.read", "legacy.quotations.read", "legacy.accounting.read"], state);

        await page.GotoAsync(new Uri(server.BaseUri, $"Customers/View?id=69738&tab={tab}").AbsoluteUri);
        await page.GetByRole(AriaRole.Button, new() { Name = "Next page", Exact = true }).ClickAsync();
        await page.GetByText(failureText, new() { Exact = true }).WaitForAsync();
        await page.GetByRole(AriaRole.Button, new() { Name = "Try again", Exact = true }).ClickAsync();

        var record = page.GetByRole(AriaRole.Link, new() { Name = accessibleName, Exact = true });
        await record.WaitForAsync();
        Assert.Equal(expectedHref, await record.GetAttributeAsync("href"));
        Assert.Equal([1, 2, 2], state.PageRequests[tab]);
        Assert.Equal(1, state.CustomerLoads);
    }

    [Fact]
    public async Task ActivityRecordLinksUseCoarsePointerTargetsAtWideWidths()
    {
        await using var context = await playwright.Browser.NewContextAsync(new()
        {
            ViewportSize = new() { Width = 1024, Height = 900 },
            HasTouch = true,
            ReducedMotion = ReducedMotion.Reduce
        });
        var page = await context.NewPageAsync();
        await StubCustomerDetailBoundariesAsync(page, ["legacy-customer.customers.read", "legacy.orders.read"]);

        await page.GotoAsync(new Uri(server.BaseUri, "Customers/View?id=69738&tab=activity").AbsoluteUri);
        var record = page.GetByRole(AriaRole.Link, new() { Name = "View order 901", Exact = true });
        await record.WaitForAsync();
        Assert.Equal("/Orders/View?id=901", await record.GetAttributeAsync("href"));
        Assert.True(await record.EvaluateAsync<double>("element => element.getBoundingClientRect().height") >= 44);
    }

    [Theory]
    [InlineData(401, null)]
    [InlineData(403, null)]
    [InlineData(429, "Order history is receiving too many requests. Wait a moment and try again.")]
    [InlineData(502, "Order history returned invalid data. Try again.")]
    public async Task OrderHistoryStatusOutcomesStayAtTheCorrectBoundary(int statusCode, string? expectedMessage)
    {
        await using var context = await playwright.Browser.NewContextAsync(new() { ViewportSize = new() { Width = 1280, Height = 900 } });
        var page = await context.NewPageAsync();
        var state = new CustomerBoundaryState { OrderStatusCode = statusCode };
        await StubCustomerDetailBoundariesAsync(page, ["legacy-customer.customers.read", "legacy.orders.read"], state);

        await page.GotoAsync(new Uri(server.BaseUri, "Customers/View?id=69738&tab=orders").AbsoluteUri);
        if (statusCode == 401)
        {
            await page.WaitForURLAsync(url => url.Contains("/Login?returnUrl=", StringComparison.OrdinalIgnoreCase));
            return;
        }

        if (statusCode == 403)
        {
            await page.WaitForURLAsync(url => url.Contains("tab=overview", StringComparison.OrdinalIgnoreCase));
            Assert.Equal(0, await page.GetByRole(AriaRole.Tab, new() { Name = "Orders", Exact = true }).CountAsync());
            return;
        }

        await page.GetByText(expectedMessage!, new() { Exact = true }).WaitForAsync();
        Assert.Equal(1, await page.GetByRole(AriaRole.Tab, new() { Name = "Orders", Exact = true }).CountAsync());
    }

    [Fact]
    public async Task MismatchedCustomerHistoryIsRejectedAndZoomedDarkMotionModesRemainUsable()
    {
        await using var context = await playwright.Browser.NewContextAsync(new()
        {
            ViewportSize = new() { Width = 640, Height = 900 },
            ColorScheme = ColorScheme.Dark,
            ReducedMotion = ReducedMotion.Reduce,
            ForcedColors = ForcedColors.Active
        });
        var page = await context.NewPageAsync();
        var state = new CustomerBoundaryState { MismatchedOrders = true };
        await StubCustomerDetailBoundariesAsync(page, ["legacy-customer.customers.read", "legacy.orders.read"], state);

        await page.GotoAsync(new Uri(server.BaseUri, "Customers/View?id=69738&tab=orders").AbsoluteUri);
        await page.GetByText("Orders history returned invalid data. Try again.", new() { Exact = true }).WaitForAsync();
        await page.EvaluateAsync("document.documentElement.style.zoom = '2'");

        var activeTab = page.GetByRole(AriaRole.Tab, new() { Name = "Orders", Exact = true });
        await activeTab.FocusAsync();
        Assert.Equal("solid", await activeTab.EvaluateAsync<string>("element => getComputedStyle(element).outlineStyle"));
        Assert.True(await activeTab.EvaluateAsync<bool>("element => parseFloat(getComputedStyle(element).transitionDuration) <= 0.01"));
        Assert.NotEqual("rgba(0, 0, 0, 0)", await page.Locator(".customer-detail__tabs").EvaluateAsync<string>("element => getComputedStyle(element).backgroundColor"));
        Assert.True(await page.EvaluateAsync<bool>("() => document.documentElement.scrollWidth <= document.documentElement.clientWidth"));
    }

    [Fact]
    public async Task ThaiCustomerWorkspaceRetainsLocalizedTabsWarningsAndNarrowGeometry()
    {
        await using var context = await playwright.Browser.NewContextAsync(new()
        {
            ViewportSize = new() { Width = 320, Height = 844 },
            ColorScheme = ColorScheme.Dark,
            ReducedMotion = ReducedMotion.Reduce,
            HasTouch = true
        });
        await context.AddInitScriptAsync("localStorage.setItem('maliev_culture', 'th-TH')");
        var page = await context.NewPageAsync();
        await StubCustomerDetailBoundariesAsync(page,
            [
                "legacy-customer.customers.read",
                "legacy.orders.read",
                "legacy.quotations.read",
                "legacy.accounting.read"
            ]);

        await page.GotoAsync(new Uri(server.BaseUri, "Customers/View?id=69738&tab=activity").AbsoluteUri);
        await page.GetByRole(AriaRole.Tab, new() { Name = "กิจกรรม", Exact = true }).WaitForAsync();
        await page.GetByText("ใบเสนอราคา: ไม่พร้อมใช้งานชั่วคราว", new() { Exact = true }).WaitForAsync();
        Assert.Equal(0, await page.GetByText("ใบแจ้งหนี้: ไม่มีสิทธิ์เข้าถึง", new() { Exact = true }).CountAsync());

        Assert.Equal(5, await page.GetByRole(AriaRole.Tab).CountAsync());
        Assert.Equal(
            await page.EvaluateAsync<double>("() => document.documentElement.clientWidth"),
            await page.EvaluateAsync<double>("() => document.documentElement.scrollWidth"));
        Assert.All(
            await page.GetByRole(AriaRole.Tab).EvaluateAllAsync<double[]>("elements => elements.map(element => element.getBoundingClientRect().height)"),
            height => Assert.True(height >= 44, $"Expected 44px Thai tab target, found {height:F2}px."));
    }

    [Fact]
    public async Task CustomerWorkspaceNormalizesUnauthorizedDeepLinksAndKeepsFamilyFailuresLocal()
    {
        await using var context = await playwright.Browser.NewContextAsync(new()
        {
            ViewportSize = new() { Width = 390, Height = 844 },
            HasTouch = true,
            ReducedMotion = ReducedMotion.Reduce,
            ForcedColors = ForcedColors.Active
        });
        var page = await context.NewPageAsync();
        var unauthorizedState = new CustomerBoundaryState();
        await StubCustomerDetailBoundariesAsync(page,
            ["legacy-customer.customers.read"], unauthorizedState);

        await page.GotoAsync(new Uri(server.BaseUri, "Customers/View?id=69738&tab=orders").AbsoluteUri);
        await page.WaitForURLAsync(url => url.Contains("tab=overview", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(2, await page.GetByRole(AriaRole.Tab).CountAsync());
        Assert.Equal(0, unauthorizedState.OrderLoads);

        var tabHeights = await page.GetByRole(AriaRole.Tab).EvaluateAllAsync<double[]>(
            "elements => elements.map(element => element.getBoundingClientRect().height)");
        Assert.All(tabHeights, height => Assert.True(height >= 44, $"Expected 44px tab target, found {height:F2}px."));
        Assert.Equal(
            await page.EvaluateAsync<double>("() => document.documentElement.clientWidth"),
            await page.EvaluateAsync<double>("() => document.documentElement.scrollWidth"));

        var retryState = new CustomerBoundaryState { FailFirstOrders = true };
        var retryPage = await context.NewPageAsync();
        await StubCustomerDetailBoundariesAsync(retryPage,
            ["legacy-customer.customers.read", "legacy.orders.read"], retryState);
        await retryPage.GotoAsync(new Uri(server.BaseUri, "Customers/View?id=69738&tab=orders").AbsoluteUri);
        await retryPage.GetByText("Order history is temporarily unavailable.", new() { Exact = true }).WaitForAsync();
        await retryPage.GetByRole(AriaRole.Button, new() { Name = "Try again", Exact = true }).ClickAsync();
        await retryPage.GetByRole(AriaRole.Link, new() { Name = "View order 901", Exact = true }).WaitForAsync();
        Assert.Equal(2, retryState.OrderLoads);
        Assert.Equal(1, retryState.CustomerLoads);

        await retryPage.GetByRole(AriaRole.Button, new() { Name = "Next page", Exact = true }).ClickAsync();
        await retryPage.GetByRole(AriaRole.Link, new() { Name = "View order 902", Exact = true }).WaitForAsync();
        Assert.Equal(3, retryState.OrderLoads);
        Assert.Equal(1, retryState.CustomerLoads);
    }

    [Fact]
    public async Task FinalGateCapturesEveryCustomerWorkspaceTabAcrossRequiredModes()
    {
        var captureRoot = Path.Combine(
            FindRepositoryRoot(),
            ".superpowers",
            "sdd",
            "2026-08-12-customer-history-site-links-plan",
            "task7-captures");
        Directory.CreateDirectory(captureRoot);
        var tabExpectations = new Dictionary<string, (string Role, string Name)>(StringComparer.Ordinal)
        {
            ["overview"] = ("heading", "Contact"),
            ["activity"] = ("link", "View order 901"),
            ["orders"] = ("link", "View order 901"),
            ["quotations"] = ("link", "View quotation 801"),
            ["invoices"] = ("link", "View invoice INV-701")
        };

        foreach (var width in new[] { 1280, 768, 390, 320 })
        {
            await using var context = await playwright.Browser.NewContextAsync(new()
            {
                ViewportSize = new() { Width = width, Height = 900 },
                ReducedMotion = ReducedMotion.NoPreference
            });
            var page = await context.NewPageAsync();
            var errors = new List<string>();
            page.Console += (_, message) => { if (message.Type == "error") errors.Add(message.Text); };
            page.PageError += (_, error) => errors.Add(error);
            await StubCustomerDetailBoundariesAsync(page,
                ["legacy-customer.customers.read", "legacy.orders.read", "legacy.quotations.read", "legacy.accounting.read"]);

            foreach (var (tab, expectation) in tabExpectations)
            {
                await page.GotoAsync(new Uri(server.BaseUri, $"Customers/View?id=69738&tab={tab}").AbsoluteUri);
                var expected = expectation.Role == "heading"
                    ? page.GetByRole(AriaRole.Heading, new() { Name = expectation.Name, Exact = true })
                    : page.GetByRole(AriaRole.Link, new() { Name = expectation.Name, Exact = true });
                await expected.WaitForAsync();
                await page.WaitForTimeoutAsync(300);
                Assert.Equal(width, await page.EvaluateAsync<int>("() => document.documentElement.scrollWidth"));
                var activeTab = page.Locator("[role='tab'][aria-selected='true']");
                Assert.Equal(1, await activeTab.CountAsync());
                var activeTabIsFullyVisible = await activeTab.EvaluateAsync<bool>("""
                    element => {
                        const viewport = element.closest('.shadcn-tabs-list');
                        const tab = element.getBoundingClientRect();
                        const bounds = viewport.getBoundingClientRect();
                        return tab.left >= bounds.left - 0.5 && tab.right <= bounds.right + 0.5;
                    }
                    """);
                var tabGeometry = await activeTab.EvaluateAsync<string>("""
                    element => {
                        const ancestors = [];
                        let current = element.parentElement;
                        while (current && ancestors.length < 5) {
                            const rect = current.getBoundingClientRect();
                            const style = getComputedStyle(current);
                            ancestors.push(`${current.className}|left=${rect.left}|right=${rect.right}|client=${current.clientWidth}|scroll=${current.scrollWidth}|scrollLeft=${current.scrollLeft}|overflow=${style.overflowX}`);
                            current = current.parentElement;
                        }
                        const rect = element.getBoundingClientRect();
                        return `tab=${rect.left},${rect.right};${ancestors.join(';')}`;
                    }
                    """);
                Assert.True(activeTabIsFullyVisible, $"Expected active {tab} tab to be fully visible at {width}px. {tabGeometry}");
                if (width <= 390)
                {
                    Assert.All(
                        await page.GetByRole(AriaRole.Tab).EvaluateAllAsync<double[]>("elements => elements.map(element => element.getBoundingClientRect().height)"),
                        height => Assert.True(height >= 44, $"Expected a 44px tab target, found {height:F2}px."));
                }
                await page.ScreenshotAsync(new() { Path = Path.Combine(captureRoot, $"en-{width}-{tab}.png"), FullPage = true });
            }

            Assert.Empty(errors);
        }

        await CaptureModeAsync("dark", new() { ViewportSize = new() { Width = 1280, Height = 900 }, ColorScheme = ColorScheme.Dark });
        await CaptureModeAsync("forced-colors", new() { ViewportSize = new() { Width = 390, Height = 844 }, ForcedColors = ForcedColors.Active });
        await CaptureModeAsync("reduced-motion", new() { ViewportSize = new() { Width = 390, Height = 844 }, ReducedMotion = ReducedMotion.Reduce });

        await using (var zoomContext = await playwright.Browser.NewContextAsync(new() { ViewportSize = new() { Width = 640, Height = 900 } }))
        {
            var zoomPage = await zoomContext.NewPageAsync();
            await StubCustomerDetailBoundariesAsync(zoomPage, ["legacy-customer.customers.read", "legacy.orders.read"]);
            await zoomPage.GotoAsync(new Uri(server.BaseUri, "Customers/View?id=69738&tab=orders").AbsoluteUri);
            await zoomPage.GetByRole(AriaRole.Link, new() { Name = "View order 901", Exact = true }).WaitForAsync();
            await zoomPage.EvaluateAsync("document.documentElement.style.zoom = '2'");
            Assert.True(await zoomPage.EvaluateAsync<bool>("() => document.documentElement.scrollWidth <= document.documentElement.clientWidth"));
            await zoomPage.ScreenshotAsync(new() { Path = Path.Combine(captureRoot, "zoom-200-orders.png"), FullPage = true });
        }

        await using (var thaiContext = await playwright.Browser.NewContextAsync(new()
        {
            ViewportSize = new() { Width = 320, Height = 844 },
            HasTouch = true
        }))
        {
            await thaiContext.AddInitScriptAsync("localStorage.setItem('maliev_culture', 'th-TH')");
            var thaiPage = await thaiContext.NewPageAsync();
            await StubCustomerDetailBoundariesAsync(thaiPage,
                ["legacy-customer.customers.read", "legacy.orders.read", "legacy.quotations.read", "legacy.accounting.read"]);
            foreach (var tab in tabExpectations.Keys)
            {
                await thaiPage.GotoAsync(new Uri(server.BaseUri, $"Customers/View?id=69738&tab={tab}").AbsoluteUri);
                await thaiPage.GetByRole(AriaRole.Tab, new()
                {
                    Name = tab switch
                    {
                        "overview" => "ภาพรวม",
                        "activity" => "กิจกรรม",
                        "orders" => "คำสั่งซื้อ",
                        "quotations" => "ใบเสนอราคา",
                        _ => "ใบแจ้งหนี้"
                    },
                    Exact = true
                }).WaitForAsync();
                Assert.Equal(320, await thaiPage.EvaluateAsync<int>("() => document.documentElement.scrollWidth"));
                await thaiPage.ScreenshotAsync(new() { Path = Path.Combine(captureRoot, $"th-320-{tab}.png"), FullPage = true });
            }
        }

        await using (var keyboardContext = await playwright.Browser.NewContextAsync(new() { ViewportSize = new() { Width = 1280, Height = 900 } }))
        {
            var keyboardPage = await keyboardContext.NewPageAsync();
            await StubCustomerDetailBoundariesAsync(keyboardPage,
                ["legacy-customer.customers.read", "legacy.orders.read", "legacy.quotations.read", "legacy.accounting.read"]);
            await keyboardPage.GotoAsync(new Uri(server.BaseUri, "Customers/View?id=69738&tab=overview").AbsoluteUri);
            var activity = keyboardPage.GetByRole(AriaRole.Tab, new() { Name = "Activity", Exact = true });
            await activity.FocusAsync();
            await activity.PressAsync("Enter");
            await keyboardPage.WaitForURLAsync(url => url.Contains("tab=activity", StringComparison.OrdinalIgnoreCase));
            var orders = keyboardPage.GetByRole(AriaRole.Tab, new() { Name = "Orders", Exact = true });
            await orders.FocusAsync();
            var focusTreatment = await orders.EvaluateAsync<string>(
                "element => `${getComputedStyle(element).outlineStyle}|${getComputedStyle(element).boxShadow}`");
            Assert.NotEqual("none|none", focusTreatment);
            await orders.PressAsync("Space");
            await keyboardPage.WaitForURLAsync(url => url.Contains("tab=orders", StringComparison.OrdinalIgnoreCase));
        }

        async Task CaptureModeAsync(string name, BrowserNewContextOptions options)
        {
            await using var context = await playwright.Browser.NewContextAsync(options);
            var page = await context.NewPageAsync();
            await StubCustomerDetailBoundariesAsync(page, ["legacy-customer.customers.read", "legacy.orders.read"]);
            await page.GotoAsync(new Uri(server.BaseUri, "Customers/View?id=69738&tab=orders").AbsoluteUri);
            await page.GetByRole(AriaRole.Link, new() { Name = "View order 901", Exact = true }).WaitForAsync();
            Assert.True(await page.EvaluateAsync<bool>("() => document.documentElement.scrollWidth <= document.documentElement.clientWidth"));
            await page.ScreenshotAsync(new() { Path = Path.Combine(captureRoot, $"{name}-orders.png"), FullPage = true });
        }
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Legacy.Maliev.Intranet.slnx")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new DirectoryNotFoundException("Could not locate repository root.");
    }

    private static async Task StubCustomerDetailBoundariesAsync(
        IPage page,
        IReadOnlyList<string>? permissions = null,
        CustomerBoundaryState? state = null)
    {
        state ??= new();
        permissions ??= ["legacy-customer.customers.read", "legacy-customer.customers.update"];
        await page.RouteAsync("**/bff/session", route => route.FulfillAsync(new()
        {
            Status = 200,
            ContentType = "application/json",
            Body = JsonSerializer.Serialize(new
            {
                isAuthenticated = true,
                employeeId = "customer-detail-browser-employee",
                displayName = "Customer Detail Browser Employee",
                roles = new[] { "Employee" },
                csrfToken = "customer-detail-browser-csrf",
                legacyDatabaseId = 1,
                permissions
            })
        }));
        await page.RouteAsync("**/bff/customers/69738/activity*", route =>
        {
            Interlocked.Increment(ref state.ActivityLoads);
            return route.FulfillAsync(new()
            {
                Status = 200,
                ContentType = "application/json",
                Body = JsonSerializer.Serialize(new
                {
                    items = new[]
                    {
                        new { kind = 0, id = 901, label = (string?)null, status = 0, completedUnits = 1, totalUnits = 4, amount = (decimal?)null, currency = (string?)null, timestamp = "2026-08-11T04:00:00Z" }
                    },
                    orders = new { state = 0, totalRecords = 2 },
                    quotations = new { state = 3, totalRecords = (int?)null },
                    invoices = new { state = 1, totalRecords = (int?)null }
                })
            });
        });
        await page.RouteAsync("**/bff/customers/69738/orders*", route =>
        {
            var load = Interlocked.Increment(ref state.OrderLoads);
            if (state.FailFirstOrders && load == 1)
                return route.FulfillAsync(new() { Status = 503, ContentType = "application/problem+json", Body = "{}" });

            var secondPage = new Uri(route.Request.Url).Query.Contains("index=2", StringComparison.Ordinal);
            state.PageRequests["orders"].Add(secondPage ? 2 : 1);
            if (state.OrderStatusCode is int orderStatus)
                return route.FulfillAsync(new() { Status = orderStatus, ContentType = "application/problem+json", Body = "{}" });
            if (secondPage && state.FailPageTwoFamily == "orders" && state.PageRequests["orders"].Count(value => value == 2) == 1)
                return route.FulfillAsync(new() { Status = 503, ContentType = "application/problem+json", Body = "{}" });
            return route.FulfillAsync(new()
            {
                Status = 200,
                ContentType = "application/json",
                Body = JsonSerializer.Serialize(CreateOrderPage(secondPage ? 2 : 1, state.MismatchedOrders ? 123 : 69738))
            });
        });
        await page.RouteAsync("**/bff/customers/69738/quotations*", route =>
        {
            Interlocked.Increment(ref state.QuotationLoads);
            var secondPage = new Uri(route.Request.Url).Query.Contains("index=2", StringComparison.Ordinal);
            state.PageRequests["quotations"].Add(secondPage ? 2 : 1);
            if (secondPage && state.FailPageTwoFamily == "quotations" && state.PageRequests["quotations"].Count(value => value == 2) == 1)
                return route.FulfillAsync(new() { Status = 503, ContentType = "application/problem+json", Body = "{}" });
            var id = secondPage ? 802 : 801;
            return route.FulfillAsync(new()
            {
                Status = 200,
                ContentType = "application/json",
                Body = JsonSerializer.Serialize(new
                {
                    items = new[]
                    {
                        new { id, customerId = 69738, employeeId = 1, invoiceId = (int?)null, period = 30, expirationDate = "2026-09-11T00:00:00Z", subtotal = 100m, vat = 7m, total = 107m, withholdingTax = (decimal?)null, quotedAmount = 107m, currencyId = 1, comment = (string?)null, fob = (string?)null, shippedVia = (string?)null, terms = (string?)null, accepted = (bool?)null, createdDate = "2026-08-11T00:00:00Z", modifiedDate = (string?)null }
                    },
                    pageIndex = secondPage ? 2 : 1,
                    totalPages = 2,
                    totalRecords = 2,
                    hasNextPage = !secondPage,
                    hasPreviousPage = secondPage
                })
            });
        });
        await page.RouteAsync("**/bff/customers/69738/invoices*", route =>
        {
            Interlocked.Increment(ref state.InvoiceLoads);
            var secondPage = new Uri(route.Request.Url).Query.Contains("index=2", StringComparison.Ordinal);
            state.PageRequests["invoices"].Add(secondPage ? 2 : 1);
            if (secondPage && state.FailPageTwoFamily == "invoices" && state.PageRequests["invoices"].Count(value => value == 2) == 1)
                return route.FulfillAsync(new() { Status = 503, ContentType = "application/problem+json", Body = "{}" });
            var id = secondPage ? 702 : 701;
            return route.FulfillAsync(new()
            {
                Status = 200,
                ContentType = "application/json",
                Body = JsonSerializer.Serialize(new
                {
                    items = new[]
                    {
                        new { id, customerId = 69738, number = $"INV-{id}", currency = "THB", purchaseOrderNumber = (string?)null, subtotal = 100m, vat = 7m, total = 107m, withholdingTax = (decimal?)null, outstanding = 107m, isPaid = false, receiptId = (int?)null, paymentDate = (string?)null, createdDate = "2026-08-11T00:00:00Z" }
                    },
                    pageIndex = secondPage ? 2 : 1,
                    totalPages = 2,
                    totalRecords = 2,
                    hasNextPage = !secondPage,
                    hasPreviousPage = secondPage
                })
            });
        });

        await page.RouteAsync("**/bff/customers/69738", route =>
        {
            Interlocked.Increment(ref state.CustomerLoads);
            return route.FulfillAsync(new()
            {
                Status = 200,
                ContentType = "application/json",
                Body = JsonSerializer.Serialize(CreateCustomer())
            });
        });
    }

    private static object CreateCustomer() => new
    {
        id = 69738,
        firstName = "ธันวรินต์",
        lastName = "กวินภัทรลักษณ์",
        fullName = "ธันวรินต์ กวินภัทรลักษณ์",
        telephone = (string?)null,
        mobile = "0612024146",
        fax = (string?)null,
        email = "thunwalin@theonesamui.com",
        dateOfBirth = "1992-08-12T00:00:00Z",
        companyId = 77,
        billingAddressId = 101,
        shippingAddressId = 102,
        createdDate = "2026-07-13T02:43:00Z",
        modifiedDate = "2026-07-13T02:44:00Z",
        billingAddress = new { id = 101, building = "128/41", addressLine1 = "หมู่ที่ 1 ตำบลบ่อผุด", addressLine2 = (string?)null, city = "อำเภอเกาะสมุย", state = "สุราษฎร์ธานี", postalCode = "84320", countryId = 764, createdDate = "2026-07-13T02:43:00Z", modifiedDate = "2026-07-13T02:44:00Z" },
        company = new { id = 77, name = "บริษัท เดอะ สมุย วัน จำกัด", taxNumber = "0845560005099 (สำนักงานใหญ่)", registrar = (string?)null, createdDate = "2026-07-13T02:43:00Z", modifiedDate = "2026-07-13T02:44:00Z" },
        shippingAddress = new { id = 102, building = "128/41", addressLine1 = "หมู่ที่ 1 ตำบลบ่อผุด", addressLine2 = (string?)null, city = "อำเภอเกาะสมุย", state = "สุราษฎร์ธานี", postalCode = "84320", countryId = 764, createdDate = "2026-07-13T02:43:00Z", modifiedDate = "2026-07-13T02:44:00Z" }
    };

    private static object CreateOrderPage(int pageIndex, int customerId = 69738)
    {
        var id = pageIndex == 2 ? 902 : 901;
        return new
        {
            items = new[]
            {
                new { id, customerId, employeeId = 1, name = $"Order {id}", processId = 1, quantity = 4, manufactured = 1, remaining = 3, subtotal = (decimal?)null, promisedDate = "2026-08-20T00:00:00Z", allowSocialMedia = false, createdDate = "2026-08-11T00:00:00Z", modifiedDate = (string?)null }
            },
            pageIndex,
            totalPages = 2,
            totalRecords = 2,
            hasNextPage = pageIndex == 1,
            hasPreviousPage = pageIndex == 2
        };
    }

    private sealed class CustomerBoundaryState
    {
        public int CustomerLoads;
        public int ActivityLoads;
        public int OrderLoads;
        public int QuotationLoads;
        public int InvoiceLoads;
        public bool FailFirstOrders;
        public string? FailPageTwoFamily;
        public int? OrderStatusCode;
        public bool MismatchedOrders;
        public Dictionary<string, List<int>> PageRequests { get; } = new(StringComparer.Ordinal)
        {
            ["orders"] = [],
            ["quotations"] = [],
            ["invoices"] = []
        };
    }
}
