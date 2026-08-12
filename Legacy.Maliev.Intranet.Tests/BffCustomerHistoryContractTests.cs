extern alias Bff;

using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Legacy.Maliev.Intranet.Auth;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using BffProgram = Bff::Program;
using InvoicesProxy = Bff::Legacy.Maliev.Intranet.Bff.Accounting.InvoicesProxy;
using OrdersProxy = Bff::Legacy.Maliev.Intranet.Bff.Orders.OrdersProxy;
using QuotationsProxy = Bff::Legacy.Maliev.Intranet.Bff.Quotations.QuotationsProxy;

namespace Legacy.Maliev.Intranet.Tests;

public sealed class BffCustomerHistoryContractTests
{
    [Theory]
    [InlineData("orders", LegacyEmployeePermissions.OrdersRead, "/Orders/customers/42?sort=OrderCreatedDate_Descending&search=&index=1&size=100")]
    [InlineData("quotations", LegacyEmployeePermissions.QuotationsRead, "/quotations/customers/42?sort=QuotationCreatedDate_Descending&search=&index=1&size=100")]
    [InlineData("invoices", LegacyEmployeePermissions.AccountingRead, "/invoices/customers/42?sort=InvoiceCreatedDate_Descending&search=&index=1&size=100")]
    public async Task CustomerFamily_ForwardsExplicitOwnerRoute(
        string family,
        string permission,
        string expectedPath)
    {
        var downstream = new RecordingHandler(PageJson(family, 42));
        await using var factory = new CustomerHistoryBffFactory(family, downstream, [permission]);
        using var client = CreateClient(factory);
        await SignInAsync(client);

        using var response = await client.GetAsync($"/bff/customers/42/{family}?index=-3&size=999");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(expectedPath, downstream.PathAndQuery);
        Assert.Equal("Bearer signed-service-token", downstream.Authorization);
    }

    [Fact]
    public async Task CustomerOrders_PreserveServiceTimestampsInBrowserSafeProjection()
    {
        var downstream = new RecordingHandler(PageJson("orders", 42));
        await using var factory = new CustomerHistoryBffFactory(
            "orders",
            downstream,
            [LegacyEmployeePermissions.OrdersRead]);
        using var client = CreateClient(factory);
        await SignInAsync(client);

        using var response = await client.GetAsync("/bff/customers/42/orders");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("2030-07-15T00:00:00", body.GetProperty("items")[0].GetProperty("createdDate").GetString());
        Assert.Equal("2030-07-16T00:00:00", body.GetProperty("items")[0].GetProperty("modifiedDate").GetString());
    }

    [Theory]
    [InlineData("orders")]
    [InlineData("quotations")]
    [InlineData("invoices")]
    public async Task AnonymousCustomerFamily_IsUnauthorizedBeforeDownstream(string family)
    {
        var downstream = new RecordingHandler(PageJson(family, 42));
        await using var factory = new CustomerHistoryBffFactory(family, downstream, [PermissionFor(family)]);
        using var client = CreateClient(factory);

        using var response = await client.GetAsync($"/bff/customers/42/{family}");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Null(downstream.PathAndQuery);
    }

    [Theory]
    [InlineData("orders")]
    [InlineData("quotations")]
    [InlineData("invoices")]
    public async Task MissingExactFamilyPermission_IsForbiddenBeforeDownstream(string family)
    {
        var downstream = new RecordingHandler(PageJson(family, 42));
        await using var factory = new CustomerHistoryBffFactory(
            family,
            downstream,
            [LegacyEmployeePermissions.CustomersRead]);
        using var client = CreateClient(factory);
        await SignInAsync(client);

        using var response = await client.GetAsync($"/bff/customers/42/{family}");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Null(downstream.PathAndQuery);
    }

