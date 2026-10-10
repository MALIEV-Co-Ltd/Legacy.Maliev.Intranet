extern alias Bff;

using System.Net;
using System.Text;
using Legacy.Maliev.Intranet.Contracts;
using Legacy.Maliev.Intranet.PurchaseOrders;
using Microsoft.Extensions.Logging.Abstractions;
using CreationGateway = Bff::Legacy.Maliev.Intranet.Bff.Procurement.PurchaseOrderCreationGateway;

namespace Legacy.Maliev.Intranet.Tests;

/// <summary>Exercises the real HTTP gateway and workflow against controlled replay and lost-response boundaries.</summary>
public sealed class PurchaseOrderReplayCompensationBoundaryTests
{
    [Theory]
    [InlineData("conflict")]
    [InlineData("unavailable")]
    [InlineData("lost-response")]
    [InlineData("malformed-response")]
    public async Task RepeatedAttemptWithReplayedRootAndItemNeverDeletesPriorResources(string failure)
    {
        using var transport = new ReplayTransport(failure);
        using var client = new HttpClient(transport) { BaseAddress = new Uri("https://procurement.invalid") };
        var service = new PurchaseOrderCreationService(new CreationGateway(new ClientFactory(client)),
            NullLogger<PurchaseOrderCreationService>.Instance);
        const string attempt = "0f727e0f-a4f3-4e1c-995a-c0d37ea2a972";
        var request = new PurchaseOrderCreateRequest
        {
            SupplierId = 4, ShippingAddressId = 1, BillingAddressId = 2, EmployeeId = 7,
            ShippingCompanyName = "MALIEV", BillingCompanyName = "MALIEV", Notes = " literal notes ",
            Items = [new() { PartNumber = " P-1 ", Description = "first", Quantity = 2, UnitPrice = 3m },
                new() { Description = "second", Quantity = 1, UnitPrice = 4m }],
        };

        var first = await service.CreateAsync(request, attempt, CancellationToken.None);
        var second = await service.CreateAsync(request, attempt, CancellationToken.None);

        Assert.Equal(PurchaseOrderCreationStatus.OutcomeUnknown, first.Status);
        Assert.Equal(PurchaseOrderCreationStatus.OutcomeUnknown, second.Status);
        Assert.Equal(0, transport.Deletes);
        Assert.Equal(6, transport.Keys.Count);
        Assert.Equal(attempt, transport.Keys[0]);
        Assert.Equal(attempt, transport.Keys[3]);
        Assert.Equal(transport.Keys[1], transport.Keys[4]);
        Assert.Equal(transport.Keys[2], transport.Keys[5]);
        Assert.NotEqual(transport.Keys[1], transport.Keys[2]);
        Assert.NotEqual(attempt, transport.Keys[1]);
        Assert.Equal(transport.Bodies[0], transport.Bodies[3]);
        Assert.Equal(transport.Bodies[1], transport.Bodies[4]);
        Assert.Contains(" literal notes ", transport.Bodies[0], StringComparison.Ordinal);
        Assert.Contains(" P-1 ", transport.Bodies[1], StringComparison.Ordinal);
        Assert.Equal(new[] { 9, 10, 84 }, transport.SurvivingIds.Order().ToArray());
    }

    private sealed class ClientFactory(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name)
        {
            Assert.Equal(CreationGateway.ProcurementClient, name);
            return client;
        }
    }

    private sealed class ReplayTransport(string failure) : HttpMessageHandler
    {
        public List<string> Keys { get; } = [];
        public List<string> Bodies { get; } = [];
        public HashSet<int> SurvivingIds { get; } = [84, 9, 10];
        public int Deletes { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.Method == HttpMethod.Delete)
            {
                Deletes++;
                SurvivingIds.Clear();
                return new(HttpStatusCode.NoContent);
            }
            Assert.Equal(HttpMethod.Post, request.Method);
            Keys.Add(Assert.Single(request.Headers.GetValues("Idempotency-Key")));
            Bodies.Add(await request.Content!.ReadAsStringAsync(cancellationToken));
            if (request.RequestUri!.AbsolutePath == "/PurchaseOrders")
                return Json(HttpStatusCode.Created, "{\"Id\":84,\"CreatedDate\":\"2030-07-15T10:30:00Z\"}");
            Assert.Equal("/purchaseorders/orderitems", request.RequestUri.AbsolutePath);
            if (Keys.Count % 3 == 2) return Json(HttpStatusCode.Created, "{\"Id\":9}");
            return failure switch
            {
                "conflict" => Json(HttpStatusCode.Conflict, "{}"),
                "unavailable" => Json(HttpStatusCode.ServiceUnavailable, "{}"),
                "lost-response" => throw new HttpRequestException("Controlled committed child acknowledgement loss."),
                "malformed-response" => Json(HttpStatusCode.Created, "not-json"),
                _ => throw new InvalidOperationException("Unknown synthetic failure mode."),
            };
        }

        private static HttpResponseMessage Json(HttpStatusCode status, string body) =>
            new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
    }
}
