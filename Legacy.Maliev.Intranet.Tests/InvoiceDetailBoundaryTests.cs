extern alias Bff;

using System.Net;
using System.Text;
using Legacy.Maliev.Intranet.Contracts;
using InvoiceDetailAggregator = Bff::Legacy.Maliev.Intranet.Bff.Accounting.InvoiceDetailAggregator;
using InvoiceDetailProxy = Bff::Legacy.Maliev.Intranet.Bff.Accounting.InvoiceDetailProxy;
using InvoiceFileProxy = Bff::Legacy.Maliev.Intranet.Bff.Accounting.InvoiceFileProxy;

namespace Legacy.Maliev.Intranet.Tests;

public sealed class InvoiceDetailBoundaryTests
{
    [Fact]
    public async Task Detail_ResolvesOnlyOwnedMetadataAndDoesNotExposeBucket()
    {
        var accounting = new RouteHandler(request => request.RequestUri?.AbsolutePath switch
        {
            "/invoices/7" => Json(InvoiceJson),
            "/invoices/7/orderitems" => Json("""[{"id":2,"invoiceId":7,"description":"น้ำ","quantity":2,"unitPrice":500.125,"subtotal":1000.25}]"""),
            "/invoices/7/files" => Json("""[{"id":4,"invoiceId":7,"bucket":"maliev.com","objectName":"accounting/invoices/7/invoice.pdf"}]"""),
            "/receipts/9/files" => Json("""[{"id":5,"receiptId":9,"bucket":"maliev.com","objectName":"accounting/receipts/9/receipt.pdf"}]"""),
            _ => new(HttpStatusCode.NotFound),
        });
        var storage = new RouteHandler(_ => Json("\"https://storage.test/signed\""));
        var aggregator = CreateAggregator(accounting, storage);

        var detail = await aggregator.GetAsync(7, CancellationToken.None);

        Assert.NotNull(detail);
        Assert.Equal("น้ำ", detail.OrderItems.Single().Description);
        Assert.Equal(7, detail.InvoiceFiles.Single().OwnerId);
        Assert.Equal(9, detail.ReceiptFiles.Single().OwnerId);
        Assert.All(detail.InvoiceFiles.Concat(detail.ReceiptFiles), file => Assert.Equal("https://storage.test/signed", file.Uri?.AbsoluteUri));
        var wire = System.Text.Json.JsonSerializer.Serialize(detail, new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web));
        Assert.DoesNotContain("bucket", wire, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Detail_RejectsFileMetadataOwnedByAnotherInvoiceBeforeSigning()
    {
        var accounting = new RouteHandler(request => request.RequestUri?.AbsolutePath switch
        {
            "/invoices/7" => Json(InvoiceJson),
            "/invoices/7/orderitems" => Json("[]"),
            "/invoices/7/files" => Json("""[{"id":4,"invoiceId":8,"bucket":"maliev.com","objectName":"other.pdf"}]"""),
            "/receipts/9/files" => Json("[]"),
            _ => new(HttpStatusCode.NotFound),
        });
        var storage = new RouteHandler(_ => Json("\"https://storage.test/should-not-run\""));

        await Assert.ThrowsAsync<InvalidDataException>(() => CreateAggregator(accounting, storage).GetAsync(7, CancellationToken.None));
        Assert.Empty(storage.Requests);
    }

    [Fact]
    public async Task Update_PreservesReadOnlyFieldsAndSendsOptimisticConcurrencyHeader()
    {
        var downstream = new RouteHandler(_ => new(HttpStatusCode.NoContent));
        var proxy = new InvoiceDetailProxy(new HttpClient(downstream) { BaseAddress = new("http://accounting") });
        var current = System.Text.Json.JsonSerializer.Deserialize<InvoiceDetail>(InvoiceJson, new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web))!;
        var modified = new DateTime(2030, 7, 19, 0, 0, 0, DateTimeKind.Utc);

        using var response = await proxy.UpdateAsync(7, current, new(true, modified, "paid", 30m, 1040.27m, modified), CancellationToken.None);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var request = Assert.Single(downstream.Requests);
        Assert.Equal("2030-07-19T00:00:00.0000000Z", request.IfUnmodifiedSince);
        Assert.Contains("\"number\":\"INV-7\"", request.Body, StringComparison.Ordinal);
        Assert.Contains("\"internalComment\":\"paid\"", request.Body, StringComparison.Ordinal);
    }

    private static InvoiceDetailAggregator CreateAggregator(HttpMessageHandler accounting, HttpMessageHandler storage) => new(
        new InvoiceDetailProxy(new HttpClient(accounting) { BaseAddress = new("http://accounting") }),
        new InvoiceFileProxy(new HttpClient(storage) { BaseAddress = new("http://files") }));

    private static HttpResponseMessage Json(string json) => new(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    private sealed class RouteHandler(Func<HttpRequestMessage, HttpResponseMessage> response) : HttpMessageHandler
    {
        public List<RequestRecord> Requests { get; } = [];
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            request.Headers.TryGetValues("If-Unmodified-Since", out var concurrency);
            Requests.Add(new(request.RequestUri?.PathAndQuery, concurrency?.FirstOrDefault(), request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken)));
            return response(request);
        }
    }

    private sealed record RequestRecord(string? Path, string? IfUnmodifiedSince, string? Body);

    private const string InvoiceJson = """{"id":7,"customerId":3,"number":"INV-7","comment":"customer","internalComment":"old","salesPerson":"Employee","currency":"THB","subtotal":1000.25,"vat":70.02,"total":1070.27,"withholdingTax":null,"outstanding":1070.27,"isPaid":true,"receiptId":9,"paymentDate":"2030-07-18T00:00:00Z","createdDate":"2030-07-18T00:00:00Z","modifiedDate":"2030-07-18T00:00:00Z"}""";
}
