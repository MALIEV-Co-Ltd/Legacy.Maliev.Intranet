using System.Text.Json;
using Legacy.Maliev.Intranet.BrowserTests.Infrastructure;
using Microsoft.Playwright;

namespace Legacy.Maliev.Intranet.BrowserTests;

[Collection(CustomerBrowserCollection.Name)]
public sealed class InvoiceCreateRetryBrowserTests(
    IntranetClientServerFixture server,
    PlaywrightFixture playwright)
{
    [Fact]
    public async Task UncertainCreate_LocksOriginalIntentAndRetriesSameOperation()
    {
        await using var context = await playwright.Browser.NewContextAsync();
        var page = await context.NewPageAsync();
        var errors = new List<string>();
        page.PageError += (_, error) => errors.Add(error);
        var writes = new List<(string? Key, string? Body)>();
        await StubAsync(page, writes);

        await page.GotoAsync(new Uri(server.BaseUri, "Invoices/Create?quotationId=84").AbsoluteUri);
        var number = page.Locator("#invoice-number");
        await number.WaitForAsync();
        Assert.True(await page.Locator("#blazor-error-ui").IsHiddenAsync(), string.Join("\n", errors));
        await page.GetByRole(AriaRole.Button, new() { Name = "Create invoice" }).ClickAsync();
        await page.GetByText("The invoice outcome is uncertain.", new() { Exact = false }).WaitForAsync();
        Assert.True(await number.IsDisabledAsync());
        Assert.True(await page.Locator("#invoice-quotation").IsDisabledAsync());
        Assert.True(await page.Locator("#billing-line1").IsDisabledAsync());
        Assert.True(await page.Locator("#shipping-telephone").IsDisabledAsync());
        Assert.True(await page.Locator("#invoice-comment").IsDisabledAsync());
        Assert.True(await page.Locator("#invoice-email").IsDisabledAsync());
        Assert.Equal(0, await page.Locator("form.invoice-editor__form input:not(:disabled), form.invoice-editor__form textarea:not(:disabled)").CountAsync());
        Assert.True(await page.GetByRole(AriaRole.Button, new() { Name = "Get quotation data" }).IsDisabledAsync());
        Assert.Equal(0, await page.GetByRole(AriaRole.Link, new() { Name = "Back to invoices" }).CountAsync());
        await page.GetByRole(AriaRole.Button, new() { Name = "Retry original invoice" }).ClickAsync();

        Assert.Equal(2, writes.Count);
        Assert.Equal(writes[0].Key, writes[1].Key);
        Assert.Equal(writes[0].Body, writes[1].Body);
        using var second = JsonDocument.Parse(writes[1].Body!);
        Assert.Equal("INV-84", second.RootElement.GetProperty("invoiceNumber").GetString());
        Assert.Empty(errors);
    }

    [Fact]
    public async Task UncertainCreate_SubsequentConflictKeepsOriginalOperationFrozen()
    {
        await using var context = await playwright.Browser.NewContextAsync();
        var page = await context.NewPageAsync();
        var writes = new List<(string? Key, string? Body)>();
        await StubAsync(page, writes, (503, "{}"), (409, "{}"), (503, "{}"));

        await page.GotoAsync(new Uri(server.BaseUri, "Invoices/Create?quotationId=84").AbsoluteUri);
        await page.Locator("#invoice-number").WaitForAsync();
        await page.GetByRole(AriaRole.Button, new() { Name = "Create invoice" }).ClickAsync();
        await page.GetByRole(AriaRole.Button, new() { Name = "Retry original invoice" }).ClickAsync();
        Assert.True(await page.Locator("#invoice-number").IsDisabledAsync());
        Assert.True(await page.GetByText("contact support before starting another invoice", new() { Exact = false }).IsVisibleAsync());
        Assert.True(await page.GetByRole(AriaRole.Button, new() { Name = "Retry original invoice" }).IsEnabledAsync());
        await page.GetByRole(AriaRole.Button, new() { Name = "Retry original invoice" }).ClickAsync();

        Assert.Equal(3, writes.Count);
        Assert.All(writes, write =>
        {
            Assert.Equal(writes[0].Key, write.Key);
            Assert.Equal(writes[0].Body, write.Body);
        });
    }

    [Fact]
    public async Task UncertainCreate_MalformedSuccessResponseKeepsOriginalOperationFrozen()
    {
        await using var context = await playwright.Browser.NewContextAsync();
        var page = await context.NewPageAsync();
        var writes = new List<(string? Key, string? Body)>();
        var errors = new List<string>();
        page.PageError += (_, error) => errors.Add(error);
        await StubAsync(page, writes, (200, "not-json"), (503, "{}"));

        await page.GotoAsync(new Uri(server.BaseUri, "Invoices/Create?quotationId=84").AbsoluteUri);
        await page.Locator("#invoice-number").WaitForAsync();
        await page.GetByRole(AriaRole.Button, new() { Name = "Create invoice" }).ClickAsync();
        await page.GetByRole(AriaRole.Button, new() { Name = "Retry original invoice" }).ClickAsync();

        Assert.Equal(2, writes.Count);
        Assert.Equal(writes[0].Key, writes[1].Key);
        Assert.Equal(writes[0].Body, writes[1].Body);
        Assert.True(await page.Locator("#invoice-number").IsDisabledAsync());
        Assert.Empty(errors);
    }

    private static async Task StubAsync(
        IPage page,
        List<(string? Key, string? Body)> writes,
        params (int Status, string Body)[] responses)
    {
        await page.RouteAsync("**/bff/session", route => route.FulfillAsync(new()
        {
            Status = 200,
            ContentType = "application/json",
            Body = """{"isAuthenticated":true,"employeeId":"test-employee","displayName":"Test Employee","roles":["Employee"],"csrfToken":"test-csrf","legacyDatabaseId":1,"permissions":["legacy-accounting.invoices.create"]}""",
        }));
        await page.RouteAsync("**/bff/invoices/from-quotation/84/preview", route => route.FulfillAsync(new()
        {
            Status = 200,
            ContentType = "application/json",
            Body = """{"quotationId":84,"customerId":3,"invoiceNumber":"INV-84","salesPerson":"Employee","currency":"THB","comment":null,"shippedVia":null,"fob":null,"terms":null,"billingAddress":{},"shippingAddress":{},"taxIdentification":null,"commercialRegistration":null,"subtotal":1000,"vat":70,"total":1070,"availableWithholdingTax":0,"outstanding":1070,"orderItems":[]}""",
        }));
        await page.RouteAsync("**/bff/invoices/from-quotation/84", async route =>
        {
            writes.Add((route.Request.Headers.GetValueOrDefault("idempotency-key"), route.Request.PostData));
            var response = responses.Length == 0
                ? (Status: 503, Body: "{}")
                : responses[Math.Min(writes.Count - 1, responses.Length - 1)];
            await route.FulfillAsync(new() { Status = response.Status, ContentType = "application/json", Body = response.Body });
        });
    }
}