    [Theory]
    [InlineData("orders")]
    [InlineData("quotations")]
    [InlineData("invoices")]
    public async Task InvalidCustomerId_IsRejectedBeforeDownstream(string family)
    {
        var downstream = new RecordingHandler(PageJson(family, 42));
        await using var factory = new CustomerHistoryBffFactory(family, downstream, [PermissionFor(family)]);
        using var client = CreateClient(factory);
        await SignInAsync(client);

        using var response = await client.GetAsync($"/bff/customers/0/{family}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Null(downstream.PathAndQuery);
    }

    [Theory]
    [InlineData("orders")]
    [InlineData("quotations")]
    [InlineData("invoices")]
    public async Task MismatchedCustomerItem_IsBadGatewayWithoutPayloadLeak(string family)
    {
        var downstream = new RecordingHandler(PageJson(family, 41, "owner-secret"));
        await using var factory = new CustomerHistoryBffFactory(family, downstream, [PermissionFor(family)]);
        using var client = CreateClient(factory);
        await SignInAsync(client);

        using var response = await client.GetAsync($"/bff/customers/42/{family}");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
        Assert.DoesNotContain("owner-secret", body, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("orders")]
    [InlineData("quotations")]
    [InlineData("invoices")]
    public async Task InvalidPayload_IsBadGatewayWithoutPayloadLeak(string family)
    {
        var downstream = new RecordingHandler("history-secret-not-json");
        await using var factory = new CustomerHistoryBffFactory(family, downstream, [PermissionFor(family)]);
        using var client = CreateClient(factory);
        await SignInAsync(client);

        using var response = await client.GetAsync($"/bff/customers/42/{family}");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
        Assert.DoesNotContain("history-secret-not-json", body, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("orders", HttpStatusCode.Unauthorized)]
    [InlineData("orders", HttpStatusCode.Forbidden)]
    [InlineData("quotations", HttpStatusCode.Unauthorized)]
    [InlineData("quotations", HttpStatusCode.Forbidden)]
    [InlineData("invoices", HttpStatusCode.Unauthorized)]
    [InlineData("invoices", HttpStatusCode.Forbidden)]
    public async Task DownstreamAuthorizationFailure_IsPreserved(string family, HttpStatusCode statusCode)
    {
        var downstream = new RecordingHandler("{}") { StatusCode = statusCode };
        await using var factory = new CustomerHistoryBffFactory(family, downstream, [PermissionFor(family)]);
        using var client = CreateClient(factory);
        await SignInAsync(client);

        using var response = await client.GetAsync($"/bff/customers/42/{family}");

        Assert.Equal(statusCode, response.StatusCode);
    }

    [Theory]
    [InlineData("orders")]
    [InlineData("quotations")]
    [InlineData("invoices")]
    public async Task NotFound_BecomesEmptyPageForRequestedIndex(string family)
    {
        var downstream = new RecordingHandler("{}") { StatusCode = HttpStatusCode.NotFound };
        await using var factory = new CustomerHistoryBffFactory(family, downstream, [PermissionFor(family)]);
        using var client = CreateClient(factory);
        await SignInAsync(client);

        using var response = await client.GetAsync($"/bff/customers/42/{family}?index=3");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var page = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(3, page.GetProperty("pageIndex").GetInt32());
        Assert.Empty(page.GetProperty("items").EnumerateArray());
        Assert.True(page.GetProperty("hasPreviousPage").GetBoolean());
    }

    [Theory]
    [InlineData("orders")]
    [InlineData("quotations")]
    [InlineData("invoices")]
    public async Task RateLimit_PreservesBoundedRetryAfterWithoutPayload(string family)
    {
        var downstream = new RecordingHandler("rate-limit-secret")
        {
            StatusCode = HttpStatusCode.TooManyRequests,
            RetryAfterSeconds = 2,
        };
        await using var factory = new CustomerHistoryBffFactory(family, downstream, [PermissionFor(family)]);
        using var client = CreateClient(factory);
        await SignInAsync(client);

        using var response = await client.GetAsync($"/bff/customers/42/{family}");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode);
        Assert.Equal(TimeSpan.FromSeconds(2), response.Headers.RetryAfter?.Delta);
        Assert.DoesNotContain("rate-limit-secret", body, StringComparison.Ordinal);
        Assert.Equal(1, downstream.RequestCount);
    }

    [Theory]
    [InlineData("orders", false)]
    [InlineData("orders", true)]
    [InlineData("quotations", false)]
    [InlineData("quotations", true)]
    [InlineData("invoices", false)]
    [InlineData("invoices", true)]
    public async Task TransportAndTimeoutFailures_AreServiceUnavailable(string family, bool timeout)
    {
        var downstream = new RecordingHandler("{}")
        {
            Exception = timeout
                ? new TaskCanceledException("history timeout")
                : new HttpRequestException("history unavailable"),
        };
        await using var factory = new CustomerHistoryBffFactory(family, downstream, [PermissionFor(family)]);
        using var client = CreateClient(factory);
        await SignInAsync(client);

        using var response = await client.GetAsync($"/bff/customers/42/{family}");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
    }

    [Theory]
    [InlineData("orders", "transport")]
    [InlineData("orders", "cancellation")]
    [InlineData("orders", "timeout")]
    [InlineData("quotations", "transport")]
    [InlineData("quotations", "cancellation")]
    [InlineData("quotations", "timeout")]
    [InlineData("invoices", "transport")]
    [InlineData("invoices", "cancellation")]
    [InlineData("invoices", "timeout")]
    public async Task BodyReadFailure_IsServiceUnavailableWithoutPayloadLeak(
        string family,
        string failure)
    {
        Exception exception = failure switch
        {
            "transport" => new HttpRequestException("body-read-transport-secret"),
            "cancellation" => new TaskCanceledException("body-read-cancellation-secret"),
            "timeout" => new Polly.Timeout.TimeoutRejectedException("body-read-timeout-secret"),
            _ => throw new ArgumentOutOfRangeException(nameof(failure)),
        };
        var downstream = new RecordingHandler("unused") { BodyReadException = exception };
        await using var factory = new CustomerHistoryBffFactory(family, downstream, [PermissionFor(family)]);
        using var client = CreateClient(factory);
        await SignInAsync(client);

        using var response = await client.GetAsync($"/bff/customers/42/{family}");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.DoesNotContain("body-read-", body, StringComparison.Ordinal);
    }

    private static string PermissionFor(string family) => family switch
    {
        "orders" => LegacyEmployeePermissions.OrdersRead,
        "quotations" => LegacyEmployeePermissions.QuotationsRead,
        "invoices" => LegacyEmployeePermissions.AccountingRead,
        _ => throw new ArgumentOutOfRangeException(nameof(family)),
    };

    private static string PageJson(string family, int customerId, string marker = "history-row") => family switch
    {
        "orders" => $$"""{"Items":[{"Id":84,"CustomerId":{{customerId}},"EmployeeId":7,"Name":"{{marker}}","ProcessId":3,"Quantity":2,"Manufactured":1,"Remaining":1,"Subtotal":225,"PromisedDate":"2030-07-20T00:00:00","AllowSocialMedia":false,"CreatedDate":"2030-07-15T00:00:00","ModifiedDate":"2030-07-16T00:00:00"}],"PageIndex":1,"TotalPages":1,"TotalRecords":1,"HasNextPage":false,"HasPreviousPage":false}""",
        "quotations" => $$"""{"Items":[{"Id":7,"CustomerId":{{customerId}},"EmployeeId":2,"InvoiceId":null,"Period":14,"ExpirationDate":"2030-08-01T00:00:00Z","Subtotal":1000.25,"Vat":70.02,"Total":1070.27,"WithholdingTax":30.00,"QuotedAmount":1040.27,"CurrencyId":1,"Comment":"{{marker}}","Fob":"Bangkok","ShippedVia":"Courier","Terms":"Net 7","Accepted":null,"CreatedDate":"2030-07-18T00:00:00Z","ModifiedDate":null}],"PageIndex":1,"TotalPages":1,"TotalRecords":1,"HasNextPage":false,"HasPreviousPage":false}""",
        "invoices" => $$"""{"Items":[{"Id":7,"CustomerId":{{customerId}},"Number":"{{marker}}","Currency":"THB","PurchaseOrderNumber":"PO-7","Subtotal":1000.25,"Vat":70.02,"Total":1070.27,"WithholdingTax":30.00,"Outstanding":1040.27,"IsPaid":false,"ReceiptId":null,"PaymentDate":null,"CreatedDate":"2030-07-18T00:00:00Z"}],"PageIndex":1,"TotalPages":1,"TotalRecords":1,"HasNextPage":false,"HasPreviousPage":false}""",
        _ => throw new ArgumentOutOfRangeException(nameof(family)),
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
        var session = await sessionResponse.Content.ReadFromJsonAsync<JsonElement>();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/bff/login")
        {
            Content = JsonContent.Create(new
            {
                email = "employee@maliev.com",
                password = "password",
                returnUrl = "/Customers/View?id=42",
            }),
        };
        request.Headers.Add("X-CSRF-TOKEN", session.GetProperty("csrfToken").GetString());
        using var response = await client.SendAsync(request);
        response.EnsureSuccessStatusCode();
    }

    private sealed class CustomerHistoryBffFactory(
        string family,
        RecordingHandler downstream,
        IReadOnlyList<string> permissions) : WebApplicationFactory<BffProgram>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing");
            TestJwtConfiguration.Configure(builder);
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<ILegacyAuthClient>();
                services.AddSingleton<ILegacyAuthClient>(new HistoryAuthClient(permissions));
                services.RemoveAll<IServiceAccessTokenProvider>();
                var tokenProvider = new HistoryServiceTokenProvider();
                services.AddSingleton<IServiceAccessTokenProvider>(tokenProvider);

                switch (family)
                {
                    case "orders":
                        services.RemoveAll<OrdersProxy>();
                        services.AddSingleton(new OrdersProxy(CreateDownstreamClient(downstream, tokenProvider, "http://order/")));
                        break;
                    case "quotations":
                        services.RemoveAll<QuotationsProxy>();
                        services.AddSingleton(new QuotationsProxy(CreateDownstreamClient(downstream, tokenProvider, "http://quotation/")));
                        break;
                    case "invoices":
                        services.RemoveAll<InvoicesProxy>();
                        services.AddSingleton(new InvoicesProxy(CreateDownstreamClient(downstream, tokenProvider, "http://accounting/")));
                        break;
                    default:
                        throw new ArgumentOutOfRangeException(nameof(family));
                }
            });
        }

        private static HttpClient CreateDownstreamClient(
            HttpMessageHandler downstream,
            IServiceAccessTokenProvider tokenProvider,
            string baseAddress)
        {
            var authHandler = new LegacyServiceAuthenticationHandler(tokenProvider) { InnerHandler = downstream };
            return new HttpClient(authHandler)
            {
                BaseAddress = new Uri(baseAddress),
                Timeout = TimeSpan.FromSeconds(10),
            };
        }
    }

    private sealed class HistoryServiceTokenProvider : IServiceAccessTokenProvider
    {
        public ValueTask<string?> GetAccessTokenAsync(CancellationToken cancellationToken) =>
            ValueTask.FromResult<string?>("signed-service-token");

        public void Invalidate(string token)
        {
        }
    }

    private sealed class HistoryAuthClient(IReadOnlyList<string> permissions) : ILegacyAuthClient
    {
        public Task<EmployeeLoginResult> LoginAsync(string email, string password, CancellationToken cancellationToken) =>
            Task.FromResult(new EmployeeLoginResult(
                true,
                new AuthTokenResponse(
                    "server-only-access-token",
                    "server-only-refresh-token",
                    "Bearer",
                    900,
                    DateTimeOffset.UtcNow.AddDays(1)),
                new EmployeeIdentity("employee-id", email, email, permissions, 7)));

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

    private sealed class RecordingHandler(string body) : HttpMessageHandler
    {
        public HttpStatusCode StatusCode { get; set; } = HttpStatusCode.OK;
        public int? RetryAfterSeconds { get; set; }
        public Exception? Exception { get; set; }
        public Exception? BodyReadException { get; set; }
        public string? PathAndQuery { get; private set; }
        public string? Authorization { get; private set; }
        public int RequestCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
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
                Content = BodyReadException is null
                    ? new StringContent(body, Encoding.UTF8, "application/json")
                    : new FaultingHttpContent(BodyReadException),
            };
            if (RetryAfterSeconds is not null)
            {
                response.Headers.RetryAfter = new(TimeSpan.FromSeconds(RetryAfterSeconds.Value));
            }

            return Task.FromResult(response);
        }
    }

    private sealed class FaultingHttpContent(Exception exception) : HttpContent
    {
        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) =>
            Task.FromException(exception);

        protected override bool TryComputeLength(out long length)
        {
            length = 0;
            return false;
        }
    }
}
