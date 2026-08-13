using System.Text.Json;
using Maliev.ShadcnBlazor.BrowserTests.Infrastructure;
using Microsoft.Playwright;

namespace Maliev.ShadcnBlazor.BrowserTests;

[Collection(CustomerBrowserCollection.Name)]
public sealed class CustomerResponsiveBrowserTests(
    IntranetClientServerFixture server,
    PlaywrightFixture playwright)
{
    private const string LongName = "Natthapol Vanasrivilai with an intentionally complete customer name";
    private const string LongEmail = "natthapol.long.address@maliev.com";
    private const string LongCompany = "MALIEV Precision Manufacturing Company Limited";

    [Theory]
    [InlineData(1280)]
    [InlineData(768)]
    [InlineData(390)]
    [InlineData(320)]
    public async Task CustomerFullValuesRemainAvailableThroughOneQuickViewAtEveryWidth(int width)
    {
        await using var context = await ContextAsync(width);
        var page = await context.NewPageAsync();
        await StubCustomersAsync(page);
        await page.GotoAsync(new Uri(server.BaseUri, "customers").AbsoluteUri);
        await page.Locator("table.operational-table").WaitForAsync();

        await page.GetByRole(AriaRole.Button, new() { Name = "Expand customer 101" }).ClickAsync();
        var quickView = page.Locator(".operational-table__quick-view");
        Assert.Equal(1, await quickView.CountAsync());
        var text = await quickView.InnerTextAsync();
        Assert.Contains(LongName, text);
        Assert.Contains(LongEmail, text);
        Assert.Contains(LongCompany, text);

        await page.GetByRole(AriaRole.Button, new() { Name = "Expand customer 102" }).ClickAsync();
        Assert.Equal(1, await quickView.CountAsync());
        Assert.DoesNotContain(LongName, await quickView.InnerTextAsync());
        Assert.Equal(
            await page.EvaluateAsync<int>("() => document.documentElement.clientWidth"),
            await page.EvaluateAsync<int>("() => document.documentElement.scrollWidth"));
    }

    [Fact]
    public async Task CustomerSortPreservesAllTenWireValuesAndHeaderAriaSort()
    {
        await using var context = await ContextAsync(1280);
        var page = await context.NewPageAsync();
        await StubCustomersAsync(page);

        var expectedSorts = new (string Value, string Label)[]
        {
            ("CustomerCreatedDate_Descending", "Newest customers"),
            ("CustomerCreatedDate_Ascending", "Oldest customers"),
            ("CustomerModifiedDate_Descending", "Recently updated"),
            ("CustomerModifiedDate_Ascending", "Updated longest ago"),
            ("CustomerCompany_Ascending", "Company A–Z"),
            ("CustomerCompany_Descending", "Company Z–A"),
            ("CustomerEmail_Ascending", "Email A–Z"),
            ("CustomerEmail_Descending", "Email Z–A"),
            ("CustomerId_Descending", "Highest ID first"),
            ("CustomerId_Ascending", "Lowest ID first"),
        };
        foreach (var (value, label) in expectedSorts)
        {
            await page.GotoAsync(new Uri(server.BaseUri, $"customers?sort={value}&index=2&size=25").AbsoluteUri);
            await page.Locator("table.operational-table").WaitForAsync();
            Assert.Contains($"sort={value}", page.Url, StringComparison.Ordinal);
            Assert.Equal(label, (await page.GetByRole(AriaRole.Combobox, new() { Name = "Sort by" }).InnerTextAsync()).Trim());
        }

        await page.GotoAsync(new Uri(server.BaseUri, "customers?sort=CustomerCreatedDate_Descending&index=3&size=25").AbsoluteUri);
        await page.GetByRole(AriaRole.Button, new() { Name = "Sort by ID" }).PressAsync("Enter");
        await page.WaitForURLAsync(url => url.Contains("sort=CustomerId_Ascending", StringComparison.Ordinal) && url.Contains("index=1", StringComparison.Ordinal));
        Assert.Equal("ascending", await page.Locator("th.customer-id").GetAttributeAsync("aria-sort"));
        await page.GetByRole(AriaRole.Button, new() { Name = "Sort by ID" }).PressAsync("Space");
        await page.WaitForURLAsync(url => url.Contains("sort=CustomerId_Descending", StringComparison.Ordinal));
        Assert.Equal("descending", await page.Locator("th.customer-id").GetAttributeAsync("aria-sort"));
    }

    [Fact]
    public async Task CustomerSearchClearAndPagerPreserveQueryBehaviorAndClearExpansion()
    {
        await using var context = await ContextAsync(390);
        var page = await context.NewPageAsync();
        await StubCustomersAsync(page);
        await page.GotoAsync(new Uri(server.BaseUri, "customers?index=2&size=25").AbsoluteUri);
        await page.Locator("table.operational-table").WaitForAsync();

        Assert.Equal(1, await page.GetByRole(AriaRole.Navigation, new() { Name = "Customer pages before results" }).CountAsync());
        Assert.Equal(1, await page.GetByRole(AriaRole.Navigation, new() { Name = "Customer pages after results" }).CountAsync());
        Assert.Equal("Page 2 of 4 · 76 records", (await page.Locator(".customers-results-summary").InnerTextAsync()).Trim());

        await page.GetByRole(AriaRole.Button, new() { Name = "Expand customer 101" }).ClickAsync();
        await page.GetByRole(AriaRole.Textbox, new() { Name = "Search" }).FillAsync("Natthapol");
        await page.WaitForURLAsync(url => url.Contains("search=Natthapol", StringComparison.Ordinal));
        Assert.Equal(0, await page.Locator(".operational-table__quick-view").CountAsync());
        await page.GetByRole(AriaRole.Button, new() { Name = "Clear", Exact = true }).ClickAsync();
        await page.WaitForURLAsync(url => url.Contains("search=&", StringComparison.Ordinal));
        await page.WaitForFunctionAsync("() => document.querySelectorAll('.operational-table__row').length === 2");
        Assert.Equal(2, await page.Locator(".operational-table__row").CountAsync());
    }

    [Theory]
    [InlineData(390)]
    [InlineData(320)]
    public async Task NarrowCustomerSupportingHeaderIsOutsideTheVisibleAccessibilityTree(int width)
    {
        await using var context = await ContextAsync(width);
        var page = await context.NewPageAsync();
        await StubCustomersAsync(page);
        await page.GotoAsync(new Uri(server.BaseUri, "customers").AbsoluteUri);
        await page.Locator("table.operational-table").WaitForAsync();

        Assert.False(await page.Locator("th.customer-company").IsVisibleAsync());
        Assert.Equal(0, await page.GetByRole(AriaRole.Button, new() { Name = "Sort by company" }).CountAsync());
        Assert.Equal(1, await page.GetByRole(AriaRole.Button, new() { Name = "Sort by ID" }).CountAsync());
        Assert.Equal(1, await page.GetByRole(AriaRole.Button, new() { Name = "Sort by email" }).CountAsync());
    }

    [Fact]
    public async Task CustomerToolbarKeepsSearchPrimaryMutedAndContainedAcrossSupportedWidths()
    {
        await using var context = await ContextAsync(1280);
        var page = await context.NewPageAsync();
        await StubCustomersAsync(page);
        await page.GotoAsync(new Uri(server.BaseUri, "customers").AbsoluteUri);
        await page.Locator("table.operational-table").WaitForAsync();

        var expectedMutedSurface = await page.EvaluateAsync<string>("""
            () => {
                const probe = document.createElement('span');
                probe.style.background = 'var(--shadcn-muted)';
                document.querySelector('[data-shadcn-scope]').append(probe);
                const color = getComputedStyle(probe).backgroundColor;
                probe.remove();
                return color;
            }
            """);
        var surface = await page.Locator(".list-toolbar").EvaluateAsync<JsonElement>("""
            element => {
                const style = getComputedStyle(element);
                return { background: style.backgroundColor, borderWidth: style.borderTopWidth, boxShadow: style.boxShadow, borderRadius: style.borderRadius };
            }
            """);
        Assert.Equal(expectedMutedSurface, surface.GetProperty("background").GetString());
        Assert.Equal("0px", surface.GetProperty("borderWidth").GetString());
        Assert.Equal("none", surface.GetProperty("boxShadow").GetString());
        Assert.NotEqual("0px", surface.GetProperty("borderRadius").GetString());

        foreach (var width in new[] { 1280, 768, 390, 320 })
        {
            await page.SetViewportSizeAsync(width, 844);
            if (width <= 900)
            {
                await page.WaitForFunctionAsync("""
                    () => Array.from(document.querySelectorAll('.list-toolbar .mud-input'))
                        .filter(input => {
                            const style = getComputedStyle(input);
                            const rect = input.getBoundingClientRect();
                            return style.display !== 'none' && style.visibility !== 'hidden' && rect.width > 0 && rect.height > 0;
                        })
                        .every(input => input.getBoundingClientRect().height >= 44)
                    """);
            }
            var metrics = await page.Locator(".list-toolbar").EvaluateAsync<JsonElement>("""
                element => {
                    const bounds = node => { const rect = node.getBoundingClientRect(); return { x: rect.x, y: rect.y, width: rect.width, height: rect.height }; };
                    const controls = Array.from(element.querySelectorAll('.mud-input-control'));
                    const interactiveInputs = Array.from(element.querySelectorAll('.mud-input')).filter(input => {
                        const style = getComputedStyle(input);
                        const rect = input.getBoundingClientRect();
                        return style.display !== 'none' && style.visibility !== 'hidden' && rect.width > 0 && rect.height > 0;
                    });
                    const buttons = Array.from(element.querySelectorAll('.list-toolbar__actions .mud-button-root'));
                    return {
                        toolbar: bounds(element), search: bounds(controls[0]), sort: bounds(controls[1]), pageSize: bounds(controls[2]),
                        controls: controls.map(bounds),
                        interactiveInputs: interactiveInputs.map(input => ({ ...bounds(input), classes: input.className })),
                        buttons: buttons.map(bounds)
                    };
                }
                """);
            Assert.True(metrics.GetProperty("toolbar").GetProperty("height").GetDouble() <= (width >= 1024 ? 100 : width > 600 ? 200 : 250), metrics.ToString());
            Assert.True(metrics.GetProperty("search").GetProperty("width").GetDouble() >= metrics.GetProperty("sort").GetProperty("width").GetDouble(), metrics.ToString());
            if (width <= 900)
            {
                Assert.All(metrics.GetProperty("controls").EnumerateArray(), control => Assert.True(control.GetProperty("height").GetDouble() >= 44, metrics.ToString()));
                Assert.Equal(3, metrics.GetProperty("interactiveInputs").GetArrayLength());
                Assert.All(metrics.GetProperty("interactiveInputs").EnumerateArray(), input => Assert.True(input.GetProperty("height").GetDouble() >= 44, metrics.ToString()));
                Assert.All(metrics.GetProperty("buttons").EnumerateArray(), button => Assert.True(button.GetProperty("height").GetDouble() >= 44, metrics.ToString()));
            }
            if (width <= 600)
            {
                Assert.InRange(Math.Abs(metrics.GetProperty("sort").GetProperty("y").GetDouble() - metrics.GetProperty("pageSize").GetProperty("y").GetDouble()), 0, 1);
            }
            Assert.Equal(width > 900 ? "1px" : "0px", await page.Locator(".list-toolbar__actions").EvaluateAsync<string>("element => getComputedStyle(element).borderInlineStartWidth"));
            Assert.Equal(
                await page.EvaluateAsync<int>("() => document.documentElement.clientWidth"),
                await page.EvaluateAsync<int>("() => document.documentElement.scrollWidth"));
        }
    }

    [Fact]
    public async Task ThaiCustomerToolbarKeepsRefreshAccessibleAndAllSortOptionsContainedAt320()
    {
        await using var context = await ContextAsync(320);
        await context.AddInitScriptAsync("localStorage.setItem('maliev_culture', 'th-TH')");
        var page = await context.NewPageAsync();
        await StubCustomersAsync(page);
        await page.GotoAsync(new Uri(server.BaseUri, "customers").AbsoluteUri);
        await page.Locator("table.operational-table").WaitForAsync();

        await page.GetByRole(AriaRole.Textbox, new() { Name = "ค้นหา" }).FillAsync("Natthapol");
        await page.WaitForURLAsync(url => url.Contains("search=Natthapol", StringComparison.Ordinal));
        var actions = page.Locator(".list-toolbar__actions .mud-button-root");
        Assert.Equal(2, await actions.CountAsync());
        Assert.Equal("ล้างตัวกรอง", (await actions.Nth(0).InnerTextAsync()).Trim());
        Assert.Equal(string.Empty, (await actions.Nth(1).InnerTextAsync()).Trim());
        Assert.Equal("รีเฟรชข้อมูล", await actions.Nth(1).GetAttributeAsync("aria-label"));
        Assert.Equal("รีเฟรชข้อมูล", await actions.Nth(1).GetAttributeAsync("title"));
        var actionHeights = await actions.EvaluateAllAsync<double[]>("elements => elements.map(element => element.getBoundingClientRect().height)");
        Assert.All(actionHeights, height => Assert.True(height >= 44));

        var selectedSort = page.Locator(".list-toolbar .mud-select-input").First;
        Assert.Equal("ลูกค้าใหม่ล่าสุด", (await selectedSort.InnerTextAsync()).Trim());
        await page.Locator(".list-toolbar .mud-select").First.ClickAsync();
        var options = page.Locator(".mud-popover-open .mud-list-item");
        await options.First.WaitForAsync();
        Assert.Equal(new[] { "ลูกค้าใหม่ล่าสุด", "ลูกค้าเก่าสุด", "อัปเดตล่าสุด", "อัปเดตนานที่สุด", "บริษัท ก–ฮ", "บริษัท ฮ–ก", "อีเมล A–Z", "อีเมล Z–A", "ID มากไปน้อย", "ID น้อยไปมาก" }, await options.AllInnerTextsAsync());
        var menu = await page.Locator(".mud-popover-open").Last.EvaluateAsync<JsonElement>("element => { const rect = element.getBoundingClientRect(); return { left: rect.left, right: rect.right, width: rect.width, viewport: innerWidth }; }");
        Assert.True(menu.GetProperty("left").GetDouble() >= 8, menu.ToString());
        Assert.True(menu.GetProperty("right").GetDouble() <= menu.GetProperty("viewport").GetDouble() - 8, menu.ToString());
        Assert.True(menu.GetProperty("width").GetDouble() <= 304, menu.ToString());
    }

    [Fact]
    public async Task CustomerRefreshUsesDenseIconAndTouchTargetAt1280CoarsePointer()
    {
        await using var context = await playwright.Browser.NewContextAsync(new()
        {
            ViewportSize = new() { Width = 1280, Height = 900 },
            DeviceScaleFactor = 1,
            HasTouch = true,
            ReducedMotion = ReducedMotion.Reduce,
        });
        var page = await context.NewPageAsync();
        await StubCustomersAsync(page);
        await page.GotoAsync(new Uri(server.BaseUri, "customers").AbsoluteUri);
        await page.Locator("table.operational-table").WaitForAsync();

        var geometry = await page.Locator("button.list-toolbar__refresh").EvaluateAsync<JsonElement>("""
            element => {
                const root = element.getBoundingClientRect();
                const icon = element.querySelector('svg').getBoundingClientRect();
                return { rootWidth: root.width, rootHeight: root.height, iconWidth: icon.width, iconHeight: icon.height };
            }
            """);
        Assert.True(geometry.GetProperty("rootWidth").GetDouble() >= 44, geometry.ToString());
        Assert.True(geometry.GetProperty("rootHeight").GetDouble() >= 44, geometry.ToString());
        Assert.Equal(20, geometry.GetProperty("iconWidth").GetDouble());
        Assert.Equal(20, geometry.GetProperty("iconHeight").GetDouble());
    }

    private Task<IBrowserContext> ContextAsync(int width) => playwright.Browser.NewContextAsync(new()
    {
        ViewportSize = new() { Width = width, Height = 844 },
        DeviceScaleFactor = 1,
        ReducedMotion = ReducedMotion.Reduce,
    });

    private static async Task StubCustomersAsync(IPage page)
    {
        await page.RouteAsync("**/bff/session", route => route.FulfillAsync(new()
        {
            Status = 200,
            ContentType = "application/json",
            Body = JsonSerializer.Serialize(new
            {
                isAuthenticated = true,
                employeeId = "customer-browser-employee",
                displayName = "Customer Browser Employee",
                roles = new[] { "Employee" },
                csrfToken = "customer-browser-csrf",
                legacyDatabaseId = 1,
                permissions = new[] { "customers.read", "customers.create" },
            }),
        }));
        await page.RouteAsync("**/bff/customers?*", async route =>
        {
            var uri = new Uri(route.Request.Url);
            var query = System.Web.HttpUtility.ParseQueryString(uri.Query);
            var isSearch = string.Equals(query["search"], "Natthapol", StringComparison.OrdinalIgnoreCase);
            var pageIndex = int.TryParse(query["index"], out var parsedIndex) ? parsedIndex : 1;
            var isPaged = pageIndex > 1;
            var items = new object[]
            {
                new { id = 101, firstName = "Natthapol", lastName = "Vanasrivilai", fullName = LongName, email = LongEmail, company = new { id = 77, name = LongCompany } },
                new { id = 102, firstName = "Mali", lastName = "Dee", fullName = "Mali Dee", email = "mali@maliev.com", company = (object?)null },
            };
            await route.FulfillAsync(new()
            {
                Status = 200,
                ContentType = "application/json",
                Body = JsonSerializer.Serialize(new
                {
                    items = isSearch ? items.Take(1).ToArray() : items,
                    pageIndex,
                    totalPages = isPaged ? 4 : 1,
                    totalRecords = isSearch ? 1 : isPaged ? 76 : 2,
                    hasNextPage = isPaged && pageIndex < 4,
                    hasPreviousPage = isPaged,
                }),
            });
        });
    }
}
