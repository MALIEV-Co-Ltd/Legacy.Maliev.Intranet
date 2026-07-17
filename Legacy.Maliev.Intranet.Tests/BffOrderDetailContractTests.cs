extern alias Bff;

using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using Legacy.Maliev.Intranet.Auth;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using BffProgram = Bff::Program;
using OrderCatalogReferenceProxy = Bff::Legacy.Maliev.Intranet.Bff.Orders.OrderCatalogReferenceProxy;
using OrderDetailProxy = Bff::Legacy.Maliev.Intranet.Bff.Orders.OrderDetailProxy;
using OrderDocumentProxy = Bff::Legacy.Maliev.Intranet.Bff.Orders.OrderDocumentProxy;
using OrderEmployeeReferenceProxy = Bff::Legacy.Maliev.Intranet.Bff.Orders.OrderEmployeeReferenceProxy;
using OrderFileProxy = Bff::Legacy.Maliev.Intranet.Bff.Orders.OrderFileProxy;
using OrdersProxy = Bff::Legacy.Maliev.Intranet.Bff.Orders.OrdersProxy;

namespace Legacy.Maliev.Intranet.Tests;

public sealed class BffOrderDetailContractTests
{
    [Fact]
    public async Task AuthorizedEmployee_GetsCompleteAggregateWithServerOnlyCredentials()
    {
        var downstream = new OrderDetailHandler();
        await using var factory = new OrderDetailBffFactory(downstream, AllPermissions);
        using var client = CreateClient(factory);
        await SignInAsync(client);

        using var response = await client.GetAsync("/bff/orders/84");
        var json = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("\"id\":84", json, StringComparison.Ordinal);
        Assert.Contains("\"description\":\"ไม้เอก ไม้โท\"", json, StringComparison.Ordinal);
        Assert.Contains("\"processes\":[{\"id\":3,\"name\":\"FDM\"}]", json, StringComparison.Ordinal);
        Assert.Contains("\"employees\":[{\"id\":7,\"name\":\"Employee Fixture\"}]", json, StringComparison.Ordinal);
        Assert.DoesNotContain("signed-service-token", json, StringComparison.Ordinal);
        Assert.DoesNotContain("server-only-refresh-token", json, StringComparison.Ordinal);
        Assert.All(downstream.Requests, request => Assert.Equal("Bearer signed-service-token", request.Authorization));
    }

    [Fact]
    public async Task MissingReadPermission_IsForbiddenBeforeAnyDetailDownstreamCall()
    {
        var downstream = new OrderDetailHandler();
        await using var factory = new OrderDetailBffFactory(
            downstream,
            AllPermissions.Where(permission => permission != LegacyEmployeePermissions.OrderStatusRead).ToArray());
        using var client = CreateClient(factory);
        await SignInAsync(client);
        downstream.Requests.Clear();

        using var response = await client.GetAsync("/bff/orders/84");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Empty(downstream.Requests);
    }

    [Fact]
    public async Task Update_RequiresCsrfAndForwardsCompletePayloadWithConcurrencyToken()
    {
        var downstream = new OrderDetailHandler();
        await using var factory = new OrderDetailBffFactory(downstream, AllPermissions);
        using var client = CreateClient(factory);
        var csrf = await SignInAsync(client);
        var payload = new
        {
            customerId = 999,
            employeeId = 7,
            name = "Updated fixture",
            description = "ไม้เอก ไม้โท",
            processId = 3,
            materialId = 5,
            surfaceFinishId = 6,
            colorId = 4,
            quantity = 2,
            manufactured = 1,
            unitPrice = 125m,
            discountPercent = 10m,
            currencyId = 1,
            leadTime = 3,
            promisedDate = "2030-07-20T00:00:00Z",
            finishedDate = (string?)null,
            comment = "note",
            allowSocialMedia = false,
            allowCancellation = true,
            allowPayment = false,
            trackingNumber = "TRACK-1",
            modifiedDate = "2030-07-15T08:30:00Z",
        };

        using var missingCsrf = new HttpRequestMessage(HttpMethod.Put, "/bff/orders/84") { Content = JsonContent.Create(payload) };
        using var rejected = await client.SendAsync(missingCsrf);
        Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);

