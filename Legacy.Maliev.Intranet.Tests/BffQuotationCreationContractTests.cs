extern alias Bff;

using System.Net;
using System.Net.Http.Json;
using System.Text;
using Legacy.Maliev.Intranet.Auth;
using Legacy.Maliev.Intranet.Contracts;
using Legacy.Maliev.Intranet.Server.Quotations;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using BffProgram = Bff::Program;
using CatalogMaterialsProxy = Bff::Legacy.Maliev.Intranet.Bff.Catalog.CatalogMaterialsProxy;
using CustomersProxy = Bff::Legacy.Maliev.Intranet.Bff.Customers.CustomersProxy;
using EmployeesProxy = Bff::Legacy.Maliev.Intranet.Bff.Employees.EmployeesProxy;
using OrdersProxy = Bff::Legacy.Maliev.Intranet.Bff.Orders.OrdersProxy;

namespace Legacy.Maliev.Intranet.Tests;

public sealed class BffQuotationCreationContractTests
{
    private static readonly string[] CreatePermissions =
    [
        LegacyEmployeePermissions.QuotationsCreate,
        LegacyEmployeePermissions.QuotationLinesWrite,
        LegacyEmployeePermissions.QuotationOrdersWrite,
        LegacyEmployeePermissions.CustomersRead,
        LegacyEmployeePermissions.EmployeesRead,
        LegacyEmployeePermissions.CatalogCurrenciesRead,
        LegacyEmployeePermissions.OrdersRead,
    ];

    [Fact]
    public async Task MissingOneWritePermission_IsRejectedBeforeDownstreamReads()
    {
        var handler = new ReferenceHandler();
        var gateway = new Gateway();
        await using var factory = new Factory(handler, gateway, CreatePermissions.Where(value => value != LegacyEmployeePermissions.QuotationLinesWrite).ToArray());
        using var client = CreateClient(factory);
        await SignInAsync(client);

        using var response = await client.GetAsync("/bff/quotations/create?customerId=3");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(0, handler.Calls);
    }

