extern alias Bff;

using System.Net;
using System.Text;
using BffOrders = Bff::Legacy.Maliev.Intranet.Bff.Orders;

namespace Legacy.Maliev.Intranet.Tests;

public sealed class OrderDetailAggregatorTests
{
    [Fact]
    public async Task PartialParallelFailure_DisposesEveryCompletedDownstreamResponse()
    {
        var downstream = new PartiallyFailingHandler();
        HttpClient Client(string authority) => new(downstream) { BaseAddress = new Uri(authority) };
        var aggregator = new BffOrders.OrderDetailAggregator(
            new BffOrders.OrderDetailProxy(Client("http://order/")),
            new BffOrders.OrdersProxy(Client("http://order/")),
            new BffOrders.OrderCatalogReferenceProxy(Client("http://catalog/")),
            new BffOrders.OrderEmployeeReferenceProxy(Client("http://employee/")),
            new BffOrders.OrderFileProxy(Client("http://file/")));

        await Assert.ThrowsAsync<HttpRequestException>(() => aggregator.GetAsync(84, CancellationToken.None));

        Assert.NotEmpty(downstream.Responses);
        Assert.All(downstream.Responses, response => Assert.True(response.WasDisposed));
    }

    private sealed class PartiallyFailingHandler : HttpMessageHandler
    {
        public List<TrackingResponse> Responses { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri?.PathAndQuery ?? string.Empty;
            if (path == "/orders/processes") throw new HttpRequestException("process lookup failed");
            var json = path switch
            {
                "/Orders/84" => OrderJson,
                var value when value.StartsWith("/Materials?", StringComparison.Ordinal) => """{"Items":[]}""",
                var value when value.StartsWith("/employees?", StringComparison.Ordinal) => """{"Items":[]}""",
                "/orderstatuses/Histories/84/latest" => """{"Id":1,"Name":"New"}""",
                "/orderstatuses/Histories/84" => "[]",
                "/orders/84/files" => "[]",
                _ => "[]",
            };
            var response = new TrackingResponse(json);
            Responses.Add(response);
            return Task.FromResult<HttpResponseMessage>(response);
        }
    }

    private sealed class TrackingResponse : HttpResponseMessage
    {
        public TrackingResponse(string json) : base(HttpStatusCode.OK) =>
            Content = new StringContent(json, Encoding.UTF8, "application/json");

        public bool WasDisposed { get; private set; }

        protected override void Dispose(bool disposing)
        {
            WasDisposed = true;
            base.Dispose(disposing);
        }
    }

    private const string OrderJson = """{"Id":84,"CustomerId":42,"Name":"fixture","ProcessId":3,"Quantity":1,"Manufactured":0,"AllowSocialMedia":false,"AllowCancellation":true,"AllowPayment":false}""";
}