        using var request = new HttpRequestMessage(HttpMethod.Put, "/bff/orders/84") { Content = JsonContent.Create(payload) };
        request.Headers.Add("X-CSRF-TOKEN", csrf);
        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var forwarded = Assert.Single(downstream.Requests, item => item.Path == "/Orders/84" && item.Method == "PUT");
        Assert.Equal("2030-07-15T08:30:00.0000000Z", forwarded.ExpectedModifiedDate);
        Assert.Contains("\"name\":\"Updated fixture\"", forwarded.Body, StringComparison.Ordinal);
        Assert.Contains("\"customerId\":42", forwarded.Body, StringComparison.Ordinal);
        Assert.DoesNotContain("modifiedDate", forwarded.Body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Update_NullCustomerOrder_PreservesNullAndIgnoresAttemptedReassignment()
    {
        var downstream = new OrderDetailHandler(customerId: null);
        await using var factory = new OrderDetailBffFactory(downstream, AllPermissions);
        using var client = CreateClient(factory);
        var csrf = await SignInAsync(client);
        using var request = new HttpRequestMessage(HttpMethod.Put, "/bff/orders/84")
        {
            Content = JsonContent.Create(new
            {
                customerId = 999,
                employeeId = (int?)null,
                name = "Null customer fixture",
                description = (string?)null,
                processId = 3,
                materialId = (int?)null,
                surfaceFinishId = (int?)null,
                colorId = (int?)null,
                quantity = 1,
                manufactured = 0,
                unitPrice = (decimal?)null,
                discountPercent = (decimal?)null,
                currencyId = (int?)null,
                leadTime = (int?)null,
                promisedDate = (DateTime?)null,
                finishedDate = (DateTime?)null,
                comment = (string?)null,
                allowSocialMedia = false,
                allowCancellation = true,
                allowPayment = false,
                trackingNumber = (string?)null,
                modifiedDate = "2030-07-15T08:30:00Z",
            }),
        };
        request.Headers.Add("X-CSRF-TOKEN", csrf);

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var forwarded = Assert.Single(downstream.Requests, item => item.Path == "/Orders/84" && item.Method == "PUT");
        Assert.Contains("\"customerId\":null", forwarded.Body, StringComparison.Ordinal);
        Assert.DoesNotContain("999", forwarded.Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task StatusTransition_RequiresCsrfAndForwardsCallerIdempotencyKey()
    {
        var downstream = new OrderDetailHandler();
        await using var factory = new OrderDetailBffFactory(downstream, AllPermissions);
        using var client = CreateClient(factory);
        var csrf = await SignInAsync(client);
        var idempotencyKey = Guid.Empty.ToString("D");

        using var missingCsrf = new HttpRequestMessage(HttpMethod.Post, "/bff/orders/84/status/2");
        missingCsrf.Headers.Add("Idempotency-Key", idempotencyKey);
        using var rejected = await client.SendAsync(missingCsrf);
        Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);

        using var request = new HttpRequestMessage(HttpMethod.Post, "/bff/orders/84/status/2");
        request.Headers.Add("X-CSRF-TOKEN", csrf);
        request.Headers.Add("Idempotency-Key", idempotencyKey);
        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var forwarded = Assert.Single(
            downstream.Requests,
            item => item.Path == "/orderstatuses/Histories/84/2" && item.Method == "POST");
        Assert.Equal(idempotencyKey, forwarded.IdempotencyKey);
    }

    [Fact]
    public async Task OrderLabel_ReturnsPdfHeadersAndUsesOnlyLabelReferenceCalls()
    {
        var downstream = new OrderDetailHandler();
        await using var factory = new OrderDetailBffFactory(downstream, AllPermissions);
        using var client = CreateClient(factory);
        await SignInAsync(client);
        downstream.Requests.Clear();

        using var response = await client.GetAsync("/bff/orders/84/label");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/pdf", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal("OrderLabel_84.pdf", response.Content.Headers.ContentDisposition?.FileNameStar);
        Assert.DoesNotContain(downstream.Requests, request => request.Path.Contains("Histories", StringComparison.Ordinal));
        Assert.DoesNotContain(downstream.Requests, request => request.Path.Contains("files", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(downstream.Requests, request => request.Path.Contains("employees", StringComparison.OrdinalIgnoreCase));
    }

    private static readonly string[] AllPermissions =
    [
        LegacyEmployeePermissions.OrdersRead,
        LegacyEmployeePermissions.OrderCatalogRead,
        LegacyEmployeePermissions.EmployeesList,
        LegacyEmployeePermissions.CatalogMaterialsRead,
        LegacyEmployeePermissions.OrderStatusRead,
        LegacyEmployeePermissions.OrderStatusWrite,
        LegacyEmployeePermissions.OrderFilesRead,
        LegacyEmployeePermissions.OrderFilesWrite,
        LegacyEmployeePermissions.OrderFilesDelete,
        LegacyEmployeePermissions.FileUploadsRead,
        LegacyEmployeePermissions.FileUploadsCreate,
        LegacyEmployeePermissions.FileUploadsDelete,
        LegacyEmployeePermissions.OrdersUpdate,
    ];

    private static HttpClient CreateClient(WebApplicationFactory<BffProgram> factory) => factory.CreateClient(new()
    {
        AllowAutoRedirect = false,
        BaseAddress = new Uri("https://localhost"),
        HandleCookies = true,
    });

    private static async Task<string> SignInAsync(HttpClient client)
    {
        using var anonymousSession = await client.GetAsync("/bff/session");
        var anonymous = await anonymousSession.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        using var login = new HttpRequestMessage(HttpMethod.Post, "/bff/login")
        {
            Content = JsonContent.Create(new { email = "employee@maliev.com", password = "password", returnUrl = "/Orders/View?id=84" }),
        };
        login.Headers.Add("X-CSRF-TOKEN", anonymous.GetProperty("csrfToken").GetString());
        using var response = await client.SendAsync(login);
        response.EnsureSuccessStatusCode();
        using var authenticatedSession = await client.GetAsync("/bff/session");
        var authenticated = await authenticatedSession.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        return authenticated.GetProperty("csrfToken").GetString() ?? throw new InvalidOperationException("Missing CSRF token.");
    }

    private sealed class OrderDetailBffFactory(OrderDetailHandler downstream, IReadOnlyList<string> permissions)
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
            builder.UseSetting("Services:File", "http://file/");
            builder.UseSetting("Services:Document", "http://document/");
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<ILegacyAuthClient>();
                services.AddSingleton<ILegacyAuthClient>(new DetailAuthClient(permissions));
                services.RemoveAll<IServiceAccessTokenProvider>();
                var tokenProvider = new DetailTokenProvider();
                services.AddSingleton<IServiceAccessTokenProvider>(tokenProvider);
                Replace(services, new OrdersProxy(Client("http://order/", downstream, tokenProvider)));
                Replace(services, new OrderDetailProxy(Client("http://order/", downstream, tokenProvider)));
                Replace(services, new OrderCatalogReferenceProxy(Client("http://catalog/", downstream, tokenProvider)));
                Replace(services, new OrderEmployeeReferenceProxy(Client("http://employee/", downstream, tokenProvider)));
                Replace(services, new OrderFileProxy(Client("http://file/", downstream, tokenProvider)));
                Replace(services, new OrderDocumentProxy(Client("http://document/", downstream, tokenProvider)));
            });
        }

        private static HttpClient Client(string baseAddress, HttpMessageHandler downstream, IServiceAccessTokenProvider tokens)
        {
            var auth = new LegacyServiceAuthenticationHandler(tokens) { InnerHandler = downstream };
            return new HttpClient(auth) { BaseAddress = new Uri(baseAddress), Timeout = TimeSpan.FromSeconds(10) };
        }

        private static void Replace<T>(IServiceCollection services, T instance) where T : class
        {
            services.RemoveAll<T>();
            services.AddSingleton(instance);
        }
    }

