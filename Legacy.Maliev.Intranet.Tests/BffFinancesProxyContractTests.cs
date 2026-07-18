extern alias Bff;

using System.Net;
using System.Net.Http.Json;
using System.Text;
using Legacy.Maliev.Intranet.Auth;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using BffProgram = Bff::Program;
using FinancesProxy = Bff::Legacy.Maliev.Intranet.Bff.Accounting.FinancesProxy;
using FinanceFileProxy = Bff::Legacy.Maliev.Intranet.Bff.Accounting.FinanceFileProxy;

namespace Legacy.Maliev.Intranet.Tests;

public sealed class BffFinancesProxyContractTests
{
    [Fact]
    public async Task Update_ForwardsExactPaymentContractAndConcurrencyHeader()
    {
        var downstream = new RecordingFinanceHandler("{}");
        var proxy = new FinancesProxy(new HttpClient(downstream) { BaseAddress = new("http://accounting/") });
        var modified = new DateTime(2030, 7, 18, 8, 30, 0, DateTimeKind.Utc);

        using var response = await proxy.UpdateAsync(
            84,
            new(7, 1, 2, "fixture", 3, 123.45m, 1, "MALIEV", "TX-84", modified, modified),
            CancellationToken.None);

        Assert.Equal(HttpMethod.Put, downstream.Method);
        Assert.Equal("/payments/84", downstream.PathAndQuery);
        Assert.Equal(modified.ToString("O", System.Globalization.CultureInfo.InvariantCulture), downstream.IfUnmodifiedSince);
        Assert.Contains("\"amount\":123.45", downstream.Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task FileUpload_UsesServerOwnedPathAndStableAttempt()
    {
        var downstream = new RecordingFinanceHandler("{\"object\":[]}");
        var proxy = new FinanceFileProxy(new HttpClient(downstream) { BaseAddress = new("http://files/") });
        var attempt = Guid.NewGuid().ToString("D");
        var formFile = new FormFile(new MemoryStream(Encoding.UTF8.GetBytes("safe fixture")), 0, 12, "files", "receipt.pdf")
        {
            Headers = new HeaderDictionary(),
            ContentType = "application/pdf",
        };

        using var response = await proxy.UploadAsync(84, [formFile], attempt, CancellationToken.None);

        Assert.Equal(HttpMethod.Post, downstream.Method);
        Assert.Equal("/Uploads?bucket=maliev.com&path=accounting%2Fpayments%2F84", downstream.PathAndQuery);
        Assert.Equal(attempt, downstream.IdempotencyKey);
        Assert.Contains("receipt.pdf", downstream.Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task FinanceUpdate_RequiresCsrfBeforeCallingAccounting()
    {
        var downstream = new RecordingFinanceHandler("{}");
        await using var factory = new FinanceBffFactory(downstream, accountingRead: true);
        using var client = CreateClient(factory);
        await SignInAsync(client);
        using var request = new HttpRequestMessage(HttpMethod.Put, "/bff/finances/84")
        {
            Content = JsonContent.Create(UpdateFixture),
        };

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(0, downstream.RequestCount);
    }

    [Fact]
    public async Task FinanceUpdate_WithCsrfForwardsExactConcurrencyContract()
    {
        var downstream = new RecordingFinanceHandler("{}");
        await using var factory = new FinanceBffFactory(downstream, accountingRead: true);
        using var client = CreateClient(factory);
        var csrf = await SignInAsync(client);
        using var request = new HttpRequestMessage(HttpMethod.Put, "/bff/finances/84")
        {
            Content = JsonContent.Create(UpdateFixture),
        };
        request.Headers.Add("X-CSRF-TOKEN", csrf);

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(HttpMethod.Put, downstream.Method);
        Assert.Equal("/payments/84", downstream.PathAndQuery);
        Assert.Equal("2030-07-18T08:30:00.0000000Z", downstream.IfUnmodifiedSince);
    }

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

    private static async Task<string> SignInAsync(HttpClient client)
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
        using var signedIn = await client.GetAsync("/bff/session");
        var current = await signedIn.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        return current.GetProperty("csrfToken").GetString() ?? throw new InvalidOperationException("Missing CSRF token.");
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
                    accountingRead ?
                    [
                        LegacyEmployeePermissions.AccountingRead,
                        LegacyEmployeePermissions.AccountingUpdate,
                        LegacyEmployeePermissions.AccountingDelete,
                        LegacyEmployeePermissions.AccountingFilesRead,
                        LegacyEmployeePermissions.AccountingFilesWrite,
                        LegacyEmployeePermissions.AccountingFilesDelete,
                        LegacyEmployeePermissions.FileUploadsRead,
                        LegacyEmployeePermissions.FileUploadsCreate,
                        LegacyEmployeePermissions.FileUploadsDelete,
                    ] : [],
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
        public HttpMethod? Method { get; private set; }
        public string? IfUnmodifiedSince { get; private set; }
        public string? IdempotencyKey { get; private set; }
        public string Body { get; private set; } = string.Empty;
        public int RequestCount { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestCount++;
            Method = request.Method;
            PathAndQuery = request.RequestUri?.PathAndQuery;
            Authorization = request.Headers.Authorization?.ToString();
            IfUnmodifiedSince = request.Headers.TryGetValues("If-Unmodified-Since", out var expected) ? expected.Single() : null;
            IdempotencyKey = request.Headers.TryGetValues("Idempotency-Key", out var idempotency) ? idempotency.Single() : null;
            Body = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);
            var response = new HttpResponseMessage(StatusCode)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json"),
            };
            if (RetryAfterSeconds is not null)
            {
                response.Headers.RetryAfter = new(TimeSpan.FromSeconds(RetryAfterSeconds.Value));
            }

            return response;
        }
    }

    private const string PaymentPageJson =
        """{"Items":[{"Id":7,"EmployeeId":3,"PaymentDirectionId":1,"PaymentTypeId":2,"Description":"Thai fixture","PaymentMethodId":4,"Amount":1234.56,"CurrencyId":1,"Recipient":"MALIEV","TransactionNumber":"TX-7","PaymentDate":"2030-07-18T00:00:00Z","CreatedDate":"2030-07-17T00:00:00Z","ModifiedDate":null,"PaymentDirection":{"Name":"Income"},"PaymentFile":[{"Id":99}]}],"PageIndex":1,"TotalPages":1,"TotalRecords":1,"HasNextPage":false,"HasPreviousPage":false}""";
    private const string SummaryJson =
        """{"Details":[{"CurrencyId":"1","CurrentAmount":1500.25,"PreviousAmount":1200.00,"DeltaAmount":300.25,"DeltaPercent":25.02}]}""";
    private static readonly object UpdateFixture = new
    {
        employeeId = 7,
        paymentDirectionId = 1,
        paymentTypeId = 2,
        description = "fixture",
        paymentMethodId = 3,
        amount = 123.45m,
        currencyId = 1,
        recipient = "MALIEV",
        transactionNumber = "TX-84",
        paymentDate = new DateTime(2030, 7, 18, 8, 30, 0, DateTimeKind.Utc),
        modifiedDate = new DateTime(2030, 7, 18, 8, 30, 0, DateTimeKind.Utc),
    };
}
