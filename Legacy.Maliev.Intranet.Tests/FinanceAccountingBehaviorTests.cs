extern alias Bff;

using System.Collections.Concurrent;
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
using FinanceDetailAggregator = Bff::Legacy.Maliev.Intranet.Bff.Accounting.FinanceDetailAggregator;
using FinanceFileProxy = Bff::Legacy.Maliev.Intranet.Bff.Accounting.FinanceFileProxy;
using FinancesProxy = Bff::Legacy.Maliev.Intranet.Bff.Accounting.FinancesProxy;
using InvoiceCreationProxy = Bff::Legacy.Maliev.Intranet.Bff.Accounting.InvoiceCreationProxy;
using InvoiceDetailAggregator = Bff::Legacy.Maliev.Intranet.Bff.Accounting.InvoiceDetailAggregator;
using InvoiceDetailProxy = Bff::Legacy.Maliev.Intranet.Bff.Accounting.InvoiceDetailProxy;
using InvoiceFileProxy = Bff::Legacy.Maliev.Intranet.Bff.Accounting.InvoiceFileProxy;
using OrderCatalogReferenceProxy = Bff::Legacy.Maliev.Intranet.Bff.Orders.OrderCatalogReferenceProxy;
using OrderEmployeeReferenceProxy = Bff::Legacy.Maliev.Intranet.Bff.Orders.OrderEmployeeReferenceProxy;

namespace Legacy.Maliev.Intranet.Tests;

public sealed class FinanceAccountingBehaviorTests
{
    [Fact]
    public async Task Detail_AggregatesExactServiceBoundariesAndReturnsSignedFile()
    {
        var accounting = AccountingBehaviorTestHost.Routes(request => (request.Method.Method, request.RequestUri?.AbsolutePath) switch
        {
            ("GET", "/payments/84") => AccountingBehaviorTestHost.Json(PaymentJson),
            ("GET", "/payments/directions") => AccountingBehaviorTestHost.Json("""[{"id":1,"name":"Income"}]"""),
            ("GET", "/payments/types") => AccountingBehaviorTestHost.Json("""[{"id":2,"name":"Job"}]"""),
            ("GET", "/payments/methods") => AccountingBehaviorTestHost.Json("""[{"id":3,"name":"Transfer"}]"""),
            ("GET", "/payments/84/files") => AccountingBehaviorTestHost.Json(FinanceFilesJson),
            _ => new(HttpStatusCode.NotFound),
        });
        var files = AccountingBehaviorTestHost.Routes(request =>
            request.Method == HttpMethod.Get && request.RequestUri?.AbsolutePath == "/uploads/SignedUrl"
                ? AccountingBehaviorTestHost.Json("\"https://storage.test/clean/receipt.pdf\"")
                : new(HttpStatusCode.NotFound));
        var employees = AccountingBehaviorTestHost.Routes(request =>
            request.Method == HttpMethod.Get && request.RequestUri?.PathAndQuery == "/employees?sort=EmployeeId_Ascending&search=&index=1&size=250"
                ? AccountingBehaviorTestHost.Json("""{"items":[{"id":7,"fullName":"Natthapol V."}]}""")
                : new(HttpStatusCode.NotFound));
        var catalog = AccountingBehaviorTestHost.Routes(request =>
            request.Method == HttpMethod.Get && request.RequestUri?.PathAndQuery == "/Currencies"
                ? AccountingBehaviorTestHost.Json("""[{"id":1,"shortName":"THB"}]""")
                : new(HttpStatusCode.NotFound));
        await using var factory = AccountingBehaviorTestHost.CreateFactory(accounting, files, employees, catalog);
        using var client = AccountingBehaviorTestHost.CreateClient(factory);
        await AccountingBehaviorTestHost.SignInAsync(client);

        using var response = await client.GetAsync("/bff/finances/84");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("\"amount\":1234.56", body, StringComparison.Ordinal);
        Assert.Contains("\"name\":\"Natthapol V.\"", body, StringComparison.Ordinal);
        Assert.Contains("\"shortName\":\"THB\"", body, StringComparison.Ordinal);
        Assert.Contains("https://storage.test/clean/receipt.pdf", body, StringComparison.Ordinal);
        Assert.Equal(
            [
                "GET /payments/84",
                "GET /payments/84/files",
                "GET /payments/directions",
                "GET /payments/methods",
                "GET /payments/types",
            ],
            accounting.Requests.Select(RequestBoundary).Order(StringComparer.Ordinal));
        Assert.All(accounting.Requests, request => Assert.Equal("Bearer signed-service-token", request.Authorization));
        Assert.Equal("GET /uploads/SignedUrl?bucket=maliev.com&objectName=accounting%2Fpayments%2F84%2Freceipt.pdf", RequestBoundary(Assert.Single(files.Requests)));
        Assert.Equal("GET /employees?sort=EmployeeId_Ascending&search=&index=1&size=250", RequestBoundary(Assert.Single(employees.Requests)));
        Assert.Equal("GET /Currencies", RequestBoundary(Assert.Single(catalog.Requests)));
    }