    private sealed class DetailTokenProvider : IServiceAccessTokenProvider
    {
        public ValueTask<string?> GetAccessTokenAsync(CancellationToken cancellationToken) => ValueTask.FromResult<string?>("signed-service-token");
        public void Invalidate(string token) { }
    }

    private sealed class DetailAuthClient(IReadOnlyList<string> permissions) : ILegacyAuthClient
    {
        public Task<EmployeeLoginResult> LoginAsync(string email, string password, CancellationToken cancellationToken) => Task.FromResult(new EmployeeLoginResult(true, new("server-only-access-token", "server-only-refresh-token", "Bearer", 900, DateTimeOffset.UtcNow.AddDays(1)), new("employee-id", email, email, permissions, 7)));
        public Task<EmployeeRefreshResult?> RefreshAsync(string refreshToken, CancellationToken cancellationToken) => Task.FromResult<EmployeeRefreshResult?>(null);
        public Task RevokeAsync(string refreshToken, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<CustomerIdentityResponse?> CreateCustomerIdentityAsync(int databaseId, CreateCustomerIdentityRequest request, string accessToken, CancellationToken cancellationToken) => Task.FromResult<CustomerIdentityResponse?>(null);
        public Task<EmployeeIdentityResponse?> CreateEmployeeIdentityAsync(int databaseId, CreateEmployeeIdentityRequest request, string accessToken, CancellationToken cancellationToken) => Task.FromResult<EmployeeIdentityResponse?>(null);
    }

