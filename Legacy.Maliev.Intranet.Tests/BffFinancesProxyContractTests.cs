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
using FinancesProxy = Bff::Legacy.Maliev.Intranet.Bff.Accounting.FinancesProxy;

namespace Legacy.Maliev.Intranet.Tests;

public sealed class BffFinancesProxyContractTests
{
    [Fact]
    public async Task AuthorizedEmployee_ForwardsBoundedQueryAndServerOnlyToken()
    {
        var downstream = new RecordingFinanceHandler(PaymentPageJson);
        await using var factory = new FinanceBffFactory(downstream, accountingRead: true);
        using var client = CreateClient(factory);
        await SignInAsync(client);

        using var response = await client.GetAsync("/bff/finances?sort=PaymentDate_Descending&search=Thai%20fixture&index=-4&size=999");
        var json = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("/payments?sort=PaymentDate_Descending&search=Thai%20fixture&index=1&size=100", downstream.PathAndQuery);
        Assert.Equal("Bearer signed-service-token", downstream.Authorization);
        Assert.Contains("\"amount\":1234.56", json, StringComparison.Ordinal);
        Assert.DoesNotContain("paymentDirection\"", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("paymentFile", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("signed-service-token", json, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("/bff/finances/summaries/weekly", "/payments/summaries/weekly")]
    [InlineData("/bff/finances/summaries/monthly", "/payments/summaries/monthly")]
    [InlineData("/bff/finances/summaries/monthly-job-income", "/payments/summaries/monthly/income/job")]
    [InlineData("/bff/finances/summaries/yearly", "/payments/summaries/yearly")]
    public async Task SummaryEndpoints_UseExactAllowlistedRoutes(string bffPath, string servicePath)
    {
        var downstream = new RecordingFinanceHandler(SummaryJson);
        await using var factory = new FinanceBffFactory(downstream, accountingRead: true);
        using var client = CreateClient(factory);
        await SignInAsync(client);

        using var response = await client.GetAsync(bffPath);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(servicePath, downstream.PathAndQuery);
    }

    [Fact]
    public async Task MissingAccountingRead_IsForbiddenBeforeDownstream()
    {
        var downstream = new RecordingFinanceHandler(PaymentPageJson);
        await using var factory = new FinanceBffFactory(downstream, accountingRead: false);
        using var client = CreateClient(factory);
        await SignInAsync(client);

        using var response = await client.GetAsync("/bff/finances");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Null(downstream.PathAndQuery);
    }

    [Fact]
    public async Task AnonymousRequest_IsUnauthorizedBeforeDownstream()
    {
        var downstream = new RecordingFinanceHandler(PaymentPageJson);
        await using var factory = new FinanceBffFactory(downstream, accountingRead: true);
        using var client = CreateClient(factory);

        using var response = await client.GetAsync("/bff/finances");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Null(downstream.PathAndQuery);
    }

    [Fact]
    public async Task InvalidAccountingPayload_IsBadGatewayWithoutPayloadLeak()
    {
        var downstream = new RecordingFinanceHandler("accounting-secret-not-json");
        await using var factory = new FinanceBffFactory(downstream, accountingRead: true);
        using var client = CreateClient(factory);
        await SignInAsync(client);

        using var response = await client.GetAsync("/bff/finances");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
        Assert.DoesNotContain("accounting-secret-not-json", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RateLimit_PreservesBoundedRetryAfterWithoutRetry()
    {
        var downstream = new RecordingFinanceHandler("{}")
        {
            StatusCode = HttpStatusCode.TooManyRequests,
            RetryAfterSeconds = 3,
        };
        await using var factory = new FinanceBffFactory(downstream, accountingRead: true);
        using var client = CreateClient(factory);
        await SignInAsync(client);

        using var response = await client.GetAsync("/bff/finances");

        Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode);
        Assert.Equal(TimeSpan.FromSeconds(3), response.Headers.RetryAfter?.Delta);
        Assert.Equal(1, downstream.RequestCount);
    }

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
            Content = JsonContent.Create(new { email = "employee@maliev.com", password = "password", returnUrl = "/Finances/Index" }),
        };
        request.Headers.Add("X-CSRF-TOKEN", session.GetProperty("csrfToken").GetString());
        using var response = await client.SendAsync(request);
        response.EnsureSuccessStatusCode();
    }

    private sealed class FinanceBffFactory(HttpMessageHandler downstream, bool accountingRead)
        : WebApplicationFactory<BffProgram>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing");
            TestJwtConfiguration.Configure(builder);
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<ILegacyAuthClient>();
                services.AddSingleton<ILegacyAuthClient>(new FinanceAuthClient(accountingRead));
                services.RemoveAll<IServiceAccessTokenProvider>();
                var tokenProvider = new FinanceServiceTokenProvider();
                services.AddSingleton<IServiceAccessTokenProvider>(tokenProvider);
                services.RemoveAll<FinancesProxy>();
                var authHandler = new LegacyServiceAuthenticationHandler(tokenProvider) { InnerHandler = downstream };
                services.AddSingleton(new FinancesProxy(new HttpClient(authHandler)
                {
                    BaseAddress = new Uri("http://accounting/"),
                    Timeout = TimeSpan.FromSeconds(10),
                }));
            });
        }
    }

    private sealed class FinanceServiceTokenProvider : IServiceAccessTokenProvider
    {
        public ValueTask<string?> GetAccessTokenAsync(CancellationToken cancellationToken) =>
            ValueTask.FromResult<string?>("signed-service-token");

        public void Invalidate(string token)
        {
        }
    }

    private sealed class FinanceAuthClient(bool accountingRead) : ILegacyAuthClient
    {
        public Task<EmployeeLoginResult> LoginAsync(string email, string password, CancellationToken cancellationToken) =>
            Task.FromResult(new EmployeeLoginResult(
                true,
                new AuthTokenResponse("server-only-access-token", "server-only-refresh-token", "Bearer", 900, DateTimeOffset.UtcNow.AddDays(1)),
                new EmployeeIdentity(
                    "employee-id",
                    email,
                    email,
                    accountingRead ? [LegacyEmployeePermissions.AccountingRead] : [],
                    7)));

        public Task<EmployeeRefreshResult?> RefreshAsync(string refreshToken, CancellationToken cancellationToken) => Task.FromResult<EmployeeRefreshResult?>(null);
        public Task RevokeAsync(string refreshToken, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<CustomerIdentityResponse?> CreateCustomerIdentityAsync(int databaseId, CreateCustomerIdentityRequest request, string accessToken, CancellationToken cancellationToken) => Task.FromResult<CustomerIdentityResponse?>(null);
        public Task<EmployeeIdentityResponse?> CreateEmployeeIdentityAsync(int databaseId, CreateEmployeeIdentityRequest request, string accessToken, CancellationToken cancellationToken) => Task.FromResult<EmployeeIdentityResponse?>(null);
    }

    private sealed class RecordingFinanceHandler(string body) : HttpMessageHandler
    {
        public HttpStatusCode StatusCode { get; set; } = HttpStatusCode.OK;
        public int? RetryAfterSeconds { get; set; }
        public string? PathAndQuery { get; private set; }
        public string? Authorization { get; private set; }
        public int RequestCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestCount++;
            PathAndQuery = request.RequestUri?.PathAndQuery;
            Authorization = request.Headers.Authorization?.ToString();
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

    private const string PaymentPageJson =
        """{"Items":[{"Id":7,"EmployeeId":3,"PaymentDirectionId":1,"PaymentTypeId":2,"Description":"Thai fixture","PaymentMethodId":4,"Amount":1234.56,"CurrencyId":1,"Recipient":"MALIEV","TransactionNumber":"TX-7","PaymentDate":"2030-07-18T00:00:00Z","CreatedDate":"2030-07-17T00:00:00Z","ModifiedDate":null,"PaymentDirection":{"Name":"Income"},"PaymentFile":[{"Id":99}]}],"PageIndex":1,"TotalPages":1,"TotalRecords":1,"HasNextPage":false,"HasPreviousPage":false}""";
    private const string SummaryJson =
        """{"Details":[{"CurrencyId":"1","CurrentAmount":1500.25,"PreviousAmount":1200.00,"DeltaAmount":300.25,"DeltaPercent":25.02}]}""";
}
