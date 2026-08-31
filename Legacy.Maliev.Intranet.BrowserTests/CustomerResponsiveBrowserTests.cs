using System.Text.Json;
using Legacy.Maliev.Intranet.BrowserTests.Infrastructure;
using Microsoft.Playwright;

namespace Legacy.Maliev.Intranet.BrowserTests;

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
    public async Task CustomerDataTableKeepsCompleteValuesReachableWithoutDocumentOverflow(int width)
    {
        await using var context = await ContextAsync(width);
        var page = await context.NewPageAsync();
        await StubCustomersAsync(page);
        await page.GotoAsync(new Uri(server.BaseUri, "customers").AbsoluteUri);
        await page.Locator("[data-slot='data-table']").WaitForAsync();

        Assert.Equal(
            await page.EvaluateAsync<int>("() => document.documentElement.clientWidth"),
            await page.EvaluateAsync<int>("() => document.documentElement.scrollWidth"));
        Assert.Contains(
            await page.Locator(".shadcn-table-container").EvaluateAsync<string>("node => getComputedStyle(node).overflowX"),
            new[] { "auto", "scroll" });

        await page.GetByRole(AriaRole.Button, new() { Name = "Expand customer 101" }).ClickAsync();
        var quickView = page.Locator(".operational-data-table__popover");
        await quickView.WaitForAsync();
        var text = await quickView.InnerTextAsync();
        Assert.Contains(LongName, text);
        Assert.Contains(LongEmail, text);
        Assert.Contains(LongCompany, text);

        await page.GetByRole(AriaRole.Button, new() { Name = "Expand customer 102" }).ClickAsync();
        await quickView.WaitForAsync();
        Assert.DoesNotContain(LongName, await quickView.InnerTextAsync());
    }

    [Fact]
    public async Task CustomerDataTablePreservesUrlStateAndHeaderSorting()
    {
        await using var context = await ContextAsync(1280);
        var page = await context.NewPageAsync();
        await StubCustomersAsync(page);
        await page.GotoAsync(new Uri(server.BaseUri, "customers?index=2&size=25").AbsoluteUri);
        await page.Locator("[data-slot='data-table']").WaitForAsync();

        Assert.Contains("Page 2 of 4", await page.Locator("[data-slot='data-table-page-summary']").InnerTextAsync());
        var idSort = page.Locator(".shadcn-data-table-sort").First;
        await idSort.PressAsync("Enter");
        await page.WaitForURLAsync(url => url.Contains("sort=CustomerId_Ascending", StringComparison.Ordinal) && url.Contains("index=1", StringComparison.Ordinal));
        Assert.Equal("ascending", await page.Locator(".shadcn-data-table-sort").First.GetAttributeAsync("data-sort"));
    }

    [Fact]
    public async Task CustomerDataTableFilterAndPagingUseTheServerContract()
    {
        await using var context = await ContextAsync(390);
        var page = await context.NewPageAsync();
        await StubCustomersAsync(page);
        await page.GotoAsync(new Uri(server.BaseUri, "customers?index=2&size=25").AbsoluteUri);
        await page.Locator("[data-slot='data-table']").WaitForAsync();

        await page.Locator("[data-slot='data-table-filter']").FillAsync("Natthapol");
        await page.WaitForURLAsync(url => url.Contains("search=Natthapol", StringComparison.Ordinal));
        await page.WaitForFunctionAsync("() => document.querySelectorAll(\"[data-slot='data-table'] tbody tr\").length === 1");
        Assert.Equal(1, await page.Locator("[data-slot='data-table'] tbody tr").CountAsync());

        await page.Locator("[data-slot='data-table-filter']").FillAsync(string.Empty);
        await page.WaitForURLAsync(url => url.Contains("search=&", StringComparison.Ordinal));
        Assert.Equal("25", await page.Locator("[data-slot='data-table-page-size']").InputValueAsync());
    }

    [Fact]
    public async Task CustomerDataTableUsesTheApplicationTypeface()
    {
        await using var context = await ContextAsync(1280);
        var page = await context.NewPageAsync();
        await StubCustomersAsync(page);
        await page.GotoAsync(new Uri(server.BaseUri, "customers").AbsoluteUri);
        var cell = page.Locator("[data-slot='data-table'] tbody td").First;
        await cell.WaitForAsync();

        var fonts = await page.EvaluateAsync<JsonElement>("""
            () => ({
                body: getComputedStyle(document.body).fontFamily,
                cell: getComputedStyle(document.querySelector("[data-slot='data-table'] tbody td")).fontFamily
            })
            """);
        Assert.Equal(fonts.GetProperty("body").GetString(), fonts.GetProperty("cell").GetString());
        Assert.DoesNotContain("mono", fonts.GetProperty("cell").GetString()!, StringComparison.OrdinalIgnoreCase);
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
