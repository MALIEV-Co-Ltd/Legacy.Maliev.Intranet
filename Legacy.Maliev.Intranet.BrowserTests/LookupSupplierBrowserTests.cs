using System.Text.Json;
using Legacy.Maliev.Intranet.BrowserTests.Infrastructure;
using Microsoft.Playwright;

namespace Legacy.Maliev.Intranet.BrowserTests;

/// <summary>Real rendered WASM controls with controlled lookup responses; not live-provider or persisted-domain acceptance.</summary>
[Collection(CustomerBrowserCollection.Name)]
public sealed class LookupSupplierBrowserTests(IntranetClientServerFixture server, PlaywrightFixture playwright)
{
    [Fact]
    public async Task PostcodeFirstKeyboardSelectionPreservesStreetAndManualCountry()
    {
        await using var context = await playwright.Browser.NewContextAsync();
        var page = await context.NewPageAsync();
        await SessionAsync(page);
        await page.RouteAsync("**/bff/lookups/thai-addresses/**", route =>
        {
            var path = new Uri(route.Request.Url).AbsolutePath;
            var item = path.EndsWith("provinces", StringComparison.Ordinal) ? Province :
                path.EndsWith("districts", StringComparison.Ordinal) && !path.EndsWith("subdistricts", StringComparison.Ordinal) ? District :
                path.EndsWith("subdistricts", StringComparison.Ordinal) ? Subdistrict : Combination;
            return route.FulfillAsync(new() { ContentType = "application/json", Body = "{\"datasetVersion\":\"v1\",\"items\":[" + item + "],\"hasMore\":false,\"nextCursor\":null}" });
        });
        await page.GotoAsync(new Uri(server.BaseUri, "/Suppliers/Create").AbsoluteUri);
        await page.Locator("#supplier-address-1").FillAsync("1 Main Road");
        await page.Locator("#supplier-address-2").FillAsync("Floor 2");
        await page.Locator("#supplier-country-id").FillAsync("66");
        await page.Locator("#supplier-address-lookup-enabled").ClickAsync();
        await page.Locator("#supplier-address-lookup-postcode-first").FillAsync("๑๐๑๑๐");
        var result = page.Locator("#supplier-address-lookup-combination-results");
        await Assertions.Expect(result).ToBeEnabledAsync();
        await result.FocusAsync();
        await result.PressAsync("ArrowDown");
        await result.PressAsync("Enter");
        await Assertions.Expect(page.Locator("#supplier-state")).ToHaveValueAsync("กรุงเทพมหานคร");
        await Assertions.Expect(page.Locator("#supplier-city")).ToHaveValueAsync("คลองเตย, คลองเตย");
        await Assertions.Expect(page.Locator("#supplier-postal-code")).ToHaveValueAsync("10110");
        await Assertions.Expect(page.Locator("#supplier-address-1")).ToHaveValueAsync("1 Main Road");
        await Assertions.Expect(page.Locator("#supplier-address-2")).ToHaveValueAsync("Floor 2");
        await Assertions.Expect(page.Locator("#supplier-country-id")).ToHaveValueAsync("66");
        await Assertions.Expect(page.Locator("#blazor-error-ui")).ToBeHiddenAsync();
    }

    [Fact]
    public async Task UnavailableCredenSearchLeavesManualCompanyFieldsEditable()
    {
        await using var context = await playwright.Browser.NewContextAsync();
        var page = await context.NewPageAsync();
        await SessionAsync(page);
        await page.RouteAsync("**/bff/lookups/companies/search?**", route => route.FulfillAsync(new()
            { Status = 503, ContentType = "application/problem+json", Body = "{\"status\":503}" }));
        await page.GotoAsync(new Uri(server.BaseUri, "/Suppliers/Create").AbsoluteUri);
        await page.Locator("#supplier-company-lookup").FillAsync("test");
        await Assertions.Expect(page.GetByRole(AriaRole.Status).Filter(new() { HasText = "Suggestions are unavailable" })).ToBeVisibleAsync();
        await page.Locator("#supplier-name").FillAsync("Manual company");
        await page.Locator("#supplier-tax-number").FillAsync("0123456789012");
        await Assertions.Expect(page.Locator("#supplier-name")).ToHaveValueAsync("Manual company");
        await Assertions.Expect(page.Locator("#supplier-tax-number")).ToHaveValueAsync("0123456789012");
    }

    private static Task SessionAsync(IPage page) => page.RouteAsync("**/bff/session", route => route.FulfillAsync(new()
    {
        ContentType = "application/json", Body = JsonSerializer.Serialize(new
        { isAuthenticated = true, employeeId = "staff", displayName = "Staff", roles = new[] { "Employee" }, csrfToken = "csrf", permissions = new[] { "legacy-catalog.locations.read", "legacy-catalog.companies.read" } })
    }));
    private const string Province = """{"code":"10","nameTh":"กรุงเทพมหานคร","nameEn":"Bangkok","parentCode":null}""";
    private const string District = """{"code":"1033","nameTh":"คลองเตย","nameEn":"Khlong Toei","parentCode":"10"}""";
    private const string Subdistrict = """{"code":"103301","nameTh":"คลองเตย","nameEn":"Khlong Toei","parentCode":"1033"}""";
    private const string Combination = "{\"province\":" + Province + ",\"district\":" + District + ",\"subdistrict\":" + Subdistrict + ",\"postcode\":\"10110\"}";
}