    [Fact]
    public async Task CreateLookup_AggregatesOnlyAllowlistedReferenceRoutes()
    {
        var accounting = AccountingBehaviorTestHost.Routes(request => (request.Method.Method, request.RequestUri?.AbsolutePath) switch
        {
            ("GET", "/payments/directions") => AccountingBehaviorTestHost.Json("""[{"id":1,"name":"Income"}]"""),
            ("GET", "/payments/types") => AccountingBehaviorTestHost.Json("""[{"id":2,"name":"Job"}]"""),
            ("GET", "/payments/methods") => AccountingBehaviorTestHost.Json("""[{"id":3,"name":"Transfer"}]"""),
            _ => new(HttpStatusCode.NotFound),
        });
        var employees = AccountingBehaviorTestHost.Routes(request =>
            request.Method == HttpMethod.Get && request.RequestUri?.PathAndQuery == "/employees?sort=EmployeeId_Ascending&search=&index=1&size=250"
                ? AccountingBehaviorTestHost.Json("""{"items":[{"id":7,"fullName":"Natthapol V."}]}""")
                : new(HttpStatusCode.NotFound));
        var catalog = AccountingBehaviorTestHost.Routes(request =>
            request.Method == HttpMethod.Get && request.RequestUri?.PathAndQuery == "/Currencies"
                ? AccountingBehaviorTestHost.Json("""[{"id":1,"shortName":"THB"}]""")
                : new(HttpStatusCode.NotFound));
        await using var factory = AccountingBehaviorTestHost.CreateFactory(accounting, employees: employees, catalog: catalog);
        using var client = AccountingBehaviorTestHost.CreateClient(factory);
        await AccountingBehaviorTestHost.SignInAsync(client);

        using var response = await client.GetAsync("/bff/finances/create");
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Natthapol V.", json.GetProperty("employees")[0].GetProperty("name").GetString());
        Assert.Equal("Income", json.GetProperty("directions")[0].GetProperty("name").GetString());
        Assert.Equal("Job", json.GetProperty("types")[0].GetProperty("name").GetString());
        Assert.Equal("Transfer", json.GetProperty("methods")[0].GetProperty("name").GetString());
        Assert.Equal("THB", json.GetProperty("currencies")[0].GetProperty("shortName").GetString());
        Assert.Equal(
            ["GET /payments/directions", "GET /payments/methods", "GET /payments/types"],
            accounting.Requests.Select(RequestBoundary).Order(StringComparer.Ordinal));
        Assert.Equal("GET /employees?sort=EmployeeId_Ascending&search=&index=1&size=250", RequestBoundary(Assert.Single(employees.Requests)));
        Assert.Equal("GET /Currencies", RequestBoundary(Assert.Single(catalog.Requests)));
    }

