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
using CatalogMaterialsProxy = Bff::Legacy.Maliev.Intranet.Bff.Catalog.CatalogMaterialsProxy;
using CustomersProxy = Bff::Legacy.Maliev.Intranet.Bff.Customers.CustomersProxy;
using EmployeesProxy = Bff::Legacy.Maliev.Intranet.Bff.Employees.EmployeesProxy;
using OrderCatalogReferenceProxy = Bff::Legacy.Maliev.Intranet.Bff.Orders.OrderCatalogReferenceProxy;
using OrderDetailProxy = Bff::Legacy.Maliev.Intranet.Bff.Orders.OrderDetailProxy;
using OrderFileProxy = Bff::Legacy.Maliev.Intranet.Bff.Orders.OrderFileProxy;
using OrdersProxy = Bff::Legacy.Maliev.Intranet.Bff.Orders.OrdersProxy;
using QuotationRequestFilesProxy = Bff::Legacy.Maliev.Intranet.Bff.Quotations.QuotationRequestFilesProxy;
using QuotationRequestsProxy = Bff::Legacy.Maliev.Intranet.Bff.Quotations.QuotationRequestsProxy;
using QuotationsProxy = Bff::Legacy.Maliev.Intranet.Bff.Quotations.QuotationsProxy;

namespace Legacy.Maliev.Intranet.Tests;

public sealed class OrdersQuotationsBffBehaviorTests
{
    [Fact]
    public async Task QuotationCreateOptions_UsesExactReferenceRoutesAndReturnsBrowserSafeJson()
    {
        var downstream = new RoutingHandler();
        await using var factory = new Factory(downstream);
        using var client = CreateClient(factory);
        await SignInAsync(client);
        downstream.Requests.Clear();

        using var response = await client.GetAsync("/bff/quotations/create?customerId=42");
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains(downstream.Requests, item => item.Method == "GET" && item.PathAndQuery == "/employees?sort=EmployeeId_Ascending&search=&index=1&size=250");
        Assert.Contains(downstream.Requests, item => item.Method == "GET" && item.PathAndQuery == "/Currencies");
        Assert.Contains(downstream.Requests, item => item.Method == "GET" && item.PathAndQuery == "/customers/42");
        Assert.Contains(downstream.Requests, item => item.Method == "GET" && item.PathAndQuery == "/Orders/customers/42?sort=OrderCreatedDate_Descending&search=&index=1&size=250");
        Assert.All(downstream.Requests, item => Assert.Equal("Bearer bff-service-token", item.Authorization));
        Assert.Equal(7, body.RootElement.GetProperty("currentEmployeeId").GetInt32());
        Assert.Equal("THB", body.RootElement.GetProperty("currencies")[0].GetProperty("shortName").GetString());
        Assert.Equal(42, body.RootElement.GetProperty("customer").GetProperty("id").GetInt32());
        Assert.False(body.RootElement.TryGetProperty("accessToken", out _));
    }

    [Fact]
    public async Task QuotationOrderSearch_UsesCustomerOwnedBoundedRouteAndRejectsMismatchedOwner()
    {
        var downstream = new RoutingHandler();
        await using var factory = new Factory(downstream);
        using var client = CreateClient(factory);
        await SignInAsync(client);
        downstream.Requests.Clear();

        using var response = await client.GetAsync("/bff/quotations/create/orders?search=%20bolt%20&customerId=42");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains(downstream.Requests, item => item.PathAndQuery == "/Orders/customers/42?sort=OrderCreatedDate_Descending&search=bolt&index=1&size=10");

        downstream.OrderCustomerId = 99;
        using var invalid = await client.GetAsync("/bff/quotations/create/orders?search=bolt&customerId=42");
        Assert.Equal(HttpStatusCode.BadGateway, invalid.StatusCode);
    }

