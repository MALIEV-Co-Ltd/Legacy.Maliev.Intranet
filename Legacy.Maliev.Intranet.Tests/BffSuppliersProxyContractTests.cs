extern alias Bff;

using System.Net;
using System.Net.Http.Json;
using System.Text;
using Legacy.Maliev.Intranet.Auth;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using BffProgram = Bff::Program;

namespace Legacy.Maliev.Intranet.Tests;

public sealed class BffSuppliersProxyContractTests
{
    [Fact]
    public async Task AuthorizedEmployee_ForwardsExactQueryAndServerOnlyToken()
    {
        var downstream = new RecordingSupplierHandler(SupplierPageJson);
        await using var factory = new SuppliersBffFactory(downstream, suppliersRead: true);
        using var client = CreateClient(factory);
        await SignInAsync(client);

        using var response = await client.GetAsync("/bff/suppliers?sort=SupplierName_Ascending&search=Thai%20supplier&index=2&size=25");
        var json = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("/Suppliers?sort=SupplierName_Ascending&search=Thai%20supplier&index=2&size=25", downstream.PathAndQuery);
        Assert.Equal("Bearer signed-service-token", downstream.Authorization);
        Assert.Contains("\"name\":\"Thai supplier\"", json, StringComparison.Ordinal);
        Assert.DoesNotContain("website", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("taxNumber", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("note", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("signed-service-token", json, StringComparison.Ordinal);
        Assert.DoesNotContain("server-only-access-token", json, StringComparison.Ordinal);
    }

    [Fact]
    public async Task MissingExactPermission_IsForbiddenBeforeDownstream()
    {
        var downstream = new RecordingSupplierHandler(SupplierPageJson);
        await using var factory = new SuppliersBffFactory(downstream, suppliersRead: false);
        using var client = CreateClient(factory);
        await SignInAsync(client);

        using var response = await client.GetAsync("/bff/suppliers");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Null(downstream.PathAndQuery);
    }

    [Fact]
    public async Task AnonymousRequest_IsUnauthorizedBeforeDownstream()
    {
        var downstream = new RecordingSupplierHandler(SupplierPageJson);
        await using var factory = new SuppliersBffFactory(downstream, suppliersRead: true);
        using var client = CreateClient(factory);

        using var response = await client.GetAsync("/bff/suppliers");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Null(downstream.PathAndQuery);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    public async Task DownstreamAuthorizationFailure_IsPreserved(HttpStatusCode statusCode)
    {
        var downstream = new RecordingSupplierHandler("{}") { StatusCode = statusCode };
        await using var factory = new SuppliersBffFactory(downstream, suppliersRead: true);
        using var client = CreateClient(factory);
        await SignInAsync(client);

        using var response = await client.GetAsync("/bff/suppliers");

        Assert.Equal(statusCode, response.StatusCode);
    }

    [Fact]
    public async Task NotFound_BecomesNormalizedEmptyLegacyPage()
    {
        var downstream = new RecordingSupplierHandler("{}") { StatusCode = HttpStatusCode.NotFound };
        await using var factory = new SuppliersBffFactory(downstream, suppliersRead: true);
        using var client = CreateClient(factory);
        await SignInAsync(client);

        using var response = await client.GetAsync("/bff/suppliers?index=-2&size=999");
        var json = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("/Suppliers?sort=SupplierId_Descending&search=&index=1&size=250", downstream.PathAndQuery);
        Assert.Contains("\"items\":[]", json, StringComparison.Ordinal);
        Assert.Contains("\"pageIndex\":1", json, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RateLimit_PreservesBoundedRetryAfterWithoutRetry()
    {
        var downstream = new RecordingSupplierHandler("{}") { StatusCode = HttpStatusCode.TooManyRequests, RetryAfterSeconds = 2 };
        await using var factory = new SuppliersBffFactory(downstream, suppliersRead: true);
        using var client = CreateClient(factory);
        await SignInAsync(client);

        using var response = await client.GetAsync("/bff/suppliers");

        Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode);
        Assert.Equal(TimeSpan.FromSeconds(2), response.Headers.RetryAfter?.Delta);
        Assert.Equal(1, downstream.RequestCount);
    }

    [Fact]
    public async Task InvalidPayload_IsBadGatewayWithoutPayloadLeak()
    {
        var downstream = new RecordingSupplierHandler("procurement-secret-not-json");
        await using var factory = new SuppliersBffFactory(downstream, suppliersRead: true);
        using var client = CreateClient(factory);
        await SignInAsync(client);

        using var response = await client.GetAsync("/bff/suppliers");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
        Assert.DoesNotContain("procurement-secret-not-json", body, StringComparison.Ordinal);
    }

    [Theory]
    [MemberData(nameof(TransportFailures))]
    public async Task TransportFailure_IsServiceUnavailable(Exception exception)
    {
        var downstream = new RecordingSupplierHandler("{}") { Exception = exception };
        await using var factory = new SuppliersBffFactory(downstream, suppliersRead: true);
        using var client = CreateClient(factory);
        await SignInAsync(client);

        using var response = await client.GetAsync("/bff/suppliers");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
    }

    public static TheoryData<Exception> TransportFailures => new()
    {
        new HttpRequestException("procurement unavailable"),
        new TaskCanceledException("procurement timeout"),
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
        using var request = new HttpRequestMessage(HttpMethod.Post, "/bff/login")
        {
            Content = JsonContent.Create(new { email = "employee@maliev.com", password = "password", returnUrl = "/Suppliers/Index" }),
        };
        request.Headers.Add("X-CSRF-TOKEN", session.GetProperty("csrfToken").GetString());
        using var response = await client.SendAsync(request);
        response.EnsureSuccessStatusCode();
    }

    private sealed class SuppliersBffFactory(HttpMessageHandler downstream, bool suppliersRead)
        : WebApplicationFactory<BffProgram>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing");
            TestJwtConfiguration.Configure(builder);
            builder.UseSetting("Services:Auth", "http://auth/");
            builder.UseSetting("Services:Catalog", "http://catalog/");
            builder.UseSetting("Services:Customer", "http://customer/");
            builder.UseSetting("Services:Employee", "http://employee/");
            builder.UseSetting("Services:Order", "http://order/");
            builder.UseSetting("Services:Procurement", "http://procurement/");
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<ILegacyAuthClient>();
                services.AddSingleton<ILegacyAuthClient>(new SuppliersAuthClient(suppliersRead));
                services.RemoveAll<IServiceAccessTokenProvider>();
                var tokenProvider = new SuppliersServiceTokenProvider();
                services.AddSingleton<IServiceAccessTokenProvider>(tokenProvider);

                var proxyType = typeof(BffProgram).Assembly.GetType("Legacy.Maliev.Intranet.Bff.Procurement.SuppliersProxy");
                if (proxyType is null)
                {
                    return;
                }

                for (var index = services.Count - 1; index >= 0; index--)
                {
                    if (services[index].ServiceType == proxyType)
                    {
                        services.RemoveAt(index);
                    }
                }

                var authHandler = new LegacyServiceAuthenticationHandler(tokenProvider) { InnerHandler = downstream };
                var client = new HttpClient(authHandler) { BaseAddress = new Uri("http://procurement/"), Timeout = TimeSpan.FromSeconds(10) };
                services.AddSingleton(proxyType, Activator.CreateInstance(proxyType, client)
                    ?? throw new InvalidOperationException("SuppliersProxy could not be created."));
            });
        }
    }

    private sealed class SuppliersServiceTokenProvider : IServiceAccessTokenProvider
    {
        public ValueTask<string?> GetAccessTokenAsync(CancellationToken cancellationToken) =>
            ValueTask.FromResult<string?>("signed-service-token");

        public void Invalidate(string token)
        {
        }
    }

    private sealed class SuppliersAuthClient(bool suppliersRead) : ILegacyAuthClient
    {
        public Task<EmployeeLoginResult> LoginAsync(string email, string password, CancellationToken cancellationToken) =>
            Task.FromResult(new EmployeeLoginResult(
                true,
                new AuthTokenResponse("server-only-access-token", "server-only-refresh-token", "Bearer", 900, DateTimeOffset.UtcNow.AddDays(1)),
                new EmployeeIdentity(
                    "employee-id",
                    email,
                    email,
                    suppliersRead ? ["legacy-procurement.suppliers.read"] : [],
                    7)));

        public Task<EmployeeRefreshResult?> RefreshAsync(string refreshToken, CancellationToken cancellationToken) => Task.FromResult<EmployeeRefreshResult?>(null);
        public Task RevokeAsync(string refreshToken, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<CustomerIdentityResponse?> CreateCustomerIdentityAsync(int databaseId, CreateCustomerIdentityRequest request, string accessToken, CancellationToken cancellationToken) => Task.FromResult<CustomerIdentityResponse?>(null);
        public Task<EmployeeIdentityResponse?> CreateEmployeeIdentityAsync(int databaseId, CreateEmployeeIdentityRequest request, string accessToken, CancellationToken cancellationToken) => Task.FromResult<EmployeeIdentityResponse?>(null);
    }

    private sealed class RecordingSupplierHandler(string body) : HttpMessageHandler
    {
        public HttpStatusCode StatusCode { get; set; } = HttpStatusCode.OK;
        public int? RetryAfterSeconds { get; set; }
        public Exception? Exception { get; set; }
        public string? PathAndQuery { get; private set; }
        public string? Authorization { get; private set; }
        public int RequestCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestCount++;
            PathAndQuery = request.RequestUri?.PathAndQuery;
            Authorization = request.Headers.Authorization?.ToString();
            if (Exception is not null)
            {
                throw Exception;
            }

            var response = new HttpResponseMessage(StatusCode)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json"),
            };
            if (RetryAfterSeconds is not null)
            {
                response.Headers.RetryAfter = new(TimeSpan.FromSeconds(RetryAfterSeconds.Value));
            }

            return Task.FromResult(response);
        }
    }

    private const string SupplierPageJson =
        """{"Items":[{"Id":42,"Name":"Thai supplier","Website":"https://secret.example","TaxNumber":"123","Email":"supplier@example.com","Note":"private note","AddressId":7,"Telephone":"02-123-4567","Mobile":"089-000-0000","Fax":"02-987-6543","CreatedDate":"2030-07-15T00:00:00","ModifiedDate":null}],"PageIndex":2,"TotalPages":4,"TotalRecords":77,"HasNextPage":true,"HasPreviousPage":true}""";
}