    [Fact]
    public async Task Create_ValidMultipart_ForwardsAuthoritativePaymentShapeAndStableOperation()
    {
        var accounting = AccountingBehaviorTestHost.Routes(request =>
            request.Method == HttpMethod.Post && request.RequestUri?.AbsolutePath == "/payments"
                ? AccountingBehaviorTestHost.Json("{\"id\":91}", HttpStatusCode.Created)
                : new(HttpStatusCode.NotFound));
        await using var factory = AccountingBehaviorTestHost.CreateFactory(accounting);
        using var client = AccountingBehaviorTestHost.CreateClient(factory);
        var csrf = await AccountingBehaviorTestHost.SignInAsync(client);
        var operationId = Guid.Parse("32407927-a825-4717-a1c9-3e329eef843d");
        using var form = new MultipartFormDataContent();
        form.Add(new StringContent(PaymentCreateJson, Encoding.UTF8, "application/json"), "payment");
        using var request = new HttpRequestMessage(HttpMethod.Post, "/bff/finances") { Content = form };
        request.Headers.Add("X-CSRF-TOKEN", csrf);
        request.Headers.Add("Idempotency-Key", operationId.ToString("D"));

        using var response = await client.SendAsync(request);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(91, json.GetProperty("id").GetInt32());
        var forwarded = Assert.Single(accounting.Requests);
        Assert.Equal("POST", forwarded.Method);
        Assert.Equal("/payments", forwarded.Path);
        Assert.Equal(operationId.ToString("D"), forwarded.IdempotencyKey);
        using var body = JsonDocument.Parse(forwarded.Body!);
        Assert.Equal(
            [
                "amount",
                "createdDate",
                "currencyId",
                "description",
                "employeeId",
                "modifiedDate",
                "paymentDate",
                "paymentDirectionId",
                "paymentMethodId",
                "paymentTypeId",
                "recipient",
                "transactionNumber",
            ],
            body.RootElement.EnumerateObject().Select(property => property.Name).Order());
        Assert.Equal(7, body.RootElement.GetProperty("employeeId").GetInt32());
        Assert.Equal(1, body.RootElement.GetProperty("paymentDirectionId").GetInt32());
        Assert.Equal(2, body.RootElement.GetProperty("paymentTypeId").GetInt32());
        Assert.Equal("THB transfer", body.RootElement.GetProperty("description").GetString());
        Assert.Equal(3, body.RootElement.GetProperty("paymentMethodId").GetInt32());
        Assert.Equal(1234.56m, body.RootElement.GetProperty("amount").GetDecimal());
        Assert.Equal(1, body.RootElement.GetProperty("currencyId").GetInt32());
        Assert.Equal("MALIEV", body.RootElement.GetProperty("recipient").GetString());
        Assert.Equal("TX-91", body.RootElement.GetProperty("transactionNumber").GetString());
        Assert.Equal(
            DateTime.Parse("2030-07-18T00:00:00Z").ToUniversalTime(),
            body.RootElement.GetProperty("paymentDate").GetDateTime().ToUniversalTime());
        var createdDate = body.RootElement.GetProperty("createdDate").GetDateTime();
        var modifiedDate = body.RootElement.GetProperty("modifiedDate").GetDateTime();
        Assert.Equal(DateTimeKind.Utc, createdDate.Kind);
        Assert.Equal(createdDate, modifiedDate);
    }

