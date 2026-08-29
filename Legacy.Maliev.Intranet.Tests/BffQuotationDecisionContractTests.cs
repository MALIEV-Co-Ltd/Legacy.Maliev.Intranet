extern alias Bff;

using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Legacy.Maliev.Intranet.Auth;
using Legacy.Maliev.Intranet.Contracts;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using BffProgram = Bff::Program;
using QuotationDecisionProxy = Bff::Legacy.Maliev.Intranet.Bff.Quotations.QuotationDecisionProxy;

namespace Legacy.Maliev.Intranet.Tests;

public sealed class BffQuotationDecisionContractTests
{
    [Fact]
    public async Task Decision_RequiresExactPermissionAndAntiforgeryBeforeDownstream()
    {
        var downstream = new DecisionHandler();
        await using var missingPermissionFactory = new Factory(downstream, []);
        using var missingPermissionClient = CreateClient(missingPermissionFactory);
        var missingPermissionCsrf = await SignInAsync(missingPermissionClient);
        using var forbidden = await SendAsync(missingPermissionClient, missingPermissionCsrf);
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
        Assert.Equal(0, downstream.CallCount);

        await using var permittedFactory = new Factory(downstream, [LegacyEmployeePermissions.QuotationsUpdate]);
        using var permittedClient = CreateClient(permittedFactory);
        await SignInAsync(permittedClient);
        using var missingCsrf = await SendAsync(permittedClient, csrf: null);
        Assert.Equal(HttpStatusCode.BadRequest, missingCsrf.StatusCode);
        Assert.Equal(0, downstream.CallCount);
    }

    [Fact]
    public async Task Decision_ForwardsOnlyServerOwnedEmployeeDecisionAndConcurrencyToken()
    {
        var downstream = new DecisionHandler();
        await using var factory = new Factory(downstream, [LegacyEmployeePermissions.QuotationsUpdate]);
        using var client = CreateClient(factory);
        var csrf = await SignInAsync(client);

        using var response = await SendAsync(client, csrf);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(1, downstream.CallCount);
        Assert.Equal("PUT", downstream.Method);
        Assert.Equal("/quotations/84/decision", downstream.PathAndQuery);
        Assert.Equal("Bearer signed-service-token", downstream.Authorization);
        Assert.Equal("2030-07-18T08:30:00.0000000Z", downstream.ExpectedModifiedDate);
        using var forwarded = JsonDocument.Parse(Assert.IsType<string>(downstream.RequestBody));
        Assert.Equal(["accepted", "employeeInitiated"], forwarded.RootElement.EnumerateObject().Select(value => value.Name).Order(StringComparer.Ordinal));
        Assert.True(forwarded.RootElement.GetProperty("accepted").GetBoolean());
        Assert.True(forwarded.RootElement.GetProperty("employeeInitiated").GetBoolean());
        Assert.DoesNotContain("browser-access", downstream.RequestBody, StringComparison.Ordinal);
        Assert.DoesNotContain("browser-refresh", downstream.RequestBody, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Decision_RejectsBrowserAttemptToSetEmployeeInitiated()
    {
        var downstream = new DecisionHandler();
        await using var factory = new Factory(downstream, [LegacyEmployeePermissions.QuotationsUpdate]);
        using var client = CreateClient(factory);
        var csrf = await SignInAsync(client);
        using var request = new HttpRequestMessage(HttpMethod.Put, "/bff/quotations/84/decision")
        {
            Content = JsonContent.Create(new
            {
                accepted = true,
                expectedModifiedDate = "2030-07-18T08:30:00Z",
                employeeInitiated = false,
            }),
        };
        request.Headers.Add("X-CSRF-TOKEN", csrf);

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(0, downstream.CallCount);
    }

    [Theory]
    [InlineData("{\"accepted\":true}")]
    [InlineData("{\"accepted\":true,\"expectedModifiedDate\":null}")]
    [InlineData("{\"accepted\":true,\"expectedModifiedDate\":\"not-a-date\"}")]
    public async Task Decision_RejectsMissingNullOrInvalidConcurrencyTokenBeforeDownstream(string body)
    {
        var downstream = new DecisionHandler();
        await using var factory = new Factory(downstream, [LegacyEmployeePermissions.QuotationsUpdate]);
        using var client = CreateClient(factory);
        var csrf = await SignInAsync(client);
        using var request = new HttpRequestMessage(HttpMethod.Put, "/bff/quotations/84/decision")
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };
        request.Headers.Add("X-CSRF-TOKEN", csrf);

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(0, downstream.CallCount);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, HttpStatusCode.Unauthorized, null)]
    [InlineData(HttpStatusCode.Forbidden, HttpStatusCode.Forbidden, null)]
    [InlineData(HttpStatusCode.NotFound, HttpStatusCode.NotFound, null)]
    [InlineData(HttpStatusCode.Conflict, HttpStatusCode.Conflict, "DependencyConflict")]
    [InlineData(HttpStatusCode.TooManyRequests, HttpStatusCode.TooManyRequests, null)]
    [InlineData(HttpStatusCode.BadGateway, HttpStatusCode.ServiceUnavailable, null)]
    [InlineData(HttpStatusCode.ServiceUnavailable, HttpStatusCode.ServiceUnavailable, null)]
    public async Task Decision_MapsDownstreamFailuresWithoutLeakingBodies(
        HttpStatusCode downstreamStatus,
        HttpStatusCode expectedStatus,
        string? safeDecisionStatus)
    {
        var downstream = new DecisionHandler
        {
            Status = downstreamStatus,
            Body = safeDecisionStatus is null
                ? "downstream-secret"
                : """{"status":3,"completedOrders":1,"totalOrders":2,"modifiedDate":"2030-07-18T08:31:00Z"}""",
        };
        await using var factory = new Factory(downstream, [LegacyEmployeePermissions.QuotationsUpdate]);
        using var client = CreateClient(factory);
        var csrf = await SignInAsync(client);

        using var response = await SendAsync(client, csrf);
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(expectedStatus, response.StatusCode);
        Assert.DoesNotContain("downstream-secret", body, StringComparison.Ordinal);
        if (safeDecisionStatus is not null)
        {
            using var json = JsonDocument.Parse(body);
            Assert.Equal(safeDecisionStatus, json.RootElement.GetProperty("status").GetString());
            Assert.Equal(1, json.RootElement.GetProperty("completedOrders").GetInt32());
        }
        if (downstreamStatus == HttpStatusCode.TooManyRequests)
        {
            Assert.Equal("17", response.Headers.GetValues("Retry-After").Single());
        }
    }

