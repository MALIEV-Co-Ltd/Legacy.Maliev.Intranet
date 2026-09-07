extern alias Bff;

using System.Net;
using CustomerAccountNotificationProxy = Bff::Legacy.Maliev.Intranet.Bff.Customers.CustomerAccountNotificationProxy;

namespace Legacy.Maliev.Intranet.Tests;

public sealed class CustomerAccountNotificationProxyTests
{
    [Fact]
    public async Task SendCreated_EmailsOnlyEncodedSetupLinkWithoutRawCredential()
    {
        var handler = new RecordingHandler();
        var proxy = new CustomerAccountNotificationProxy(
            new HttpClient(handler) { BaseAddress = new Uri("http://notification") });

        using var response = await proxy.SendCreatedAsync(
            "customer@example.com",
            "Ada Lovelace",
            "https://www.maliev.com/Account/SetInitialPassword?email=customer%40example.com&token=opaque-token",
            default);

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        Assert.Contains("SetInitialPassword", handler.Body, StringComparison.Ordinal);
        Assert.Contains("opaque-token", handler.Body, StringComparison.Ordinal);
        Assert.DoesNotContain("Temporary password", handler.Body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Bootstrap", handler.Body, StringComparison.OrdinalIgnoreCase);
    }

    private sealed class RecordingHandler : HttpMessageHandler
    {
        public string Body { get; private set; } = string.Empty;

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Body = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);
            return new(HttpStatusCode.Accepted);
        }
    }
}