    [Fact]
    public async Task Create_WithoutCsrf_IsRejectedBeforeAccountingWrite()
    {
        var accounting = AccountingBehaviorTestHost.Routes(_ => AccountingBehaviorTestHost.Json("{\"id\":91}"));
        await using var factory = AccountingBehaviorTestHost.CreateFactory(accounting);
        using var client = AccountingBehaviorTestHost.CreateClient(factory);
        await AccountingBehaviorTestHost.SignInAsync(client);
        using var form = new MultipartFormDataContent();
        form.Add(new StringContent(PaymentCreateJson, Encoding.UTF8, "application/json"), "payment");
        using var request = new HttpRequestMessage(HttpMethod.Post, "/bff/finances") { Content = form };
        request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString("D"));

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(accounting.Requests);
    }

    [Fact]
    public async Task Upload_RetryUsesStableDistinctUploadAndLinkOperations()
    {
        var accounting = AccountingBehaviorTestHost.Routes(request => request.RequestUri?.AbsolutePath switch
        {
            "/payments/84" when request.Method == HttpMethod.Get => AccountingBehaviorTestHost.Json(PaymentJson),
            "/payments/files" when request.Method == HttpMethod.Post => AccountingBehaviorTestHost.Json(
                """{"id":17,"paymentId":84,"bucket":"maliev.com","objectName":"accounting/payments/84/receipt.pdf","createdDate":"2030-07-18T00:00:00Z","uri":null}""",
                HttpStatusCode.Created),
            _ => new(HttpStatusCode.NotFound),
        });
        var files = AccountingBehaviorTestHost.Routes(request =>
            request.Method == HttpMethod.Post && request.RequestUri?.AbsolutePath == "/Uploads"
                ? AccountingBehaviorTestHost.Json(
                    """{"object":[{"bucket":"maliev.com","objectName":"accounting/payments/84/receipt.pdf","uri":"https://storage.test/clean/receipt.pdf"}]}""")
                : new(HttpStatusCode.NotFound));
        await using var factory = AccountingBehaviorTestHost.CreateFactory(accounting, files);
        using var client = AccountingBehaviorTestHost.CreateClient(factory);
        var csrf = await AccountingBehaviorTestHost.SignInAsync(client);
        var operationId = Guid.Parse("72c35c7d-17f6-4a70-be6d-482021bbb06f");
        async Task<HttpStatusCode> UploadOnceAsync()
        {
            using var form = new MultipartFormDataContent();
            form.Add(new ByteArrayContent("safe-pdf"u8.ToArray()), "files", "receipt.pdf");
            using var request = new HttpRequestMessage(HttpMethod.Post, "/bff/finances/84/files") { Content = form };
            request.Headers.Add("X-CSRF-TOKEN", csrf);
            request.Headers.Add("Idempotency-Key", operationId.ToString("D"));
            using var response = await client.SendAsync(request);
            return response.StatusCode;
        }

        Assert.Equal(HttpStatusCode.OK, await UploadOnceAsync());
        Assert.Equal(HttpStatusCode.OK, await UploadOnceAsync());

        var uploads = files.Requests.ToArray();
        Assert.Equal(2, uploads.Length);
        Assert.All(uploads, upload => Assert.Equal("POST /Uploads?bucket=maliev.com&path=accounting%2Fpayments%2F84", RequestBoundary(upload)));
        Assert.Equal(uploads[0].IdempotencyKey, uploads[1].IdempotencyKey);
        var links = accounting.Requests.Where(request => request.Path == "/payments/files").ToArray();
        Assert.Equal(2, links.Length);
        Assert.All(links, link =>
        {
            Assert.Equal("POST /payments/files", RequestBoundary(link));
            Assert.Contains("\"paymentId\":84", link.Body, StringComparison.Ordinal);
            Assert.Contains("\"objectName\":\"accounting/payments/84/receipt.pdf\"", link.Body, StringComparison.Ordinal);
        });
        Assert.Equal(links[0].IdempotencyKey, links[1].IdempotencyKey);
        Assert.NotEqual(operationId.ToString("D"), uploads[0].IdempotencyKey);
        Assert.NotEqual(uploads[0].IdempotencyKey, links[0].IdempotencyKey);
    }

    [Fact]
    public async Task Delete_RemovesStorageThenMetadataThenPayment()
    {
        var order = new ConcurrentQueue<string>();
        var accounting = AccountingBehaviorTestHost.Routes(request =>
        {
            order.Enqueue($"accounting:{request.Method}:{request.RequestUri?.AbsolutePath}");
            return request.RequestUri?.AbsolutePath switch
            {
                "/payments/84/files" when request.Method == HttpMethod.Get => AccountingBehaviorTestHost.Json(FinanceFilesJson),
                "/payments/files/17" when request.Method == HttpMethod.Delete => new(HttpStatusCode.NoContent),
                "/payments/84" when request.Method == HttpMethod.Delete => new(HttpStatusCode.NoContent),
                _ => new(HttpStatusCode.NotFound),
            };
        });
        var files = AccountingBehaviorTestHost.Routes(request =>
        {
            order.Enqueue($"files:{request.Method}:{request.RequestUri?.AbsolutePath}");
            return new(HttpStatusCode.NoContent);
        });
        await using var factory = AccountingBehaviorTestHost.CreateFactory(accounting, files);
        using var client = AccountingBehaviorTestHost.CreateClient(factory);
        var csrf = await AccountingBehaviorTestHost.SignInAsync(client);
        using var request = new HttpRequestMessage(HttpMethod.Delete, "/bff/finances/84");
        request.Headers.Add("X-CSRF-TOKEN", csrf);

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(
            [
                "accounting:GET:/payments/84/files",
                "files:DELETE:/Uploads",
                "accounting:DELETE:/payments/files/17",
                "accounting:DELETE:/payments/84",
            ],
            order);
        Assert.Equal("/Uploads?bucket=maliev.com&objectName=accounting%2Fpayments%2F84%2Freceipt.pdf", Assert.Single(files.Requests).Path);
    }

    [Theory]
    [InlineData("/bff/finances/summaries/weekly", "{\"details\":null}")]
    [InlineData("/bff/finances/trends/yearly-income?year=2030&currencyId=1", "not-json")]
    public async Task SummaryAndTrend_MalformedPayload_IsBadGateway(string path, string payload)
    {
        var accounting = AccountingBehaviorTestHost.Routes(_ => AccountingBehaviorTestHost.Json(payload));
        await using var factory = AccountingBehaviorTestHost.CreateFactory(accounting);
        using var client = AccountingBehaviorTestHost.CreateClient(factory);
        await AccountingBehaviorTestHost.SignInAsync(client);

        using var response = await client.GetAsync(path);

        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
    }

    private const string PaymentCreateJson =
        """{"employeeId":7,"paymentDirectionId":1,"paymentTypeId":2,"description":"THB transfer","paymentMethodId":3,"amount":1234.56,"currencyId":1,"recipient":"MALIEV","transactionNumber":"TX-91","paymentDate":"2030-07-18T00:00:00Z"}""";

    internal const string PaymentJson =
        """{"id":84,"employeeId":7,"paymentDirectionId":1,"paymentTypeId":2,"description":"THB transfer","paymentMethodId":3,"amount":1234.56,"currencyId":1,"recipient":"MALIEV","transactionNumber":"TX-84","paymentDate":"2030-07-18T00:00:00Z","createdDate":"2030-07-17T00:00:00Z","modifiedDate":"2030-07-18T00:00:00Z"}""";

    internal const string FinanceFilesJson =
        """[{"id":17,"paymentId":84,"bucket":"maliev.com","objectName":"accounting/payments/84/receipt.pdf","createdDate":"2030-07-18T00:00:00Z"}]""";

    private static string RequestBoundary(AccountingBehaviorTestHost.RequestRecord request) => $"{request.Method} {request.Path}";
}

