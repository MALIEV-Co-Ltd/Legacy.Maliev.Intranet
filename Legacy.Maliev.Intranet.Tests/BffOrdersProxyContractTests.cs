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

public sealed class BffOrdersProxyContractTests
{
    [Fact]
    public async Task SessionProjection_UsesStableDatabaseIdWhenUserNameAndEmailDiffer()
    {
        var downstream = new RecordingOrderHandler(OrderPageJson);
        await using var factory = new OrdersBffFactory(
            downstream,
            ordersRead: true,
            catalogRead: true,
            userName: "employee-user",
            email: "employee@maliev.com",
            legacyDatabaseId: 7);
        using var client = CreateClient(factory);
        await SignInAsync(client);

        using var response = await client.GetAsync("/bff/session");
        var session = await response.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("employee-user", session.GetProperty("displayName").GetString());
        Assert.Equal(7, session.GetProperty("legacyDatabaseId").GetInt32());
    }

    [Fact]
    public async Task AuthorizedEmployee_ForwardsOrderQueriesAndServerOnlyToken()
    {
        var downstream = new RecordingOrderHandler(OrderPageJson);
        await using var factory = new OrdersBffFactory(downstream, ordersRead: true, catalogRead: true);
        using var client = CreateClient(factory);
        await SignInAsync(client);

        using var response = await client.GetAsync("/bff/orders?sort=OrderCreatedDate_Descending&search=Thai%20fixture&index=2&size=25");
        var json = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("/Orders?sort=OrderCreatedDate_Descending&search=Thai%20fixture&index=2&size=25", downstream.PathAndQuery);
        Assert.Equal("Bearer signed-service-token", downstream.Authorization);
        Assert.Contains("\"name\":\"Thai fixture\"", json, StringComparison.Ordinal);
        Assert.DoesNotContain("description", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("comment", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("trackingNumber", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("signed-service-token", json, StringComparison.Ordinal);
        Assert.DoesNotContain("server-only-access-token", json, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PendingAndProcesses_UseExactBoundedServiceRoutes()
    {
        var downstream = new RecordingOrderHandler(OrderPageJson);
        await using var factory = new OrdersBffFactory(downstream, ordersRead: true, catalogRead: true);
        using var client = CreateClient(factory);
        await SignInAsync(client);

        using var pending = await client.GetAsync("/bff/orders/pending?size=5000");
        Assert.Equal(HttpStatusCode.OK, pending.StatusCode);
        Assert.Equal("/Orders/pending?index=1&size=1000", downstream.PathAndQuery);

        downstream.Body = ProcessesJson;
        using var processes = await client.GetAsync("/bff/order-processes");
        var processJson = await processes.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, processes.StatusCode);
        Assert.Equal("/orders/processes", downstream.PathAndQuery);
        Assert.Contains("\"id\":3", processJson, StringComparison.Ordinal);
        Assert.Contains("\"name\":\"FDM\"", processJson, StringComparison.Ordinal);
        Assert.DoesNotContain("categoryId", processJson, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("createdDate", processJson, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("modifiedDate", processJson, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("/bff/orders", true, false)]
    [InlineData("/bff/orders/pending", true, false)]
    [InlineData("/bff/order-processes", false, true)]
    public async Task MissingExactPermission_IsForbiddenBeforeDownstream(string path, bool catalogRead, bool ordersRead)
    {
        var downstream = new RecordingOrderHandler(OrderPageJson);
        await using var factory = new OrdersBffFactory(downstream, ordersRead, catalogRead);
        using var client = CreateClient(factory);
        await SignInAsync(client);

        using var response = await client.GetAsync(path);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Null(downstream.PathAndQuery);
    }

    [Fact]
    public async Task AnonymousRequest_IsUnauthorizedBeforeDownstream()
    {
        var downstream = new RecordingOrderHandler(OrderPageJson);
        await using var factory = new OrdersBffFactory(downstream, ordersRead: true, catalogRead: true);
        using var client = CreateClient(factory);

        using var response = await client.GetAsync("/bff/orders");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Null(downstream.PathAndQuery);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    public async Task DownstreamAuthorizationFailure_IsPreserved(HttpStatusCode statusCode)
    {
        var downstream = new RecordingOrderHandler("{}") { StatusCode = statusCode };
        await using var factory = new OrdersBffFactory(downstream, ordersRead: true, catalogRead: true);
        using var client = CreateClient(factory);
        await SignInAsync(client);

        using var response = await client.GetAsync("/bff/orders");

        Assert.Equal(statusCode, response.StatusCode);
    }

    [Fact]
    public async Task NotFound_BecomesEmptyLegacyShapes()
    {
        var downstream = new RecordingOrderHandler("{}") { StatusCode = HttpStatusCode.NotFound };
        await using var factory = new OrdersBffFactory(downstream, ordersRead: true, catalogRead: true);
        using var client = CreateClient(factory);
        await SignInAsync(client);

        using var page = await client.GetAsync("/bff/orders?index=-2&size=999");
        var pageJson = await page.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, page.StatusCode);
        Assert.Equal("/Orders?sort=OrderCreatedDate_Descending&search=&index=1&size=250", downstream.PathAndQuery);
        Assert.Contains("\"items\":[]", pageJson, StringComparison.Ordinal);

        using var processes = await client.GetAsync("/bff/order-processes");
        Assert.Equal(HttpStatusCode.OK, processes.StatusCode);
        Assert.Equal("[]", await processes.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task RateLimit_PreservesBoundedRetryAfterWithoutRetry()
    {
        var downstream = new RecordingOrderHandler("{}") { StatusCode = HttpStatusCode.TooManyRequests, RetryAfterSeconds = 2 };
        await using var factory = new OrdersBffFactory(downstream, ordersRead: true, catalogRead: true);
        using var client = CreateClient(factory);
        await SignInAsync(client);

        using var response = await client.GetAsync("/bff/orders");

        Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode);
        Assert.Equal(TimeSpan.FromSeconds(2), response.Headers.RetryAfter?.Delta);
        Assert.Equal(1, downstream.RequestCount);
    }

    [Theory]
    [InlineData("/bff/orders")]
    [InlineData("/bff/order-processes")]
    public async Task InvalidPayload_IsBadGatewayWithoutPayloadLeak(string path)
    {
        var downstream = new RecordingOrderHandler("order-secret-not-json");
        await using var factory = new OrdersBffFactory(downstream, ordersRead: true, catalogRead: true);
        using var client = CreateClient(factory);
        await SignInAsync(client);

        using var response = await client.GetAsync(path);
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
        Assert.DoesNotContain("order-secret-not-json", body, StringComparison.Ordinal);
    }

    [Theory]
    [MemberData(nameof(TransportFailures))]
    public async Task TransportFailure_IsServiceUnavailable(Exception exception)
    {
        var downstream = new RecordingOrderHandler("{}") { Exception = exception };
        await using var factory = new OrdersBffFactory(downstream, ordersRead: true, catalogRead: true);
        using var client = CreateClient(factory);
        await SignInAsync(client);

        using var response = await client.GetAsync("/bff/orders");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
    }

    public static TheoryData<Exception> TransportFailures => new()
    {
        new HttpRequestException("orders unavailable"),
        new TaskCanceledException("orders timeout"),
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
            Content = JsonContent.Create(new { email = "employee@maliev.com", password = "password", returnUrl = "/Orders/Index" }),
        };
        request.Headers.Add("X-CSRF-TOKEN", session.GetProperty("csrfToken").GetString());
        using var response = await client.SendAsync(request);
        response.EnsureSuccessStatusCode();
    }

    private sealed class OrdersBffFactory(
        HttpMessageHandler downstream,
        bool ordersRead,
        bool catalogRead,
        string userName = "employee@maliev.com",
        string email = "employee@maliev.com",
        int? legacyDatabaseId = 7)
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
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<ILegacyAuthClient>();
                services.AddSingleton<ILegacyAuthClient>(new OrdersAuthClient(
                    ordersRead,
                    catalogRead,
                    userName,
                    email,
                    legacyDatabaseId));
                services.RemoveAll<IServiceAccessTokenProvider>();
                var tokenProvider = new OrdersServiceTokenProvider();
                services.AddSingleton<IServiceAccessTokenProvider>(tokenProvider);

                var proxyType = typeof(BffProgram).Assembly.GetType("Legacy.Maliev.Intranet.Bff.Orders.OrdersProxy");
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
                var client = new HttpClient(authHandler) { BaseAddress = new Uri("http://order/"), Timeout = TimeSpan.FromSeconds(10) };
                services.AddSingleton(proxyType, Activator.CreateInstance(proxyType, client)
                    ?? throw new InvalidOperationException("OrdersProxy could not be created."));
            });
        }
    }

    private sealed class OrdersServiceTokenProvider : IServiceAccessTokenProvider
    {
        public ValueTask<string?> GetAccessTokenAsync(CancellationToken cancellationToken) =>
            ValueTask.FromResult<string?>("signed-service-token");

        public void Invalidate(string token)
        {
        }
    }

    private sealed class OrdersAuthClient(
        bool ordersRead,
        bool catalogRead,
        string userName,
        string email,
        int? legacyDatabaseId) : ILegacyAuthClient
    {
        public Task<EmployeeLoginResult> LoginAsync(string loginEmail, string password, CancellationToken cancellationToken) =>
            Task.FromResult(new EmployeeLoginResult(
                true,
                new AuthTokenResponse("server-only-access-token", "server-only-refresh-token", "Bearer", 900, DateTimeOffset.UtcNow.AddDays(1)),
                new EmployeeIdentity(
                    "employee-id",
                    userName,
                    email,
                    [
                        .. ordersRead ? ["legacy.orders.read"] : Array.Empty<string>(),
                        .. catalogRead ? ["legacy.order-catalog.read"] : Array.Empty<string>(),
                    ],
                    legacyDatabaseId)));

        public Task<EmployeeRefreshResult?> RefreshAsync(string refreshToken, CancellationToken cancellationToken) => Task.FromResult<EmployeeRefreshResult?>(null);
        public Task RevokeAsync(string refreshToken, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<CustomerIdentityResponse?> CreateCustomerIdentityAsync(int databaseId, CreateCustomerIdentityRequest request, string accessToken, CancellationToken cancellationToken) => Task.FromResult<CustomerIdentityResponse?>(null);
        public Task<EmployeeIdentityResponse?> CreateEmployeeIdentityAsync(int databaseId, CreateEmployeeIdentityRequest request, string accessToken, CancellationToken cancellationToken) => Task.FromResult<EmployeeIdentityResponse?>(null);
    }

    private sealed class RecordingOrderHandler(string body) : HttpMessageHandler
    {
        public string Body { get; set; } = body;
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
                Content = new StringContent(Body, Encoding.UTF8, "application/json"),
            };
            if (RetryAfterSeconds is not null)
            {
                response.Headers.RetryAfter = new(TimeSpan.FromSeconds(RetryAfterSeconds.Value));
            }

            return Task.FromResult(response);
        }
    }

    private const string OrderPageJson =
        """{"Items":[{"Id":84,"CustomerId":42,"EmployeeId":7,"Name":"Thai fixture","Description":"ไม้เอก ไม้โท","ProcessId":3,"MaterialId":5,"SurfaceFinishId":6,"ColorId":4,"Quantity":2,"Manufactured":1,"Remaining":1,"UnitPrice":125,"DiscountPercent":10,"Subtotal":225,"CurrencyId":1,"LeadTime":3,"PromisedDate":"2030-07-20T00:00:00","FinishedDate":null,"Turnaround":null,"Comment":"note","AllowSocialMedia":false,"AllowCancellation":true,"AllowPayment":false,"TrackingNumber":"TRACK-1","CreatedDate":"2030-07-15T00:00:00","ModifiedDate":null}],"PageIndex":2,"TotalPages":4,"TotalRecords":77,"HasNextPage":true,"HasPreviousPage":true}""";
    private const string ProcessesJson = """[{"Id":3,"CategoryId":1,"Name":"FDM","CreatedDate":null,"ModifiedDate":null}]""";
}
