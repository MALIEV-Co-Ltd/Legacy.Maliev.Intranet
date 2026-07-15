using Legacy.Maliev.Intranet.Orders;
using System.Net;
using System.Text;
using System.Text.Json;

namespace Legacy.Maliev.Intranet.Tests;

public sealed class OrderClientContractTests
{
    [Fact]
    public async Task GetOrders_UsesLegacyQueryShapeAndBearerToken()
    {
        var handler = new RecordingHandler("""{"Items":[],"PageIndex":2,"TotalPages":4,"TotalRecords":77}""");
        var client = new LegacyOrderClient(new HttpClient(handler) { BaseAddress = new("http://orders/") });

        var result = await client.GetOrdersAsync(
            OrderSortType.OrderCreatedDate_Descending,
            "Thai tone / mark?",
            2,
            25,
            "employee-token",
            CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal(77, result.TotalRecords);
        Assert.Equal("/Orders", handler.Path);
        Assert.Contains("sort=OrderCreatedDate_Descending", handler.Query, StringComparison.Ordinal);
        Assert.Contains("search=Thai%20tone%20%2F%20mark%3F", handler.Query, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("index=2", handler.Query, StringComparison.Ordinal);
        Assert.Contains("size=25", handler.Query, StringComparison.Ordinal);
        Assert.Equal("Bearer employee-token", handler.Authorization);
    }

    [Fact]
    public async Task GetPendingOrders_UsesBoundedPendingEndpoint()
    {
        var handler = new RecordingHandler("""{"Items":[],"PageIndex":1,"TotalPages":1,"TotalRecords":0}""");
        var client = new LegacyOrderClient(new HttpClient(handler) { BaseAddress = new("http://orders/") });

        await client.GetPendingOrdersAsync(1000, "employee-token", CancellationToken.None);

        Assert.Equal("/Orders/pending", handler.Path);
        Assert.Equal("?index=1&size=1000", handler.Query);
        Assert.Equal("Bearer employee-token", handler.Authorization);
    }

    [Fact]
    public async Task CreateOrder_UsesFullJsonContractAndIdempotencyKey()
    {
        var handler = new RecordingHandler("""{"Id":84,"CustomerId":42,"ProcessId":3,"Quantity":2,"Manufactured":0,"AllowSocialMedia":true,"AllowCancellation":true,"AllowPayment":false}""", HttpStatusCode.Created);
        var client = new LegacyOrderClient(new HttpClient(handler) { BaseAddress = new("http://orders/") });
        var payload = new UpsertOrderRequest(42, 7, "Thai fixture", "ไม้เอก ไม้โท", 3, 5, 6, 4, 2, 0, 125m, 10m, 1, 3, new(2030, 7, 20), null, "note", true, true, false, "TRACK-1");

        var result = await client.CreateOrderAsync(payload, "employee-token", CancellationToken.None);

        Assert.Equal(84, result.Id);
        Assert.Equal("/Orders", handler.Path);
        Assert.True(Guid.TryParse(handler.IdempotencyKey, out _));
        using var json = JsonDocument.Parse(handler.Body!);
        Assert.Equal("ไม้เอก ไม้โท", json.RootElement.GetProperty("description").GetString());
        Assert.Equal(10m, json.RootElement.GetProperty("discountPercent").GetDecimal());
        Assert.Equal("TRACK-1", json.RootElement.GetProperty("trackingNumber").GetString());
    }

    [Fact]
    public async Task UpdateOrder_UsesOptimisticConcurrencyHeader()
    {
        var handler = new RecordingHandler(string.Empty, HttpStatusCode.NoContent);
        var client = new LegacyOrderClient(new HttpClient(handler) { BaseAddress = new("http://orders/") });
        var modified = new DateTimeOffset(2030, 7, 15, 8, 30, 0, TimeSpan.Zero);
        var payload = new UpsertOrderRequest(42, 7, "Updated", null, 3, null, null, null, 2, 1, null, null, null, null, null, null, null, true, true, false, null);

        await client.UpdateOrderAsync(84, payload, modified, "employee-token", CancellationToken.None);

        Assert.Equal("/Orders/84", handler.Path);
        Assert.Equal(modified.ToString("O"), handler.ExpectedModifiedDate);
    }

    [Fact]
    public async Task StatusAndFileWrites_UseOwnedRoutesAndIdempotency()
    {
        var handler = new RecordingHandler("""{"Id":5,"OrderId":84,"Bucket":"maliev.com","ObjectName":"uploads/42/fixture.stl"}""", HttpStatusCode.Created);
        var client = new LegacyOrderClient(new HttpClient(handler) { BaseAddress = new("http://orders/") });

        var file = await client.CreateOrderFileAsync(84, "maliev.com", "uploads/42/fixture.stl", "employee-token", CancellationToken.None);
        Assert.Equal(5, file.Id);
        Assert.Equal("/orders/84/files", handler.Path);
        Assert.Contains("objectName=uploads%2F42%2Ffixture.stl", handler.Query, StringComparison.OrdinalIgnoreCase);

        handler.SetResponse(string.Empty, HttpStatusCode.Created);
        await client.TransitionOrderAsync(84, 3, "employee-token", CancellationToken.None);
        Assert.Equal("/orderstatuses/Histories/84/3", handler.Path);
        Assert.True(Guid.TryParse(handler.IdempotencyKey, out _));
    }

    private sealed class RecordingHandler(string body, HttpStatusCode status = HttpStatusCode.OK) : HttpMessageHandler
    {
        public string? Path { get; private set; }
        public string? Query { get; private set; }
        public string? Authorization { get; private set; }
        public string? IdempotencyKey { get; private set; }
        public string? ExpectedModifiedDate { get; private set; }
        public string? Body { get; private set; }
        private string responseBody = body;
        private HttpStatusCode responseStatus = status;

        public void SetResponse(string value, HttpStatusCode valueStatus)
        {
            responseBody = value;
            responseStatus = valueStatus;
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Path = request.RequestUri?.AbsolutePath;
            Query = request.RequestUri?.Query;
            Authorization = request.Headers.Authorization?.ToString();
            IdempotencyKey = request.Headers.TryGetValues("Idempotency-Key", out var keys) ? keys.Single() : null;
            ExpectedModifiedDate = request.Headers.TryGetValues("X-Expected-Modified-Date", out var dates) ? dates.Single() : null;
            Body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(responseStatus)
            {
                Content = new StringContent(responseBody, Encoding.UTF8, "application/json"),
            };
        }
    }
}
