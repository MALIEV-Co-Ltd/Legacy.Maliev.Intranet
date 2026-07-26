extern alias Bff;

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Legacy.Maliev.Intranet.Auth;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using BffProgram = Bff::Program;

namespace Legacy.Maliev.Intranet.Tests;

public sealed class BffGoogleIdentityFlowContractTests
{
    [Fact]
    public async Task Nonce_MissingClientId_FailsClosedBeforeAuthServiceCall()
    {
        var google = new StubGoogleClient();
        await using var factory = new GoogleBffFactory(google, clientId: string.Empty);
        using var client = CreateClient(factory);

        using var response = await client.PostAsJsonAsync("/bff/google/nonce", new { returnUrl = "/Dashboard" });

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal(0, google.NonceCalls);
    }

    [Fact]
    public async Task Nonce_StoresProtectedStrictHttpOnlyStateAndReturnsOnlyBrowserSafeFields()
    {
        var google = new StubGoogleClient();
        await using var factory = new GoogleBffFactory(google, clientId: "test-google-client-id");
        using var client = CreateClient(factory);

        using var response = await client.PostAsJsonAsync(
            "/bff/google/nonce",
            new { returnUrl = "https://attacker.example/steal" });
        var payload = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("test-google-client-id", payload.GetProperty("clientId").GetString());
        Assert.Equal(StubGoogleClient.Nonce, payload.GetProperty("nonce").GetString());
        Assert.DoesNotContain("service-access-token", payload.GetRawText(), StringComparison.Ordinal);
        var cookie = Assert.Single(
            response.Headers.GetValues("Set-Cookie"),
            value => value.StartsWith("Legacy.Maliev.Intranet.GoogleIdentity=", StringComparison.Ordinal));
        Assert.Contains("httponly", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=strict", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("path=/bff/google", cookie, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Exchange_TamperedOrReplayedNonce_IsRejectedBeforeCredentialForwarding()
    {
        var google = new StubGoogleClient();
        await using var factory = new GoogleBffFactory(google, clientId: "test-google-client-id");
        using var client = CreateClient(factory);

        using var noCookie = await client.PostAsJsonAsync(
            "/bff/google",
            new { credential = "raw-google-credential", nonce = StubGoogleClient.Nonce });
        Assert.Equal(HttpStatusCode.Unauthorized, noCookie.StatusCode);
        Assert.Equal(0, google.ExchangeCalls);

        using var nonceResponse = await client.PostAsJsonAsync("/bff/google/nonce", new { returnUrl = "/Dashboard" });
        Assert.Equal(HttpStatusCode.OK, nonceResponse.StatusCode);
        using var cookie = await client.PostAsJsonAsync(
            "/bff/google",
            new { credential = "raw-google-credential", nonce = "tampered-nonce-at-least-32-characters" });
        Assert.Equal(HttpStatusCode.Unauthorized, cookie.StatusCode);
        Assert.Equal(0, google.ExchangeCalls);
    }

    [Fact]
    public async Task Exchange_ValidNonceEstablishesOpaqueSessionAndConsumesStateExactlyOnce()
    {
        var google = new StubGoogleClient();
        await using var factory = new GoogleBffFactory(google, clientId: "test-google-client-id");
        using var client = CreateClient(factory);

        using var nonceResponse = await client.PostAsJsonAsync("/bff/google/nonce", new { returnUrl = "/Orders" });
        Assert.Equal(HttpStatusCode.OK, nonceResponse.StatusCode);
        using var exchangeResponse = await client.PostAsJsonAsync(
            "/bff/google",
            new { credential = "raw-google-credential", nonce = StubGoogleClient.Nonce });
        var exchangeJson = await exchangeResponse.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, exchangeResponse.StatusCode);
        Assert.Contains("/Orders", exchangeJson, StringComparison.Ordinal);
        Assert.DoesNotContain("raw-google-credential", exchangeJson, StringComparison.Ordinal);
        Assert.DoesNotContain(StubGoogleClient.AccessToken, exchangeJson, StringComparison.Ordinal);
        Assert.Equal(1, google.ExchangeCalls);
        Assert.Contains(
            exchangeResponse.Headers.GetValues("Set-Cookie"),
            value => value.StartsWith("__Host-Legacy.Maliev.Intranet.Bff=", StringComparison.Ordinal));

        using var replayResponse = await client.PostAsJsonAsync(
            "/bff/google",
            new { credential = "raw-google-credential", nonce = StubGoogleClient.Nonce });
        Assert.Equal(HttpStatusCode.Unauthorized, replayResponse.StatusCode);
        Assert.Equal(1, google.ExchangeCalls);
    }

    private static HttpClient CreateClient(WebApplicationFactory<BffProgram> factory) =>
        factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            BaseAddress = new Uri("https://localhost"),
            HandleCookies = true,
        });

    private sealed class GoogleBffFactory(StubGoogleClient google, string clientId) : WebApplicationFactory<BffProgram>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing");
            TestJwtConfiguration.Configure(builder);
            builder.UseSetting("Services:Auth", "http://auth/");
            builder.UseSetting("Authentication:Google:ClientId", clientId);
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IGoogleIdentityAuthClient>();
                services.AddSingleton<IGoogleIdentityAuthClient>(google);
            });
        }
    }

    private sealed class StubGoogleClient : IGoogleIdentityAuthClient
    {
        public const string AccessToken = "server-only-google-access-token";
        public const string RefreshToken = "server-only-google-refresh-token";
        public const string Nonce = "nonce-at-least-32-characters-long-value";

        public int NonceCalls { get; private set; }
        public int ExchangeCalls { get; private set; }

        public Task<GoogleIdentityNonceResponse?> IssueNonceAsync(CancellationToken cancellationToken)
        {
            NonceCalls++;
            return Task.FromResult<GoogleIdentityNonceResponse?>(
                new(Nonce, DateTimeOffset.UtcNow.AddMinutes(10)));
        }

        public Task<EmployeeLoginResult> ExchangeAsync(
            string credential,
            string nonce,
            CancellationToken cancellationToken)
        {
            ExchangeCalls++;
            return Task.FromResult(new EmployeeLoginResult(
                true,
                new AuthTokenResponse(AccessToken, RefreshToken, "Bearer", 900, DateTimeOffset.UtcNow.AddDays(14)),
                new EmployeeIdentity("employee-id", "employee@maliev.com", "employee@maliev.com")));
        }
    }
}
