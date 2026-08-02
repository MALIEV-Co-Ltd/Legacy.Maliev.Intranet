extern alias Bff;

using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

namespace Legacy.Maliev.Intranet.Tests;

public sealed class InvoiceAccountingBehaviorTests
{
    [Fact]
    public async Task CreationPreview_ReturnsAuthoritativeAccountingProjection()
    {
        var accounting = AccountingBehaviorTestHost.Routes(request =>
            request.RequestUri?.AbsolutePath == "/invoices/from-quotation/84/preview"
                ? AccountingBehaviorTestHost.Json(PreviewJson)
                : new(HttpStatusCode.NotFound));
        await using var factory = AccountingBehaviorTestHost.CreateFactory(accounting);
        using var client = AccountingBehaviorTestHost.CreateClient(factory);
        await AccountingBehaviorTestHost.SignInAsync(client);

        using var response = await client.GetAsync("/bff/invoices/from-quotation/84/preview");
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(84, json.GetProperty("quotationId").GetInt32());
        Assert.Equal("THB", json.GetProperty("currency").GetString());
        Assert.Equal(1070.27m, json.GetProperty("total").GetDecimal());
        Assert.Equal("GET", Assert.Single(accounting.Requests).Method);
        Assert.Equal("/invoices/from-quotation/84/preview", Assert.Single(accounting.Requests).Path);
    }

