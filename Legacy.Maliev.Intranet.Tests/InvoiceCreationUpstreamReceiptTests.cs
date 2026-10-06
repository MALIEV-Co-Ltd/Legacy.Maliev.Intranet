using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

namespace Legacy.Maliev.Intranet.Tests;

public sealed class InvoiceCreationUpstreamReceiptTests
{
    [Theory]
    [InlineData("{}")]
    [InlineData("[]")]
    [InlineData("null")]
    [InlineData("{\"state\":0,\"emailState\":0}")]
    [InlineData("{\"invoiceId\":55,\"emailState\":0}")]
    [InlineData("{\"invoiceId\":55,\"state\":0}")]
    [InlineData("{\"invoiceId\":null,\"state\":0,\"emailState\":0}")]
    [InlineData("{\"invoiceId\":55,\"state\":null,\"emailState\":0}")]
    [InlineData("{\"invoiceId\":55,\"state\":0,\"emailState\":null}")]
    [InlineData("{\"invoiceId\":\"55\",\"state\":0,\"emailState\":0}")]
    [InlineData("{\"invoiceId\":55,\"state\":\"0\",\"emailState\":0}")]
    [InlineData("{\"invoiceId\":55,\"state\":0,\"emailState\":\"0\"}")]
    [InlineData("{\"invoiceId\":55,\"state\":false,\"emailState\":0}")]
    [InlineData("{\"invoiceId\":55,\"state\":0,\"emailState\":{}}")]
    [InlineData("{\"invoiceId\":0,\"state\":0,\"emailState\":0}")]
    [InlineData("{\"invoiceId\":-1,\"state\":0,\"emailState\":0}")]
    [InlineData("{\"invoiceId\":2147483648,\"state\":0,\"emailState\":0}")]
    [InlineData("{\"invoiceId\":55,\"state\":0.5,\"emailState\":0}")]
    [InlineData("{\"invoiceId\":55,\"state\":0,\"emailState\":2147483648}")]
    [InlineData("{\"invoiceId\":54,\"invoiceId\":55,\"state\":0,\"emailState\":0}")]
    [InlineData("{\"invoiceId\":55,\"InvoiceId\":55,\"state\":0,\"emailState\":0}")]
    [InlineData("{\"invoiceId\":55,\"state\":99,\"state\":0,\"emailState\":0}")]
    [InlineData("{\"invoiceId\":55,\"State\":99,\"state\":0,\"emailState\":0}")]
    [InlineData("{\"invoiceId\":55,\"state\":0,\"emailState\":2,\"emailState\":0}")]
    [InlineData("{\"invoiceId\":55,\"state\":0,\"EmailState\":2,\"emailState\":0}")]
    [InlineData("{\"invoiceId\":55,\"state\":0,\"emailState\":0,\"providerMessageId\":42}")]
    [InlineData("{\"invoiceId\":55,\"state\":0,\"emailState\":0,\"providerMessageId\":\"\\uD800\"}")]
    [InlineData("{\"invoiceId\":55,\"state\":0,\"emailState\":0,\"\\uD800\":null}")]
    [InlineData("{\"invoiceId\":55,\"state\":0,\"emailState\":0,\"ProviderMessageId\":null,\"providerMessageId\":\"synthetic-message\"}")]
    public async Task Create_InvalidUpstreamReceipt_ReturnsGenericBadGateway(string receipt)
    {
        using var upstream = AccountingBehaviorTestHost.Routes(_ => AccountingBehaviorTestHost.Json(receipt));
        await using var factory = AccountingBehaviorTestHost.CreateFactory(upstream);
        using var client = AccountingBehaviorTestHost.CreateClient(factory);
        var csrf = await AccountingBehaviorTestHost.SignInAsync(client);
        using var request = CreateRequest(csrf);

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
        Assert.Equal(string.Empty, await response.Content.ReadAsStringAsync());
        var sent = Assert.Single(upstream.Requests);
        Assert.Equal("POST", sent.Method);
        Assert.Equal("/invoices/from-quotation/84", sent.Path);
        Assert.Equal(OperationId, sent.IdempotencyKey);
    }

    [Theory]
    [InlineData("{\"invoiceId\":55,\"state\":0,\"emailState\":0}", 0, 0, null)]
    [InlineData("{\"InvoiceId\":55,\"State\":1,\"EmailState\":2,\"ProviderMessageId\":\"synthetic-message\",\"StoredFile\":{\"Bucket\":\"synthetic-bucket\",\"ObjectName\":\"invoices/55/invoice.pdf\"}}", 1, 2, "synthetic-message")]
    [InlineData("{\"invoiceId\":55,\"State\":1,\"emailState\":1,\"providerMessageId\":null}", 1, 1, null)]
    [InlineData("{\"InvoiceId\":55,\"State\":99,\"EmailState\":0}", 99, 0, null)]
    [InlineData("{\"InvoiceId\":55,\"State\":0,\"EmailState\":3,\"StoredFile\":{\"Bucket\":\"synthetic-bucket\",\"ObjectName\":\"invoices/55/invoice.pdf\"}}", 0, 3, null)]
    [InlineData("{\"invoiceId\":55,\"state\":0,\"emailState\":99}", 0, 99, null)]
    public async Task Create_CompleteNumericReceipt_PreservesValuesAndBrowserCasing(string receipt, int state, int emailState, string? message)
    {
        using var upstream = AccountingBehaviorTestHost.Routes(_ => AccountingBehaviorTestHost.Json(receipt, HttpStatusCode.Created));
        await using var factory = AccountingBehaviorTestHost.CreateFactory(upstream);
        using var client = AccountingBehaviorTestHost.CreateClient(factory);
        var csrf = await AccountingBehaviorTestHost.SignInAsync(client);
        using var request = CreateRequest(csrf);

        using var response = await client.SendAsync(request);
        var result = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(55, result.GetProperty("invoiceId").GetInt32());
        Assert.Equal(state, result.GetProperty("state").GetInt32());
        Assert.Equal(emailState, result.GetProperty("emailState").GetInt32());
        Assert.Equal(message, result.GetProperty("providerMessageId").GetString());
        Assert.False(result.TryGetProperty("StoredFile", out _));
        Assert.Single(upstream.Requests);
    }

    internal const string OperationId = "2cdf7ca3-0a11-45e2-92f5-1d25292777a9";
    internal const string CreateInvoiceJson = """{"invoiceNumber":"INV-SYNTHETIC-84","billingAddress":{"country":"Thailand"},"shippingAddress":{"country":"Thailand"},"sendEmail":false}""";
    internal const string PreviewJson = """{"quotationId":84,"customerId":3,"invoiceNumber":"INV-SYNTHETIC-84","salesPerson":"Synthetic Thai employee","currency":"THB","billingAddress":{"country":"Thailand"},"shippingAddress":{"country":"Thailand"},"subtotal":100,"vat":7,"total":107,"availableWithholdingTax":0,"outstanding":107,"orderItems":[]}""";

    private static HttpRequestMessage CreateRequest(string csrf)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/bff/invoices/from-quotation/84")
        {
            Content = new StringContent(CreateInvoiceJson, Encoding.UTF8, "application/json"),
        };
        request.Headers.Add("X-CSRF-TOKEN", csrf);
        request.Headers.Add("Idempotency-Key", OperationId);
        return request;
    }
}
