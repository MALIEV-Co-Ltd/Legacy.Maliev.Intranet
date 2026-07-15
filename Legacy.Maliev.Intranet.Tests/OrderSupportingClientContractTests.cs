using Legacy.Maliev.Intranet.Orders;
using Legacy.Maliev.Intranet.PurchaseOrders;
using Microsoft.AspNetCore.Http;
using System.Net;
using System.Text;
using System.Text.Json;

namespace Legacy.Maliev.Intranet.Tests;

public sealed class OrderSupportingClientContractTests
{
    [Fact]
    public async Task FileClient_StreamsOrderFilesThroughScannedUploadBoundary()
    {
        var handler = new RecordingHandler("""{"Object":[{"Bucket":"maliev.com","ObjectName":"uploads/42/fixture.stl","Uri":"https://storage.test/fixture"}]}""");
        var client = new LegacyFileClient(new HttpClient(handler) { BaseAddress = new("http://files/") });
        var formFile = new FormFile(new MemoryStream(Encoding.UTF8.GetBytes("solid fixture")), 0, 13, "Files", "fixture.stl")
        {
            Headers = new HeaderDictionary(),
            ContentType = "model/stl",
        };

        var uploads = await client.UploadOrderFilesAsync(42, [formFile], "employee-token", CancellationToken.None);

        Assert.Single(uploads);
        Assert.Equal("/Uploads", handler.Path);
        Assert.Contains("bucket=maliev.com", handler.Query, StringComparison.Ordinal);
        Assert.Contains("path=uploads%2F42%2F", handler.Query, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("fixture.stl", handler.Body, StringComparison.Ordinal);
        Assert.Contains("solid fixture", handler.Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DocumentClient_RendersQuestPdfOrderLabelContract()
    {
        var handler = new RecordingHandler("%PDF-1.7", "application/pdf");
        var client = new OrderDocumentClient(new HttpClient(handler) { BaseAddress = new("http://documents/") });

        var bytes = await client.RenderOrderLabelAsync(new("84", "Thai fixture", 5, 2, 3, "FDM", "PLA", "Black", "As printed", "ไม้เอก ไม้โท"), "employee-token", CancellationToken.None);

        Assert.StartsWith("%PDF", Encoding.UTF8.GetString(bytes), StringComparison.Ordinal);
        Assert.Equal("/Pdfs/orderlabel", handler.Path);
        using var json = JsonDocument.Parse(handler.Body!);
        Assert.Equal("ไม้เอก ไม้โท", json.RootElement.GetProperty("description").GetString());
        Assert.Equal(3, json.RootElement.GetProperty("remainingQuantity").GetInt32());
    }

    [Fact]
    public async Task NotificationClient_SendsModernNoReplyJsonWithoutQuerySecrets()
    {
        var handler = new RecordingHandler("""{"providerMessageId":"recorded-1"}""");
        var client = new LegacyOrderNotificationClient(new HttpClient(handler) { BaseAddress = new("http://notifications/") });

        await client.SendCreatedAsync("customer@example.com", 84, "employee-token", CancellationToken.None);

        Assert.Equal("/notifications/v1/email/NoReply", handler.Path);
        Assert.Equal(string.Empty, handler.Query);
        using var json = JsonDocument.Parse(handler.Body!);
        Assert.Equal("customer@example.com", json.RootElement.GetProperty("to").GetString());
        Assert.Contains("#84", json.RootElement.GetProperty("subject").GetString(), StringComparison.Ordinal);
        Assert.Equal("mail-tracking@maliev.com", json.RootElement.GetProperty("bcc")[0].GetString());
    }

    private sealed class RecordingHandler(string responseBody, string mediaType = "application/json") : HttpMessageHandler
    {
        public string? Path { get; private set; }
        public string? Query { get; private set; }
        public string? Body { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Path = request.RequestUri?.AbsolutePath;
            Query = request.RequestUri?.Query ?? string.Empty;
            Body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            return new(HttpStatusCode.OK) { Content = new StringContent(responseBody, Encoding.UTF8, mediaType) };
        }
    }
}
