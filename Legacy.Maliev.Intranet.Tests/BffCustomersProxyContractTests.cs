extern alias Bff;

using System.Net;
using System.Net.Http.Json;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Legacy.Maliev.Intranet.Auth;
using Maliev.Aspire.ServiceDefaults;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.IdentityModel.Tokens;
using BffProgram = Bff::Program;
using CustomersProxy = Bff::Legacy.Maliev.Intranet.Bff.Customers.CustomersProxy;

namespace Legacy.Maliev.Intranet.Tests;

public sealed class BffCustomersProxyContractTests
{
    [Fact]
    public async Task AuthorizedEmployee_ForwardsExactQueryAndServerOnlyBearerToken()
    {
        var downstream = new RecordingCustomerHandler(HttpStatusCode.OK, CustomerPageJson);
        await using var factory = new CustomersBffFactory(downstream, hasPermission: true);
        using var client = CreateClient(factory);
        await SignInAsync(client);

        using var response = await client.GetAsync(
            "/bff/customers?sort=CustomerEmail_Ascending&search=ada%20lovelace&index=2&size=25");
        var json = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("/customers?sort=CustomerEmail_Ascending&search=ada%20lovelace&index=2&size=25", downstream.PathAndQuery);
        Assert.Equal("Bearer signed-service-token", downstream.Authorization);
        Assert.Contains("\"pageIndex\":2", json, StringComparison.Ordinal);
        Assert.Contains("\"fullName\":\"Ada Lovelace\"", json, StringComparison.Ordinal);
        Assert.DoesNotContain("server-only-access-token", json, StringComparison.Ordinal);
    }

    [Fact]
    public async Task EmployeeWithoutExactPermission_IsForbiddenBeforeCustomerServiceCall()
    {
        var downstream = new RecordingCustomerHandler(HttpStatusCode.OK, CustomerPageJson);
        await using var factory = new CustomersBffFactory(downstream, hasPermission: false);
        using var client = CreateClient(factory);
        await SignInAsync(client);

        using var response = await client.GetAsync("/bff/customers");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Null(downstream.PathAndQuery);
    }

    [Fact]
    public async Task AnonymousRequest_IsUnauthorizedBeforeCustomerServiceCall()
    {
        var downstream = new RecordingCustomerHandler(HttpStatusCode.OK, CustomerPageJson);
        await using var factory = new CustomersBffFactory(downstream, hasPermission: true);
        using var client = CreateClient(factory);

        using var response = await client.GetAsync("/bff/customers");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Null(downstream.PathAndQuery);
    }

