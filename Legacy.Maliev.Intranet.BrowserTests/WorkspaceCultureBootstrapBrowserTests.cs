using System.Text.Json;
using Legacy.Maliev.Intranet.BrowserTests.Infrastructure;
using Microsoft.Playwright;

namespace Legacy.Maliev.Intranet.BrowserTests;

[Collection(CustomerBrowserCollection.Name)]
public sealed class WorkspaceCultureBootstrapBrowserTests(
    IntranetClientServerFixture server,
    PlaywrightFixture playwright)
{
    [Theory]
    [InlineData("%")]
    [InlineData("%E0%A4%A")]
    public async Task MalformedCultureCookieDefaultsWithoutPreventingWorkspaceBootstrap(string cookieValue)
    {
        await using var context = await playwright.Browser.NewContextAsync();
        await AddCultureCookieAsync(context, cookieValue);
        var page = await context.NewPageAsync();
        var errors = new List<string>();
        page.PageError += (_, error) => errors.Add(error);
        await StubProductionBoundariesAsync(page);

        await page.GotoAsync(new Uri(server.BaseUri, "sales/orders").AbsoluteUri);

        await page.GetByRole(AriaRole.Button, new() { Name = "Employee menu", Exact = true }).WaitForAsync();
        Assert.Equal("en-TH", await page.EvaluateAsync<string>("() => window.malievCulture.get()"));
        Assert.Equal("en", await page.Locator("html").GetAttributeAsync("lang"));
        Assert.Empty(errors);
    }

    [Theory]
    [InlineData(false, "TH-th")]
    [InlineData(false, "  Th-tH  ")]
    [InlineData(true, "TH-th")]
    [InlineData(true, "  Th-tH  ")]
    [InlineData(false, "\u0085TH-th\u0085")]
    [InlineData(true, "\u0085TH-th\u0085")]
    public async Task RecognizedThaiPreferenceMatchesPrebootLabelsAndRenderedWorkspace(
        bool fromCookie, string preference)
    {
        await using var context = await playwright.Browser.NewContextAsync();
        if (fromCookie)
            await AddCultureCookieAsync(context, Uri.EscapeDataString(preference));
        else
            await context.AddInitScriptAsync($"localStorage.setItem('maliev_culture', {JsonSerializer.Serialize(preference)});");
        var page = await context.NewPageAsync();
        await StubProductionBoundariesAsync(page);

        // Observe the real bootstrap script and static labels before WASM replaces the loading DOM.
        // This boundary double suppresses only WASM startup, not preference resolution or rendering code.
        const string bootstrapPattern = "**/_framework/blazor.webassembly.js";
        await page.RouteAsync(bootstrapPattern, route => route.FulfillAsync(new()
        {
            Status = 200,
            ContentType = "application/javascript",
            Body = "",
        }));
        await page.GotoAsync(new Uri(server.BaseUri, "sales/orders").AbsoluteUri);

        Assert.Equal("th-TH", await page.EvaluateAsync<string>("() => window.malievCulture.get()"));
        Assert.Equal("th", await page.Locator("html").GetAttributeAsync("lang"));
        Assert.Equal("กำลังโหลดระบบอินทราเน็ต MALIEV", await page.Locator("#workspace-loading").GetAttributeAsync("aria-label"));
        Assert.Equal("เกิดข้อผิดพลาดที่ไม่คาดคิด", await page.Locator("#workspace-fatal-message").TextContentAsync());
        Assert.Equal("โหลดใหม่", await page.Locator("#workspace-reload").TextContentAsync());
        Assert.Equal("ปิดข้อความข้อผิดพลาด", await page.Locator("#workspace-dismiss").GetAttributeAsync("aria-label"));

        await page.UnrouteAsync(bootstrapPattern);
        await page.ReloadAsync();
        await page.GetByRole(AriaRole.Button, new() { Name = "เมนูพนักงาน", Exact = true }).WaitForAsync();
        Assert.Equal("th", await page.Locator("html").GetAttributeAsync("lang"));
    }

    [Fact]
    public async Task DeniedCultureStorageFallsBackToRecognizedCookieAndBootstrapsThaiWorkspace()
    {
        await using var context = await playwright.Browser.NewContextAsync();
        await AddCultureCookieAsync(context, "%20Th-tH%20");
        await context.AddInitScriptAsync("""
            const originalGetItem = Storage.prototype.getItem;
            Storage.prototype.getItem = function (key) {
                if (key === 'maliev_culture') throw new DOMException('Storage denied', 'SecurityError');
                return originalGetItem.call(this, key);
            };
            """);
        var page = await context.NewPageAsync();
        await StubProductionBoundariesAsync(page);

        await page.GotoAsync(new Uri(server.BaseUri, "sales/orders").AbsoluteUri);

        await page.GetByRole(AriaRole.Button, new() { Name = "เมนูพนักงาน", Exact = true }).WaitForAsync();
        Assert.Equal("th-TH", await page.EvaluateAsync<string>("() => window.malievCulture.get()"));
        Assert.Equal("th", await page.Locator("html").GetAttributeAsync("lang"));
    }

    [Theory]
    [InlineData("en-TH", "th-TH", "en-TH", "en", "Employee menu")]
    [InlineData("unknown", "th-TH", "en-TH", "en", "Employee menu")]
    [InlineData("  EN-us  ", "th-TH", "en-US", "en", "Employee menu")]
    [InlineData("  tH-Th  ", "en-TH", "th-TH", "th", "เมนูพนักงาน")]
    [InlineData("", "th-TH", "th-TH", "th", "เมนูพนักงาน")]
    [InlineData(null, "unknown", "en-TH", "en", "Employee menu")]
    [InlineData(null, null, "en-TH", "en", "Employee menu")]
    [InlineData("   ", "th-TH", "en-TH", "en", "Employee menu")]
    [InlineData("th-unknown", "th-TH", "en-TH", "en", "Employee menu")]
    [InlineData(null, "th-unknown", "en-TH", "en", "Employee menu")]
    [InlineData("\uFEFFth-TH\uFEFF", "th-TH", "en-TH", "en", "Employee menu")]
    [InlineData(null, "%EF%BB%BFth-TH%EF%BB%BF", "en-TH", "en", "Employee menu")]
    public async Task PreferenceResolutionPreservesStoragePrecedenceSupportedCulturesAndDefault(
        string? stored, string? cookie, string expectedCulture, string expectedLanguage, string menuName)
    {
        await using var context = await playwright.Browser.NewContextAsync();
        if (stored is not null)
            await context.AddInitScriptAsync($"localStorage.setItem('maliev_culture', {JsonSerializer.Serialize(stored)});");
        if (cookie is not null)
            await AddCultureCookieAsync(context, cookie);
        var page = await context.NewPageAsync();
        await StubProductionBoundariesAsync(page);

        await page.GotoAsync(new Uri(server.BaseUri, "sales/orders").AbsoluteUri);

        await page.GetByRole(AriaRole.Button, new() { Name = menuName, Exact = true }).WaitForAsync();
        Assert.Equal(expectedCulture, await page.EvaluateAsync<string>("() => window.malievCulture.get()"));
        Assert.Equal(expectedLanguage, await page.Locator("html").GetAttributeAsync("lang"));
        // Bootstrap normalizes its return value but must not overwrite saved preferences.
        Assert.Equal(stored, await page.EvaluateAsync<string?>("() => localStorage.getItem('maliev_culture')"));
        var cookies = await context.CookiesAsync();
        Assert.Equal(cookie, cookies.SingleOrDefault(value => value.Name == "maliev_culture")?.Value);
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData(null, "%")]
    [InlineData("   ", "th-TH")]
    [InlineData("th-unknown", "th-TH")]
    [InlineData(null, "th-unknown")]
    [InlineData("\uFEFFth-TH\uFEFF", "th-TH")]
    [InlineData(null, "%EF%BB%BFth-TH%EF%BB%BF")]
    public async Task DefaultAndUnsupportedPreferencesKeepEnglishPrebootAndRuntime(string? stored, string? cookie)
    {
        await using var context = await playwright.Browser.NewContextAsync();
        if (stored is not null)
            await context.AddInitScriptAsync($"localStorage.setItem('maliev_culture', {JsonSerializer.Serialize(stored)});");
        if (cookie is not null)
            await AddCultureCookieAsync(context, cookie);
        var page = await context.NewPageAsync();
        await StubProductionBoundariesAsync(page);
        const string bootstrapPattern = "**/_framework/blazor.webassembly.js";
        await page.RouteAsync(bootstrapPattern, route => route.FulfillAsync(new()
        {
            Status = 200,
            ContentType = "application/javascript",
            Body = "",
        }));
        await page.GotoAsync(new Uri(server.BaseUri, "sales/orders").AbsoluteUri);

        Assert.Equal("en-TH", await page.EvaluateAsync<string>("() => window.malievCulture.get()"));
        Assert.Equal("en", await page.Locator("html").GetAttributeAsync("lang"));
        Assert.Equal("Loading workspace", await page.Locator(".legacy-loading-status").TextContentAsync());
        Assert.Null(await page.Locator("#workspace-loading").GetAttributeAsync("aria-label"));
        Assert.Equal("An unexpected error occurred.", await page.Locator("#workspace-fatal-message").TextContentAsync());
        Assert.Equal("Reload", await page.Locator("#workspace-reload").TextContentAsync());
        Assert.Equal("Dismiss error", await page.Locator("#workspace-dismiss").GetAttributeAsync("aria-label"));

        await page.UnrouteAsync(bootstrapPattern);
        await page.ReloadAsync();
        await page.GetByRole(AriaRole.Button, new() { Name = "Employee menu", Exact = true }).WaitForAsync();
        Assert.Equal("en", await page.Locator("html").GetAttributeAsync("lang"));
        Assert.Equal(stored, await page.EvaluateAsync<string?>("() => localStorage.getItem('maliev_culture')"));
        Assert.Equal(cookie, (await context.CookiesAsync()).SingleOrDefault(value => value.Name == "maliev_culture")?.Value);
    }

    private Task AddCultureCookieAsync(IBrowserContext context, string value) => context.AddCookiesAsync(
        [new Cookie { Name = "maliev_culture", Value = value, Url = server.BaseUri.AbsoluteUri }]);

    private static async Task StubProductionBoundariesAsync(IPage page)
    {
        // Same downstream boundary shape as OperationalShellBrowserTests; culture JS, WASM,
        // resources, navigation and shell components remain the real production implementations.
        var session = JsonSerializer.Serialize(new
        {
            isAuthenticated = true,
            employeeId = "browser-shell-employee",
            email = "browser.shell@maliev.com",
            displayName = "Browser Shell Employee",
            roles = new[] { "Employee" },
            csrfToken = "browser-shell-csrf",
            legacyDatabaseId = 1,
            permissions = new[]
            {
                "legacy.orders.read", "legacy.orders.create",
                "legacy.quotations.read", "legacy.quotations.create",
                "legacy-customer.customers.list", "legacy-customer.customers.create",
                "legacy.accounting.read", "legacy.accounting.create",
                "legacy-catalog.materials.read", "legacy-catalog.materials.create",
                "legacy-procurement.purchase-orders.read", "legacy-procurement.purchase-orders.create",
                "legacy-procurement.suppliers.read", "legacy-procurement.suppliers.create",
            },
        });
        await page.RouteAsync("**/bff/session", route => route.FulfillAsync(new()
        {
            Status = 200,
            ContentType = "application/json",
            Body = session,
        }));
        await page.RouteAsync("**/bff/orders?*", route => route.FulfillAsync(new()
        {
            Status = 200,
            ContentType = "application/json",
            Body = "{\"items\":[],\"pageIndex\":1,\"totalPages\":1,\"totalRecords\":0,\"hasNextPage\":false,\"hasPreviousPage\":false}",
        }));
        await page.RouteAsync("**/bff/orders/pending?*", route => route.FulfillAsync(new()
        {
            Status = 200,
            ContentType = "application/json",
            Body = "[]",
        }));
    }
}
