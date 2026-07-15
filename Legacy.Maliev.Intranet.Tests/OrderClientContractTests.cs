using Legacy.Maliev.Intranet.Orders;
using System.Net;
using System.Text;

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

    private sealed class RecordingHandler(string body) : HttpMessageHandler
    {
        public string? Path { get; private set; }
        public string? Query { get; private set; }
        public string? Authorization { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Path = request.RequestUri?.AbsolutePath;
            Query = request.RequestUri?.Query;
            Authorization = request.Headers.Authorization?.ToString();
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json"),
            });
        }
    }
}
