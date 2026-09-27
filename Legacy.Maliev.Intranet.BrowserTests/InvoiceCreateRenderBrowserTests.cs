using System.Text.Json;
using Legacy.Maliev.Intranet.BrowserTests.Infrastructure;
using Microsoft.Playwright;

namespace Legacy.Maliev.Intranet.BrowserTests;

[Collection(CustomerBrowserCollection.Name)]
public sealed class InvoiceCreateRenderBrowserTests(
    IntranetClientServerFixture server,
    PlaywrightFixture playwright)
{
    [Fact]
    public async Task DirectCreateRoute_QuotationLookupRendersWithoutEditContextError()
    {
        await using var context = await playwright.Browser.NewContextAsync();
        var page = await context.NewPageAsync();
        var errors = new List<string>();
        page.PageError += (_, error) => errors.Add(error);
        await page.RouteAsync("**/bff/session", route => route.FulfillAsync(new()
        {
            Status = 200,
            ContentType = "application/json",
            Body = """{"isAuthenticated":true,"employeeId":"test-employee","displayName":"Test Employee","roles":["Employee"],"csrfToken":"test-csrf","legacyDatabaseId":1,"permissions":["legacy-accounting.invoices.create"]}""",
        }));
        await page.RouteAsync("**/bff/invoices/from-quotation/*/preview", route =>
        {
            var id = route.Request.Url.Contains("/85/", StringComparison.Ordinal) ? 85 : 84;
            return route.FulfillAsync(new()
            {
                Status = 200,
                ContentType = "application/json",
                Body = JsonSerializer.Serialize(new
                {
                    quotationId = id,
                    customerId = 3,
                    invoiceNumber = $"INV-{id}",
                    salesPerson = "Employee",
                    currency = "THB",
                    billingAddress = new { },
                    shippingAddress = new { },
                    subtotal = 1000,
                    vat = 70,
                    total = 1070,
                    availableWithholdingTax = 0,
                    outstanding = 1070,
                    orderItems = Array.Empty<object>(),
                }),
            });
        });

        await page.GotoAsync(new Uri(server.BaseUri, "Invoices/Create?quotationId=84").AbsoluteUri);
        await page.Locator("#invoice-number").WaitForAsync();

        Assert.True(await page.Locator("#invoice-quotation").IsEnabledAsync());
        Assert.Equal("INV-84", await page.Locator("#invoice-number").InputValueAsync());
        await page.Locator("#invoice-quotation").FillAsync("85");
        await page.GetByRole(AriaRole.Button, new() { Name = "Get quotation data" }).ClickAsync();
        Assert.Equal("INV-85", await page.Locator("#invoice-number").InputValueAsync());
        Assert.True(await page.Locator("#blazor-error-ui").IsHiddenAsync());
        Assert.Empty(errors);
    }
}