    [Fact]
    public async Task Decision_MalformedSuccessIsSafeBadGateway()
    {
        var downstream = new DecisionHandler { Body = "downstream-secret" };
        await using var factory = new Factory(downstream, [LegacyEmployeePermissions.QuotationsUpdate]);
        using var client = CreateClient(factory);
        var csrf = await SignInAsync(client);

        using var response = await SendAsync(client, csrf);
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
        Assert.DoesNotContain("downstream-secret", body, StringComparison.Ordinal);
    }

    private static async Task<HttpResponseMessage> SendAsync(HttpClient client, string? csrf)
    {
        var request = new HttpRequestMessage(HttpMethod.Put, "/bff/quotations/84/decision")
        {
            Content = JsonContent.Create(new QuotationDecisionInput(true, new DateTime(2030, 7, 18, 8, 30, 0, DateTimeKind.Unspecified))),
        };
        if (csrf is not null) request.Headers.Add("X-CSRF-TOKEN", csrf);
        return await client.SendAsync(request);
    }

    private static HttpClient CreateClient(WebApplicationFactory<BffProgram> factory) =>
        factory.CreateClient(new()
        {
            AllowAutoRedirect = false,
            BaseAddress = new Uri("https://localhost"),
            HandleCookies = true,
        });

    private static async Task<string> SignInAsync(HttpClient client)
    {
        using var anonymousResponse = await client.GetAsync("/bff/session");
        var anonymous = await anonymousResponse.Content.ReadFromJsonAsync<JsonElement>();
        using var login = new HttpRequestMessage(HttpMethod.Post, "/bff/login")
        {
            Content = JsonContent.Create(new { email = "employee@maliev.com", password = "password", returnUrl = "/Quotations/View?id=84" }),
        };
        login.Headers.Add("X-CSRF-TOKEN", anonymous.GetProperty("csrfToken").GetString());
        using var loginResponse = await client.SendAsync(login);
        loginResponse.EnsureSuccessStatusCode();
        using var authenticatedResponse = await client.GetAsync("/bff/session");
        var authenticated = await authenticatedResponse.Content.ReadFromJsonAsync<JsonElement>();
        return authenticated.GetProperty("csrfToken").GetString() ?? throw new InvalidOperationException("Missing CSRF token.");
    }