    [Fact]
    public async Task Creation_ForwardsEditableIntentAndStableUuidOnly()
    {
        var accounting = AccountingBehaviorTestHost.Routes(request =>
            request.RequestUri?.AbsolutePath == "/invoices/from-quotation/84"
                ? AccountingBehaviorTestHost.Json(
                    """{"invoiceId":55,"state":1,"emailState":2,"providerMessageId":"msg-1"}""",
                    HttpStatusCode.Created)
                : new(HttpStatusCode.NotFound));
        await using var factory = AccountingBehaviorTestHost.CreateFactory(accounting);
        using var client = AccountingBehaviorTestHost.CreateClient(factory);
        var csrf = await AccountingBehaviorTestHost.SignInAsync(client);
        var operationId = Guid.Parse("2cdf7ca3-0a11-45e2-92f5-1d25292777a9");
        using var request = new HttpRequestMessage(HttpMethod.Post, "/bff/invoices/from-quotation/84")
        {
            Content = new StringContent(CreateInvoiceJson, Encoding.UTF8, "application/json"),
        };
        request.Headers.Add("X-CSRF-TOKEN", csrf);
        request.Headers.Add("Idempotency-Key", operationId.ToString("D"));

        using var response = await client.SendAsync(request);
        var result = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(55, result.GetProperty("invoiceId").GetInt32());
        var forwarded = Assert.Single(accounting.Requests);
        Assert.Equal("POST", forwarded.Method);
        Assert.Equal("/invoices/from-quotation/84", forwarded.Path);
        Assert.Equal(operationId.ToString("D"), forwarded.IdempotencyKey);
        Assert.Contains("\"invoiceNumber\":\"INV-84\"", forwarded.Body, StringComparison.Ordinal);
        Assert.DoesNotContain("customerId", forwarded.Body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("subtotal", forwarded.Body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("vat", forwarded.Body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("total", forwarded.Body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("orderItems", forwarded.Body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Creation_WithoutCsrf_IsRejectedBeforeAccountingCall()
    {
        var accounting = AccountingBehaviorTestHost.Routes(_ => AccountingBehaviorTestHost.Json("{}"));
        await using var factory = AccountingBehaviorTestHost.CreateFactory(accounting);
        using var client = AccountingBehaviorTestHost.CreateClient(factory);
        await AccountingBehaviorTestHost.SignInAsync(client);
        using var request = new HttpRequestMessage(HttpMethod.Post, "/bff/invoices/from-quotation/84")
        {
            Content = new StringContent(CreateInvoiceJson, Encoding.UTF8, "application/json"),
        };
        request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString("D"));

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(accounting.Requests);
    }

    [Fact]
    public async Task CreationPreview_MalformedResponse_IsBadGateway()
    {
        var accounting = AccountingBehaviorTestHost.Routes(_ => AccountingBehaviorTestHost.Json("not-json"));
        await using var factory = AccountingBehaviorTestHost.CreateFactory(accounting);
        using var client = AccountingBehaviorTestHost.CreateClient(factory);
        await AccountingBehaviorTestHost.SignInAsync(client);

        using var response = await client.GetAsync("/bff/invoices/from-quotation/84/preview");

        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
    }

    [Fact]
    public async Task CreationPreview_OversizedResponse_IsBadGatewayWithoutReturningPayload()
    {
        var oversized = new string('x', 300 * 1024);
        var accounting = AccountingBehaviorTestHost.Routes(_ => AccountingBehaviorTestHost.Json($"\"{oversized}\""));
        await using var factory = AccountingBehaviorTestHost.CreateFactory(accounting);
        using var client = AccountingBehaviorTestHost.CreateClient(factory);
        await AccountingBehaviorTestHost.SignInAsync(client);

        using var response = await client.GetAsync("/bff/invoices/from-quotation/84/preview");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
        Assert.DoesNotContain(oversized[..100], body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Update_TooManyRequests_PreservesBoundedRetryAfter()
    {
        var accounting = AccountingBehaviorTestHost.Routes(request => request.Method == HttpMethod.Put
            ? RetryAfter(HttpStatusCode.TooManyRequests, 3)
            : request.RequestUri?.AbsolutePath == "/invoices/7"
                ? AccountingBehaviorTestHost.Json(InvoiceJson)
                : new(HttpStatusCode.NotFound));
        await using var factory = AccountingBehaviorTestHost.CreateFactory(accounting);
        using var client = AccountingBehaviorTestHost.CreateClient(factory);
        var csrf = await AccountingBehaviorTestHost.SignInAsync(client);
        using var request = new HttpRequestMessage(HttpMethod.Put, "/bff/invoices/7")
        {
            Content = JsonContent.Create(new
            {
                isPaid = true,
                paymentDate = "2030-07-18T00:00:00Z",
                internalComment = "paid",
                modifiedDate = "2030-07-18T00:00:00Z",
            }),
        };
        request.Headers.Add("X-CSRF-TOKEN", csrf);

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode);
        Assert.Equal(TimeSpan.FromSeconds(3), response.Headers.RetryAfter?.Delta);
        var update = Assert.Single(accounting.Requests, item => item.Method == "PUT");
        Assert.Equal("2030-07-18T00:00:00.0000000Z", update.IfUnmodifiedSince);
    }

    [Fact]
    public async Task Delete_RemovesOwnedObjectMetadataItemsThenInvoiceInOrder()
    {
        var order = new List<string>();
        var sync = new object();
        var accounting = AccountingBehaviorTestHost.Routes(request =>
        {
            lock (sync)
            {
                order.Add($"accounting:{request.Method}:{request.RequestUri?.AbsolutePath}");
            }
            return request.RequestUri?.AbsolutePath switch
            {
                "/invoices/7" when request.Method == HttpMethod.Get => AccountingBehaviorTestHost.Json(InvoiceJson),
                "/invoices/7/orderitems" when request.Method == HttpMethod.Get => AccountingBehaviorTestHost.Json(
                    """[{"id":21,"invoiceId":7,"description":"fixture","quantity":1,"unitPrice":1000.25,"subtotal":1000.25,"createdDate":null,"modifiedDate":null}]"""),
                "/invoices/7/files" when request.Method == HttpMethod.Get => AccountingBehaviorTestHost.Json(
                    """[{"id":31,"invoiceId":7,"receiptId":null,"bucket":"maliev.com","objectName":"accounting/invoices/7/invoice.pdf","createdDate":null}]"""),
                "/invoices/files/31" when request.Method == HttpMethod.Delete => new(HttpStatusCode.NoContent),
                "/invoices/orderitems/21" when request.Method == HttpMethod.Delete => new(HttpStatusCode.NoContent),
                "/invoices/7" when request.Method == HttpMethod.Delete => new(HttpStatusCode.NoContent),
                _ => new(HttpStatusCode.NotFound),
            };
        });
        var files = AccountingBehaviorTestHost.Routes(request =>
        {
            lock (sync)
            {
                order.Add($"files:{request.Method}:{request.RequestUri?.AbsolutePath}");
            }
            return new(HttpStatusCode.NoContent);
        });
        await using var factory = AccountingBehaviorTestHost.CreateFactory(accounting, files);
        using var client = AccountingBehaviorTestHost.CreateClient(factory);
        var csrf = await AccountingBehaviorTestHost.SignInAsync(client);
        using var request = new HttpRequestMessage(HttpMethod.Delete, "/bff/invoices/7");
        request.Headers.Add("X-CSRF-TOKEN", csrf);

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(
            [
                "accounting:GET:/invoices/7",
                "accounting:GET:/invoices/7/orderitems",
                "accounting:GET:/invoices/7/files",
                "files:DELETE:/Uploads",
                "accounting:DELETE:/invoices/files/31",
                "accounting:DELETE:/invoices/orderitems/21",
                "accounting:DELETE:/invoices/7",
            ],
            order);
        Assert.Equal("/Uploads?bucket=maliev.com&objectName=accounting%2Finvoices%2F7%2Finvoice.pdf", Assert.Single(files.Requests).Path);
    }

    [Fact]
    public async Task Delete_WithoutCsrf_IsRejectedBeforeOwnershipLookup()
    {
        var accounting = AccountingBehaviorTestHost.Routes(_ => AccountingBehaviorTestHost.Json(InvoiceJson));
        await using var factory = AccountingBehaviorTestHost.CreateFactory(accounting);
        using var client = AccountingBehaviorTestHost.CreateClient(factory);
        await AccountingBehaviorTestHost.SignInAsync(client);

        using var response = await client.DeleteAsync("/bff/invoices/7");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(accounting.Requests);
    }

    private static HttpResponseMessage RetryAfter(HttpStatusCode status, int seconds)
    {
        var response = new HttpResponseMessage(status);
        response.Headers.RetryAfter = new(TimeSpan.FromSeconds(seconds));
        return response;
    }

    private const string InvoiceJson =
        """{"id":7,"customerId":3,"number":"INV-7","comment":"customer","internalComment":"old","salesPerson":"Natthapol V.","currency":"THB","purchaseOrderNumber":null,"requisitioner":null,"shippedVia":null,"fob":null,"terms":null,"billingAddressRecipient":null,"billingAddressCompany":null,"billingAddressBuilding":null,"billingAddressLine1":null,"billingAddressLine2":null,"billingAddressCity":null,"billingAddressState":null,"billingAddressPostalCode":null,"billingAddressCountry":null,"shippingAddressRecipient":null,"shippingAddressRecipientTelephone":null,"shippingAddressCompany":null,"shippingAddressBuilding":null,"shippingAddressLine1":null,"shippingAddressLine2":null,"shippingAddressCity":null,"shippingAddressState":null,"shippingAddressPostalCode":null,"shippingAddressCountry":null,"commercialRegistration":null,"taxIdentification":null,"subtotal":1000.25,"vat":70.02,"total":1070.27,"withholdingTax":null,"outstanding":1070.27,"isPaid":false,"receiptId":null,"paymentDate":null,"createdDate":"2030-07-18T00:00:00Z","modifiedDate":"2030-07-18T00:00:00Z"}""";

    private const string PreviewJson =
        """{"quotationId":84,"customerId":3,"invoiceNumber":"INV-84","salesPerson":"Natthapol V.","currency":"THB","comment":null,"shippedVia":null,"fob":null,"terms":null,"billingAddress":{"recipient":null,"company":null,"building":null,"line1":null,"line2":null,"city":null,"state":null,"postalCode":null,"country":"Thailand","telephone":null},"shippingAddress":{"recipient":null,"company":null,"building":null,"line1":null,"line2":null,"city":null,"state":null,"postalCode":null,"country":"Thailand","telephone":null},"taxIdentification":null,"commercialRegistration":null,"subtotal":1000.25,"vat":70.02,"total":1070.27,"availableWithholdingTax":30.00,"outstanding":1070.27,"orderItems":[]}""";

    private const string CreateInvoiceJson =
        """{"invoiceNumber":"INV-84","comment":"customer copy","purchaseOrderNumber":null,"requisitioner":null,"shippedVia":null,"fob":null,"terms":null,"billingAddress":{"recipient":null,"company":null,"building":null,"line1":null,"line2":null,"city":null,"state":null,"postalCode":null,"country":"Thailand","telephone":null},"shippingAddress":{"recipient":null,"company":null,"building":null,"line1":null,"line2":null,"city":null,"state":null,"postalCode":null,"country":"Thailand","telephone":null},"taxIdentification":null,"commercialRegistration":null,"deductWithholdingTax":false,"sendEmail":true}""";
}