    [Fact]
    public async Task InvalidPaging_IsClampedAndNotFoundBecomesAnEmptyPage()
    {
        var downstream = new RecordingCustomerHandler(HttpStatusCode.NotFound, "{}");
        await using var factory = new CustomersBffFactory(downstream, hasPermission: true);
        using var client = CreateClient(factory);
        await SignInAsync(client);

        using var response = await client.GetAsync("/bff/customers?index=-5&size=999");
        var json = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("/customers?sort=CustomerCreatedDate_Descending&search=&index=1&size=250", downstream.PathAndQuery);
        Assert.Contains("\"items\":[]", json, StringComparison.Ordinal);
        Assert.Contains("\"pageIndex\":1", json, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    public async Task DownstreamAuthorizationFailure_IsPreserved(HttpStatusCode statusCode)
    {
        var downstream = new RecordingCustomerHandler(statusCode, "{}");
        await using var factory = new CustomersBffFactory(downstream, hasPermission: true);
        using var client = CreateClient(factory);
        await SignInAsync(client);

        using var response = await client.GetAsync("/bff/customers");

        Assert.Equal(statusCode, response.StatusCode);
    }

    [Fact]
    public async Task RateLimit_PreservesStatusAndBoundedRetryAfterWithoutRetry()
    {
        var downstream = new RecordingCustomerHandler(HttpStatusCode.TooManyRequests, "{}", retryAfterSeconds: 1);
        await using var factory = new CustomersBffFactory(downstream, hasPermission: true);
        using var client = CreateClient(factory);
        await SignInAsync(client);

        using var response = await client.GetAsync("/bff/customers");

        Assert.Equal(1, downstream.RequestCount);
        Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode);
        Assert.Equal(TimeSpan.FromSeconds(1), response.Headers.RetryAfter?.Delta);
    }

    [Fact]
    public async Task InvalidPayload_IsMappedToBadGatewayWithoutLeakingPayload()
    {
        var downstream = new RecordingCustomerHandler(HttpStatusCode.OK, "not-json");
        await using var factory = new CustomersBffFactory(downstream, hasPermission: true);
        using var client = CreateClient(factory);
        await SignInAsync(client);

        using var response = await client.GetAsync("/bff/customers");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
        Assert.DoesNotContain("not-json", body, StringComparison.Ordinal);
    }

    [Theory]
    [MemberData(nameof(TransportFailures))]
    public async Task TransportFailure_IsMappedToServiceUnavailable(Exception exception)
    {
        var downstream = new RecordingCustomerHandler(HttpStatusCode.OK, "{}", exception: exception);
        await using var factory = new CustomersBffFactory(downstream, hasPermission: true);
        using var client = CreateClient(factory);
        await SignInAsync(client);

        using var response = await client.GetAsync("/bff/customers");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
    }

    [Fact]
    public async Task AuthorizedCookie_WithSignedCustomerListServiceToken_PassesCustomerPermissionPipeline()
    {
        using var signingKey = RSA.Create(2048);
        await using var customer = await StartCustomerPermissionPipelineAsync(signingKey);
        var serviceToken = CreateSignedToken(signingKey, includeCustomerListPermission: true);
        await using var factory = new CustomersBffFactory(
            customer.GetTestServer().CreateHandler(),
            hasPermission: true,
            serviceToken);
        using var client = CreateClient(factory);
        await SignInAsync(client);

        using var response = await client.GetAsync("/bff/customers");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task CallerCancellation_IsNotTranslatedIntoAServiceUnavailableResponse()
    {
        await using var factory = new CustomersBffFactory(new CallerCancellationHandler(), hasPermission: true);
        using var client = CreateClient(factory);
        await SignInAsync(client);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(250));

        var exception = await Record.ExceptionAsync(() =>
            client.GetAsync("/bff/customers", cancellation.Token));

        Assert.IsAssignableFrom<OperationCanceledException>(exception);
        Assert.True(cancellation.IsCancellationRequested);
    }

    public static TheoryData<Exception> TransportFailures => new()
    {
        new HttpRequestException("customer unavailable"),
        new TaskCanceledException("customer timeout"),
    };

    private static HttpClient CreateClient(WebApplicationFactory<BffProgram> factory) =>
        factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            BaseAddress = new Uri("https://localhost"),
            HandleCookies = true,
        });

    private static async Task SignInAsync(HttpClient client)
    {
        using var sessionResponse = await client.GetAsync("/bff/session");
        var session = await sessionResponse.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        var csrf = session.GetProperty("csrfToken").GetString();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/bff/login")
        {
            Content = JsonContent.Create(new
            {
                email = "employee@maliev.com",
                password = "password",
                returnUrl = "/Customers/Index",
            }),
        };
        request.Headers.Add("X-CSRF-TOKEN", csrf);
        using var response = await client.SendAsync(request);
        response.EnsureSuccessStatusCode();
    }

    private sealed class CustomersBffFactory(
        HttpMessageHandler downstream,
        bool hasPermission,
        string serviceToken = "signed-service-token")
        : WebApplicationFactory<BffProgram>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing");
            TestJwtConfiguration.Configure(builder);
            builder.UseSetting("Services:Auth", "http://auth/");
            builder.UseSetting("Services:Catalog", "http://catalog/");
            builder.UseSetting("Services:Customer", "http://customer/");
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<ILegacyAuthClient>();
                services.AddSingleton<ILegacyAuthClient>(new CustomersAuthClient(hasPermission));
                services.RemoveAll<IServiceAccessTokenProvider>();
                services.AddSingleton<IServiceAccessTokenProvider>(new CustomersServiceTokenProvider(serviceToken));
                services.AddHttpClient<CustomersProxy>()
                    .ConfigurePrimaryHttpMessageHandler(() => downstream);
            });
        }
    }

    private sealed class CustomersServiceTokenProvider(string serviceToken) : IServiceAccessTokenProvider
    {
        public ValueTask<string?> GetAccessTokenAsync(CancellationToken cancellationToken) =>
            ValueTask.FromResult<string?>(serviceToken);

        public void Invalidate(string token)
        {
        }
    }

    private sealed class CustomersAuthClient(bool hasPermission) : ILegacyAuthClient
    {
        public Task<EmployeeLoginResult> LoginAsync(string email, string password, CancellationToken cancellationToken) =>
            Task.FromResult(new EmployeeLoginResult(
                true,
                new AuthTokenResponse("server-only-access-token", "server-only-refresh-token", "Bearer", 900, DateTimeOffset.UtcNow.AddDays(1)),
                new EmployeeIdentity(
                    "employee-id",
                    email,
                    email,
                    hasPermission ? ["legacy-customer.customers.list"] : [])));

        public Task<EmployeeRefreshResult?> RefreshAsync(string refreshToken, CancellationToken cancellationToken) => Task.FromResult<EmployeeRefreshResult?>(null);
        public Task RevokeAsync(string refreshToken, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<CustomerIdentityResponse?> CreateCustomerIdentityAsync(int databaseId, CreateCustomerIdentityRequest request, string accessToken, CancellationToken cancellationToken) => Task.FromResult<CustomerIdentityResponse?>(null);
        public Task<EmployeeIdentityResponse?> CreateEmployeeIdentityAsync(int databaseId, CreateEmployeeIdentityRequest request, string accessToken, CancellationToken cancellationToken) => Task.FromResult<EmployeeIdentityResponse?>(null);
    }

    private sealed class RecordingCustomerHandler(
        HttpStatusCode statusCode,
        string body,
        int? retryAfterSeconds = null,
        Exception? exception = null) : HttpMessageHandler
    {
        public string? PathAndQuery { get; private set; }
        public string? Authorization { get; private set; }
        public int RequestCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestCount++;
            PathAndQuery = request.RequestUri?.PathAndQuery;
            Authorization = request.Headers.Authorization?.ToString();
            if (exception is not null)
            {
                throw exception;
            }

            var response = new HttpResponseMessage(statusCode)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json"),
            };
            if (retryAfterSeconds is not null)
            {
                response.Headers.RetryAfter = new(TimeSpan.FromSeconds(retryAfterSeconds.Value));
            }

            return Task.FromResult(response);
        }
    }

    private sealed class CallerCancellationHandler : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            throw new InvalidOperationException("The caller cancellation should stop the request.");
        }
    }

    private const string CustomerPageJson =
        """{"Items":[{"Id":42,"FirstName":"Ada","LastName":"Lovelace","FullName":"Ada Lovelace","Email":"ada@example.com","Company":{"Id":7,"Name":"Analytical Engines Ltd"}}],"PageIndex":2,"TotalPages":4,"TotalRecords":75,"HasNextPage":true,"HasPreviousPage":true}""";

    private static async Task<WebApplication> StartCustomerPermissionPipelineAsync(RSA signingKey)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = "Production",
        });
        builder.WebHost.UseTestServer();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Jwt:Issuer"] = "https://auth.test",
            ["Jwt:Audience"] = "legacy-test",
            ["Jwt:PublicKey"] = Convert.ToBase64String(Encoding.UTF8.GetBytes(signingKey.ExportSubjectPublicKeyInfoPem())),
        });
        builder.AddJwtAuthentication();
        var app = builder.Build();
        app.UseAuthentication();
        app.UseAuthorization();
        app.MapGet("/customers", () => Results.Text(CustomerPageJson, "application/json"))
            .RequireAuthorization($"Permission:{LegacyEmployeePermissions.CustomersList}");
        await app.StartAsync();
        return app;
    }

    private static string CreateSignedToken(RSA signingKey, bool includeCustomerListPermission)
    {
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, "legacy-intranet"),
            new("identity_kind", "service"),
        };
        if (includeCustomerListPermission)
        {
            claims.Add(new Claim("permissions", LegacyEmployeePermissions.CustomersList));
        }

        var key = new RsaSecurityKey(signingKey) { KeyId = "customer-contract-key" };
        var token = new JwtSecurityToken(
            "https://auth.test",
            "legacy-test",
            claims,
            DateTime.UtcNow.AddMinutes(-1),
            DateTime.UtcNow.AddMinutes(10),
            new SigningCredentials(key, SecurityAlgorithms.RsaSha256));
        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