    [Fact]
    public async Task QuotationRequestDetailAndUpdate_ResolveCleanFileAndPreserveConcurrencyContract()
    {
        var downstream = new RoutingHandler();
        await using var factory = new Factory(downstream);
        using var client = CreateClient(factory);
        var csrf = await SignInAsync(client);
        downstream.Requests.Clear();

        using var detail = await client.GetAsync("/bff/quotation-requests/9");
        using var detailBody = JsonDocument.Parse(await detail.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.OK, detail.StatusCode);
        Assert.Contains(downstream.Requests, item => item.Method == "GET" && item.PathAndQuery == "/quotationrequests/9");
        Assert.Contains(downstream.Requests, item => item.Method == "GET" && item.PathAndQuery == "/quotationrequests/9/files");
        Assert.Contains(downstream.Requests, item => item.Method == "GET" && item.PathAndQuery == "/uploads/SignedUrl?bucket=maliev.com&objectName=requests%2F9%2Fdrawing.step");
        Assert.Equal("https://files.example.test/request-9", detailBody.RootElement.GetProperty("files")[0].GetProperty("uri").GetString());

        var update = new
        {
            firstName = "สมชาย",
            lastName = "ทดสอบ",
            email = "buyer@example.com",
            telephoneNumber = "0690",
            country = "TH",
            companyName = "MALIEV",
            taxIdentification = "0100000000000",
            message = "ผลิตชิ้นงาน",
            internalComment = "ตรวจแบบแล้ว",
            done = true,
            modifiedDate = "2030-07-18T08:30:00Z",
        };
        using var missingCsrf = await client.PutAsJsonAsync("/bff/quotation-requests/9", update);
        Assert.Equal(HttpStatusCode.BadRequest, missingCsrf.StatusCode);

        using var request = new HttpRequestMessage(HttpMethod.Put, "/bff/quotation-requests/9") { Content = JsonContent.Create(update) };
        request.Headers.Add("X-CSRF-TOKEN", csrf);
        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var forwarded = Assert.Single(downstream.Requests, item => item.Method == "PUT" && item.PathAndQuery == "/quotationrequests/9");
        Assert.Equal("2030-07-18T08:30:00.0000000Z", forwarded.ExpectedModifiedDate);
        using var forwardedBody = JsonDocument.Parse(forwarded.Body!);
        Assert.Equal("สมชาย", forwardedBody.RootElement.GetProperty("firstName").GetString());
        Assert.Equal("ทดสอบ", forwardedBody.RootElement.GetProperty("lastName").GetString());
        Assert.Equal("buyer@example.com", forwardedBody.RootElement.GetProperty("email").GetString());
        Assert.Equal("0690", forwardedBody.RootElement.GetProperty("telephoneNumber").GetString());
        Assert.Equal("TH", forwardedBody.RootElement.GetProperty("country").GetString());
        Assert.Equal("MALIEV", forwardedBody.RootElement.GetProperty("companyName").GetString());
        Assert.Equal("0100000000000", forwardedBody.RootElement.GetProperty("taxIdentification").GetString());
        Assert.Equal("ผลิตชิ้นงาน", forwardedBody.RootElement.GetProperty("message").GetString());
        Assert.Equal("ตรวจแบบแล้ว", forwardedBody.RootElement.GetProperty("internalComment").GetString());
        Assert.True(forwardedBody.RootElement.GetProperty("done").GetBoolean());
        Assert.Equal(10, forwardedBody.RootElement.EnumerateObject().Count());
    }

