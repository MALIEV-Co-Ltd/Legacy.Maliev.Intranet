using System.Text.Json;
using Legacy.Maliev.Intranet.BrowserTests.Infrastructure;
using Microsoft.Playwright;

namespace Legacy.Maliev.Intranet.BrowserTests;

// Controlled browser boundary only; this is not producer acceptance.
[Collection(CustomerBrowserCollection.Name)]
public sealed class ReplacementBrowserTests(IntranetClientServerFixture server, PlaywrightFixture playwright)
{
    [Theory]
    [InlineData("en-TH", 375)]
    [InlineData("th-TH", 375)]
    [InlineData("en-TH", 1440)]
    [InlineData("th-TH", 1440)]
    public async Task Original_and_replacement_history_are_separate_and_responsive(string culture, int width)
    {
        await using var context = await Context(culture, width); var page = await context.NewPageAsync(); var errors = new List<string>(); page.PageError += (_, error) => errors.Add(error);
        await Stub(page, [], false); await page.GotoAsync(new Uri(server.BaseUri, "Orders/View?id=94826").AbsoluteUri);
        await page.Locator(".replacement-case").WaitForAsync(); var text = await page.Locator(".replacement-case").InnerTextAsync();
        Assert.Contains("LALAMOVE", text); Assert.Contains("NEW-TRACK", text); Assert.DoesNotContain("94876", text);
        Assert.True(await page.EvaluateAsync<bool>("document.documentElement.scrollWidth<=window.innerWidth+1"));
        Assert.True(await page.Locator(".replacement-panel").EvaluateAsync<bool>("panel=>panel.scrollWidth<=panel.clientWidth+1"));
        Assert.Contains(culture == "th-TH" ? "งานผลิตทดแทนและแก้ไข" : "Replacement and rework", await page.Locator(".replacement-panel").InnerTextAsync());
        var audit = page.Locator(".replacement-audit"); var trigger = audit.GetByRole(AriaRole.Button);
        await trigger.PressAsync("Enter"); await audit.Locator("li").WaitForAsync(new() { State = WaitForSelectorState.Visible });
        Assert.Contains("Defect", await audit.InnerTextAsync());
        await trigger.PressAsync("Enter"); await audit.Locator("li").WaitForAsync(new() { State = WaitForSelectorState.Hidden });
        Assert.Empty(errors); Directory.CreateDirectory("output/playwright"); await page.Locator(".replacement-panel").ScreenshotAsync(new() { Path = $"output/playwright/replacement-{culture}-{width}.png" });
    }
    [Theory]
    [InlineData("en-TH")]
    [InlineData("th-TH")]
    public async Task Lost_ack_uses_same_key_and_blocks_original_write(string culture)
    {
        await using var context = await Context(culture, 390); var page = await context.NewPageAsync(); var writes = new List<(string? Key, string? Body)>(); await Stub(page, writes, true);
        await page.GotoAsync(new Uri(server.BaseUri, "Orders/View?id=94826").AbsoluteUri);
        await page.Locator("#replacement-document").WaitForAsync(); await page.Locator("#replacement-document").ClickAsync(); await page.GetByRole(AriaRole.Option, new() { Name = "Damage evidence", Exact = true }).ClickAsync();
        await page.Locator("#replacement-version").ClickAsync(); await page.GetByRole(AriaRole.Option).Filter(new() { HasText = culture == "th-TH" ? "รุ่นที่ 1" : "Version 1" }).ClickAsync();
        await page.Locator("#replacement-submit").ClickAsync(); await page.Locator("#replacement-retry").WaitForAsync(); Assert.True(await page.Locator(".order-save-bar button").IsDisabledAsync());
        await page.Locator("form.order-edit-form").EvaluateAsync("form=>form.requestSubmit()"); Assert.Single(writes);
        await page.Locator("#replacement-reconcile").ClickAsync(); Assert.Single(writes);
        await page.Locator("#replacement-retry").ClickAsync(); await page.Locator("#replacement-retry").WaitForAsync(new() { State = WaitForSelectorState.Detached });
        Assert.Equal(2, writes.Count); Assert.Equal(writes[0], writes[1]); Assert.False(await page.Locator(".order-save-bar button").IsDisabledAsync());
    }
    private async Task<IBrowserContext> Context(string culture, int width)
    {
        var context = await playwright.Browser.NewContextAsync(new() { ViewportSize = new() { Width = width, Height = 900 } });
        await context.AddInitScriptAsync($"localStorage.setItem('maliev_culture','{culture}');document.cookie='maliev_culture={culture}; path=/';"); return context;
    }
    private static async Task Stub(IPage page, List<(string? Key, string? Body)> writes, bool lostAck)
    {
        await page.RouteAsync("**/bff/**", async route =>
        {
            var path = new Uri(route.Request.Url).AbsolutePath; string body;
            if (path == "/bff/session") body = Session;
            else if (path == "/bff/orders/94826" && route.Request.Method == "GET") body = Order;
            else if (path.EndsWith("/evidence", StringComparison.Ordinal)) body = Documents;
            else if (path.EndsWith("/versions", StringComparison.Ordinal)) body = Versions;
            else if (path.Contains("/operations/", StringComparison.Ordinal)) { await route.FulfillAsync(new() { Status = 404 }); return; }
            else if (route.Request.Method != "GET")
            {
                writes.Add((route.Request.Headers.GetValueOrDefault("idempotency-key"), route.Request.PostData));
                if (lostAck && writes.Count == 1) { await route.FulfillAsync(new() { Status = 503 }); return; }
                await route.FulfillAsync(new() { Status = 201, ContentType = "application/json", Body = Case }); return;
            }
            else if (path == "/bff/orders/94826/replacements") body = lostAck && writes.Count < 2 ? "[]" : "[" + Case + "]";
            else { await route.FulfillAsync(new() { Status = 404 }); return; }
            await route.FulfillAsync(new() { Status = 200, ContentType = "application/json", Body = body });
        });
    }
    private const string Session = """
    {"isAuthenticated":true,"employeeId":"staff","displayName":"Staff","roles":["Employee"],"csrfToken":"csrf","legacyDatabaseId":7,"permissions":["legacy.orders.read","legacy.orders.update","legacy.replacements.read","legacy.replacements.write","legacy.replacements.approve","legacy-file.documents.read"]}
    """;
    private const string Order = """
    {"order":{"id":94826,"customerId":69797,"name":"Mold","processId":1,"quantity":1,"manufactured":1,"remaining":0,"trackingNumber":"LALAMOVE","finishedDate":"2026-09-30"},"processes":[{"id":1,"name":"Mold"}],"materials":[],"colors":[],"surfaceFinishes":[],"currencies":[],"employees":[],"currentStatus":null,"availableStatuses":[],"history":[],"files":[]}
    """;
    private const string Case = """
    {"Id":3,"Value":{"CustomerId":69797,"Reason":"ManufacturingNonconformance","State":"Approved","Revision":4,"ReportEvidence":{"DocumentId":"3d6dfc5e-d22c-4b63-b840-bf8925cc5c51","VersionId":"b9b7ba14-aac7-4964-a50a-c37ea8da812d"},"Originals":[{"OrderId":94826,"CustomerId":69797,"Quantity":1,"Manufactured":1,"AffectedQuantity":1,"TrackingNumber":"LALAMOVE","FinishedDate":"2026-09-30"}],"Attempts":[{"Id":1,"OrderId":94826,"Quantity":1,"StartedAt":"2026-10-08T00:00:00Z","ProducedQuantity":1,"AcceptedQuantity":1,"RejectedQuantity":0,"FinishedDate":"2026-10-08"}],"Shipments":[{"Id":1,"AttemptId":1,"OrderId":94826,"Quantity":1,"Carrier":"Carrier","TrackingNumber":"NEW-TRACK","DestinationSnapshot":"Approved address","ShippedDate":"2026-10-08","Outcome":"InTransit","RetryAuthorized":false}],"Returns":[],"Waivers":[],"RecoveryFacts":[],"Audit":[{"Revision":1,"Action":"Reported","EmployeeId":7,"OccurredAt":"2026-10-08T00:00:00Z","Reason":"Defect"}],"ReturnDecision":"Waived","BlockProductionUntilReturn":false,"BlockShipmentUntilReturn":false}}
    """;
    private const string Documents = """
    [{"documentId":"3d6dfc5e-d22c-4b63-b840-bf8925cc5c51","customerId":69797,"kind":"Evidence","title":"Damage evidence","visibility":"Internal","revision":1}]
    """;
    private const string Versions = """
    [{"documentId":"3d6dfc5e-d22c-4b63-b840-bf8925cc5c51","versionId":"b9b7ba14-aac7-4964-a50a-c37ea8da812d","versionNumber":1,"kind":"Evidence","contentSha256":"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa","createdAtUtc":"2026-10-08T00:00:00Z","verificationStatus":"Verified","verifiedBySubject":"staff","verifiedAtUtc":"2026-10-08T00:00:00Z","revision":1}]
    """;
}