    private sealed class OrderDetailHandler(int? customerId = 42) : HttpMessageHandler
    {
        public ConcurrentBag<RecordedRequest> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            Requests.Add(new(
                request.Method.Method,
                request.RequestUri?.AbsolutePath ?? string.Empty,
                request.Headers.Authorization?.ToString(),
                request.Headers.TryGetValues("X-Expected-Modified-Date", out var expected) ? expected.Single() : null,
                request.Headers.TryGetValues("Idempotency-Key", out var idempotencyKey) ? idempotencyKey.Single() : null,
                body));
            if (request.Method == HttpMethod.Put && request.RequestUri?.AbsolutePath == "/Orders/84") return new(HttpStatusCode.NoContent);
            if (request.Method == HttpMethod.Post && request.RequestUri?.AbsolutePath == "/orderstatuses/Histories/84/2") return new(HttpStatusCode.NoContent);
            if (request.Method == HttpMethod.Post && request.RequestUri?.AbsolutePath == "/Pdfs/orderlabel")
            {
                return new(HttpStatusCode.OK)
                {
                    Content = new ByteArrayContent([0x25, 0x50, 0x44, 0x46])
                    {
                        Headers = { ContentType = new("application/pdf") },
                    },
                };
            }
            var path = request.RequestUri?.PathAndQuery ?? string.Empty;
            return Json(path switch
            {
                "/Orders/84" => customerId is null
                    ? OrderJson.Replace("\"CustomerId\":42", "\"CustomerId\":null", StringComparison.Ordinal)
                    : OrderJson.Replace("\"CustomerId\":42", $"\"CustomerId\":{customerId.Value}", StringComparison.Ordinal),
                "/orders/processes" => """[{"Id":3,"Name":"FDM"}]""",
                var value when value.StartsWith("/Materials?", StringComparison.Ordinal) => """{"Items":[{"Id":5,"Name":"PLA"}]}""",
                "/materials/Colors" => """[{"Id":4,"Name":"Black"}]""",
                "/materials/SurfaceFinishes" => """[{"Id":6,"Name":"As printed"}]""",
                "/Currencies" => """[{"Id":1,"ShortName":"THB"}]""",
                var value when value.StartsWith("/employees?", StringComparison.Ordinal) => """{"Items":[{"Id":7,"FullName":"Employee Fixture"}]}""",
                "/orderstatuses/Histories/84/latest" => """{"Id":1,"Name":"New"}""",
                "/orderstatuses/1/available" => """[{"Id":2,"Name":"Reviewed"}]""",
                "/orderstatuses/Histories/84" => """[{"Id":1,"OrderId":84,"OrderStatusId":1,"Name":"New"}]""",
                "/orders/84/files" => "[]",
                _ => throw new InvalidOperationException($"Unexpected downstream route {path}"),
            });
        }

        private static HttpResponseMessage Json(string body) => new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
    }

    private sealed record RecordedRequest(
        string Method,
        string Path,
        string? Authorization,
        string? ExpectedModifiedDate,
        string? IdempotencyKey,
        string? Body);

    private const string OrderJson = """{"Id":84,"CustomerId":42,"EmployeeId":7,"Name":"Thai fixture","Description":"ไม้เอก ไม้โท","ProcessId":3,"MaterialId":5,"SurfaceFinishId":6,"ColorId":4,"Quantity":2,"Manufactured":1,"Remaining":1,"UnitPrice":125,"DiscountPercent":10,"Subtotal":225,"CurrencyId":1,"LeadTime":3,"PromisedDate":"2030-07-20T00:00:00Z","FinishedDate":null,"Turnaround":null,"Comment":"note","AllowSocialMedia":false,"AllowCancellation":true,"AllowPayment":false,"TrackingNumber":"TRACK-1","CreatedDate":"2030-07-15T00:00:00Z","ModifiedDate":"2030-07-15T08:30:00Z"}""";
}