    [Fact]
    public async Task MissingCsrf_IsRejectedBeforeReferencesAndWorkflow()
    {
        var handler = new ReferenceHandler();
        var gateway = new Gateway();
        await using var factory = new Factory(handler, gateway, CreatePermissions);
        using var client = CreateClient(factory);
        await SignInAsync(client);
        using var request = new HttpRequestMessage(HttpMethod.Post, "/bff/quotations") { Content = JsonContent.Create(ValidRequest) };
        request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString("D"));

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(0, handler.Calls);
        Assert.Equal(0, gateway.RootCreates);
    }

    [Fact]
    public async Task AuthorizedCreate_ValidatesOwnershipAndReturnsSafeCreatedResult()
    {
        var handler = new ReferenceHandler();
        var gateway = new Gateway();
        await using var factory = new Factory(handler, gateway, CreatePermissions);
        using var client = CreateClient(factory);
        var csrf = await SignInAsync(client);
        using var request = new HttpRequestMessage(HttpMethod.Post, "/bff/quotations") { Content = JsonContent.Create(ValidRequest) };
        request.Headers.Add("X-CSRF-TOKEN", csrf);
        request.Headers.Add("Idempotency-Key", Guid.Empty.ToString("D"));

        using var response = await client.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal("/Quotations/View?id=77", response.Headers.Location?.OriginalString);
        Assert.Contains("\"id\":77", body, StringComparison.Ordinal);
        Assert.DoesNotContain("server-only", body, StringComparison.Ordinal);
        Assert.Equal(1, gateway.RootCreates);
        Assert.Equal(1, gateway.LineCreates);
        Assert.Equal(1, gateway.LinkCreates);
        Assert.Equal(1, gateway.StatusCreates);
        Assert.Equal(1, gateway.Finalizations);
    }

    private static readonly QuotationCreateRequest ValidRequest = new(
        3, 2, 1, 30, "Courier", "Bangkok", "Net 30", "fixture", false,
        [new(42, "Thai tone mark น้ำ", 2, 50m, 0m)]);

    private static HttpClient CreateClient(WebApplicationFactory<BffProgram> factory) => factory.CreateClient(new()
    {
        AllowAutoRedirect = false,
        BaseAddress = new Uri("https://localhost"),
        HandleCookies = true,
    });

    private static async Task<string> SignInAsync(HttpClient client)
    {
        using var sessionResponse = await client.GetAsync("/bff/session");
        var session = await sessionResponse.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        var csrf = session.GetProperty("csrfToken").GetString()!;
        using var request = new HttpRequestMessage(HttpMethod.Post, "/bff/login")
        {
            Content = JsonContent.Create(new { email = "employee@maliev.com", password = "password", returnUrl = "/Quotations/Create" }),
        };
        request.Headers.Add("X-CSRF-TOKEN", csrf);
        using var response = await client.SendAsync(request);
        response.EnsureSuccessStatusCode();
        using var refreshedResponse = await client.GetAsync("/bff/session");
        var refreshed = await refreshedResponse.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        return refreshed.GetProperty("csrfToken").GetString()!;
    }

    private sealed class Factory(ReferenceHandler handler, Gateway gateway, IReadOnlyList<string> permissions) : WebApplicationFactory<BffProgram>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing");
            TestJwtConfiguration.Configure(builder);
            builder.UseSetting("Services:Auth", "http://auth/");
            builder.UseSetting("Services:Catalog", "http://catalog/");
            builder.UseSetting("Services:Customer", "http://customer/");
            builder.UseSetting("Services:Document", "http://document/");
            builder.UseSetting("Services:Employee", "http://employee/");
            builder.UseSetting("Services:File", "http://file/");
            builder.UseSetting("Services:Notification", "http://notification/");
            builder.UseSetting("Services:Order", "http://order/");
            builder.UseSetting("Services:Procurement", "http://procurement/");
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<ILegacyAuthClient>();
                services.AddSingleton<ILegacyAuthClient>(new AuthClient(permissions));
                services.RemoveAll<IQuotationCreationGateway>();
                services.AddSingleton<IQuotationCreationGateway>(gateway);
                var http = new HttpClient(handler, disposeHandler: false) { BaseAddress = new Uri("http://downstream/") };
                services.RemoveAll<CustomersProxy>(); services.AddSingleton(new CustomersProxy(http));
                services.RemoveAll<EmployeesProxy>(); services.AddSingleton(new EmployeesProxy(http));
                services.RemoveAll<CatalogMaterialsProxy>(); services.AddSingleton(new CatalogMaterialsProxy(http));
                services.RemoveAll<OrdersProxy>(); services.AddSingleton(new OrdersProxy(http));
            });
        }
    }

    private sealed class AuthClient(IReadOnlyList<string> permissions) : ILegacyAuthClient
    {
        public Task<EmployeeLoginResult> LoginAsync(string email, string password, CancellationToken cancellationToken) => Task.FromResult(new EmployeeLoginResult(true,
            new AuthTokenResponse("server-only-access-token", "server-only-refresh-token", "Bearer", 900, DateTimeOffset.UtcNow.AddDays(1)),
            new EmployeeIdentity("employee-id", email, email, permissions, 2)));
        public Task<EmployeeRefreshResult?> RefreshAsync(string refreshToken, CancellationToken cancellationToken) => Task.FromResult<EmployeeRefreshResult?>(null);
        public Task RevokeAsync(string refreshToken, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<CustomerIdentityResponse?> CreateCustomerIdentityAsync(int databaseId, CreateCustomerIdentityRequest request, string accessToken, CancellationToken cancellationToken) => Task.FromResult<CustomerIdentityResponse?>(null);
        public Task<EmployeeIdentityResponse?> CreateEmployeeIdentityAsync(int databaseId, CreateEmployeeIdentityRequest request, string accessToken, CancellationToken cancellationToken) => Task.FromResult<EmployeeIdentityResponse?>(null);
    }

    private sealed class ReferenceHandler : HttpMessageHandler
    {
        public int Calls { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            var body = request.RequestUri!.AbsolutePath switch
            {
                "/customers/3" => CustomerJson,
                "/employees/2" => EmployeeJson,
                "/Currencies" => "[{\"id\":1,\"shortName\":\"THB\",\"longName\":\"Thai Baht\"}]",
                "/Orders/customers/3" => "{\"items\":[{\"id\":42,\"customerId\":3,\"employeeId\":2,\"name\":\"Order\",\"processId\":1,\"quantity\":2,\"manufactured\":0,\"remaining\":2,\"subtotal\":100,\"promisedDate\":null,\"allowSocialMedia\":false}],\"pageIndex\":1,\"totalPages\":1,\"totalRecords\":1,\"hasNextPage\":false,\"hasPreviousPage\":false}",
                _ => "{}",
            };
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") });
        }
    }

    private sealed class Gateway : IQuotationCreationGateway
    {
        public int RootCreates { get; private set; }
        public int LineCreates { get; private set; }
        public int LinkCreates { get; private set; }
        public int StatusCreates { get; private set; }
        public int Finalizations { get; private set; }
        public Task<int> CreateQuotationAsync(QuotationCreateRequest input, PricedQuotation priced, string idempotencyKey, CancellationToken cancellationToken) { RootCreates++; return Task.FromResult(77); }
        public Task<int> CreateLineAsync(int quotationId, PricedQuotationLine line, string idempotencyKey, CancellationToken cancellationToken) { LineCreates++; return Task.FromResult(101); }
        public Task<int> CreateOrderLinkAsync(int quotationId, int orderId, string idempotencyKey, CancellationToken cancellationToken) { LinkCreates++; return Task.FromResult(201); }
        public Task MarkOrderQuotedAsync(int orderId, string idempotencyKey, CancellationToken cancellationToken) { StatusCreates++; return Task.CompletedTask; }
        public Task FinalizeDocumentDeliveryAsync(int quotationId, QuotationCreateRequest input, PricedQuotation priced, CancellationToken cancellationToken) { Finalizations++; return Task.CompletedTask; }
    }

    private const string CustomerJson = "{\"id\":3,\"firstName\":\"Ada\",\"lastName\":\"Lovelace\",\"fullName\":\"Ada Lovelace\",\"telephone\":\"02\",\"mobile\":null,\"fax\":null,\"email\":\"ada@example.test\",\"dateOfBirth\":null,\"companyId\":null,\"billingAddressId\":null,\"shippingAddressId\":null,\"createdDate\":null,\"modifiedDate\":null,\"billingAddress\":null,\"company\":null,\"shippingAddress\":null}";
    private const string EmployeeJson = "{\"id\":2,\"roleId\":1,\"firstName\":\"Grace\",\"lastName\":\"Hopper\",\"fullName\":\"Grace Hopper\",\"phoneNumber\":\"081\",\"email\":\"grace@example.test\",\"dateOfBirth\":null,\"homeAddressId\":null,\"createdDate\":null,\"modifiedDate\":null,\"homeAddress\":null,\"role\":null}";
}
