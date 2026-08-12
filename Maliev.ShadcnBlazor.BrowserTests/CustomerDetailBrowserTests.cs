using System.Text.Json;
using Maliev.ShadcnBlazor.BrowserTests.Infrastructure;
using Microsoft.Playwright;

namespace Maliev.ShadcnBlazor.BrowserTests;

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
                const control = element.closest('.mud-input-control');
                const input = element.closest('.mud-input');
                const border = input.querySelector('.mud-input-outlined-border') ?? input;
                const label = control.querySelector('.mud-input-label');
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
            "element => element.closest('.mud-input').getBoundingClientRect().height >= 44",
            dateInputHandle,
            new() { Timeout = 10_000 });
        var narrowDateGeometry = await dateInput.EvaluateAsync<JsonElement>("""
            element => {
                const input = element.closest('.mud-input');
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
        await page.GetByText("Invoices: Not permitted", new() { Exact = true }).WaitForAsync();
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
        Assert.Equal(1, state.CustomerLoads);
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
        await page.GetByText("ใบแจ้งหนี้: ไม่มีสิทธิ์เข้าถึง", new() { Exact = true }).WaitForAsync();

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
            return route.FulfillAsync(new()
            {
                Status = 200,
                ContentType = "application/json",
                Body = JsonSerializer.Serialize(CreateOrderPage(secondPage ? 2 : 1))
            });
        });
        await page.RouteAsync("**/bff/customers/69738/quotations*", route =>
        {
            Interlocked.Increment(ref state.QuotationLoads);
            return route.FulfillAsync(new()
            {
                Status = 200,
                ContentType = "application/json",
                Body = JsonSerializer.Serialize(new
                {
                    items = new[]
                    {
                        new { id = 801, customerId = 69738, employeeId = 1, invoiceId = (int?)null, period = 30, expirationDate = "2026-09-11T00:00:00Z", subtotal = 100m, vat = 7m, total = 107m, withholdingTax = (decimal?)null, quotedAmount = 107m, currencyId = 1, comment = (string?)null, fob = (string?)null, shippedVia = (string?)null, terms = (string?)null, accepted = (bool?)null, createdDate = "2026-08-11T00:00:00Z", modifiedDate = (string?)null }
                    },
                    pageIndex = 1,
                    totalPages = 1,
                    totalRecords = 1,
                    hasNextPage = false,
                    hasPreviousPage = false
                })
            });
        });
        await page.RouteAsync("**/bff/customers/69738/invoices*", route =>
        {
            Interlocked.Increment(ref state.InvoiceLoads);
            return route.FulfillAsync(new()
            {
                Status = 200,
                ContentType = "application/json",
                Body = JsonSerializer.Serialize(new
                {
                    items = new[]
                    {
                        new { id = 701, customerId = 69738, number = "INV-701", currency = "THB", purchaseOrderNumber = (string?)null, subtotal = 100m, vat = 7m, total = 107m, withholdingTax = (decimal?)null, outstanding = 107m, isPaid = false, receiptId = (int?)null, paymentDate = (string?)null, createdDate = "2026-08-11T00:00:00Z" }
                    },
                    pageIndex = 1,
                    totalPages = 1,
                    totalRecords = 1,
                    hasNextPage = false,
                    hasPreviousPage = false
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

    private static object CreateOrderPage(int pageIndex)
    {
        var id = pageIndex == 2 ? 902 : 901;
        return new
        {
            items = new[]
            {
                new { id, customerId = 69738, employeeId = 1, name = $"Order {id}", processId = 1, quantity = 4, manufactured = 1, remaining = 3, subtotal = (decimal?)null, promisedDate = "2026-08-20T00:00:00Z", allowSocialMedia = false, createdDate = "2026-08-11T00:00:00Z", modifiedDate = (string?)null }
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
    }
}
