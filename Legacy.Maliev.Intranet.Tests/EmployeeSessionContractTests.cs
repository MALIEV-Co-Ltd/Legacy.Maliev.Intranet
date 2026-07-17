using Legacy.Maliev.Intranet.Auth;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using System.Net;
using System.Security.Claims;
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

    [Fact]
    public async Task GetAccessToken_InsideRefreshSkew_RotatesAndRenewsServerTicket()
    {
        var auth = new StubAuthClient
        {
            RefreshResult = new AuthTokenResponse(
                "rotated-access-token",
                "rotated-refresh-token",
                "Bearer",
                900,
                Now.AddDays(14)),
        };
        var properties = CreateTokenProperties(Now.AddMinutes(1));
        var authentication = new RecordingAuthenticationService(properties);
        var context = CreateHttpContext(authentication);
        var sessions = CreateSessionService(auth);

        var accessToken = await sessions.GetAccessTokenAsync(context, CancellationToken.None);

        Assert.Equal("rotated-access-token", accessToken);
        Assert.Equal(StubAuthClient.RefreshToken, auth.RefreshedRefreshToken);
        Assert.Equal("rotated-refresh-token", authentication.CurrentProperties?.GetTokenValue("legacy_refresh_token"));
        Assert.Equal("rotated-access-token", authentication.CurrentProperties?.GetTokenValue("legacy_access_token"));
        Assert.False(authentication.SignedOut);
    }

    [Fact]
    public async Task GetAccessToken_WhenRefreshFails_ClearsLocalSession()
    {
        var auth = new StubAuthClient();
        var authentication = new RecordingAuthenticationService(CreateTokenProperties(Now.AddMinutes(1)));
        var context = CreateHttpContext(authentication);
        var sessions = CreateSessionService(auth);

        var accessToken = await sessions.GetAccessTokenAsync(context, CancellationToken.None);

        Assert.Null(accessToken);
        Assert.True(authentication.SignedOut);
    }

    [Fact]
    public async Task SignOut_WhenRevokeIsUnavailable_StillClearsLocalSession()
    {
        var auth = new StubAuthClient { RevokeException = new HttpRequestException("unavailable") };
        var authentication = new RecordingAuthenticationService(CreateTokenProperties(Now.AddMinutes(10)));
        var context = CreateHttpContext(authentication);
        var sessions = CreateSessionService(auth);

        await sessions.SignOutAsync(context, CancellationToken.None);

        Assert.Equal(StubAuthClient.RefreshToken, auth.RevokedRefreshToken);
        Assert.True(authentication.SignedOut);
    }

    private static AuthenticationProperties CreateTokenProperties(DateTimeOffset accessExpiresAt)
    {
        var properties = new AuthenticationProperties
        {
            IssuedUtc = Now,
            ExpiresUtc = Now.AddHours(8),
        };
        properties.StoreTokens(
        [
            new AuthenticationToken { Name = "legacy_access_token", Value = StubAuthClient.AccessToken },
            new AuthenticationToken { Name = "legacy_refresh_token", Value = StubAuthClient.RefreshToken },
            new AuthenticationToken { Name = "legacy_access_expires_at", Value = accessExpiresAt.ToString("O") },
        ]);
        return properties;
    }

    private static DefaultHttpContext CreateHttpContext(IAuthenticationService authentication)
    {
        var services = new ServiceCollection()
            .AddSingleton(authentication)
            .BuildServiceProvider();
        return new DefaultHttpContext { RequestServices = services };
    }

    private static EmployeeSessionService CreateSessionService(ILegacyAuthClient authClient) =>
        new(authClient, new FakeTimeProvider(Now), NullLogger<EmployeeSessionService>.Instance);

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

        public AuthTokenResponse? RefreshResult { get; init; }

        public Exception? RevokeException { get; init; }

        public string? RefreshedRefreshToken { get; private set; }

        public string? RevokedRefreshToken { get; private set; }

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

        public Task<AuthTokenResponse?> RefreshAsync(string refreshToken, CancellationToken cancellationToken)
        {
            RefreshedRefreshToken = refreshToken;
            return Task.FromResult(RefreshResult);
        }

        public Task RevokeAsync(string refreshToken, CancellationToken cancellationToken)
        {
            RevokedRefreshToken = refreshToken;
            return RevokeException is null ? Task.CompletedTask : Task.FromException(RevokeException);
        }

        public Task<CustomerIdentityResponse?> CreateCustomerIdentityAsync(
            int databaseId,
            CreateCustomerIdentityRequest request,
            string accessToken,
            CancellationToken cancellationToken) => Task.FromResult<CustomerIdentityResponse?>(null);

        public Task<EmployeeIdentityResponse?> CreateEmployeeIdentityAsync(
            int databaseId,
            CreateEmployeeIdentityRequest request,
            string accessToken,
            CancellationToken cancellationToken) => Task.FromResult<EmployeeIdentityResponse?>(null);
    }

    private sealed class RecordingAuthenticationService : IAuthenticationService
    {
        private readonly ClaimsPrincipal principal = new(new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, "employee-id")],
            CookieAuthenticationDefaults.AuthenticationScheme));

        public RecordingAuthenticationService(AuthenticationProperties properties)
        {
            CurrentProperties = properties;
        }

        public AuthenticationProperties? CurrentProperties { get; private set; }

        public bool SignedOut { get; private set; }

        public Task<AuthenticateResult> AuthenticateAsync(HttpContext context, string? scheme)
        {
            if (SignedOut || CurrentProperties is null)
            {
                return Task.FromResult(AuthenticateResult.NoResult());
            }

            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(
                principal,
                CurrentProperties,
                CookieAuthenticationDefaults.AuthenticationScheme)));
        }

        public Task ChallengeAsync(
            HttpContext context,
            string? scheme,
            AuthenticationProperties? properties) => Task.CompletedTask;

        public Task ForbidAsync(
            HttpContext context,
            string? scheme,
            AuthenticationProperties? properties) => Task.CompletedTask;

        public Task SignInAsync(
            HttpContext context,
            string? scheme,
            ClaimsPrincipal signedInPrincipal,
            AuthenticationProperties? properties)
        {
            CurrentProperties = properties;
            SignedOut = false;
            return Task.CompletedTask;
        }

        public Task SignOutAsync(
            HttpContext context,
            string? scheme,
            AuthenticationProperties? properties)
        {
            SignedOut = true;
            CurrentProperties = null;
            return Task.CompletedTask;
        }
    }
}
