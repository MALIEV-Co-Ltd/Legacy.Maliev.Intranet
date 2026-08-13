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