    [Fact]
    public async Task QuotationsPage_PropagatesRateLimitAndInvalidPayloadWithoutLeakingDownstreamBody()
    {
        var downstream = new RoutingHandler { QuotationPageStatus = HttpStatusCode.TooManyRequests };
        await using var factory = new Factory(downstream);
        using var client = CreateClient(factory);
        await SignInAsync(client);

        using var limited = await client.GetAsync("/bff/quotations");
        Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
        Assert.Equal("17", limited.Headers.RetryAfter?.Delta?.TotalSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? limited.Headers.GetValues("Retry-After").Single());

        downstream.QuotationPageStatus = HttpStatusCode.OK;
        downstream.InvalidQuotationPage = true;
        using var invalid = await client.GetAsync("/bff/quotations");
        var body = await invalid.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.BadGateway, invalid.StatusCode);
        Assert.DoesNotContain("downstream-secret", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task OrderCreateOptionsAndMaterialOptions_UseExactServiceRoutes()
    {
        var downstream = new RoutingHandler();
        await using var factory = new Factory(downstream);
        using var client = CreateClient(factory);
        await SignInAsync(client);
        downstream.Requests.Clear();

        using var create = await client.GetAsync("/bff/orders/create?customerId=42");
        Assert.Equal(HttpStatusCode.OK, create.StatusCode);
        Assert.Contains(downstream.Requests, item => item.PathAndQuery == "/orders/processes");
        Assert.Contains(downstream.Requests, item => item.PathAndQuery == "/Materials?sort=MaterialId_Ascending&search=&index=1&size=1000");
        Assert.Contains(downstream.Requests, item => item.PathAndQuery == "/customers/42");

        using var options = await client.GetAsync("/bff/orders/create/materials/5");
        using var json = JsonDocument.Parse(await options.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.OK, options.StatusCode);
        Assert.Contains(downstream.Requests, item => item.PathAndQuery == "/materials/5/colors");
        Assert.Contains(downstream.Requests, item => item.PathAndQuery == "/materials/5/surfacefinishes");
        Assert.Equal("Black", json.RootElement.GetProperty("colors")[0].GetProperty("name").GetString());
    }

    [Fact]
    public async Task OrderFileUploadAndRemove_RequireCsrfAndForwardExactMultipartAndDeleteRoutes()
    {
        var downstream = new RoutingHandler();
        await using var factory = new Factory(downstream);
        using var client = CreateClient(factory);
        var csrf = await SignInAsync(client);
        downstream.Requests.Clear();

        using var rejectedContent = UploadContent();
        using var rejected = await client.PostAsync("/bff/orders/84/files", rejectedContent);
        Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);
        Assert.Empty(downstream.Requests);

        using var uploadContent = UploadContent();
        using var upload = new HttpRequestMessage(HttpMethod.Post, "/bff/orders/84/files") { Content = uploadContent };
        upload.Headers.Add("X-CSRF-TOKEN", csrf);
        using var uploaded = await client.SendAsync(upload);
        Assert.Equal(HttpStatusCode.OK, uploaded.StatusCode);
        var fileUpload = Assert.Single(downstream.Requests, item => item.Method == "POST" && item.PathAndQuery.StartsWith("/Uploads?bucket=maliev.com&path=uploads%2F42%2F", StringComparison.Ordinal));
        Assert.Contains("name=files", fileUpload.Body, StringComparison.Ordinal);
        Assert.Contains("filename=drawing.step", fileUpload.Body, StringComparison.Ordinal);
        Assert.Contains("model/step", fileUpload.Body, StringComparison.Ordinal);
        Assert.Contains(downstream.Requests, item => item.Method == "POST" && item.PathAndQuery == "/orders/84/files?bucket=maliev.com&objectName=orders%2F42%2Fdrawing.step");

        downstream.Requests.Clear();
        using var remove = new HttpRequestMessage(HttpMethod.Delete, "/bff/orders/84/files/901");
        remove.Headers.Add("X-CSRF-TOKEN", csrf);
        using var removed = await client.SendAsync(remove);
        Assert.Equal(HttpStatusCode.NoContent, removed.StatusCode);
        Assert.Collection(
            downstream.Requests.Where(item => item.Method == "DELETE").OrderBy(item => item.Host),
            item => Assert.Equal("/Uploads?bucket=maliev.com&objectName=orders%2F42%2Fdrawing.step", item.PathAndQuery),
            item => Assert.Equal("/orders/files/901", item.PathAndQuery));
    }

    private static MultipartFormDataContent UploadContent()
    {
        var content = new MultipartFormDataContent();
        content.Add(new ByteArrayContent(Encoding.UTF8.GetBytes("STEP-DATA"))
        {
            Headers = { ContentType = new("model/step") },
        }, "files", "drawing.step");
        return content;
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
        var anonymous = await anonymousResponse.Content.ReadFromJsonAsync<JsonElement>();
        using var login = new HttpRequestMessage(HttpMethod.Post, "/bff/login")
        {
            Content = JsonContent.Create(new { email = "employee@maliev.com", password = "password", returnUrl = "/Dashboard" }),
        };
        login.Headers.Add("X-CSRF-TOKEN", anonymous.GetProperty("csrfToken").GetString());
        using var loginResponse = await client.SendAsync(login);
        loginResponse.EnsureSuccessStatusCode();
        using var authenticatedResponse = await client.GetAsync("/bff/session");
        var authenticated = await authenticatedResponse.Content.ReadFromJsonAsync<JsonElement>();
        return authenticated.GetProperty("csrfToken").GetString() ?? throw new InvalidOperationException("Missing CSRF token.");
    }

