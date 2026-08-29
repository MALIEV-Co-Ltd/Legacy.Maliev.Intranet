using System.Text.Json;
using Maliev.ShadcnBlazor.BrowserTests.Infrastructure;
using Microsoft.Playwright;

namespace Maliev.ShadcnBlazor.BrowserTests;

[Collection(CustomerBrowserCollection.Name)]
public sealed class QuotationDecisionBrowserTests(
    IntranetClientServerFixture server,
    PlaywrightFixture playwright)
{
    [Fact]
    public async Task AuthorizedEmployeeConfirmsDecisionUsingFreshCsrfAndSafeBrowserPayload()
    {
        await using var context = await playwright.Browser.NewContextAsync(new()
        {
            ViewportSize = new() { Width = 1280, Height = 900 },
            ReducedMotion = ReducedMotion.Reduce,
        });
        var page = await context.NewPageAsync();
        string? decisionBody = null;
        string? csrf = null;

        await page.RouteAsync("**/bff/session", route => route.FulfillAsync(new()
        {
            Status = 200,
            ContentType = "application/json",
            Body = JsonSerializer.Serialize(new
            {
                isAuthenticated = true,
                employeeId = "employee-7",
                displayName = "Employee Seven",
                roles = new[] { "Employee" },
                csrfToken = "fresh-browser-csrf",
                legacyDatabaseId = 7,
                permissions = new[] { "legacy.quotations.update" },
            }),
        }));
        await page.RouteAsync("**/bff/quotations/84", route => route.FulfillAsync(new()
        {
            Status = 200,
            ContentType = "application/json",
            Body = JsonSerializer.Serialize(new
            {
                quotation = new
                {
                    id = 84,
                    customerId = 42,
                    employeeId = 7,
                    invoiceId = (int?)null,
                    period = 30,
                    expirationDate = "2030-08-01T00:00:00Z",
                    subtotal = 100m,
                    vat = 7m,
                    total = 107m,
                    withholdingTax = (decimal?)null,
                    quotedAmount = 107m,
                    currencyId = 1,
                    comment = (string?)null,
                    fob = (string?)null,
                    shippedVia = (string?)null,
                    terms = (string?)null,
                    accepted = (bool?)null,
                    createdDate = "2030-07-18T08:30:00Z",
                    modifiedDate = "2030-07-18T08:30:00",
                },
                customer = (object?)null,
                employee = (object?)null,
                currency = new { id = 1, shortName = "THB", fullName = "Thai Baht" },
                invoice = (object?)null,
                orders = Array.Empty<object>(),
                files = Array.Empty<object>(),
            }),
        }));
        await page.RouteAsync("**/bff/quotations/84/decision", async route =>
        {
            decisionBody = route.Request.PostData;
            csrf = (await route.Request.AllHeadersAsync()).GetValueOrDefault("x-csrf-token");
            await route.FulfillAsync(new()
            {
                Status = 200,
                ContentType = "application/json",
                Body = """{"status":"Completed","completedOrders":2,"totalOrders":2,"modifiedDate":"2030-07-18T08:31:00Z"}""",
            });
        });

        await page.GotoAsync(new Uri(server.BaseUri, "Quotations/View?id=84").AbsoluteUri);
        var accept = page.GetByRole(AriaRole.Button, new() { NameRegex = new("Accept quotation|ยอมรับใบเสนอราคา") });
        await accept.WaitForAsync();
        await accept.ClickAsync();
        await page.GetByRole(AriaRole.Button, new() { NameRegex = new("Confirm decision|ยืนยันผลการพิจารณา") }).ClickAsync();
        await page.GetByText(new System.Text.RegularExpressions.Regex("The quotation decision was saved|บันทึกผลการพิจารณาเรียบร้อยแล้ว")).WaitForAsync();

        Assert.Equal("fresh-browser-csrf", csrf);
        using var payload = JsonDocument.Parse(Assert.IsType<string>(decisionBody));
        Assert.Equal(
            ["accepted", "expectedModifiedDate"],
            payload.RootElement.EnumerateObject().Select(property => property.Name).Order(StringComparer.Ordinal));
        Assert.True(payload.RootElement.GetProperty("accepted").GetBoolean());
        Assert.Equal("2030-07-18T08:30:00", payload.RootElement.GetProperty("expectedModifiedDate").GetString());
    }
}
