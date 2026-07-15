using Legacy.Maliev.Intranet.PurchaseOrders;
using Legacy.Maliev.Intranet.Suppliers;
using System.Net;
using System.Text;
using System.Text.Json;

namespace Legacy.Maliev.Intranet.Tests;

public sealed class PurchaseOrderClientContractTests
{
    [Fact]
    public async Task CreatePurchaseOrder_UsesBearerJsonAndIdempotencyKey()
    {
        var handler = new RecordingHandler(HttpStatusCode.Created, "application/json", """{"Id":84,"SupplierId":4}""");
        var client = Procurement(handler);
        var result = await client.CreatePurchaseOrderAsync(new(4, "คุณสมชาย", 1, null, null, null, null, 2, null, null, null, null, "Bangkok", "Net 30", "Courier", 7, "ทดสอบ"), "token", CancellationToken.None);
        Assert.Equal(84, result.Id); Assert.Equal("/PurchaseOrders", handler.Path); Assert.Equal("Bearer token", handler.Authorization);
        Assert.True(Guid.TryParse(handler.IdempotencyKey, out _));
        using var json = JsonDocument.Parse(handler.Body!); Assert.Equal("คุณสมชาย", json.RootElement.GetProperty("supplierContactPerson").GetString());
    }

    [Fact]
    public async Task CreateLineItem_UsesOwnedPurchaseOrderIdInJson()
    {
        var handler = new RecordingHandler(HttpStatusCode.Created, "application/json", """{"Id":9,"PurchaseOrderId":84,"Description":"Resin"}""");
        var result = await Procurement(handler).CreateOrderItemAsync(new(84, "R-1", "Resin", 2, 450m), "token", CancellationToken.None);
        Assert.Equal(9, result.Id); Assert.Equal("/purchaseorders/orderitems", handler.Path); Assert.Contains("\"purchaseOrderId\":84", handler.Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task LinkFile_UsesExactQueryAndReturnsMetadata()
    {
        var handler = new RecordingHandler(HttpStatusCode.Created, "application/json", """{"Id":5,"PurchaseOrderId":84,"Bucket":"maliev.com","ObjectName":"purchaseorders/84.pdf"}""");
        var result = await Procurement(handler).CreatePurchaseOrderFileAsync(84, "maliev.com", "purchaseorders/84.pdf", "token", CancellationToken.None);
        Assert.Equal(5, result.Id); Assert.Equal("/purchaseorders/84/files", handler.Path); Assert.Contains("bucket=maliev.com", handler.Query, StringComparison.Ordinal); Assert.Contains("objectName=purchaseorders%2F84.pdf", handler.Query, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task DocumentClient_SendsThaiQuestPdfContractAndRequiresPdf()
    {
        var handler = new RecordingHandler(HttpStatusCode.OK, "application/pdf", "%PDF-1.7");
        var client = new PurchaseOrderDocumentClient(new HttpClient(handler) { BaseAddress = new("http://document/") });
        var address = new PurchaseOrderDocumentAddress("1 ถนนสุขุมวิท", null, null, "กรุงเทพฯ", "Thailand", "10110", null);
        var company = new CompanyInformation(address, "มาลีฟ", "คุณสมชาย", null, null, null);
        var bytes = await client.RenderAsync(new(company, DateTime.UnixEpoch, "Bangkok", "ไม้เอก ไม้โท", "Natt", [new("THB", "เรซิน", "R-1", 1, 450m, 450m)], 84, "Courier", company, company, "Net 30"), "token", CancellationToken.None);
        Assert.StartsWith("%PDF", Encoding.UTF8.GetString(bytes), StringComparison.Ordinal); Assert.Equal("/Pdfs/purchaseorder", handler.Path); Assert.Equal("Bearer token", handler.Authorization);
        using var json = JsonDocument.Parse(handler.Body!); Assert.Equal("ไม้เอก ไม้โท", json.RootElement.GetProperty("notes").GetString()); Assert.Equal("Bangkok", json.RootElement.GetProperty("FOB").GetString()); Assert.Equal(84, json.RootElement.GetProperty("referenceNumber").GetInt32()); Assert.Equal("THB", json.RootElement.GetProperty("orderItems")[0].GetProperty("currency").GetString());
    }

    [Fact]
    public async Task FileClient_UploadsPdfAsExpectedMultipartField()
    {
        var handler = new RecordingHandler(HttpStatusCode.Created, "application/json", """{"Object":[{"Bucket":"maliev.com","ObjectName":"purchaseorders/2026/07/15/PurchaseOrder_84.pdf","Uri":"https://storage.test/file"}]}""");
        var result = await new LegacyFileClient(new HttpClient(handler) { BaseAddress = new("http://files/") }).UploadPdfAsync(84, Encoding.UTF8.GetBytes("%PDF"), "token", CancellationToken.None);
        Assert.Equal("maliev.com", result.Bucket); Assert.Equal("/Uploads", handler.Path); Assert.Contains("bucket=maliev.com", handler.Query, StringComparison.Ordinal); Assert.Contains("name=files", handler.Body, StringComparison.Ordinal); Assert.Contains("PurchaseOrder_84.pdf", handler.Body, StringComparison.Ordinal);
    }

    private static LegacyProcurementClient Procurement(HttpMessageHandler handler) => new(new HttpClient(handler) { BaseAddress = new("http://procurement/") });

    private sealed class RecordingHandler(HttpStatusCode status, string mediaType, string body) : HttpMessageHandler
    {
        public string? Path { get; private set; }
        public string? Query { get; private set; }
        public string? Authorization { get; private set; }
        public string? IdempotencyKey { get; private set; }
        public string? Body { get; private set; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Path = request.RequestUri?.AbsolutePath; Query = request.RequestUri?.Query; Authorization = request.Headers.Authorization?.ToString();
            IdempotencyKey = request.Headers.TryGetValues("Idempotency-Key", out var values) ? values.Single() : null;
            Body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            return new(status) { Content = new StringContent(body, Encoding.UTF8, mediaType) };
        }
    }
}