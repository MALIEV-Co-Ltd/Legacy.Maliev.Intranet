using Legacy.Maliev.Intranet.Auth;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using System.Net;
using System.Text;

namespace Legacy.Maliev.Intranet.Tests;

public sealed class ServiceAuthenticationContractTests
{
    [Fact]
    public async Task Provider_CachesShortLivedServiceTokenWithoutPersistingSecret()
    {
        var handler = new RecordingHandler("""{"accessToken":"service-token","tokenType":"Bearer","expiresIn":900}""");
        var provider = new ServiceAccessTokenProvider(
            new StubClientFactory(new HttpClient(handler) { BaseAddress = new("http://auth/") }),
            Options.Create(new ServiceAuthenticationOptions { ClientId = "legacy-intranet", ClientSecret = "runtime-secret" }),
            TimeProvider.System,
            NullLogger<ServiceAccessTokenProvider>.Instance);

        var first = await provider.GetAccessTokenAsync(CancellationToken.None);
        var second = await provider.GetAccessTokenAsync(CancellationToken.None);

        Assert.Equal("service-token", first);
        Assert.Equal(first, second);
        Assert.Equal(1, handler.RequestCount);
        Assert.Equal("/auth/v1/service/login", handler.Path);
        Assert.Contains("\"clientId\":\"legacy-intranet\"", handler.Body, StringComparison.Ordinal);
        Assert.Contains("\"clientSecret\":\"runtime-secret\"", handler.Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Handler_ReplacesEmployeeTokenAndInvalidatesRejectedServiceToken()
    {
        var provider = new StubTokenProvider();
        var downstream = new RecordingHandler("forbidden", HttpStatusCode.Forbidden);
        var handler = new LegacyServiceAuthenticationHandler(provider) { InnerHandler = downstream };
        using var client = new HttpClient(handler) { BaseAddress = new("http://orders/") };
        using var request = new HttpRequestMessage(HttpMethod.Get, "/Orders");
        request.Headers.Authorization = new("Bearer", "employee-token");

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("Bearer service-token", downstream.Authorization);
        Assert.Equal("service-token", provider.InvalidatedToken);
    }

    private sealed class StubClientFactory(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => client;
    }

    private sealed class StubTokenProvider : IServiceAccessTokenProvider
    {
        public string? InvalidatedToken { get; private set; }
        public ValueTask<string?> GetAccessTokenAsync(CancellationToken cancellationToken) => ValueTask.FromResult<string?>("service-token");
        public void Invalidate(string token) => InvalidatedToken = token;
    }

    private sealed class RecordingHandler(string body, HttpStatusCode status = HttpStatusCode.OK) : HttpMessageHandler
    {
        public int RequestCount { get; private set; }
        public string? Path { get; private set; }
        public string? Body { get; private set; }
        public string? Authorization { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestCount++;
            Path = request.RequestUri?.AbsolutePath;
            Authorization = request.Headers.Authorization?.ToString();
            Body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            return new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        }
    }
}
