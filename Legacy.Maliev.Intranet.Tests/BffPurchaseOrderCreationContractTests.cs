extern alias Bff;

using System.Net;
using System.Net.Http.Json;
using Legacy.Maliev.Intranet.Auth;
using Legacy.Maliev.Intranet.Contracts;
using Legacy.Maliev.Intranet.PurchaseOrders;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using BffProgram = Bff::Program;

namespace Legacy.Maliev.Intranet.Tests;

public sealed class BffPurchaseOrderCreationContractTests
{
    [Fact]
    public async Task MissingCreatePermission_IsRejectedBeforeOptionsLookup()
    {
        var gateway = new Gateway();
        await using var factory = new Factory(gateway, [LegacyEmployeePermissions.PurchaseOrdersRead]);
        using var client = CreateClient(factory);
        await SignInAsync(client);

        using var response = await client.GetAsync("/bff/purchase-orders/create-options");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(0, gateway.OptionsReads);
    }

    [Fact]
    public async Task AuthorizedOptions_ReturnBrowserSafeSelectors()
    {
        var gateway = new Gateway();
        await using var factory = new Factory(gateway, [LegacyEmployeePermissions.PurchaseOrdersCreate]);
        using var client = CreateClient(factory);
        await SignInAsync(client);

        using var response = await client.GetAsync("/bff/purchase-orders/create-options");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("\"name\":\"Acme\"", body, StringComparison.Ordinal);
        Assert.Contains("\"fullName\":\"Somchai Tester\"", body, StringComparison.Ordinal);
        Assert.DoesNotContain("server-only", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task MissingCsrf_IsRejectedBeforeCreationWorkflow()
    {
        var gateway = new Gateway();
        await using var factory = new Factory(gateway, [LegacyEmployeePermissions.PurchaseOrdersCreate]);
        using var client = CreateClient(factory);
        await SignInAsync(client);

        using var request = new HttpRequestMessage(HttpMethod.Post, "/bff/purchase-orders")
        {
            Content = JsonContent.Create(ValidRequest),
        };
        request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString("D"));
        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(0, gateway.OrderCreates);
    }

    [Fact]
    public async Task InvalidNestedLineItem_IsRejectedBeforeCreationWorkflow()
    {
        var gateway = new Gateway();
        await using var factory = new Factory(gateway, [LegacyEmployeePermissions.PurchaseOrdersCreate]);
        using var client = CreateClient(factory);
        var csrf = await SignInAsync(client);
        var invalid = WithItems([new() { Description = string.Empty, Quantity = 0, UnitPrice = -1 }]);
        using var request = CreatePost(invalid, csrf, Guid.NewGuid().ToString("D"));

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(0, gateway.OrderCreates);
    }

    [Fact]
    public async Task AuthorizedCsrfCreation_ReturnsCreatedRouteWithoutCredentials()
    {
        var gateway = new Gateway();
        await using var factory = new Factory(gateway, [LegacyEmployeePermissions.PurchaseOrdersCreate]);
        using var client = CreateClient(factory);
        var csrf = await SignInAsync(client);
        using var request = CreatePost(ValidRequest, csrf, "0f727e0f-a4f3-4e1c-995a-c0d37ea2a972");

        using var response = await client.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal("/PurchaseOrders/View?id=84", response.Headers.Location?.OriginalString);
        Assert.Contains("\"id\":84", body, StringComparison.Ordinal);
        Assert.DoesNotContain("server-only", body, StringComparison.Ordinal);
        Assert.Equal("0f727e0f-a4f3-4e1c-995a-c0d37ea2a972", gateway.AttemptId);
    }

    private static readonly PurchaseOrderCreateRequest ValidRequest = new()
    {
        SupplierId = 42,
        ShippingAddressId = 3,
        ShippingCompanyName = "MALIEV Co., Ltd.",
        BillingAddressId = 4,
        BillingCompanyName = "MALIEV Co., Ltd.",
        EmployeeId = 7,
        Items = [new() { Description = "Resin", Quantity = 2, UnitPrice = 100 }],
    };

    private static PurchaseOrderCreateRequest WithItems(List<PurchaseOrderCreateItem> items) => new()
    {
        SupplierId = ValidRequest.SupplierId,
        ShippingAddressId = ValidRequest.ShippingAddressId,
        ShippingCompanyName = ValidRequest.ShippingCompanyName,
        BillingAddressId = ValidRequest.BillingAddressId,
        BillingCompanyName = ValidRequest.BillingCompanyName,
        EmployeeId = ValidRequest.EmployeeId,
        Items = items,
    };

    private static HttpRequestMessage CreatePost(PurchaseOrderCreateRequest value, string csrf, string attemptId)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/bff/purchase-orders")
        {
            Content = JsonContent.Create(value),
        };
        request.Headers.Add("X-CSRF-TOKEN", csrf);
        request.Headers.Add("Idempotency-Key", attemptId);
        return request;
    }

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
            Content = JsonContent.Create(new { email = "employee@maliev.com", password = "password", returnUrl = "/PurchaseOrders/Create" }),
        };
        request.Headers.Add("X-CSRF-TOKEN", csrf);
        using var response = await client.SendAsync(request);
        response.EnsureSuccessStatusCode();
        using var refreshedSession = await client.GetAsync("/bff/session");
        var refreshed = await refreshedSession.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        return refreshed.GetProperty("csrfToken").GetString()!;
    }

    private sealed class Factory(Gateway gateway, IReadOnlyList<string> permissions) : WebApplicationFactory<BffProgram>
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
            builder.UseSetting("Services:Order", "http://order/");
            builder.UseSetting("Services:Procurement", "http://procurement/");
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<ILegacyAuthClient>();
                services.AddSingleton<ILegacyAuthClient>(new AuthClient(permissions));
                services.RemoveAll<IPurchaseOrderCreationGateway>();
                services.AddSingleton<IPurchaseOrderCreationGateway>(gateway);
            });
        }
    }

    private sealed class AuthClient(IReadOnlyList<string> permissions) : ILegacyAuthClient
    {
        public Task<EmployeeLoginResult> LoginAsync(string email, string password, CancellationToken cancellationToken) =>
            Task.FromResult(new EmployeeLoginResult(true,
                new AuthTokenResponse("server-only-access-token", "server-only-refresh-token", "Bearer", 900, DateTimeOffset.UtcNow.AddDays(1)),
                new EmployeeIdentity("employee-id", email, email, permissions, 7)));
        public Task<EmployeeRefreshResult?> RefreshAsync(string refreshToken, CancellationToken cancellationToken) => Task.FromResult<EmployeeRefreshResult?>(null);
        public Task RevokeAsync(string refreshToken, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<CustomerIdentityResponse?> CreateCustomerIdentityAsync(int databaseId, CreateCustomerIdentityRequest request, string accessToken, CancellationToken cancellationToken) => Task.FromResult<CustomerIdentityResponse?>(null);
        public Task<EmployeeIdentityResponse?> CreateEmployeeIdentityAsync(int databaseId, CreateEmployeeIdentityRequest request, string accessToken, CancellationToken cancellationToken) => Task.FromResult<EmployeeIdentityResponse?>(null);
    }

    private sealed class Gateway : IPurchaseOrderCreationGateway
    {
        public int OptionsReads { get; private set; }
        public int OrderCreates { get; private set; }
        public string? AttemptId { get; private set; }
        public Task<PurchaseOrderCreateOptions> GetOptionsAsync(CancellationToken cancellationToken)
        {
            OptionsReads++;
            return Task.FromResult(new PurchaseOrderCreateOptions(
                [new(42, "Acme")], [new(7, "Somchai Tester")], [new(3, "1 Main Road", "Bangkok")]));
        }
        public Task<PurchaseOrderCreatedData> CreateOrderAsync(PurchaseOrderCreateRequest request, string attemptId, CancellationToken cancellationToken)
        {
            OrderCreates++;
            AttemptId = attemptId;
            return Task.FromResult(new PurchaseOrderCreatedData(84, new DateTime(2026, 7, 18)));
        }
        public Task<int> CreateItemAsync(int purchaseOrderId, PurchaseOrderCreateItem item, string attemptId, int itemIndex, CancellationToken cancellationToken) => Task.FromResult(9);
        public Task<PurchaseOrderDocumentReferences> GetDocumentReferencesAsync(PurchaseOrderCreateRequest request, CancellationToken cancellationToken) => Task.FromResult(new PurchaseOrderDocumentReferences(
            new("Acme", null, null, null, null, new("Supplier Road", null, null, "Bangkok", null, "10110", 66)),
            new("Ship Road", null, null, "Bangkok", null, "10110", 66),
            new("Bill Road", null, null, "Bangkok", null, "10110", 66), "Somchai Tester", new Dictionary<int, string> { [66] = "Thailand" }));
        public Task<byte[]> RenderPdfAsync(PurchaseOrderPdfDocument document, CancellationToken cancellationToken) => Task.FromResult("%PDF-fake"u8.ToArray());
        public Task<PurchaseOrderStoredFile> UploadPdfAsync(int purchaseOrderId, byte[] pdf, string attemptId, CancellationToken cancellationToken) => Task.FromResult(new PurchaseOrderStoredFile("maliev.com", "purchaseorders/84/PurchaseOrder_84.pdf"));
        public Task<int> LinkFileAsync(int purchaseOrderId, PurchaseOrderStoredFile file, string attemptId, CancellationToken cancellationToken) => Task.FromResult(5);
        public Task DeleteFileLinkAsync(int fileId, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task DeleteStoredFileAsync(PurchaseOrderStoredFile file, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task DeleteItemAsync(int itemId, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task DeleteOrderAsync(int purchaseOrderId, CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
