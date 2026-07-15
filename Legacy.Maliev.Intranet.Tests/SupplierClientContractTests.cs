using Legacy.Maliev.Intranet.Suppliers;
using System.Net;
using System.Text;

namespace Legacy.Maliev.Intranet.Tests;

public sealed class SupplierClientContractTests
{
    [Fact]
    public async Task CreateSupplier_UsesBearerJsonAndStableIdempotencyHeader()
    {
        var handler = new RecordingHandler(HttpStatusCode.Created, """{"Id":42,"Name":"Acme"}""");
        var client = new LegacyProcurementClient(new HttpClient(handler) { BaseAddress = new("http://procurement/") });

        var result = await client.CreateSupplierAsync(new("Acme", null, null, "sales@acme.test", null, null, null, null), "employee-token", CancellationToken.None);

        Assert.Equal(42, result.Id);
        Assert.Equal("/Suppliers", handler.Uri?.AbsolutePath);
        Assert.Equal("Bearer employee-token", handler.Authorization);
        Assert.True(Guid.TryParse(handler.IdempotencyKey, out _));
        Assert.Contains("\"name\":\"Acme\"", handler.Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CreateAddress_UsesSupplierOwnedAddressRoute()
    {
        var handler = new RecordingHandler(HttpStatusCode.Created, """{"Id":7,"Address1":"1 Main","CountryId":66}""");
        var client = new LegacyProcurementClient(new HttpClient(handler) { BaseAddress = new("http://procurement/") });

        await client.CreateSupplierAddressAsync(42, new(null, "1 Main", null, null, null, null, 66), "employee-token", CancellationToken.None);

        Assert.Equal("/suppliers/42/addresses", handler.Uri?.AbsolutePath);
    }

    private sealed class RecordingHandler(HttpStatusCode status, string body) : HttpMessageHandler
    {
        public Uri? Uri { get; private set; }
        public string? Authorization { get; private set; }
        public string? IdempotencyKey { get; private set; }
        public string? Body { get; private set; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Uri = request.RequestUri; Authorization = request.Headers.Authorization?.ToString();
            IdempotencyKey = request.Headers.TryGetValues("Idempotency-Key", out var values) ? values.Single() : null;
            Body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            return new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        }
    }
}