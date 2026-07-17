extern alias Bff;

using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using Legacy.Maliev.Intranet.Auth;
using Legacy.Maliev.Intranet.Contracts;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using BffProgram = Bff::Program;
using CustomersProxy = Bff::Legacy.Maliev.Intranet.Bff.Customers.CustomersProxy;
using OrderCatalogReferenceProxy = Bff::Legacy.Maliev.Intranet.Bff.Orders.OrderCatalogReferenceProxy;
using OrderCreateProxy = Bff::Legacy.Maliev.Intranet.Bff.Orders.OrderCreateProxy;
using OrderDetailProxy = Bff::Legacy.Maliev.Intranet.Bff.Orders.OrderDetailProxy;
using OrderFileProxy = Bff::Legacy.Maliev.Intranet.Bff.Orders.OrderFileProxy;
using OrderNotificationProxy = Bff::Legacy.Maliev.Intranet.Bff.Orders.OrderNotificationProxy;
using OrdersProxy = Bff::Legacy.Maliev.Intranet.Bff.Orders.OrdersProxy;

namespace Legacy.Maliev.Intranet.Tests;

public sealed class BffOrderCreateContractTests
{
    [Fact]
    public async Task AuthorizedEmployee_WithCsrf_CreatesRequiredAggregateUsingServerCredentialsAndDefaults()
    {
        var downstream = new OrderCreateHandler();
        await using var factory = new OrderCreateBffFactory(downstream, [LegacyEmployeePermissions.OrdersCreate]);
        using var client = CreateClient(factory);
        var csrf = await SignInAsync(client);
        var workflowId = Guid.NewGuid().ToString("D");

        using var response = await SendCreateAsync(client, csrf, workflowId);
        var result = await response.Content.ReadFromJsonAsync<OrderCreatedResult>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(new OrderCreatedResult(84, null), result);
        var create = Assert.Single(downstream.Requests, item => item.Method == "POST" && item.Path == "/Orders");
        Assert.True(Guid.TryParse(create.IdempotencyKey, out _));
        Assert.NotEqual(workflowId, create.IdempotencyKey);
        Assert.Equal("Bearer signed-service-token", create.Authorization);
        Assert.Contains("\"customerId\":42", create.Body, StringComparison.Ordinal);
        Assert.Contains("\"manufactured\":0", create.Body, StringComparison.Ordinal);
        Assert.Contains("\"allowCancellation\":true", create.Body, StringComparison.Ordinal);
        Assert.Contains("\"allowPayment\":false", create.Body, StringComparison.Ordinal);
        var status = Assert.Single(downstream.Requests, item => item.Method == "POST" && item.Path == "/orderstatuses/Histories/84/1");
        Assert.Equal(create.IdempotencyKey, status.IdempotencyKey);
        var notification = Assert.Single(downstream.Requests, item => item.Method == "POST" && item.Path == "/notifications/v1/email/NoReply");
        Assert.Contains("customer@example.com", notification.Body, StringComparison.Ordinal);
        Assert.DoesNotContain("server-only-refresh-token", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task MissingCsrf_IsRejectedBeforeAnyDownstreamCall()
    {
        var downstream = new OrderCreateHandler();
        await using var factory = new OrderCreateBffFactory(downstream, [LegacyEmployeePermissions.OrdersCreate]);
        using var client = CreateClient(factory);
        await SignInAsync(client);
        downstream.Requests.Clear();

        using var response = await SendCreateAsync(client, null, Guid.NewGuid().ToString("D"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(downstream.Requests);
    }

    [Fact]
    public async Task EmployeeWithoutCreatePermission_IsForbiddenBeforeAnyDownstreamCall()
    {
        var downstream = new OrderCreateHandler();
        await using var factory = new OrderCreateBffFactory(downstream, []);
        using var client = CreateClient(factory);
        var csrf = await SignInAsync(client);
        downstream.Requests.Clear();

        using var response = await SendCreateAsync(client, csrf, Guid.NewGuid().ToString("D"));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Empty(downstream.Requests);
    }

    [Fact]
    public async Task UnknownCustomer_IsRejectedBeforeOrderCreation()
    {
        var downstream = new OrderCreateHandler(customerExists: false);
        await using var factory = new OrderCreateBffFactory(downstream, [LegacyEmployeePermissions.OrdersCreate]);
        using var client = CreateClient(factory);
        var csrf = await SignInAsync(client);
        downstream.Requests.Clear();

        using var response = await SendCreateAsync(client, csrf, Guid.NewGuid().ToString("D"));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.DoesNotContain(downstream.Requests, item => item.Method == "POST" && item.Path == "/Orders");
    }

    [Theory]
    [InlineData(999, 5, 4, 6)]
    [InlineData(3, 999, 4, 6)]
    [InlineData(3, 5, 999, 6)]
    [InlineData(3, 5, 4, 999)]
    public async Task TamperedReferences_AreRejectedBeforeCustomerAndOrderWrites(
        int processId,
        int materialId,
        int colorId,
        int surfaceFinishId)
    {
        var downstream = new OrderCreateHandler();
        await using var factory = new OrderCreateBffFactory(downstream, [LegacyEmployeePermissions.OrdersCreate]);
        using var client = CreateClient(factory);
        var csrf = await SignInAsync(client);
        downstream.Requests.Clear();
        var input = new OrderCreateRequest(42, "Tampered", null, processId, materialId, surfaceFinishId, colorId, 1, false, false);

        using var response = await SendCreateAsync(client, csrf, Guid.NewGuid().ToString("D"), input);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.DoesNotContain(downstream.Requests, item => item.Path == "/customers/42");
        Assert.DoesNotContain(downstream.Requests, item => item.Method == "POST");
    }

    [Fact]
    public async Task CustomerWithoutEmail_CanCreateWhenConfirmationIsDisabled()
    {
        var downstream = new OrderCreateHandler(customerEmail: string.Empty);
        await using var factory = new OrderCreateBffFactory(downstream, [LegacyEmployeePermissions.OrdersCreate]);
        using var client = CreateClient(factory);
        var csrf = await SignInAsync(client);
        var input = new OrderCreateRequest(42, "No email", null, 3, 5, 6, 4, 1, false, false);

        using var response = await SendCreateAsync(client, csrf, Guid.NewGuid().ToString("D"), input);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.DoesNotContain(downstream.Requests, item => item.Path == "/notifications/v1/email/NoReply");
    }

    [Fact]
    public async Task LostOrderCreateResponse_RetriesSameDownstreamAttemptWithoutDuplicateWorkflow()
    {
        var downstream = new OrderCreateHandler(loseFirstCreateResponse: true);
        await using var factory = new OrderCreateBffFactory(downstream, [LegacyEmployeePermissions.OrdersCreate]);
        using var client = CreateClient(factory);
        var csrf = await SignInAsync(client);
        var browserWorkflow = Guid.NewGuid().ToString("D");

        using var uncertain = await SendCreateAsync(client, csrf, browserWorkflow);
        using var replay = await SendCreateAsync(client, csrf, browserWorkflow);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, uncertain.StatusCode);
        Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
        var creates = downstream.Requests.Where(item => item.Method == "POST" && item.Path == "/Orders").ToArray();
        Assert.Equal(2, creates.Length);
        Assert.Equal(creates[0].IdempotencyKey, creates[1].IdempotencyKey);
    }

    [Fact]
    public async Task ServerErrorAfterOrderCommit_RetriesSameDownstreamAttemptWithoutCompensation()
    {
        var downstream = new OrderCreateHandler(firstCreateStatus: HttpStatusCode.ServiceUnavailable);
        await using var factory = new OrderCreateBffFactory(downstream, [LegacyEmployeePermissions.OrdersCreate]);
        using var client = CreateClient(factory);
        var csrf = await SignInAsync(client);
        var browserWorkflow = Guid.NewGuid().ToString("D");

        using var uncertain = await SendCreateAsync(client, csrf, browserWorkflow);
        using var replay = await SendCreateAsync(client, csrf, browserWorkflow);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, uncertain.StatusCode);
        Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
        var creates = downstream.Requests.Where(item => item.Method == "POST" && item.Path == "/Orders").ToArray();
        Assert.Equal(2, creates.Length);
        Assert.Equal(creates[0].IdempotencyKey, creates[1].IdempotencyKey);
        Assert.DoesNotContain(downstream.Requests, item => item.Method == "DELETE");
    }

    private static HttpClient CreateClient(WebApplicationFactory<BffProgram> factory) => factory.CreateClient(new()
    {
        AllowAutoRedirect = false,
        BaseAddress = new Uri("https://localhost"),
        HandleCookies = true,
    });

    private static async Task<string> SignInAsync(HttpClient client)
    {
        using var anonymousResponse = await client.GetAsync("/bff/session");
        var anonymous = await anonymousResponse.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        using var login = new HttpRequestMessage(HttpMethod.Post, "/bff/login")
        {
            Content = JsonContent.Create(new { email = "employee@maliev.com", password = "password", returnUrl = "/Orders/Create" }),
        };
        login.Headers.Add("X-CSRF-TOKEN", anonymous.GetProperty("csrfToken").GetString());
        using var loginResponse = await client.SendAsync(login);
        loginResponse.EnsureSuccessStatusCode();
        using var sessionResponse = await client.GetAsync("/bff/session");
        var session = await sessionResponse.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        return session.GetProperty("csrfToken").GetString() ?? throw new InvalidOperationException("Missing CSRF token.");
    }

    private static async Task<HttpResponseMessage> SendCreateAsync(
        HttpClient client,
        string? csrf,
        string workflowId,
        OrderCreateRequest? input = null)
    {
        using var content = new MultipartFormDataContent("----maliev-order-create-test");
        content.Add(JsonContent.Create(input ?? new OrderCreateRequest(42, "Thai fixture", "ไม้เอก ไม้โท", 3, 5, 6, 4, 2, true, false)), "request");
        var request = new HttpRequestMessage(HttpMethod.Post, "/bff/orders") { Content = content };
        request.Headers.Add("Idempotency-Key", workflowId);
        if (csrf is not null) request.Headers.Add("X-CSRF-TOKEN", csrf);
        return await client.SendAsync(request);
    }

    private sealed class OrderCreateBffFactory(OrderCreateHandler downstream, IReadOnlyList<string> permissions)
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
            builder.UseSetting("Services:Notification", "http://notification/");
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<ILegacyAuthClient>();
                services.AddSingleton<ILegacyAuthClient>(new CreateAuthClient(permissions));
                services.RemoveAll<IServiceAccessTokenProvider>();
                var tokens = new CreateTokenProvider();
                services.AddSingleton<IServiceAccessTokenProvider>(tokens);
                Replace(services, new CustomersProxy(Client("http://customer/", downstream, tokens)));
                Replace(services, new OrdersProxy(Client("http://order/", downstream, tokens)));
                Replace(services, new OrderCreateProxy(Client("http://order/", downstream, tokens)));
                Replace(services, new OrderDetailProxy(Client("http://order/", downstream, tokens)));
                Replace(services, new OrderCatalogReferenceProxy(Client("http://catalog/", downstream, tokens)));
                Replace(services, new OrderFileProxy(Client("http://file/", downstream, tokens)));
                Replace(services, new OrderNotificationProxy(Client("http://notification/", downstream, tokens)));
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

    private sealed class CreateTokenProvider : IServiceAccessTokenProvider
    {
        public ValueTask<string?> GetAccessTokenAsync(CancellationToken cancellationToken) => ValueTask.FromResult<string?>("signed-service-token");
        public void Invalidate(string token) { }
    }

    private sealed class CreateAuthClient(IReadOnlyList<string> permissions) : ILegacyAuthClient
    {
        public Task<EmployeeLoginResult> LoginAsync(string email, string password, CancellationToken cancellationToken) => Task.FromResult(new EmployeeLoginResult(true, new("server-only-access-token", "server-only-refresh-token", "Bearer", 900, DateTimeOffset.UtcNow.AddDays(1)), new("employee-id", email, email, permissions, 7)));
        public Task<EmployeeRefreshResult?> RefreshAsync(string refreshToken, CancellationToken cancellationToken) => Task.FromResult<EmployeeRefreshResult?>(null);
        public Task RevokeAsync(string refreshToken, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<CustomerIdentityResponse?> CreateCustomerIdentityAsync(int databaseId, CreateCustomerIdentityRequest request, string accessToken, CancellationToken cancellationToken) => Task.FromResult<CustomerIdentityResponse?>(null);
        public Task<EmployeeIdentityResponse?> CreateEmployeeIdentityAsync(int databaseId, CreateEmployeeIdentityRequest request, string accessToken, CancellationToken cancellationToken) => Task.FromResult<EmployeeIdentityResponse?>(null);
    }

    private sealed class OrderCreateHandler(
        bool customerExists = true,
        string customerEmail = "customer@example.com",
        bool loseFirstCreateResponse = false,
        HttpStatusCode? firstCreateStatus = null) : HttpMessageHandler
    {
        private int createCalls;
        public ConcurrentBag<RecordedRequest> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);
            var path = request.RequestUri?.AbsolutePath ?? string.Empty;
            Requests.Add(new(
                request.Method.Method,
                path,
                request.Headers.Authorization?.ToString(),
                request.Headers.TryGetValues("Idempotency-Key", out var idempotency) ? idempotency.Single() : null,
                body));

            if (request.Method == HttpMethod.Get && path == "/customers/42")
            {
                return customerExists
                    ? Json(CustomerJson.Replace("customer@example.com", customerEmail, StringComparison.Ordinal))
                    : new(HttpStatusCode.NotFound);
            }
            if (request.Method == HttpMethod.Get && path == "/orders/processes") return Json("[{\"Id\":3,\"Name\":\"FDM\"}]");
            if (request.Method == HttpMethod.Get && path == "/Materials") return Json("{\"Items\":[{\"Id\":5,\"Name\":\"PLA\"}]}");
            if (request.Method == HttpMethod.Get && path == "/materials/5/colors") return Json("[{\"Id\":4,\"Name\":\"Black\"}]");
            if (request.Method == HttpMethod.Get && path == "/materials/5/surfacefinishes") return Json("[{\"Id\":6,\"Name\":\"As printed\"}]");
            if (request.Method == HttpMethod.Post && path == "/Orders")
            {
                var call = Interlocked.Increment(ref createCalls);
                if (loseFirstCreateResponse && call == 1)
                {
                    throw new HttpRequestException("response lost after commit");
                }
                if (firstCreateStatus is not null && call == 1) return new(firstCreateStatus.Value);
                return Json("{\"Id\":84}", HttpStatusCode.Created);
            }
            if (request.Method == HttpMethod.Get && path == "/OrderStatuses/New") return Json("{\"Id\":1,\"Name\":\"New\"}");
            if (request.Method == HttpMethod.Get && path == "/orderstatuses/Histories/84") return Json("[]");
            if (request.Method == HttpMethod.Post && path == "/orderstatuses/Histories/84/1") return new(HttpStatusCode.NoContent);
            if (request.Method == HttpMethod.Post && path == "/notifications/v1/email/NoReply") return new(HttpStatusCode.NoContent);
            throw new InvalidOperationException($"Unexpected downstream route {request.Method} {request.RequestUri?.PathAndQuery}");
        }

        private static HttpResponseMessage Json(string body, HttpStatusCode status = HttpStatusCode.OK) => new(status)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };
    }

    private sealed record RecordedRequest(string Method, string Path, string? Authorization, string? IdempotencyKey, string Body);

    private const string CustomerJson = """{"Id":42,"FirstName":"Ada","LastName":"Lovelace","FullName":"Ada Lovelace","Email":"customer@example.com","CompanyId":null,"BillingAddressId":null,"ShippingAddressId":null}""";
}