internal static class AccountingBehaviorTestHost
{
    private static readonly string[] Permissions =
    [
        LegacyEmployeePermissions.AccountingRead,
        LegacyEmployeePermissions.AccountingCreate,
        LegacyEmployeePermissions.AccountingUpdate,
        LegacyEmployeePermissions.AccountingDelete,
        LegacyEmployeePermissions.AccountingFilesRead,
        LegacyEmployeePermissions.AccountingFilesWrite,
        LegacyEmployeePermissions.AccountingFilesDelete,
        LegacyEmployeePermissions.FileUploadsRead,
        LegacyEmployeePermissions.FileUploadsCreate,
        LegacyEmployeePermissions.FileUploadsDelete,
    ];

    public static RecordingRouteHandler Routes(Func<HttpRequestMessage, HttpResponseMessage> response) => new(response);

    public static HttpResponseMessage Json(string json, HttpStatusCode status = HttpStatusCode.OK) => new(status)
    {
        Content = new StringContent(json, Encoding.UTF8, "application/json"),
    };

    public static WebApplicationFactory<BffProgram> CreateFactory(
        RecordingRouteHandler accounting,
        RecordingRouteHandler? files = null,
        RecordingRouteHandler? employees = null,
        RecordingRouteHandler? catalog = null) =>
        new Factory(
            accounting,
            files ?? Routes(_ => new(HttpStatusCode.NotFound)),
            employees ?? Routes(_ => Json("""{"items":[{"id":7,"fullName":"Natthapol V."}]}""")),
            catalog ?? Routes(_ => Json("""[{"id":1,"shortName":"THB"}]""")));

    public static HttpClient CreateClient(WebApplicationFactory<BffProgram> factory) =>
        factory.CreateClient(new()
        {
            AllowAutoRedirect = false,
            BaseAddress = new Uri("https://localhost"),
            HandleCookies = true,
        });

    public static async Task<string> SignInAsync(HttpClient client)
    {
        using var sessionResponse = await client.GetAsync("/bff/session");
        var session = await sessionResponse.Content.ReadFromJsonAsync<JsonElement>();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/bff/login")
        {
            Content = JsonContent.Create(new
            {
                email = "employee@maliev.com",
                password = "password",
                returnUrl = "/Finances/Index",
            }),
        };
        request.Headers.Add("X-CSRF-TOKEN", session.GetProperty("csrfToken").GetString());
        using var response = await client.SendAsync(request);
        response.EnsureSuccessStatusCode();
        using var signedInResponse = await client.GetAsync("/bff/session");
        var signedIn = await signedInResponse.Content.ReadFromJsonAsync<JsonElement>();
        return signedIn.GetProperty("csrfToken").GetString()
            ?? throw new InvalidOperationException("The BFF did not issue an antiforgery token.");
    }

    internal sealed class RecordingRouteHandler(Func<HttpRequestMessage, HttpResponseMessage> response) : HttpMessageHandler
    {
        private readonly object sync = new();
        private readonly List<RequestRecord> requests = [];

