using System.Collections.Concurrent;
using System.Text.Json;
using Legacy.Maliev.Intranet.BrowserTests.Infrastructure;
using Legacy.Maliev.Intranet.Contracts;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Playwright;

namespace Legacy.Maliev.Intranet.BrowserTests;

public sealed class MaterialListSourceFieldsBrowserTests
{
    [Fact]
    public async Task RoutedMaterialsShowLocalizedSourceFieldsAndRequestBothNumericSortDirections()
    {
        var server = new IntranetClientServerFixture();
        var playwright = new PlaywrightFixture();
        try
        {
            await server.InitializeAsync();
            await playwright.InitializeAsync();
            await CheckMaterialsAsync(server.BaseUri, playwright.Browser);
        }
        finally
        {
            try { await playwright.DisposeAsync(); }
            finally { await server.DisposeAsync(); }
        }
    }

    private static async Task CheckMaterialsAsync(Uri origin, IBrowser browser)
    {
        const string machinability = "ความสามารถในการตัดเฉือน (%)";
        const string thermal = "การนำความร้อน (W/m-K)";
        const string listPath = "/bff/catalog/materials";
        var low = new CatalogMaterialListItem(701, 1, true, false, "Synthetic A", "TEST-A", null, null,
            Aisi: "TEST-AISI", Din: "TEST-DIN", Bts: "TEST-BTS", Jis: "TEST-JIS",
            Uns: "TEST-UNS", En: "TEST-EN", Afnor: "TEST-AFNOR", Uni: "TEST-UNI",
            Sis: "TEST-SIS", Sae: "TEST-SAE", Astm: "TEST-ASTM", Ams: "TEST-AMS",
            MachinabilityPercent: 2.5m, ThermalConductivityWattPerMeterKelvin: 100.5m);
        var middle = new CatalogMaterialListItem(702, 1, true, false, "Synthetic B", "TEST-B", null, null,
            MachinabilityPercent: 11.5m, ThermalConductivityWattPerMeterKelvin: 2.5m);
        var high = new CatalogMaterialListItem(703, 1, true, false, "Synthetic C", "TEST-C", null, null,
            MachinabilityPercent: 100.5m, ThermalConductivityWattPerMeterKelvin: 11.5m);
        CatalogMaterialListItem[] materials = [low, middle, high];
        var json = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        var reads = new ConcurrentQueue<string>();
        var forbidden = new ConcurrentQueue<string>();

        await using var context = await browser.NewContextAsync(new()
        {
            Locale = "th-TH",
            ServiceWorkers = ServiceWorkerPolicy.Block,
            ViewportSize = new() { Width = 1800, Height = 950 },
        });
        await context.AddInitScriptAsync("localStorage.setItem('maliev_culture', 'th-TH');");
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
                // The static shell references this script. The fake employee session needs no identity service.
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
                            employeeId = "synthetic-material-reader",
                            displayName = "Synthetic Material Reader",
                            roles = new[] { "Employee" },
                            permissions = new[] { "legacy-catalog.materials.read" },
                        },
                    });
                    return;
                }

                if (path == listPath)
                {
                    var sort = QueryHelpers.ParseQuery(uri.Query)["sort"].ToString();
                    var ordered = sort switch
                    {
                        "MaterialId_Descending" => materials.OrderByDescending(row => row.Id),
                        "MaterialMachinability_Ascending" => materials.OrderBy(row => row.MachinabilityPercent),
                        "MaterialMachinability_Descending" => materials.OrderByDescending(row => row.MachinabilityPercent),
                        "MaterialThermalConductivity_Ascending" => materials.OrderBy(row => row.ThermalConductivityWattPerMeterKelvin),
                        "MaterialThermalConductivity_Descending" => materials.OrderByDescending(row => row.ThermalConductivityWattPerMeterKelvin),
                        _ => null,
                    };
                    if (ordered is not null)
                    {
                        reads.Enqueue(sort);
                        var result = new CatalogMaterialPage(ordered.ToArray(), 1, 1, materials.Length, false, false);
                        await route.FulfillAsync(new() { Status = 200, ContentType = "application/json", Body = JsonSerializer.Serialize(result, json) });
                        return;
                    }
                }

                if (path == "/mfg/materials" && request.ResourceType == "document"
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
            var response = await page.GotoAsync(new Uri(origin, "/mfg/materials").AbsoluteUri);
            Assert.Equal(200, response?.Status);
            var table = page.Locator(".materials-page [data-slot='data-table']");
            var names = table.Locator("tbody td[data-column='name']");
            var sample = table.GetByRole(AriaRole.Row).Filter(new() { HasText = low.Name });
            ILocator Cell(string column) => sample.Locator($"td[data-column='{column}']");
            await Assertions.Expect(page.GetByRole(AriaRole.Heading, new() { Name = "วัสดุ", Exact = true })).ToBeVisibleAsync();
            await Assertions.Expect(names).ToHaveTextAsync([high.Name, middle.Name, low.Name]);
            foreach (var column in new[] { "aisi", "din", "bts", "jis", "uns", "en", "afnor", "uni", "sis", "sae", "astm", "ams" })
                await Assertions.Expect(Cell(column)).ToHaveTextAsync($"TEST-{column.ToUpperInvariant()}");
            await Assertions.Expect(Cell("machinability")).ToHaveTextAsync("2.5");
            await Assertions.Expect(Cell("thermal")).ToHaveTextAsync("100.5");

            var machinabilitySort = table.Locator(".shadcn-data-table-sort").Filter(new() { HasText = machinability });
            var thermalSort = table.Locator(".shadcn-data-table-sort").Filter(new() { HasText = thermal });
            await machinabilitySort.ClickAsync();
            await Assertions.Expect(names).ToHaveTextAsync([low.Name, middle.Name, high.Name]);
            await machinabilitySort.ClickAsync();
            await Assertions.Expect(names).ToHaveTextAsync([high.Name, middle.Name, low.Name]);
            await thermalSort.ClickAsync();
            await Assertions.Expect(names).ToHaveTextAsync([middle.Name, high.Name, low.Name]);
            await thermalSort.ClickAsync();
            await Assertions.Expect(names).ToHaveTextAsync([low.Name, high.Name, middle.Name]);

            Assert.Equal(new[]
            {
                "MaterialId_Descending", "MaterialMachinability_Ascending", "MaterialMachinability_Descending",
                "MaterialThermalConductivity_Ascending", "MaterialThermalConductivity_Descending",
            }, reads.ToArray());
            Assert.Single(context.Pages);
        }
        finally
        {
            await page.CloseAsync();
            Assert.Empty(forbidden);
        }
    }
}
