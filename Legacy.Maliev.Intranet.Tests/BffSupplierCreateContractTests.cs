extern alias Bff;

using System.Net;
using System.Net.Http.Json;
using Legacy.Maliev.Intranet.Auth;
using Legacy.Maliev.Intranet.Contracts;
using Legacy.Maliev.Intranet.Suppliers;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using BffProgram = Bff::Program;

namespace Legacy.Maliev.Intranet.Tests;

public sealed class BffSupplierCreateContractTests
{
    [Fact]
    public async Task AuthorizedCsrfWrite_CreatesSupplierWithoutExposingServiceTokens()
    {
        var profiles = new ProfileClient();
        var addresses = new AddressClient();
        await using var factory = new Factory(profiles, addresses, canCreate: true);
        using var client = CreateClient(factory);
        var csrf = await SignInAsync(client);
        using var request = new HttpRequestMessage(HttpMethod.Post, "/bff/suppliers") { Content = JsonContent.Create(ValidRequest) };
        request.Headers.Add("X-CSRF-TOKEN", csrf);

        using var response = await client.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal("/Suppliers/View?id=42", response.Headers.Location?.OriginalString);
        Assert.Contains("\"id\":42", body, StringComparison.Ordinal);
        Assert.DoesNotContain("server-only", body, StringComparison.Ordinal);
        Assert.Equal("Acme", profiles.Request?.Name);
        Assert.Equal(42, addresses.SupplierId);
    }

    [Fact]
    public async Task MissingCsrf_IsRejectedBeforeWorkflow()
    {
        var profiles = new ProfileClient();
        var addresses = new AddressClient();
        await using var factory = new Factory(profiles, addresses, canCreate: true);
        using var client = CreateClient(factory);
        await SignInAsync(client);

        using var response = await client.PostAsJsonAsync("/bff/suppliers", ValidRequest);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Null(profiles.Request);
    }

    [Fact]
    public async Task MissingPermission_IsForbiddenBeforeWorkflow()
    {
        var profiles = new ProfileClient();
        var addresses = new AddressClient();
        await using var factory = new Factory(profiles, addresses, canCreate: false);
        using var client = CreateClient(factory);
        var csrf = await SignInAsync(client);
        using var request = new HttpRequestMessage(HttpMethod.Post, "/bff/suppliers") { Content = JsonContent.Create(ValidRequest) };
        request.Headers.Add("X-CSRF-TOKEN", csrf);

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Null(profiles.Request);
    }

    private static readonly SupplierCreateRequest ValidRequest = new() { Name = "Acme", Address1 = "1 Main Road", CountryId = 66 };

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
            Content = JsonContent.Create(new { email = "employee@maliev.com", password = "password", returnUrl = "/Suppliers/Create" }),
        };
        request.Headers.Add("X-CSRF-TOKEN", csrf);
        using var response = await client.SendAsync(request);
        response.EnsureSuccessStatusCode();

        using var refreshedSession = await client.GetAsync("/bff/session");
        var refreshed = await refreshedSession.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        return refreshed.GetProperty("csrfToken").GetString()!;
    }

    private sealed class Factory(ProfileClient profiles, AddressClient addresses, bool canCreate) : WebApplicationFactory<BffProgram>
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
            builder.UseSetting("Services:Procurement", "http://procurement/");
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<ILegacyAuthClient>();
                services.AddSingleton<ILegacyAuthClient>(new AuthClient(canCreate));
                services.RemoveAll<ISupplierProfileCreationClient>();
                services.RemoveAll<ISupplierAddressCreationClient>();
                services.AddSingleton<ISupplierProfileCreationClient>(profiles);
                services.AddSingleton<ISupplierAddressCreationClient>(addresses);
            });
        }
    }

    private sealed class AuthClient(bool canCreate) : ILegacyAuthClient
    {
        public Task<EmployeeLoginResult> LoginAsync(string email, string password, CancellationToken cancellationToken) => Task.FromResult(new EmployeeLoginResult(
            true,
            new AuthTokenResponse("server-only-access-token", "server-only-refresh-token", "Bearer", 900, DateTimeOffset.UtcNow.AddDays(1)),
            new EmployeeIdentity("employee-id", email, email, canCreate ? [LegacyEmployeePermissions.SuppliersCreate] : [], 7)));
        public Task<EmployeeRefreshResult?> RefreshAsync(string refreshToken, CancellationToken cancellationToken) => Task.FromResult<EmployeeRefreshResult?>(null);
        public Task RevokeAsync(string refreshToken, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<CustomerIdentityResponse?> CreateCustomerIdentityAsync(int databaseId, CreateCustomerIdentityRequest request, string accessToken, CancellationToken cancellationToken) => Task.FromResult<CustomerIdentityResponse?>(null);
        public Task<EmployeeIdentityResponse?> CreateEmployeeIdentityAsync(int databaseId, CreateEmployeeIdentityRequest request, string accessToken, CancellationToken cancellationToken) => Task.FromResult<EmployeeIdentityResponse?>(null);
    }

    private sealed class ProfileClient : ISupplierProfileCreationClient
    {
        public SupplierCreateRequest? Request { get; private set; }
        public Task<HttpResponseMessage> CreateAsync(SupplierCreateRequest request, CancellationToken cancellationToken)
        {
            Request = request;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Created) { Content = JsonContent.Create(new { id = 42 }) });
        }
        public Task<HttpResponseMessage> DeleteAsync(int supplierId, CancellationToken cancellationToken) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.NoContent));
    }

    private sealed class AddressClient : ISupplierAddressCreationClient
    {
        public int? SupplierId { get; private set; }
        public Task<HttpResponseMessage> CreateAsync(int supplierId, SupplierCreateRequest request, CancellationToken cancellationToken)
        {
            SupplierId = supplierId;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Created));
        }
    }
}