    private sealed class Factory(DecisionHandler downstream, IReadOnlyList<string> permissions) : WebApplicationFactory<BffProgram>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing");
            TestJwtConfiguration.Configure(builder);
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<ILegacyAuthClient>();
                services.AddSingleton<ILegacyAuthClient>(new AuthClient(permissions));
                services.RemoveAll<IServiceAccessTokenProvider>();
                var tokenProvider = new TokenProvider();
                services.AddSingleton<IServiceAccessTokenProvider>(tokenProvider);
                services.RemoveAll<QuotationDecisionProxy>();
                var auth = new LegacyServiceAuthenticationHandler(tokenProvider) { InnerHandler = downstream };
                services.AddSingleton(new QuotationDecisionProxy(new HttpClient(auth, disposeHandler: false)
                {
                    BaseAddress = new Uri("http://quotation/"),
                    Timeout = TimeSpan.FromSeconds(10),
                }));
            });
        }
    }

    private sealed class TokenProvider : IServiceAccessTokenProvider
    {
        public ValueTask<string?> GetAccessTokenAsync(CancellationToken cancellationToken) => ValueTask.FromResult<string?>("signed-service-token");
        public void Invalidate(string token) { }
    }

    private sealed class AuthClient(IReadOnlyList<string> permissions) : ILegacyAuthClient
    {
        public Task<EmployeeLoginResult> LoginAsync(string email, string password, CancellationToken cancellationToken) =>
            Task.FromResult(new EmployeeLoginResult(true, new("browser-access", "browser-refresh", "Bearer", 900, DateTimeOffset.UtcNow.AddDays(1)), new("employee", email, email, permissions, 7)));
        public Task<EmployeeRefreshResult?> RefreshAsync(string refreshToken, CancellationToken cancellationToken) => Task.FromResult<EmployeeRefreshResult?>(null);
        public Task RevokeAsync(string refreshToken, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<CustomerIdentityResponse?> CreateCustomerIdentityAsync(int databaseId, CreateCustomerIdentityRequest request, string accessToken, CancellationToken cancellationToken) => Task.FromResult<CustomerIdentityResponse?>(null);
        public Task<EmployeeIdentityResponse?> CreateEmployeeIdentityAsync(int databaseId, CreateEmployeeIdentityRequest request, string accessToken, CancellationToken cancellationToken) => Task.FromResult<EmployeeIdentityResponse?>(null);
    }

    private sealed class DecisionHandler : HttpMessageHandler
    {
        public int CallCount { get; private set; }
        public string? Method { get; private set; }
        public string? PathAndQuery { get; private set; }
        public string? Authorization { get; private set; }
        public string? ExpectedModifiedDate { get; private set; }
        public string? Body { get; set; } = """{"status":0,"completedOrders":2,"totalOrders":2,"modifiedDate":"2030-07-18T08:31:00Z"}""";
        public HttpStatusCode Status { get; set; } = HttpStatusCode.OK;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            CallCount++;
            Method = request.Method.Method;
            PathAndQuery = request.RequestUri?.PathAndQuery;
            Authorization = request.Headers.Authorization?.ToString();
            ExpectedModifiedDate = request.Headers.TryGetValues("X-Expected-Modified-Date", out var values) ? values.Single() : null;
            RequestBody = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            var response = new HttpResponseMessage(Status)
            {
                Content = new StringContent(Body ?? string.Empty, Encoding.UTF8, "application/json"),
            };
            if (Status == HttpStatusCode.TooManyRequests) response.Headers.RetryAfter = new(TimeSpan.FromSeconds(17));
            return response;
        }

        public string? RequestBody { get; private set; }
    }
}