        public IReadOnlyList<RequestRecord> Requests
        {
            get
            {
                lock (sync)
                {
                    return requests.ToArray();
                }
            }
        }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            var record = new RequestRecord(
                request.Method.Method,
                request.RequestUri?.PathAndQuery,
                request.Headers.Authorization?.ToString(),
                request.Headers.TryGetValues("Idempotency-Key", out var keys) ? keys.Single() : null,
                request.Headers.TryGetValues("If-Unmodified-Since", out var versions) ? versions.Single() : null,
                body);
            lock (sync)
            {
                requests.Add(record);
            }

            return response(request);
        }
    }

    internal sealed record RequestRecord(
        string Method,
        string? Path,
        string? Authorization,
        string? IdempotencyKey,
        string? IfUnmodifiedSince,
        string? Body);

    private sealed class Factory(
        RecordingRouteHandler accounting,
        RecordingRouteHandler files,
        RecordingRouteHandler employees,
        RecordingRouteHandler catalog) : WebApplicationFactory<BffProgram>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing");
            TestJwtConfiguration.Configure(builder);
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<ILegacyAuthClient>();
                services.AddSingleton<ILegacyAuthClient>(new AccountingAuthClient());
                services.RemoveAll<IServiceAccessTokenProvider>();
                var tokenProvider = new AccountingTokenProvider();
                services.AddSingleton<IServiceAccessTokenProvider>(tokenProvider);

                var finances = new FinancesProxy(Client(accounting, "http://accounting/", tokenProvider));
                var financeFiles = new FinanceFileProxy(Client(files, "http://files/", tokenProvider));
                var employeeReferences = new OrderEmployeeReferenceProxy(Client(employees, "http://employees/", tokenProvider));
                var catalogReferences = new OrderCatalogReferenceProxy(Client(catalog, "http://catalog/", tokenProvider));
                var invoiceDetails = new InvoiceDetailProxy(Client(accounting, "http://accounting/", tokenProvider));
                var invoiceFiles = new InvoiceFileProxy(Client(files, "http://files/", tokenProvider));
                var invoiceCreation = new InvoiceCreationProxy(Client(accounting, "http://accounting/", tokenProvider));

                services.RemoveAll<FinancesProxy>();
                services.RemoveAll<FinanceFileProxy>();
                services.RemoveAll<OrderEmployeeReferenceProxy>();
                services.RemoveAll<OrderCatalogReferenceProxy>();
                services.RemoveAll<FinanceDetailAggregator>();
                services.RemoveAll<InvoiceDetailProxy>();
                services.RemoveAll<InvoiceFileProxy>();
                services.RemoveAll<InvoiceCreationProxy>();
                services.RemoveAll<InvoiceDetailAggregator>();
                services.AddSingleton(finances);
                services.AddSingleton(financeFiles);
                services.AddSingleton(employeeReferences);
                services.AddSingleton(catalogReferences);
                services.AddSingleton(new FinanceDetailAggregator(finances, employeeReferences, catalogReferences, financeFiles));
                services.AddSingleton(invoiceDetails);
                services.AddSingleton(invoiceFiles);
                services.AddSingleton(invoiceCreation);
                services.AddSingleton(new InvoiceDetailAggregator(invoiceDetails, invoiceFiles));
            });
        }

        private static HttpClient Client(
            HttpMessageHandler terminal,
            string baseAddress,
            IServiceAccessTokenProvider tokenProvider)
        {
            var authentication = new LegacyServiceAuthenticationHandler(tokenProvider) { InnerHandler = terminal };
            return new HttpClient(authentication)
            {
                BaseAddress = new Uri(baseAddress),
                Timeout = TimeSpan.FromSeconds(10),
            };
        }
    }

    private sealed class AccountingTokenProvider : IServiceAccessTokenProvider
    {
        public ValueTask<string?> GetAccessTokenAsync(CancellationToken cancellationToken) =>
            ValueTask.FromResult<string?>("signed-service-token");

        public void Invalidate(string token)
        {
        }
    }

    private sealed class AccountingAuthClient : ILegacyAuthClient
    {
        public Task<EmployeeLoginResult> LoginAsync(string email, string password, CancellationToken cancellationToken) =>
            Task.FromResult(new EmployeeLoginResult(
                true,
                new AuthTokenResponse("server-access-token", "server-refresh-token", "Bearer", 900, DateTimeOffset.UtcNow.AddDays(1)),
                new EmployeeIdentity("employee-id", email, email, Permissions, 7)));

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
}
