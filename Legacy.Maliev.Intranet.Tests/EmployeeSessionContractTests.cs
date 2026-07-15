using Legacy.Maliev.Intranet.Auth;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Time.Testing;
using System.Net;
using System.Text.RegularExpressions;

namespace Legacy.Maliev.Intranet.Tests;

public sealed partial class EmployeeSessionContractTests
{
    private static readonly DateTimeOffset Now = new(2030, 7, 15, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task SuccessfulLogin_CookieContainsOnlyOpaqueTicketKeyAndUnlocksProtectedRoute()
    {
        var auth = new StubAuthClient();
        await using var factory = new AuthenticatedIntranetFactory(auth);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            HandleCookies = true,
        });
        var loginPage = await client.GetStringAsync("/Login");
        var antiForgery = AntiForgeryToken().Match(loginPage).Groups[1].Value;
        using var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Email"] = "employee@maliev.com",
            ["Password"] = "correct-password",
            ["__RequestVerificationToken"] = antiForgery,
        });

        var loginResponse = await client.PostAsync("/Login", form);

        Assert.Equal(HttpStatusCode.Redirect, loginResponse.StatusCode);
        Assert.Equal("/Dashboard", loginResponse.Headers.Location?.ToString());
        var cookie = Assert.Single(loginResponse.Headers.GetValues("Set-Cookie"), value =>
            value.StartsWith("__Host-Legacy.Maliev.Intranet=", StringComparison.Ordinal));
        Assert.DoesNotContain(StubAuthClient.AccessToken, cookie, StringComparison.Ordinal);
        Assert.DoesNotContain(StubAuthClient.RefreshToken, cookie, StringComparison.Ordinal);
        Assert.DoesNotContain("correct-password", cookie, StringComparison.Ordinal);
        Assert.Equal("employee@maliev.com", auth.Email);
        Assert.Equal("correct-password", auth.Password);

        var dashboard = await client.GetAsync("/Dashboard");
        Assert.Equal(HttpStatusCode.OK, dashboard.StatusCode);
    }

    [Fact]
    public async Task DistributedTicketStore_RoundTripsTokensServerSideButReturnsOpaqueKey()
    {
        var services = new ServiceCollection();
        services.AddDistributedMemoryCache();
        await using var provider = services.BuildServiceProvider();
        var store = new DistributedTicketStore(
            provider.GetRequiredService<IDistributedCache>(),
            new FakeTimeProvider(Now));
        var properties = new AuthenticationProperties { ExpiresUtc = Now.AddHours(1) };
        properties.StoreTokens(
        [
            new AuthenticationToken { Name = "legacy_access_token", Value = StubAuthClient.AccessToken },
            new AuthenticationToken { Name = "legacy_refresh_token", Value = StubAuthClient.RefreshToken },
        ]);
        var ticket = new AuthenticationTicket(
            new System.Security.Claims.ClaimsPrincipal(
                new System.Security.Claims.ClaimsIdentity(
                    [new System.Security.Claims.Claim("sub", "employee-id")],
                    CookieAuthenticationDefaults.AuthenticationScheme)),
            properties,
            CookieAuthenticationDefaults.AuthenticationScheme);

        var key = await store.StoreAsync(ticket);
        var restored = await store.RetrieveAsync(key);

        Assert.StartsWith("legacy-intranet:session:", key, StringComparison.Ordinal);
        Assert.DoesNotContain(StubAuthClient.AccessToken, key, StringComparison.Ordinal);
        Assert.DoesNotContain(StubAuthClient.RefreshToken, key, StringComparison.Ordinal);
        Assert.Equal(StubAuthClient.AccessToken, restored?.Properties.GetTokenValue("legacy_access_token"));
        Assert.Equal(StubAuthClient.RefreshToken, restored?.Properties.GetTokenValue("legacy_refresh_token"));
    }

    [GeneratedRegex("name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"", RegexOptions.CultureInvariant)]
    private static partial Regex AntiForgeryToken();

    private sealed class AuthenticatedIntranetFactory(ILegacyAuthClient authClient) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing");
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<ILegacyAuthClient>();
                services.AddSingleton(authClient);
            });
        }
    }

    private sealed class StubAuthClient : ILegacyAuthClient
    {
        public const string AccessToken = "server-only-access-token";
        public const string RefreshToken = "server-only-refresh-token";

        public string? Email { get; private set; }

        public string? Password { get; private set; }

        public Task<EmployeeLoginResult> LoginAsync(
            string email,
            string password,
            CancellationToken cancellationToken)
        {
            Email = email;
            Password = password;
            return Task.FromResult(new EmployeeLoginResult(
                true,
                new AuthTokenResponse(AccessToken, RefreshToken, "Bearer", 900, Now.AddDays(14)),
                new EmployeeIdentity("employee-id", email, email)));
        }

        public Task<AuthTokenResponse?> RefreshAsync(string refreshToken, CancellationToken cancellationToken) =>
            Task.FromResult<AuthTokenResponse?>(null);

        public Task RevokeAsync(string refreshToken, CancellationToken cancellationToken) => Task.CompletedTask;
    }
}