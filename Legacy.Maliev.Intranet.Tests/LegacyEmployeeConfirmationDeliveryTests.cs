using System.Net;
using System.Net.Http.Json;
using Legacy.Maliev.Intranet.Employees;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace Legacy.Maliev.Intranet.Tests;

public sealed class LegacyEmployeeConfirmationDeliveryTests
{
    [Fact]
    public async Task SendAsync_TrustedOrigin_RequestsChallengeAndSendsLink()
    {
        var handler = new RecordingHandler();
        var delivery = CreateDelivery(handler, "https://intranet.example.com/");

        var sent = await delivery.SendAsync("employee@example.com", CancellationToken.None);

        Assert.True(sent);
        Assert.Equal("/auth/v1/employee-self-service/email-confirmation/request", handler.Requests[0].Path);
        Assert.Contains("employee@example.com", handler.Requests[0].Body, StringComparison.Ordinal);
        Assert.Equal("/notifications/v1/email/NoReply", handler.Requests[1].Path);
        Assert.Contains("https://intranet.example.com/Employees/EmailConfirmation", handler.Requests[1].Body, StringComparison.Ordinal);
        Assert.Contains("opaque-confirm-token", handler.Requests[1].Body, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("http://intranet.example.com/")]
    [InlineData("https://attacker.example@intranet.example.com/")]
    [InlineData("https://intranet.example.com/path")]
    public async Task SendAsync_UnsafeOrigin_DoesNotRequestToken(string? origin)
    {
        var handler = new RecordingHandler();
        var delivery = CreateDelivery(handler, origin);

        Assert.False(await delivery.SendAsync("employee@example.com", CancellationToken.None));
        Assert.Empty(handler.Requests);
    }

    private static LegacyEmployeeConfirmationDelivery CreateDelivery(RecordingHandler handler, string? origin)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["EmployeeConfirmation:PublicOrigin"] = origin,
        }).Build();
        return new LegacyEmployeeConfirmationDelivery(new ClientFactory(handler), configuration,
            NullLogger<LegacyEmployeeConfirmationDelivery>.Instance);
    }

    private sealed class ClientFactory(RecordingHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false)
        {
            BaseAddress = new(name == "employee-confirmation-auth" ? "http://auth/" : "http://notification/"),
        };
    }

    private sealed class RecordingHandler : HttpMessageHandler
    {
        public List<(string Path, string Body)> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add((request.RequestUri!.AbsolutePath, await request.Content!.ReadAsStringAsync(cancellationToken)));
            return request.RequestUri.AbsolutePath == "/auth/v1/employee-self-service/email-confirmation/request"
                ? new(HttpStatusCode.OK) { Content = JsonContent.Create(new { accepted = true, token = "opaque-confirm-token-0123456789012345678901" }) }
                : new(HttpStatusCode.Accepted);
        }
    }
}
