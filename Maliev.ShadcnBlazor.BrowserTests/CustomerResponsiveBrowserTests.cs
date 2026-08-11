using System.Text.Json;
using Maliev.ShadcnBlazor.BrowserTests.Infrastructure;
using Microsoft.Playwright;

namespace Maliev.ShadcnBlazor.BrowserTests;

[Collection(CustomerBrowserCollection.Name)]
public sealed class CustomerResponsiveBrowserTests(
    IntranetClientServerFixture server,
    PlaywrightFixture playwright)
{
    private const string LongName = "Natthapol Vanasrivilai With An Intentionally Long Customer Display Name";
    private const string LongEmail = "natthapol.vanasrivilai+responsive-customer-fixture@international-maliev.example.com";
    private const string LongCompany = "MALIEV Precision Manufacturing and International Engineering Services Company Limited";

    [Fact]
    public async Task ProductionCustomerPageRemainsOperableAndContainedAcrossSupportedWidths()
    {
        await using var context = await playwright.Browser.NewContextAsync(new()
        {
            ViewportSize = new() { Width = 1280, Height = 900 },
            DeviceScaleFactor = 1,
            ReducedMotion = ReducedMotion.Reduce
        });
        var page = await context.NewPageAsync();
        await StubProductionBoundariesAsync(page);

        await page.GotoAsync(new Uri(server.BaseUri, "customers").AbsoluteUri);
        await page.Locator(".customers-table .mud-table-body .mud-table-row").First.WaitForAsync();

        Assert.Equal("/customers", new Uri(page.Url).AbsolutePath);
        await AssertNoDocumentOverflowAsync(page);
        await AssertAtomicAsync(page, ".customer-id-cell", "2000000001");
        await AssertAtomicAsync(page, ".customer-action-cell", "View");
        await AssertFullValueDisclosureAsync(page, ".customer-email-disclosure", LongEmail);

        await page.SetViewportSizeAsync(768, 900);
        Assert.True(await page.Locator(".mud-table-container").EvaluateAsync<bool>(
            "element => element.scrollWidth > element.clientWidth"));
        await AssertNoDocumentOverflowAsync(page);

        foreach (var width in new[] { 390, 320 })
        {
            await page.SetViewportSizeAsync(width, 844);
            await page.WaitForFunctionAsync("""
                () => getComputedStyle(document.querySelector('.customers-table .mud-table-body .mud-table-row .mud-table-cell')).padding === '0px'
                """);

            var populatedRow = page.Locator(".customers-table .mud-table-body .mud-table-row").Nth(0);
            var emptyCompanyRow = page.Locator(".customers-table .mud-table-body .mud-table-row").Nth(1);
            var rowMetrics = await populatedRow.EvaluateAsync<JsonElement>("""
                element => {
                    const style = getComputedStyle(element);
                    return {
                        height: element.getBoundingClientRect().height,
                        display: style.display,
                        rows: style.gridTemplateRows,
                        rowGap: style.rowGap,
                        padding: style.padding,
                        cells: Array.from(element.children).map(cell => ({
                            classes: cell.className,
                            height: cell.getBoundingClientRect().height,
                            display: getComputedStyle(cell).display,
                            padding: getComputedStyle(cell).padding
                        }))
                    };
                }
                """);
            var rowHeight = rowMetrics.GetProperty("height").GetDouble();
            Assert.True(rowHeight is >= 144 and <= 176, rowMetrics.ToString());
            Assert.Equal("none", await emptyCompanyRow.Locator(".customer-company-cell").EvaluateAsync<string>(
                "element => getComputedStyle(element).display"));
            Assert.True(await populatedRow.Locator(".customer-action-cell a").EvaluateAsync<bool>(
                "element => element.getBoundingClientRect().height >= 44 && element.getBoundingClientRect().width >= 44"));
            await AssertFullValueDisclosureAsync(page, ".customer-name-disclosure", LongName);
            await AssertFullValueDisclosureAsync(page, ".customer-company-disclosure", LongCompany);
            await AssertNoDocumentOverflowAsync(page);
        }
    }

    [Fact]
    public async Task ProductionCustomerToolbarKeepsSearchPrimaryAndActionsCompactAcrossSupportedWidths()
    {
        await using var context = await playwright.Browser.NewContextAsync(new()
        {
            ViewportSize = new() { Width = 1280, Height = 900 },
            DeviceScaleFactor = 1,
            ReducedMotion = ReducedMotion.Reduce
        });
        var page = await context.NewPageAsync();
        await StubProductionBoundariesAsync(page);

        await page.GotoAsync(new Uri(server.BaseUri, "customers").AbsoluteUri);
        await page.Locator(".customers-table .mud-table-body .mud-table-row").First.WaitForAsync();

        foreach (var width in new[] { 1280, 768, 390, 320 })
        {
            await page.SetViewportSizeAsync(width, 844);
            var metrics = await page.Locator(".list-toolbar").EvaluateAsync<JsonElement>("""
                element => {
                    const bounds = node => {
                        const rect = node.getBoundingClientRect();
                        return { x: rect.x, y: rect.y, width: rect.width, height: rect.height };
                    };
                    const controls = Array.from(element.querySelectorAll('.mud-input-control'));
                    const buttons = Array.from(element.querySelectorAll('.list-toolbar__actions .mud-button-root'));
                    return {
                        toolbar: bounds(element),
                        search: bounds(controls[0]),
                        sort: bounds(controls[1]),
                        pageSize: bounds(controls[2]),
                        actions: bounds(element.querySelector('.list-toolbar__actions')),
                        inputs: controls.map(bounds),
                        buttons: buttons.map(bounds)
                    };
                }
                """);

            var toolbarHeight = metrics.GetProperty("toolbar").GetProperty("height").GetDouble();
            var maximumHeight = width >= 1024 ? 100 : width > 600 ? 200 : 250;
            Assert.True(toolbarHeight <= maximumHeight, metrics.ToString());

            var searchWidth = metrics.GetProperty("search").GetProperty("width").GetDouble();
            var sortWidth = metrics.GetProperty("sort").GetProperty("width").GetDouble();
            Assert.True(searchWidth >= sortWidth, metrics.ToString());

            if (width <= 600)
            {
                foreach (var input in metrics.GetProperty("inputs").EnumerateArray())
                    Assert.True(input.GetProperty("height").GetDouble() >= 44, metrics.ToString());
                foreach (var button in metrics.GetProperty("buttons").EnumerateArray())
                    Assert.True(button.GetProperty("height").GetDouble() >= 44, metrics.ToString());

                var sortY = metrics.GetProperty("sort").GetProperty("y").GetDouble();
                var pageSizeY = metrics.GetProperty("pageSize").GetProperty("y").GetDouble();
                Assert.InRange(Math.Abs(sortY - pageSizeY), 0, 1);
            }

            await AssertNoDocumentOverflowAsync(page);
        }
    }

    [Fact]
    public async Task ClearingCustomerSearchRestoresTheUnfilteredUrlAndResultsAfterDebounce()
    {
        await using var context = await playwright.Browser.NewContextAsync(new()
        {
            ViewportSize = new() { Width = 1280, Height = 900 },
            DeviceScaleFactor = 1,
            ReducedMotion = ReducedMotion.Reduce
        });
        var page = await context.NewPageAsync();
        await StubProductionBoundariesAsync(page);

        await page.GotoAsync(new Uri(server.BaseUri, "customers").AbsoluteUri);
        var rows = page.Locator(".customers-table .mud-table-body .mud-table-row");
        await rows.First.WaitForAsync();
        Assert.Equal(2, await rows.CountAsync());

        var search = page.Locator(".list-toolbar input").First;
        await search.FillAsync("Natthapol");
        await page.WaitForURLAsync(url => url.Contains("search=Natthapol", StringComparison.Ordinal));

        await search.FillAsync(string.Empty);
        await page.WaitForURLAsync(url => url.Contains("search=&", StringComparison.Ordinal));
        await page.WaitForFunctionAsync("() => document.querySelectorAll('.customers-table .mud-table-body .mud-table-row').length === 2");
    }

    private static async Task StubProductionBoundariesAsync(IPage page)
    {
        var session = JsonSerializer.Serialize(new
        {
            isAuthenticated = true,
            employeeId = "browser-test-employee",
            displayName = "Browser Test Employee",
            roles = new[] { "Employee" },
            csrfToken = "browser-test-csrf",
            legacyDatabaseId = 1,
            permissions = new[] { "customers.read", "customers.create" }
        });
        var customers = JsonSerializer.Serialize(new
        {
            items = new object[]
            {
                new
                {
                    id = 2000000001,
                    firstName = "Natthapol",
                    lastName = "Vanasrivilai",
                    fullName = LongName,
                    email = LongEmail,
                    company = new { id = 77, name = LongCompany }
                },
                new
                {
                    id = 42,
                    firstName = "Short",
                    lastName = "Name",
                    fullName = "Short Name",
                    email = "short@maliev.com",
                    company = (object?)null
                }
            },
            pageIndex = 1,
            totalPages = 1,
            totalRecords = 2,
            hasNextPage = false,
            hasPreviousPage = false
        });

        await page.RouteAsync("**/bff/session", route => route.FulfillAsync(new()
        {
            Status = 200,
            ContentType = "application/json",
            Body = session
        }));
        await page.RouteAsync("**/bff/customers?*", async route =>
        {
            var query = new Uri(route.Request.Url).Query;
            var body = query.Contains("search=Natthapol", StringComparison.OrdinalIgnoreCase)
                ? JsonSerializer.Serialize(new
                {
                    items = JsonSerializer.Deserialize<JsonElement>(customers).GetProperty("items").EnumerateArray().Take(1).ToArray(),
                    pageIndex = 1,
                    totalPages = 1,
                    totalRecords = 1,
                    hasNextPage = false,
                    hasPreviousPage = false
                })
                : customers;
            await route.FulfillAsync(new()
            {
                Status = 200,
                ContentType = "application/json",
                Body = body
            });
        });
    }

    private static async Task AssertAtomicAsync(IPage page, string selector, string expectedText)
    {
        var element = page.Locator($".customers-table .mud-table-body .mud-table-row:first-child {selector}");
        Assert.Equal(expectedText, (await element.InnerTextAsync()).Trim());
        Assert.Equal("nowrap", await element.EvaluateAsync<string>("node => getComputedStyle(node).whiteSpace"));
        Assert.InRange(await element.EvaluateAsync<float>("node => node.getBoundingClientRect().height"), 1, 72);
    }

    private static async Task AssertFullValueDisclosureAsync(IPage page, string selector, string expectedText)
    {
        var trigger = page.Locator($".customers-table .mud-table-body .mud-table-row:first-child {selector} button");
        await trigger.FocusAsync();
        Assert.True(await trigger.EvaluateAsync<bool>("element => element === document.activeElement"));
        await trigger.PressAsync("Enter");

        var menu = page.Locator(".mud-popover-open").Last;
        await menu.WaitForAsync();
        Assert.Equal(expectedText, (await menu.Locator(".customer-full-value").InnerTextAsync()).Trim());
        await page.Keyboard.PressAsync("Escape");
        await menu.WaitForAsync(new() { State = WaitForSelectorState.Hidden });
    }

    private static async Task AssertNoDocumentOverflowAsync(IPage page)
    {
        var geometry = await page.EvaluateAsync<JsonElement>("""
            () => {
                const root = document.documentElement;
                const offenders = Array.from(document.querySelectorAll('body *'))
                    .map(element => ({
                        tag: element.tagName,
                        classes: element.className?.toString() ?? '',
                        left: element.getBoundingClientRect().left,
                        right: element.getBoundingClientRect().right,
                        scrollWidth: element.scrollWidth,
                        clientWidth: element.clientWidth
                    }))
                    .filter(item => item.right > root.clientWidth + 0.5)
                    .slice(0, 5);
                const containers = Array.from(document.querySelectorAll('.customers-table-shell, .customers-table, .mud-table-container'))
                    .map(element => {
                        const rect = element.getBoundingClientRect();
                        const style = getComputedStyle(element);
                        return {
                            classes: element.className?.toString() ?? '',
                            left: rect.left,
                            right: rect.right,
                            scrollWidth: element.scrollWidth,
                            clientWidth: element.clientWidth,
                            overflowX: style.overflowX,
                            minWidth: style.minWidth,
                            width: style.width
                        };
                    });
                return { scrollWidth: root.scrollWidth, clientWidth: root.clientWidth, offenders, containers };
            }
            """);
        var horizontalTravel = await page.EvaluateAsync<double>("""
            () => {
                window.scrollTo({ left: 100000, behavior: 'instant' });
                const travel = window.scrollX;
                window.scrollTo({ left: 0, behavior: 'instant' });
                return travel;
            }
            """);
        Assert.True(horizontalTravel == 0, geometry.ToString());
    }
}
