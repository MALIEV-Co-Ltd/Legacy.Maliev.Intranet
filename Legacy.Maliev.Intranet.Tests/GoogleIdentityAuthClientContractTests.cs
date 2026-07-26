using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Legacy.Maliev.Intranet.Auth;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace Legacy.Maliev.Intranet.Tests;

public sealed class GoogleIdentityAuthClientContractTests
{
    private static readonly DateTimeOffset Now = new(2030, 7, 27, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task IssueNonce_UsesServiceBearerAndExactAuthContract()
    {
        var handler = new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(new
            {
                nonce = "nonce-at-least-32-characters-long-value",
                expiresAtUtc = Now.AddMinutes(10),
            }),
        });
        var tokenProvider = new StubTokenProvider("service-access-token");
        var client = CreateClient(handler, tokenProvider);

        var result = await client.IssueNonceAsync(default);

        Assert.NotNull(result);
        Assert.Equal("service-access-token", handler.Authorization);
        Assert.Equal("/auth/v1/exchange/google/nonce", handler.Request.RequestUri?.AbsolutePath);
        Assert.Equal("intranet", handler.Body?.GetProperty("application").GetString());
    }

    [Fact]
    public async Task Exchange_ProjectsOnlyValidatedWorkspaceEmployeeAndNeverReturnsCredential()
    {
        var handler = new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(new
            {
                accessToken = "server-only-access-token",
                refreshToken = "server-only-refresh-token",
                tokenType = "Bearer",
                expiresIn = 900,
                refreshExpiresAt = Now.AddDays(14),
            }),
        });
        var client = CreateClient(handler, new StubTokenProvider("service-access-token"), acceptsToken: true);

        var result = await client.ExchangeAsync("raw-google-credential", "nonce-at-least-32-characters-long-value", default);

        Assert.True(result.Succeeded);
        Assert.Equal("employee@maliev.com", result.Identity?.Email);
        Assert.Equal("/auth/v1/exchange/google", handler.Request.RequestUri?.AbsolutePath);
        Assert.Equal("intranet", handler.Body?.GetProperty("application").GetString());
        Assert.Equal("raw-google-credential", handler.Body?.GetProperty("credential").GetString());
        Assert.DoesNotContain("raw-google-credential", JsonSerializer.Serialize(result), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Exchange_UntrustedTokenEnvelope_FailsClosedWithoutReturningCredentials()
    {
        var handler = new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(new
            {
                accessToken = "bad-access-token",
                refreshToken = "server-only-refresh-token",
                tokenType = "Bearer",
                expiresIn = 900,
                refreshExpiresAt = Now.AddDays(14),
            }),
        });
        var client = CreateClient(handler, new StubTokenProvider("service-access-token"), acceptsToken: false);

        var result = await client.ExchangeAsync("raw-google-credential", "nonce-at-least-32-characters-long-value", default);

        Assert.False(result.Succeeded);
        Assert.Null(result.Tokens);
        Assert.Null(result.Identity);
    }

    private static GoogleIdentityAuthClient CreateClient(
        HttpMessageHandler handler,
        StubTokenProvider tokenProvider,
        bool acceptsToken = true) =>
        new(
            new HttpClient(handler) { BaseAddress = new Uri("http://auth/") },
            tokenProvider,
            new TestAccessTokenValidator(acceptsToken),
            new FakeTimeProvider(Now),
            NullLogger<GoogleIdentityAuthClient>.Instance);

    private sealed class StubTokenProvider(string token) : IServiceAccessTokenProvider
    {
        public string? InvalidatedToken { get; private set; }

        public ValueTask<string?> GetAccessTokenAsync(CancellationToken cancellationToken) => ValueTask.FromResult<string?>(token);

        public void Invalidate(string token) => InvalidatedToken = token;
    }

    private sealed class RecordingHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
    {
        public HttpRequestMessage Request { get; private set; } = null!;
        public JsonElement? Body { get; private set; }
        public string? Authorization { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Request = request;
            Authorization = request.Headers.Authorization?.Parameter;
            Body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken)).RootElement.Clone();
            return responder(request);
        }
    }
}
