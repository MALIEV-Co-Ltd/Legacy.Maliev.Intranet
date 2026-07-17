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

public sealed class BffEmployeeAuthenticationContractTests
{
    private static readonly DateTimeOffset Now = new(2030, 7, 15, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Login_ValidEmployee_CreatesOpaqueSessionAndReturnsLocalRedirect()
    {
        var auth = new StubAuthClient();
        await using var factory = new EmployeeBffFactory(auth);
        using var client = CreateClient(factory);
        var csrfToken = await GetCsrfTokenAsync(client);

        using var request = CreateLoginRequest(
            csrfToken,
            "employee@maliev.com",
            "correct-password",
            "/Dashboard");
        using var response = await client.SendAsync(request);
        var responseJson = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var payload = JsonDocument.Parse(responseJson);
        Assert.Equal("/Dashboard", payload.RootElement.GetProperty("redirectUrl").GetString());
        Assert.DoesNotContain(StubAuthClient.AccessToken, responseJson, StringComparison.Ordinal);
        Assert.DoesNotContain(StubAuthClient.RefreshToken, responseJson, StringComparison.Ordinal);
        Assert.DoesNotContain("correct-password", responseJson, StringComparison.Ordinal);
        var cookie = Assert.Single(
            response.Headers.GetValues("Set-Cookie"),
            value => value.StartsWith("__Host-Legacy.Maliev.Intranet.Bff=", StringComparison.Ordinal));
        Assert.Contains("secure", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("httponly", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=lax", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(StubAuthClient.AccessToken, cookie, StringComparison.Ordinal);
        Assert.DoesNotContain(StubAuthClient.RefreshToken, cookie, StringComparison.Ordinal);
        Assert.DoesNotContain("correct-password", cookie, StringComparison.Ordinal);
        Assert.Equal("employee@maliev.com", auth.Email);
        Assert.Equal("correct-password", auth.Password);

        using var sessionResponse = await client.GetAsync("/bff/session");
        using var session = JsonDocument.Parse(await sessionResponse.Content.ReadAsStringAsync());
        Assert.True(session.RootElement.GetProperty("isAuthenticated").GetBoolean());
        Assert.Equal("employee-id", session.RootElement.GetProperty("employeeId").GetString());
        Assert.Equal("employee@maliev.com", session.RootElement.GetProperty("displayName").GetString());
    }

    [Fact]
    public async Task Login_MissingCsrfToken_IsRejectedBeforeCredentialsReachAuthService()
    {
        var auth = new StubAuthClient();
        await using var factory = new EmployeeBffFactory(auth);
        using var client = CreateClient(factory);
        using var response = await client.PostAsJsonAsync(
            "/bff/login",
            new { email = "employee@maliev.com", password = "correct-password", returnUrl = "/Dashboard" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Null(auth.Email);
        Assert.Null(auth.Password);
    }

    [Theory]
    [InlineData("https://attacker.example/steal")]
    [InlineData("//attacker.example/steal")]
    public async Task Login_ExternalReturnUrl_FallsBackToDashboard(string returnUrl)
    {
        var auth = new StubAuthClient();
        await using var factory = new EmployeeBffFactory(auth);
        using var client = CreateClient(factory);
        var csrfToken = await GetCsrfTokenAsync(client);
        using var request = CreateLoginRequest(
            csrfToken,
            "employee@maliev.com",
            "correct-password",
            returnUrl);

        using var response = await client.SendAsync(request);
        using var payload = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("/Dashboard", payload.RootElement.GetProperty("redirectUrl").GetString());
    }

    [Fact]
    public async Task Login_InvalidCredentials_ReturnsGenericUnauthorizedResponse()
    {
        var auth = new StubAuthClient { LoginSucceeds = false };
        await using var factory = new EmployeeBffFactory(auth);
        using var client = CreateClient(factory);
        var csrfToken = await GetCsrfTokenAsync(client);
        using var request = CreateLoginRequest(
            csrfToken,
            "missing@maliev.com",
            "wrong-password",
            "/Dashboard");

        using var response = await client.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Contains("The email or password is invalid.", body, StringComparison.Ordinal);
        Assert.DoesNotContain("missing@maliev.com", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("wrong-password", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Login_InvalidForm_IsRejectedBeforeCredentialsReachAuthService()
    {
        var auth = new StubAuthClient();
        await using var factory = new EmployeeBffFactory(auth);
        using var client = CreateClient(factory);
        var csrfToken = await GetCsrfTokenAsync(client);
        using var request = CreateLoginRequest(csrfToken, "not-an-email", string.Empty, "/Dashboard");

        using var response = await client.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("Email", body, StringComparison.Ordinal);
        Assert.Contains("Password", body, StringComparison.Ordinal);
        Assert.Null(auth.Email);
        Assert.Null(auth.Password);
    }

    [Fact]
    public async Task Login_WhenAuthServiceIsUnavailable_ReturnsBoundedGenericFailure()
    {
        var auth = new StubAuthClient { LoginException = new HttpRequestException("sensitive downstream detail") };
        await using var factory = new EmployeeBffFactory(auth);
        using var client = CreateClient(factory);
        var csrfToken = await GetCsrfTokenAsync(client);
        using var request = CreateLoginRequest(
            csrfToken,
            "employee@maliev.com",
            "correct-password",
            "/Dashboard");

        using var response = await client.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Contains("Employee sign-in is temporarily unavailable.", body, StringComparison.Ordinal);
        Assert.DoesNotContain("sensitive downstream detail", body, StringComparison.Ordinal);
        Assert.DoesNotContain("correct-password", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Login_ExceedsBoundedAttemptWindow_ReturnsTooManyRequests()
    {
        var auth = new StubAuthClient { LoginSucceeds = false };
        await using var factory = new EmployeeBffFactory(auth);
        using var client = CreateClient(factory);
        var csrfToken = await GetCsrfTokenAsync(client);

        for (var attempt = 0; attempt < 10; attempt++)
        {
            using var request = CreateLoginRequest(
                csrfToken,
                "employee@maliev.com",
                "wrong-password",
                "/Dashboard");
            using var response = await client.SendAsync(request);
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }

        using var rejectedRequest = CreateLoginRequest(
            csrfToken,
            "employee@maliev.com",
            "wrong-password",
            "/Dashboard");
        using var rejectedResponse = await client.SendAsync(rejectedRequest);

        Assert.Equal(HttpStatusCode.TooManyRequests, rejectedResponse.StatusCode);
        Assert.Equal(10, auth.LoginAttempts);
    }

    [Fact]
    public async Task Login_WhenAuthServiceRateLimits_PreservesBoundedRetryAfter()
    {
        var auth = new StubAuthClient { LoginException = new LegacyAuthRateLimitedException(75) };
        await using var factory = new EmployeeBffFactory(auth);
        using var client = CreateClient(factory);
        var csrfToken = await GetCsrfTokenAsync(client);
        using var request = CreateLoginRequest(
            csrfToken,
            "employee@maliev.com",
            "correct-password",
            "/Dashboard");

        using var response = await client.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode);
        Assert.Equal(TimeSpan.FromSeconds(75), response.Headers.RetryAfter?.Delta);
        Assert.Contains("Too many sign-in attempts. Wait and try again.", body, StringComparison.Ordinal);
        Assert.DoesNotContain("correct-password", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Logout_ValidCsrf_RevokesRefreshFamilyAndAlwaysClearsSession()
    {
        var auth = new StubAuthClient();
        await using var factory = new EmployeeBffFactory(auth);
        using var client = CreateClient(factory);
        var anonymousCsrf = await GetCsrfTokenAsync(client);
        using var loginRequest = CreateLoginRequest(
            anonymousCsrf,
            "employee@maliev.com",
            "correct-password",
            "/Dashboard");
        using var loginResponse = await client.SendAsync(loginRequest);
        Assert.Equal(HttpStatusCode.OK, loginResponse.StatusCode);
        var authenticatedCsrf = await GetCsrfTokenAsync(client);

        using var logoutRequest = new HttpRequestMessage(HttpMethod.Post, "/bff/logout");
        logoutRequest.Headers.Add("X-CSRF-TOKEN", authenticatedCsrf);
        using var logoutResponse = await client.SendAsync(logoutRequest);

        Assert.Equal(HttpStatusCode.NoContent, logoutResponse.StatusCode);
        Assert.Equal(StubAuthClient.RefreshToken, auth.RevokedRefreshToken);
        using var sessionResponse = await client.GetAsync("/bff/session");
        using var session = JsonDocument.Parse(await sessionResponse.Content.ReadAsStringAsync());
        Assert.False(session.RootElement.GetProperty("isAuthenticated").GetBoolean());
    }

    [Fact]
    public async Task Logout_WhenRevokeIsUnavailable_StillClearsSession()
    {
        var auth = new StubAuthClient { RevokeException = new HttpRequestException("unavailable") };
        await using var factory = new EmployeeBffFactory(auth);
        using var client = CreateClient(factory);
        var anonymousCsrf = await GetCsrfTokenAsync(client);
        using var loginRequest = CreateLoginRequest(
            anonymousCsrf,
            "employee@maliev.com",
            "correct-password",
            "/Dashboard");
        using var loginResponse = await client.SendAsync(loginRequest);
        Assert.Equal(HttpStatusCode.OK, loginResponse.StatusCode);
        var authenticatedCsrf = await GetCsrfTokenAsync(client);
        using var logoutRequest = new HttpRequestMessage(HttpMethod.Post, "/bff/logout");
        logoutRequest.Headers.Add("X-CSRF-TOKEN", authenticatedCsrf);

        using var logoutResponse = await client.SendAsync(logoutRequest);

        Assert.Equal(HttpStatusCode.NoContent, logoutResponse.StatusCode);
        using var sessionResponse = await client.GetAsync("/bff/session");
        using var session = JsonDocument.Parse(await sessionResponse.Content.ReadAsStringAsync());
        Assert.False(session.RootElement.GetProperty("isAuthenticated").GetBoolean());
    }

    [Fact]
    public async Task Logout_AnonymousRequestWithValidCsrf_IsUnauthorizedWithoutCallingRevoke()
    {
        var auth = new StubAuthClient();
        await using var factory = new EmployeeBffFactory(auth);
        using var client = CreateClient(factory);
        var csrfToken = await GetCsrfTokenAsync(client);
        using var request = new HttpRequestMessage(HttpMethod.Post, "/bff/logout");
        request.Headers.Add("X-CSRF-TOKEN", csrfToken);

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Null(auth.RevokedRefreshToken);
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
        string? returnUrl)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/bff/login")
        {
            Content = JsonContent.Create(new { email, password, returnUrl }),
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
                services.AddSingleton<TimeProvider>(new Microsoft.Extensions.Time.Testing.FakeTimeProvider(Now));
            });
        }
    }

    private sealed class StubAuthClient : ILegacyAuthClient
    {
        public const string AccessToken = "server-only-access-token";
        public const string RefreshToken = "server-only-refresh-token";

        public bool LoginSucceeds { get; init; } = true;

        public Exception? LoginException { get; init; }

        public Exception? RevokeException { get; init; }

        public int LoginAttempts { get; private set; }

        public string? Email { get; private set; }

        public string? Password { get; private set; }

        public string? RevokedRefreshToken { get; private set; }

        public Task<EmployeeLoginResult> LoginAsync(
            string email,
            string password,
            CancellationToken cancellationToken)
        {
            LoginAttempts++;
            Email = email;
            Password = password;
            if (LoginException is not null)
            {
                return Task.FromException<EmployeeLoginResult>(LoginException);
            }

            return Task.FromResult(LoginSucceeds
                ? new EmployeeLoginResult(
                    true,
                    new AuthTokenResponse(AccessToken, RefreshToken, "Bearer", 900, Now.AddDays(14)),
                    new EmployeeIdentity("employee-id", email, email))
                : new EmployeeLoginResult(false, null, null));
        }

        public Task<EmployeeRefreshResult?> RefreshAsync(string refreshToken, CancellationToken cancellationToken) =>
            Task.FromResult<EmployeeRefreshResult?>(null);

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
}
