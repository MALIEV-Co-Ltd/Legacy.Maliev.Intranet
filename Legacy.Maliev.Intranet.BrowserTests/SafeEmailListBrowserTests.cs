using System.Collections.Concurrent;
using Legacy.Maliev.Intranet.BrowserTests.Infrastructure;
using Microsoft.Playwright;

namespace Legacy.Maliev.Intranet.BrowserTests;

public sealed class SafeEmailListBrowserTests
{
    [Theory]
    [InlineData("/customers", "/bff/customers", "legacy-customer.customers.list", false)]
    [InlineData("/Employees/Index", "/bff/employees", "legacy-employee.employees.list", true)]
    public async Task RoutedListRendersOnlySupportedMailtoLinks(
        string documentPath, string listPath, string permission, bool expectSeparateNames)
    {
        var server = new IntranetClientServerFixture();
        var playwright = new PlaywrightFixture();
        try
        {
            await server.InitializeAsync();
            await playwright.InitializeAsync();
            await CheckListAsync(server.BaseUri, playwright.Browser, documentPath, listPath, permission, expectSeparateNames);
        }
        finally
        {
            try { await playwright.DisposeAsync(); }
            finally { await server.DisposeAsync(); }
        }
    }

    private static async Task CheckListAsync(
        Uri origin, IBrowser browser, string documentPath, string listPath, string permission, bool expectSeparateNames)
    {
        const string supported = "reader+parts@example.test";
        const string malformed = "reader@example.test?subject=unexpected";
        var rows = new[]
        {
            new { id = 701, firstName = "Synthetic Given", lastName = "Synthetic Family", fullName = "Synthetic Supported", email = supported },
            new { id = 702, firstName = "Synthetic", lastName = "Malformed", fullName = "Synthetic Malformed", email = malformed },
            new { id = 703, firstName = "Synthetic", lastName = "Blank", fullName = "Synthetic Blank", email = " " },
        };
        var forbidden = new ConcurrentQueue<string>();
        var listReads = 0;
        await using var context = await browser.NewContextAsync(new()
        {
            ServiceWorkers = ServiceWorkerPolicy.Block,
            ViewportSize = new() { Width = 1600, Height = 900 },
        });
        await context.AddInitScriptAsync("localStorage.setItem('maliev_culture', 'en-US');");
        await context.RouteWebSocketAsync("**/*", socket =>
        {
            forbidden.Enqueue("WebSocket " + socket.Url);
            _ = socket.CloseAsync();
        });
        await context.RouteAsync("**/*", async route =>
        {
            var request = route.Request;
            var uri = new Uri(request.Url);
            var path = uri.AbsolutePath;
            if (request.Method == "GET" && request.ResourceType == "script"
                && uri.AbsoluteUri == "https://accounts.google.com/gsi/client")
            {
                // The static shell references this script. The fake session needs no identity service.
                await route.AbortAsync();
                return;
            }

            if (request.Method == "GET" && uri.GetLeftPart(UriPartial.Authority) == origin.GetLeftPart(UriPartial.Authority))
            {
                if (path == "/bff/session" && uri.Query.Length == 0)
                {
                    await route.FulfillAsync(new()
                    {
                        Json = new
                        {
                            isAuthenticated = true,
                            employeeId = "synthetic-list-reader",
                            displayName = "Synthetic List Reader",
                            roles = new[] { "Employee" },
                            permissions = new[] { permission },
                        },
                    });
                    return;
                }

                if (path == listPath)
                {
                    Interlocked.Increment(ref listReads);
                    await route.FulfillAsync(new()
                    {
                        Json = new { items = rows, pageIndex = 1, totalPages = 1, totalRecords = rows.Length, hasNextPage = false, hasPreviousPage = false },
                    });
                    return;
                }

                if (path == documentPath && request.ResourceType == "document"
                    || path == "/Legacy.Maliev.Intranet.Client.styles.css"
                    || path.StartsWith("/_framework/", StringComparison.Ordinal)
                    || path.StartsWith("/_content/", StringComparison.Ordinal)
                    || path.StartsWith("/css/", StringComparison.Ordinal)
                    || path.StartsWith("/js/", StringComparison.Ordinal)
                    || path.StartsWith("/images/", StringComparison.Ordinal))
                {
                    await route.ContinueAsync();
                    return;
                }
            }

            forbidden.Enqueue(request.Method + " " + uri.AbsoluteUri);
            await route.AbortAsync();
        });

        await using var page = await context.NewPageAsync();
        page.SetDefaultTimeout(15_000);
        try
        {
            var target = new Uri(origin, documentPath);
            var response = await page.GotoAsync(target.AbsoluteUri);
            Assert.Equal(200, response?.Status);
            var table = page.Locator("[data-slot='data-table']");
            ILocator Row(string name) => table.Locator("tbody tr").Filter(new() { HasText = name });
            static ILocator Cell(ILocator row, string column) => row.Locator($"td[data-column='{column}']");

            var supportedRow = Row(rows[0].fullName);
            var link = Cell(supportedRow, "email").GetByRole(AriaRole.Link);
            await Assertions.Expect(link).ToHaveTextAsync(supported);
            await Assertions.Expect(link).ToHaveAttributeAsync("href", "mailto:reader%2Bparts@example.test");
            var malformedCell = Cell(Row(rows[1].fullName), "email");
            await Assertions.Expect(malformedCell).ToHaveTextAsync(malformed);
            await Assertions.Expect(malformedCell.Locator("a")).ToHaveCountAsync(0);
            var blankCell = Cell(Row(rows[2].fullName), "email");
            await Assertions.Expect(blankCell).ToHaveTextAsync("-");
            await Assertions.Expect(blankCell.Locator("a")).ToHaveCountAsync(0);

            if (expectSeparateNames)
            {
                await Assertions.Expect(table.GetByRole(AriaRole.Columnheader, new() { Name = "First name", Exact = true })).ToBeVisibleAsync();
                await Assertions.Expect(table.GetByRole(AriaRole.Columnheader, new() { Name = "Last name", Exact = true })).ToBeVisibleAsync();
                await Assertions.Expect(Cell(supportedRow, "name")).ToHaveTextAsync(rows[0].fullName);
                await Assertions.Expect(Cell(supportedRow, "firstName")).ToHaveTextAsync(rows[0].firstName);
                await Assertions.Expect(Cell(supportedRow, "lastName")).ToHaveTextAsync(rows[0].lastName);
            }

            Assert.Equal(target.AbsoluteUri, page.Url);
            Assert.Equal(1, Volatile.Read(ref listReads));
            Assert.Single(context.Pages);
        }
        finally
        {
            await page.CloseAsync();
            Assert.Empty(forbidden);
        }
    }
}
