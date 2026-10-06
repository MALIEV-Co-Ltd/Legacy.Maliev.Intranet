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
        await page.Locator("#supplier-city").FillAsync("Manual city");
        await page.Locator("#supplier-state").FillAsync("Manual province");
        await page.Locator("#supplier-postal-code").FillAsync("99999");
        await page.Locator("#supplier-address-lookup-enabled").ClickAsync();
        await page.Locator("#supplier-address-lookup-postcode-first").FillAsync("๑");
        await Assertions.Expect(page.Locator("#supplier-address-lookup-combination-results")).ToBeDisabledAsync();
        await page.Locator("#supplier-address-lookup-postcode-first").FillAsync("๑๐๑๑๐");
        var result = page.Locator("#supplier-address-lookup-combination-results");
        await Assertions.Expect(result).ToBeEnabledAsync();
        await Assertions.Expect(page.Locator("#supplier-city")).ToHaveValueAsync("Manual city");
        await Assertions.Expect(page.Locator("#supplier-state")).ToHaveValueAsync("Manual province");
        await Assertions.Expect(page.Locator("#supplier-postal-code")).ToHaveValueAsync("99999");
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

    [Fact]
    public async Task CompanySelectionPreservesManualTaxAndUsesLocalRetrievalTime()
    {
        await using var context = await playwright.Browser.NewContextAsync(new() { TimezoneId = "Asia/Bangkok" });
        var page = await context.NewPageAsync();
        await SessionAsync(page);
        await page.RouteAsync("**/bff/lookups/companies/search?**", route => route.FulfillAsync(new()
        {
            ContentType = "application/json", Body = """
            {"outcome":"matches","provider":"creden","capability":"suggestion","hasMore":false,"items":[{"nameTh":"บริษัท ใหม่","nameEn":"New company","taxId":null,"retrievedAt":"2026-10-06T01:00:00+00:00"}]}
            """
        }));
        await page.GotoAsync(new Uri(server.BaseUri, "/Suppliers/Create").AbsoluteUri);
        await page.Locator("#supplier-name").FillAsync("Manual company");
        await page.Locator("#supplier-tax-number").FillAsync("0123456789012");
        await page.Locator("#supplier-website").FillAsync("https://manual.test");
        await page.RunAndWaitForResponseAsync(() => page.Locator("#supplier-company-lookup").FillAsync("New company"),
            response => response.Url.Contains("/bff/lookups/companies/search", StringComparison.Ordinal));
        var result = page.Locator("#supplier-company-lookup-results");
        await Assertions.Expect(result).ToBeEnabledAsync();
        await result.FocusAsync();
        await result.PressAsync("ArrowDown");
        await result.PressAsync("Enter");
        await Assertions.Expect(page.Locator("#supplier-name")).ToHaveValueAsync("New company");
        await Assertions.Expect(page.Locator("#supplier-tax-number")).ToHaveValueAsync("0123456789012");
        await Assertions.Expect(page.Locator("#supplier-website")).ToHaveValueAsync("https://manual.test");
        await Assertions.Expect(page.Locator("section[aria-labelledby='supplier-company-lookup-heading']")).ToContainTextAsync("8:00");
        await Assertions.Expect(page.Locator("#blazor-error-ui")).ToBeHiddenAsync();
    }

    [Fact]
    public async Task PastedAddressRequiresCandidateReviewAndExplicitApply()
    {
        await using var context = await playwright.Browser.NewContextAsync();
        var page = await context.NewPageAsync();
        await SessionAsync(page);
        const string pasted = "1 Main Road Bangkok 10110";
        string? csrf = null, requestBody = null;
        var combination = JsonSerializer.Deserialize<JsonElement>(Combination);
        await page.RouteAsync("**/bff/lookups/thai-addresses/**", route =>
        {
            if (route.Request.Method != "POST")
                return route.FulfillAsync(new() { ContentType = "application/json", Body = "{\"datasetVersion\":\"v1\",\"items\":[],\"hasMore\":false,\"nextCursor\":null}" });
            route.Request.Headers.TryGetValue("x-csrf-token", out csrf);
            requestBody = route.Request.PostData;
            return route.FulfillAsync(new() { ContentType = "application/json", Body = JsonSerializer.Serialize(new
            {
                datasetVersion = "v1", originalText = pasted, normalizedText = pasted,
                outcome = "exact", candidates = new[] { combination }, hasMore = false,
                uniqueFields = new { province = combination.GetProperty("province"), district = combination.GetProperty("district"), subdistrict = combination.GetProperty("subdistrict"), postcode = "10110" },
                detailText = "1 Main Road", extractedSpans = Array.Empty<object>(), conflicts = Array.Empty<string>()
            }) });
        });
        await page.GotoAsync(new Uri(server.BaseUri, "/Suppliers/Create").AbsoluteUri);
        await page.Locator("#supplier-address-1").FillAsync("Original street");
        await page.Locator("#supplier-address-2").FillAsync("Floor 2");
        await page.Locator("#supplier-country-id").FillAsync("66");
        await page.Locator("#supplier-address-lookup-enabled").ClickAsync();
        await page.Locator("#supplier-address-lookup-paste").FillAsync(pasted);
        await page.GetByRole(AriaRole.Button, new() { Name = "Extract address fields", Exact = true }).ClickAsync();
        var apply = page.GetByRole(AriaRole.Button, new() { Name = "Apply reviewed address", Exact = true });
        await Assertions.Expect(apply).ToBeDisabledAsync();
        await Assertions.Expect(page.Locator("#supplier-address-lookup-preview-detail")).ToHaveValueAsync("1 Main Road");
        await Assertions.Expect(page.Locator("#supplier-address-1")).ToHaveValueAsync("Original street");
        var candidate = page.GetByRole(AriaRole.Combobox, new() { Name = "Matching address and postcode", Exact = true });
        await candidate.FocusAsync();
        await candidate.PressAsync("ArrowDown");
        await candidate.PressAsync("Enter");
        await Assertions.Expect(apply).ToBeEnabledAsync();
        await Assertions.Expect(page.Locator("#supplier-address-1")).ToHaveValueAsync("Original street");
        await page.Locator("#supplier-address-lookup-preview-detail").FillAsync("Reviewed street");
        await apply.ClickAsync();
        await Assertions.Expect(page.Locator("#supplier-address-1")).ToHaveValueAsync("Reviewed street");
        await Assertions.Expect(page.Locator("#supplier-state")).ToHaveValueAsync("กรุงเทพมหานคร");
        await Assertions.Expect(page.Locator("#supplier-postal-code")).ToHaveValueAsync("10110");
        await Assertions.Expect(page.Locator("#supplier-address-2")).ToHaveValueAsync("Floor 2");
        await Assertions.Expect(page.Locator("#supplier-country-id")).ToHaveValueAsync("66");
        Assert.Equal("csrf", csrf);
        Assert.Equal(pasted, JsonSerializer.Deserialize<JsonElement>(requestBody!).GetProperty("text").GetString());
        await Assertions.Expect(page.Locator("#blazor-error-ui")).ToBeHiddenAsync();
    }

    private static Task SessionAsync(IPage page) => page.RouteAsync("**/bff/session", route => route.FulfillAsync(new()
    {
        ContentType = "application/json", Body = JsonSerializer.Serialize(new
        { isAuthenticated = true, employeeId = "staff", displayName = "Staff", roles = new[] { "Employee" }, csrfToken = "csrf", permissions = new[] { "legacy-catalog.locations.read", "legacy-catalog.companies.read" } })
    }));
    private const string Province = """{"code":"10","nameTh":"กรุงเทพมหานคร","nameEn":"Bangkok","provinceCode":null,"districtCode":null}""";
    private const string District = """{"code":"1033","nameTh":"คลองเตย","nameEn":"Khlong Toei","provinceCode":"10","districtCode":null}""";
    private const string Subdistrict = """{"code":"103301","nameTh":"คลองเตย","nameEn":"Khlong Toei","provinceCode":null,"districtCode":"1033"}""";
    private const string Combination = "{\"province\":" + Province + ",\"district\":" + District + ",\"subdistrict\":" + Subdistrict + ",\"postcode\":\"10110\"}";
}
