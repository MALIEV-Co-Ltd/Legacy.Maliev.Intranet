using System.Collections.Concurrent;
using System.Text.Json;
using Legacy.Maliev.Intranet.BrowserTests.Infrastructure;
using Legacy.Maliev.Intranet.Contracts;
using Microsoft.Playwright;

namespace Legacy.Maliev.Intranet.BrowserTests;

public sealed class SupplierListContactBrowserTests
{
    [Fact]
    public async Task RoutedSupplierListRendersSafeContactCellsAndKeepsDetailLink()
    {
        var server = new IntranetClientServerFixture();
        var playwright = new PlaywrightFixture();
        try
        {
            await server.InitializeAsync();
            await playwright.InitializeAsync();
            await CheckRowsAsync(server.BaseUri, playwright.Browser);
        }
        finally
        {
            try { await playwright.DisposeAsync(); }
            finally { await server.DisposeAsync(); }
        }

        using var http = new HttpClient(new HttpClientHandler { UseProxy = false }) { Timeout = TimeSpan.FromSeconds(3) };
        await Assert.ThrowsAsync<HttpRequestException>(async () =>
        {
            using var response = await http.GetAsync(server.BaseUri);
        });
    }

    private static async Task CheckRowsAsync(Uri origin, IBrowser browser)
    {
        const string website = "https://supplier.example.test/catalog";
        const string email = "supplier+parts@example.test";
        const string invalidWebsite = "<b>not a website</b>";
        const string invalidEmail = "supplier@example.test?subject=fixture";
        SupplierListItem[] rows =
        [
            new(42, "Synthetic valid supplier", email, "000-000-0010", website, "000-000-0020", "000-000-0030"),
            new(43, "Synthetic invalid supplier", invalidEmail, null, invalidWebsite),
            new(44, "Synthetic empty supplier", null, null),
        ];
        var json = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        var list = new SupplierListPage(rows, 1, 1, rows.Length, false, false);
        var unexpected = new ConcurrentQueue<string>();
        var listReads = 0;
        await using var context = await browser.NewContextAsync(new()
        {
            ServiceWorkers = ServiceWorkerPolicy.Block,
            ViewportSize = new() { Width = 1800, Height = 950 },
        });
        await context.AddInitScriptAsync("localStorage.setItem('maliev_culture', 'en-US');");
        await context.RouteWebSocketAsync("**/*", route => _ = route.CloseAsync());
        await context.RouteAsync("**/*", async route =>
        {
            var request = route.Request;
            var uri = new Uri(request.Url);
            if (uri.GetLeftPart(UriPartial.Authority) != origin.GetLeftPart(UriPartial.Authority))
            {
                await route.AbortAsync();
                return;
            }

            object payload;
            if (request.Method == "GET" && uri.AbsolutePath == "/bff/session")
            {
                payload = new
                {
                    isAuthenticated = true,
                    employeeId = "synthetic-supplier-reader",
                    displayName = "Synthetic Supplier Reader",
                    roles = new[] { "Employee" },
                    permissions = new[] { "legacy-procurement.suppliers.read" },
                };
            }
            else if (request.Method == "GET" && uri.AbsolutePath == "/bff/suppliers")
            {
                Interlocked.Increment(ref listReads);
                payload = list;
            }
            else if (request.Method == "GET" && !uri.AbsolutePath.StartsWith("/bff", StringComparison.OrdinalIgnoreCase))
            {
                await route.ContinueAsync();
                return;
            }
            else
            {
                unexpected.Enqueue(request.Method + " " + uri.AbsolutePath);
                await route.AbortAsync();
                return;
            }

            await route.FulfillAsync(new() { Status = 200, ContentType = "application/json", Body = JsonSerializer.Serialize(payload, json) });
        });

        await using var page = await context.NewPageAsync();
        page.SetDefaultTimeout(15_000);
        var target = new Uri(origin, "/Suppliers/Index");
        var response = await page.GotoAsync(target.AbsoluteUri);
        Assert.Equal(200, response?.Status);
        var table = page.Locator("#suppliers-results");
        ILocator Row(string name) => table.GetByRole(AriaRole.Row).Filter(new() { HasText = name });
        static ILocator Cell(ILocator row, string column) => row.Locator($"td[data-column='{column}']");
        var normal = Row("Synthetic valid supplier");
        var webLink = Cell(normal, "website").GetByRole(AriaRole.Link);
        await Assertions.Expect(webLink).ToHaveTextAsync(website);
        await Assertions.Expect(webLink).ToHaveAttributeAsync("href", website);
        await Assertions.Expect(webLink).ToHaveAttributeAsync("target", "_blank");
        await Assertions.Expect(webLink).ToHaveAttributeAsync("rel", "noopener noreferrer");
        var mailLink = Cell(normal, "email").GetByRole(AriaRole.Link);
        await Assertions.Expect(mailLink).ToHaveTextAsync(email);
        await Assertions.Expect(mailLink).ToHaveAttributeAsync("href", "mailto:supplier%2Bparts@example.test");
        await Assertions.Expect(Cell(normal, "mobile")).ToHaveTextAsync("000-000-0020");
        await Assertions.Expect(Cell(normal, "fax")).ToHaveTextAsync("000-000-0030");
        await Assertions.Expect(normal.Locator("a[href='/Suppliers/View?id=42']")).ToHaveCountAsync(1);

        var invalid = Row("Synthetic invalid supplier");
        await Assertions.Expect(Cell(invalid, "website")).ToHaveTextAsync(invalidWebsite);
        await Assertions.Expect(Cell(invalid, "website").Locator("b, a")).ToHaveCountAsync(0);
        await Assertions.Expect(Cell(invalid, "email")).ToHaveTextAsync(invalidEmail);
        await Assertions.Expect(Cell(invalid, "email").Locator("a")).ToHaveCountAsync(0);

        var empty = Row("Synthetic empty supplier");
        foreach (var column in new[] { "website", "email", "mobile", "fax" })
        {
            await Assertions.Expect(Cell(empty, column)).ToHaveTextAsync("-");
            await Assertions.Expect(Cell(empty, column).Locator("a")).ToHaveCountAsync(0);
        }

        Assert.Equal(target.AbsoluteUri, page.Url);
        Assert.Equal(1, Volatile.Read(ref listReads));
        Assert.Empty(unexpected);
        Assert.Single(context.Pages);
    }
}