    private sealed class Factory(RoutingHandler downstream) : WebApplicationFactory<BffProgram>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing");
            TestJwtConfiguration.Configure(builder);
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<ILegacyAuthClient>();
                services.AddSingleton<ILegacyAuthClient>(new AuthClient());
                services.RemoveAll<IServiceAccessTokenProvider>();
                var tokens = new TokenProvider();
                services.AddSingleton<IServiceAccessTokenProvider>(tokens);
                Replace(services, new EmployeesProxy(Client("http://employee/", downstream, tokens)));
                Replace(services, new CatalogMaterialsProxy(Client("http://catalog/", downstream, tokens)));
                Replace(services, new CustomersProxy(Client("http://customer/", downstream, tokens)));
                Replace(services, new OrdersProxy(Client("http://order/", downstream, tokens)));
                Replace(services, new OrderCatalogReferenceProxy(Client("http://catalog/", downstream, tokens)));
                Replace(services, new OrderDetailProxy(Client("http://order/", downstream, tokens)));
                Replace(services, new OrderFileProxy(Client("http://file/", downstream, tokens)));
                Replace(services, new QuotationRequestsProxy(Client("http://quotation/", downstream, tokens)));
                Replace(services, new QuotationRequestFilesProxy(Client("http://file/", downstream, tokens)));
                Replace(services, new QuotationsProxy(Client("http://quotation/", downstream, tokens)));
            });
        }

        private static HttpClient Client(string baseAddress, HttpMessageHandler downstream, IServiceAccessTokenProvider tokens)
        {
            var auth = new LegacyServiceAuthenticationHandler(tokens) { InnerHandler = downstream };
            return new HttpClient(auth, disposeHandler: false) { BaseAddress = new(baseAddress), Timeout = TimeSpan.FromSeconds(10) };
        }

        private static void Replace<T>(IServiceCollection services, T value) where T : class
        {
            services.RemoveAll<T>();
            services.AddSingleton(value);
        }
    }

    private sealed class TokenProvider : IServiceAccessTokenProvider
    {
        public ValueTask<string?> GetAccessTokenAsync(CancellationToken cancellationToken) => ValueTask.FromResult<string?>("bff-service-token");
        public void Invalidate(string token) { }
    }

    private sealed class AuthClient : ILegacyAuthClient
    {
        private static readonly string[] Permissions =
        [
            LegacyEmployeePermissions.QuotationsRead,
            LegacyEmployeePermissions.QuotationsCreate,
            LegacyEmployeePermissions.QuotationLinesWrite,
            LegacyEmployeePermissions.QuotationOrdersWrite,
            LegacyEmployeePermissions.QuotationOrdersRead,
            LegacyEmployeePermissions.QuotationFilesRead,
            LegacyEmployeePermissions.QuotationRequestsRead,
            LegacyEmployeePermissions.QuotationRequestsUpdate,
            LegacyEmployeePermissions.CustomersRead,
            LegacyEmployeePermissions.EmployeesRead,
            LegacyEmployeePermissions.CatalogCurrenciesRead,
            LegacyEmployeePermissions.OrdersRead,
            LegacyEmployeePermissions.OrdersCreate,
            LegacyEmployeePermissions.OrderCatalogRead,
            LegacyEmployeePermissions.CatalogMaterialsRead,
            LegacyEmployeePermissions.OrderFilesRead,
            LegacyEmployeePermissions.OrderFilesWrite,
            LegacyEmployeePermissions.OrderFilesDelete,
            LegacyEmployeePermissions.FileUploadsRead,
            LegacyEmployeePermissions.FileUploadsCreate,
            LegacyEmployeePermissions.FileUploadsDelete,
            LegacyEmployeePermissions.AccountingRead,
        ];

        public Task<EmployeeLoginResult> LoginAsync(string email, string password, CancellationToken cancellationToken) =>
            Task.FromResult(new EmployeeLoginResult(true, new("browser-access", "browser-refresh", "Bearer", 900, DateTimeOffset.UtcNow.AddHours(1)), new("employee", email, email, Permissions, 7)));
        public Task<EmployeeRefreshResult?> RefreshAsync(string refreshToken, CancellationToken cancellationToken) => Task.FromResult<EmployeeRefreshResult?>(null);
        public Task RevokeAsync(string refreshToken, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<CustomerIdentityResponse?> CreateCustomerIdentityAsync(int databaseId, CreateCustomerIdentityRequest request, string accessToken, CancellationToken cancellationToken) => Task.FromResult<CustomerIdentityResponse?>(null);
        public Task<EmployeeIdentityResponse?> CreateEmployeeIdentityAsync(int databaseId, CreateEmployeeIdentityRequest request, string accessToken, CancellationToken cancellationToken) => Task.FromResult<EmployeeIdentityResponse?>(null);
    }

    private sealed class RoutingHandler : HttpMessageHandler
    {
        public ConcurrentBag<RecordedRequest> Requests { get; } = [];
        public int OrderCustomerId { get; set; } = 42;
        public HttpStatusCode QuotationPageStatus { get; set; } = HttpStatusCode.OK;
        public bool InvalidQuotationPage { get; set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            Requests.Add(new(
                request.Method.Method,
                request.RequestUri?.Host ?? string.Empty,
                request.RequestUri?.PathAndQuery ?? string.Empty,
                request.Headers.Authorization?.ToString(),
                request.Headers.TryGetValues("X-Expected-Modified-Date", out var modified) ? modified.Single() : null,
                body));

            var path = request.RequestUri?.PathAndQuery ?? string.Empty;
            if (path.StartsWith("/quotations?", StringComparison.Ordinal))
            {
                var response = Json(InvalidQuotationPage ? "downstream-secret" : QuotationPageJson, QuotationPageStatus);
                if (QuotationPageStatus == HttpStatusCode.TooManyRequests) response.Headers.RetryAfter = new(TimeSpan.FromSeconds(17));
                return response;
            }

            if (request.Method == HttpMethod.Put && path == "/quotationrequests/9") return new(HttpStatusCode.NoContent);
            if (request.Method == HttpMethod.Delete && path.StartsWith("/Uploads?", StringComparison.Ordinal)) return new(HttpStatusCode.NoContent);
            if (request.Method == HttpMethod.Delete && path == "/orders/files/901") return new(HttpStatusCode.NoContent);
            if (request.Method == HttpMethod.Post && path.StartsWith("/Uploads?", StringComparison.Ordinal)) return Json(UploadJson);
            if (request.Method == HttpMethod.Post && path.StartsWith("/orders/84/files?", StringComparison.Ordinal)) return Json(StoredFileJson);

            return path switch
            {
                "/employees?sort=EmployeeId_Ascending&search=&index=1&size=250" => Json(EmployeePageJson),
                "/Currencies" => Json("""[{"Id":1,"ShortName":"THB","LongName":"Thai Baht"}]"""),
                "/customers/42" => Json(CustomerJson),
                "/orders/processes" => Json("""[{"Id":3,"Name":"CNC"}]"""),
                "/Materials?sort=MaterialId_Ascending&search=&index=1&size=1000" => Json("""{"Items":[{"Id":5,"Name":"Aluminium"}]}"""),
                "/materials/5/colors" => Json("""[{"Id":4,"Name":"Black"}]"""),
                "/materials/5/surfacefinishes" => Json("""[{"Id":6,"Name":"Anodized"}]"""),
                "/quotationrequests/9" => Json(QuotationRequestJson),
                "/quotationrequests/9/files" => Json("""[{"Id":501,"RequestId":9,"Bucket":"maliev.com","ObjectName":"requests/9/drawing.step","CreatedDate":"2030-07-18T00:00:00Z"}]"""),
                "/uploads/SignedUrl?bucket=maliev.com&objectName=requests%2F9%2Fdrawing.step" => Json("\"https://files.example.test/request-9\""),
                "/Orders/84" => Json(OrderJson),
                "/orders/84/files" => Json($"[{StoredFileJson}]"),
                var value when value.StartsWith("/Orders/customers/42?", StringComparison.Ordinal) => Json(OrderPageJson(OrderCustomerId)),
                _ => throw new InvalidOperationException($"Unexpected downstream request {request.Method} {request.RequestUri}"),
            };
        }

        private static HttpResponseMessage Json(string body, HttpStatusCode status = HttpStatusCode.OK) => new(status)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };
    }

    private sealed record RecordedRequest(string Method, string Host, string PathAndQuery, string? Authorization, string? ExpectedModifiedDate, string? Body);

    private static string OrderPageJson(int customerId) => $$"""{"Items":[{"Id":84,"CustomerId":{{customerId}},"EmployeeId":7,"Name":"Fixture","ProcessId":3,"Quantity":2,"Manufactured":0,"Remaining":2,"Subtotal":null,"PromisedDate":null,"AllowSocialMedia":false}],"PageIndex":1,"TotalPages":1,"TotalRecords":1,"HasNextPage":false,"HasPreviousPage":false}""";
    private const string EmployeePageJson = """{"Items":[{"Id":7,"FirstName":"Nat","LastName":"V","FullName":"Nat V","Email":"employee@maliev.com","Role":null}],"PageIndex":1,"TotalPages":1,"TotalRecords":1,"HasNextPage":false,"HasPreviousPage":false}""";
    private const string CustomerJson = """{"Id":42,"FirstName":"Buyer","LastName":"Fixture","FullName":"Buyer Fixture","Telephone":"0690","Mobile":null,"Fax":null,"Email":"buyer@example.com","DateOfBirth":null,"CompanyId":null,"BillingAddressId":null,"ShippingAddressId":null,"CreatedDate":null,"ModifiedDate":null,"BillingAddress":null,"Company":null,"ShippingAddress":null}""";
    private const string QuotationRequestJson = """{"Id":9,"FirstName":"Buyer","LastName":"Fixture","Email":"buyer@example.com","TelephoneNumber":"0690","Country":"TH","CompanyName":"MALIEV","TaxIdentification":null,"Message":"part","InternalComment":null,"Done":false,"CreatedDate":"2030-07-18T00:00:00Z","ModifiedDate":"2030-07-18T08:30:00Z"}""";
    private const string QuotationPageJson = """{"Items":[{"Id":7,"CustomerId":42,"EmployeeId":7,"InvoiceId":null,"Period":14,"ExpirationDate":"2030-08-01T00:00:00Z","Subtotal":100,"Vat":7,"Total":107,"WithholdingTax":null,"QuotedAmount":107,"CurrencyId":1,"Comment":null,"Fob":null,"ShippedVia":null,"Terms":null,"Accepted":null,"CreatedDate":"2030-07-18T00:00:00Z","ModifiedDate":null}],"PageIndex":1,"TotalPages":1,"TotalRecords":1,"HasNextPage":false,"HasPreviousPage":false}""";
    private const string OrderJson = """{"Id":84,"CustomerId":42,"EmployeeId":7,"Name":"Fixture","Description":"Part","ProcessId":3,"MaterialId":5,"SurfaceFinishId":6,"ColorId":4,"Quantity":2,"Manufactured":0,"Remaining":2,"UnitPrice":null,"DiscountPercent":null,"Subtotal":null,"CurrencyId":null,"LeadTime":null,"PromisedDate":null,"FinishedDate":null,"Turnaround":null,"Comment":null,"AllowSocialMedia":false,"AllowCancellation":true,"AllowPayment":false,"TrackingNumber":null,"CreatedDate":"2030-07-18T00:00:00Z","ModifiedDate":"2030-07-18T08:30:00Z"}""";
    private const string UploadJson = """{"Object":[{"Bucket":"maliev.com","ObjectName":"orders/42/drawing.step","Uri":"https://files.example.test/order-84"}]}""";
    private const string StoredFileJson = """{"Id":901,"OrderId":84,"Bucket":"maliev.com","ObjectName":"orders/42/drawing.step","Uri":"https://files.example.test/order-84"}""";
}
