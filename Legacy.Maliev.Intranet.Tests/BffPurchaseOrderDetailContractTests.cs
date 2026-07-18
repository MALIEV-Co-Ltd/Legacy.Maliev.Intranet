extern alias Bff;

using System.Net;
using System.Net.Http.Json;
using Legacy.Maliev.Intranet.Auth;
using Legacy.Maliev.Intranet.PurchaseOrders;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using BffProgram = Bff::Program;

namespace Legacy.Maliev.Intranet.Tests;

public sealed class BffPurchaseOrderDetailContractTests
{
    [Fact]
    public async Task MissingReadPermission_IsRejectedBeforeGateway()
    {
        var gateway = new Gateway();
        await using var factory = new Factory(gateway, []);
        using var client = CreateClient(factory);
        await SignInAsync(client);
        using var response = await client.GetAsync("/bff/purchase-orders/84");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(0, gateway.OrderReads);
    }

    [Fact]
    public async Task AuthorizedRead_ReturnsSafeAggregateWithoutBucketOrObjectName()
    {
        var gateway = new Gateway();
        await using var factory = new Factory(gateway, [LegacyEmployeePermissions.PurchaseOrdersRead]);
        using var client = CreateClient(factory);
        await SignInAsync(client);
        using var response = await client.GetAsync("/bff/purchase-orders/84");
        var body = await response.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("\"supplierName\":\"Acme\"", body, StringComparison.Ordinal);
        Assert.Contains("https://storage.test/signed", body, StringComparison.Ordinal);
        Assert.DoesNotContain("maliev.com", body, StringComparison.Ordinal);
        Assert.DoesNotContain("server-only", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task MissingCsrf_IsRejectedBeforeDelete()
    {
        var gateway = new Gateway();
        await using var factory = new Factory(gateway, [LegacyEmployeePermissions.PurchaseOrdersDelete]);
        using var client = CreateClient(factory);
        await SignInAsync(client);
        using var response = await client.DeleteAsync("/bff/purchase-orders/84");
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(gateway.Deletes);
    }

    [Fact]
    public async Task MissingDeletePermission_IsRejectedBeforeDelete()
    {
        var gateway = new Gateway();
        await using var factory = new Factory(gateway, [LegacyEmployeePermissions.PurchaseOrdersRead]);
        using var client = CreateClient(factory);
        var csrf = await SignInAsync(client);
        using var request = new HttpRequestMessage(HttpMethod.Delete, "/bff/purchase-orders/84");
        request.Headers.Add("X-CSRF-TOKEN", csrf);
        using var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Empty(gateway.Deletes);
    }

    [Fact]
    public async Task AuthorizedCsrfDelete_RemovesDependenciesBeforeParent()
    {
        var gateway = new Gateway();
        await using var factory = new Factory(gateway, [LegacyEmployeePermissions.PurchaseOrdersDelete]);
        using var client = CreateClient(factory);
        var csrf = await SignInAsync(client);
        using var request = new HttpRequestMessage(HttpMethod.Delete, "/bff/purchase-orders/84");
        request.Headers.Add("X-CSRF-TOKEN", csrf);
        using var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(["stored", "link", "item", "order"], gateway.Deletes);
    }

    private static HttpClient CreateClient(WebApplicationFactory<BffProgram> factory) => factory.CreateClient(new() { AllowAutoRedirect = false, BaseAddress = new Uri("https://localhost"), HandleCookies = true });
    private static async Task<string> SignInAsync(HttpClient client)
    {
        using var sessionResponse = await client.GetAsync("/bff/session");
        var session = await sessionResponse.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        var csrf = session.GetProperty("csrfToken").GetString()!;
        using var request = new HttpRequestMessage(HttpMethod.Post, "/bff/login") { Content = JsonContent.Create(new { email = "employee@maliev.com", password = "password", returnUrl = "/PurchaseOrders/View?id=84" }) };
        request.Headers.Add("X-CSRF-TOKEN", csrf);
        using var response = await client.SendAsync(request);
        response.EnsureSuccessStatusCode();
        using var refreshedResponse = await client.GetAsync("/bff/session");
        var refreshed = await refreshedResponse.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        return refreshed.GetProperty("csrfToken").GetString()!;
    }

    private sealed class Factory(Gateway gateway, IReadOnlyList<string> permissions) : WebApplicationFactory<BffProgram>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing");
            TestJwtConfiguration.Configure(builder);
            foreach (var setting in new[] { "Auth", "Catalog", "Customer", "Document", "Employee", "File", "Order", "Procurement" }) builder.UseSetting($"Services:{setting}", $"http://{setting.ToLowerInvariant()}/");
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<ILegacyAuthClient>();
                services.AddSingleton<ILegacyAuthClient>(new AuthClient(permissions));
                services.RemoveAll<IPurchaseOrderDetailGateway>();
                services.AddSingleton<IPurchaseOrderDetailGateway>(gateway);
            });
        }
    }

    private sealed class AuthClient(IReadOnlyList<string> permissions) : ILegacyAuthClient
    {
        public Task<EmployeeLoginResult> LoginAsync(string email, string password, CancellationToken cancellationToken) => Task.FromResult(new EmployeeLoginResult(true, new AuthTokenResponse("server-only", "server-only-refresh", "Bearer", 900, DateTimeOffset.UtcNow.AddDays(1)), new EmployeeIdentity("employee", email, email, permissions, 7)));
        public Task<EmployeeRefreshResult?> RefreshAsync(string refreshToken, CancellationToken cancellationToken) => Task.FromResult<EmployeeRefreshResult?>(null);
        public Task RevokeAsync(string refreshToken, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<CustomerIdentityResponse?> CreateCustomerIdentityAsync(int databaseId, CreateCustomerIdentityRequest request, string accessToken, CancellationToken cancellationToken) => Task.FromResult<CustomerIdentityResponse?>(null);
        public Task<EmployeeIdentityResponse?> CreateEmployeeIdentityAsync(int databaseId, CreateEmployeeIdentityRequest request, string accessToken, CancellationToken cancellationToken) => Task.FromResult<EmployeeIdentityResponse?>(null);
    }

    private sealed class Gateway : IPurchaseOrderDetailGateway
    {
        public int OrderReads { get; private set; }
        public List<string> Deletes { get; } = [];
        public Task<PurchaseOrderDetailData?> GetOrderAsync(int id, CancellationToken cancellationToken) { OrderReads++; return Task.FromResult<PurchaseOrderDetailData?>(new(id, 42, "Buyer", 7, "Courier", "Bangkok", "Net 30", "Notes", new DateTime(2026, 7, 18))); }
        public Task<string?> GetSupplierNameAsync(int id, CancellationToken cancellationToken) => Task.FromResult<string?>("Acme");
        public Task<string?> GetEmployeeNameAsync(int id, CancellationToken cancellationToken) => Task.FromResult<string?>("Somchai");
        public Task<IReadOnlyList<PurchaseOrderDetailItemData>> GetItemsAsync(int orderId, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<PurchaseOrderDetailItemData>>([new(9, orderId, "R-1", "Resin", 2, 100, 200)]);
        public Task<IReadOnlyList<PurchaseOrderDetailFileData>> GetFilesAsync(int orderId, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<PurchaseOrderDetailFileData>>([new(5, orderId, "maliev.com", "purchaseorders/PurchaseOrder_84.pdf")]);
        public Task<Uri?> GetSignedUrlAsync(string bucket, string objectName, CancellationToken cancellationToken) => Task.FromResult<Uri?>(new("https://storage.test/signed"));
        public Task DeleteStoredFileAsync(string bucket, string objectName, CancellationToken cancellationToken) { Deletes.Add("stored"); return Task.CompletedTask; }
        public Task DeleteFileLinkAsync(int id, CancellationToken cancellationToken) { Deletes.Add("link"); return Task.CompletedTask; }
        public Task DeleteItemAsync(int id, CancellationToken cancellationToken) { Deletes.Add("item"); return Task.CompletedTask; }
        public Task DeleteOrderAsync(int id, CancellationToken cancellationToken) { Deletes.Add("order"); return Task.CompletedTask; }
    }
}
