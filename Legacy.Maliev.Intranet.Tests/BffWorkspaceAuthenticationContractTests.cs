extern alias Bff;

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Legacy.Maliev.Intranet.Auth;
using Legacy.Maliev.Intranet.Contracts;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using BffProgram = Bff::Program;

namespace Legacy.Maliev.Intranet.Tests;

public sealed class BffWorkspaceAuthenticationContractTests
{
    [Fact]
    public async Task Login_NonWorkspaceEmail_IsRejectedBeforeAuthServiceReceivesCredentials()
    {
        var auth = new StubAuthClient();
        await using var factory = new EmployeeBffFactory(auth);
        using var client = CreateClient(factory);
        var csrfToken = await GetCsrfTokenAsync(client);
        using var request = CreateLoginRequest(
            csrfToken,
            "employee@example.com",
            "correct-password",
            "/Dashboard");

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(0, auth.LoginAttempts);
        var body = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("correct-password", body, StringComparison.Ordinal);
        Assert.DoesNotContain("employee@example.com", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Login_AuthServiceReturningNonWorkspaceIdentity_IsRejectedAndSessionIsNotCreated()
    {
        var auth = new StubAuthClient { IdentityEmail = "employee@example.net" };
        await using var factory = new EmployeeBffFactory(auth);
        using var client = CreateClient(factory);
        var csrfToken = await GetCsrfTokenAsync(client);
        using var request = CreateLoginRequest(
            csrfToken,
            "employee@maliev.com",
            "correct-password",
            "/Dashboard");

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(1, auth.LoginAttempts);
        using var sessionResponse = await client.GetAsync("/bff/session");
        using var session = JsonDocument.Parse(await sessionResponse.Content.ReadAsStringAsync());
        Assert.False(session.RootElement.GetProperty("isAuthenticated").GetBoolean());
    }

    [Fact]
    public async Task Login_RememberMe_PersistsTheOpaqueCookieWithoutExposingTokens()
    {
        var auth = new StubAuthClient();
        await using var factory = new EmployeeBffFactory(auth);
        using var client = CreateClient(factory);
        var csrfToken = await GetCsrfTokenAsync(client);
        using var request = CreateLoginRequest(
            csrfToken,
            "employee@maliev.com",
            "correct-password",
            "/Dashboard",
            rememberMe: true);

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var cookie = Assert.Single(
            response.Headers.GetValues("Set-Cookie"),
            value => value.StartsWith("__Host-Legacy.Maliev.Intranet.Bff=", StringComparison.Ordinal));
        Assert.Contains("expires=", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(StubAuthClient.AccessToken, cookie, StringComparison.Ordinal);
        Assert.DoesNotContain(StubAuthClient.RefreshToken, cookie, StringComparison.Ordinal);
    }

    private static HttpClient CreateClient(WebApplicationFactory<BffProgram> factory) =>
        factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            BaseAddress = new Uri("https://localhost"),
            HandleCookies = true,
        });

    private static HttpRequestMessage CreateLoginRequest(
        string csrfToken,
        string email,
        string password,
        string? returnUrl,
        bool rememberMe = false)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/bff/login")
        {
            Content = JsonContent.Create(new { email, password, returnUrl, rememberMe }),
        };
        request.Headers.Add("X-CSRF-TOKEN", csrfToken);
        return request;
    }

    private static async Task<string> GetCsrfTokenAsync(HttpClient client)
    {
        using var response = await client.GetAsync("/bff/session");
        response.EnsureSuccessStatusCode();
        using var payload = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return payload.RootElement.GetProperty("csrfToken").GetString()
            ?? throw new InvalidOperationException("The BFF did not return an antiforgery token.");
    }

    private sealed class EmployeeBffFactory(ILegacyAuthClient authClient) : WebApplicationFactory<BffProgram>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing");
            TestJwtConfiguration.Configure(builder);
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<ILegacyAuthClient>();
                services.RemoveAll<TimeProvider>();
                services.AddSingleton(authClient);
                services.AddSingleton<TimeProvider>(new Microsoft.Extensions.Time.Testing.FakeTimeProvider(
                    new DateTimeOffset(2030, 7, 15, 0, 0, 0, TimeSpan.Zero)));
            });
        }
    }

    private sealed class StubAuthClient : ILegacyAuthClient
    {
        public const string AccessToken = "server-only-access-token";
        public const string RefreshToken = "server-only-refresh-token";

        public string? IdentityEmail { get; init; }
        public int LoginAttempts { get; private set; }

        public Task<EmployeeLoginResult> LoginAsync(string email, string password, CancellationToken cancellationToken)
        {
            LoginAttempts++;
            return Task.FromResult(new EmployeeLoginResult(
                true,
                new AuthTokenResponse(
                    AccessToken,
                    RefreshToken,
                    "Bearer",
                    900,
                    new DateTimeOffset(2030, 7, 29, 0, 0, 0, TimeSpan.Zero)),
                new EmployeeIdentity("employee-id", email, IdentityEmail ?? email)));
        }

        public Task<EmployeeRefreshResult?> RefreshAsync(string refreshToken, CancellationToken cancellationToken) =>
            Task.FromResult<EmployeeRefreshResult?>(null);

        public Task RevokeAsync(string refreshToken, CancellationToken cancellationToken) => Task.CompletedTask;

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
}
