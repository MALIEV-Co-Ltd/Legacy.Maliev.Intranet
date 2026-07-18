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

public sealed class BffSupplierManagementContractTests
{
    [Fact]
    public async Task AuthorizedRead_CombinesSupplierAndAddressWithoutExposingServerCredentials()
    {
        var downstream = new SupplierManagementClient();
        await using var factory = new Factory(downstream, [LegacyEmployeePermissions.SuppliersRead]);
        using var client = CreateClient(factory);
        await SignInAsync(client);

        using var response = await client.GetAsync("/bff/suppliers/42");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("\"name\":\"Acme\"", body, StringComparison.Ordinal);
        Assert.Contains("\"address1\":\"1 Main Road\"", body, StringComparison.Ordinal);
        Assert.DoesNotContain("server-only", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task MissingReadPermission_IsRejectedBeforeProcurementService()
    {
        var downstream = new SupplierManagementClient();
        await using var factory = new Factory(downstream, []);
        using var client = CreateClient(factory);
        await SignInAsync(client);

        using var response = await client.GetAsync("/bff/suppliers/42");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(0, downstream.ProfileReads);
    }

    [Fact]
    public async Task InvalidSupplierIdentifier_IsRejectedBeforeProcurementService()
    {
        var downstream = new SupplierManagementClient();
        await using var factory = new Factory(downstream, [LegacyEmployeePermissions.SuppliersRead]);
        using var client = CreateClient(factory);
        await SignInAsync(client);

        using var response = await client.GetAsync("/bff/suppliers/0");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(0, downstream.ProfileReads);
    }

    [Fact]
    public async Task MissingCsrf_IsRejectedBeforeSupplierUpdate()
    {
        var downstream = new SupplierManagementClient();
        await using var factory = new Factory(downstream, [LegacyEmployeePermissions.SuppliersUpdate]);
        using var client = CreateClient(factory);
        await SignInAsync(client);

        using var response = await client.PutAsJsonAsync("/bff/suppliers/42", ValidRequest);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(0, downstream.ProfileUpdates);
    }

    [Fact]
    public async Task MissingUpdatePermission_IsRejectedBeforeSupplierUpdate()
    {
        var downstream = new SupplierManagementClient();
        await using var factory = new Factory(downstream, [LegacyEmployeePermissions.SuppliersRead]);
        using var client = CreateClient(factory);
        var csrf = await SignInAsync(client);
        using var request = new HttpRequestMessage(HttpMethod.Put, "/bff/suppliers/42")
        {
            Content = JsonContent.Create(ValidRequest),
        };
        request.Headers.Add("X-CSRF-TOKEN", csrf);

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(0, downstream.ProfileUpdates);
    }

    [Fact]
    public async Task AuthorizedCsrfUpdate_UsesExistingOwnedAddress()
    {
        var downstream = new SupplierManagementClient();
        await using var factory = new Factory(downstream, [LegacyEmployeePermissions.SuppliersUpdate]);
        using var client = CreateClient(factory);
        var csrf = await SignInAsync(client);
        using var request = new HttpRequestMessage(HttpMethod.Put, "/bff/suppliers/42")
        {
            Content = JsonContent.Create(ValidRequest),
        };
        request.Headers.Add("X-CSRF-TOKEN", csrf);

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(1, downstream.ProfileUpdates);
        Assert.Equal(7, downstream.UpdatedAddressId);
    }

    [Fact]
    public async Task AuthorizedCsrfDelete_DeletesSupplierProfile()
    {
        var downstream = new SupplierManagementClient();
        await using var factory = new Factory(downstream, [LegacyEmployeePermissions.SuppliersDelete]);
        using var client = CreateClient(factory);
        var csrf = await SignInAsync(client);
        using var request = new HttpRequestMessage(HttpMethod.Delete, "/bff/suppliers/42");
        request.Headers.Add("X-CSRF-TOKEN", csrf);

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(42, downstream.DeletedSupplierId);
    }

    [Fact]
    public async Task ProcurementRateLimit_PreservesBoundedRetryAfter()
    {
        var downstream = new SupplierManagementClient
        {
            UpdateStatus = HttpStatusCode.TooManyRequests,
            RetryAfter = TimeSpan.FromSeconds(30),
        };
        await using var factory = new Factory(downstream, [LegacyEmployeePermissions.SuppliersUpdate]);
        using var client = CreateClient(factory);
        var csrf = await SignInAsync(client);
        using var request = new HttpRequestMessage(HttpMethod.Put, "/bff/suppliers/42")
        {
            Content = JsonContent.Create(ValidRequest),
        };
        request.Headers.Add("X-CSRF-TOKEN", csrf);

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode);
        Assert.Equal(TimeSpan.FromSeconds(30), response.Headers.RetryAfter?.Delta);
    }

    private static readonly SupplierCreateRequest ValidRequest = new()
    {
        Name = "Acme",
        Address1 = "1 Main Road",
        CountryId = 66,
    };

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
            Content = JsonContent.Create(new
            {
                email = "employee@maliev.com",
                password = "password",
                returnUrl = "/Suppliers/View?id=42",
            }),
        };
        request.Headers.Add("X-CSRF-TOKEN", csrf);
        using var response = await client.SendAsync(request);
        response.EnsureSuccessStatusCode();

        using var refreshedSession = await client.GetAsync("/bff/session");
        var refreshed = await refreshedSession.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        return refreshed.GetProperty("csrfToken").GetString()!;
    }

    private sealed class Factory(SupplierManagementClient downstream, IReadOnlyList<string> permissions)
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
            builder.UseSetting("Services:Procurement", "http://procurement/");
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<ILegacyAuthClient>();
                services.AddSingleton<ILegacyAuthClient>(new AuthClient(permissions));
                services.RemoveAll<ISupplierManagementClient>();
                services.AddSingleton<ISupplierManagementClient>(downstream);
            });
        }
    }

    private sealed class AuthClient(IReadOnlyList<string> permissions) : ILegacyAuthClient
    {
        public Task<EmployeeLoginResult> LoginAsync(string email, string password, CancellationToken cancellationToken) =>
            Task.FromResult(new EmployeeLoginResult(
                true,
                new AuthTokenResponse("server-only-access-token", "server-only-refresh-token", "Bearer", 900, DateTimeOffset.UtcNow.AddDays(1)),
                new EmployeeIdentity("employee-id", email, email, permissions, 7)));

        public Task<EmployeeRefreshResult?> RefreshAsync(string refreshToken, CancellationToken cancellationToken) =>
            Task.FromResult<EmployeeRefreshResult?>(null);

        public Task RevokeAsync(string refreshToken, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task<CustomerIdentityResponse?> CreateCustomerIdentityAsync(int databaseId, CreateCustomerIdentityRequest request, string accessToken, CancellationToken cancellationToken) =>
            Task.FromResult<CustomerIdentityResponse?>(null);

        public Task<EmployeeIdentityResponse?> CreateEmployeeIdentityAsync(int databaseId, CreateEmployeeIdentityRequest request, string accessToken, CancellationToken cancellationToken) =>
            Task.FromResult<EmployeeIdentityResponse?>(null);
    }

    private sealed class SupplierManagementClient : ISupplierManagementClient
    {
        public int ProfileReads { get; private set; }
        public int ProfileUpdates { get; private set; }
        public int? UpdatedAddressId { get; private set; }
        public int? DeletedSupplierId { get; private set; }
        public HttpStatusCode UpdateStatus { get; init; } = HttpStatusCode.NoContent;
        public TimeSpan? RetryAfter { get; init; }

        public Task<HttpResponseMessage> GetProfileAsync(int id, CancellationToken cancellationToken)
        {
            ProfileReads++;
            return Task.FromResult(Json(HttpStatusCode.OK, new
            {
                id,
                name = "Acme",
                website = "https://acme.test",
                taxNumber = "TAX-42",
                email = "sales@acme.test",
                note = "Preferred",
                telephone = "02-000-0000",
                mobile = "08-000-0000",
                fax = (string?)null,
            }));
        }

        public Task<HttpResponseMessage> GetAddressAsync(int id, CancellationToken cancellationToken) =>
            Task.FromResult(Json(HttpStatusCode.OK, new
            {
                id = 7,
                building = "A",
                address1 = "1 Main Road",
                address2 = (string?)null,
                city = "Bangkok",
                state = "Bangkok",
                postalCode = "10110",
                countryId = 66,
            }));

        public Task<HttpResponseMessage> UpdateProfileAsync(int id, SupplierCreateRequest request, CancellationToken cancellationToken)
        {
            ProfileUpdates++;
            var response = new HttpResponseMessage(UpdateStatus);
            if (RetryAfter is { } retryAfter)
            {
                response.Headers.RetryAfter = new(retryAfter);
            }

            return Task.FromResult(response);
        }

        public Task<HttpResponseMessage> CreateAddressAsync(int id, SupplierCreateRequest request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.Created));

        public Task<HttpResponseMessage> UpdateAddressAsync(int id, SupplierCreateRequest request, CancellationToken cancellationToken)
        {
            UpdatedAddressId = id;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NoContent));
        }

        public Task<HttpResponseMessage> DeleteProfileAsync(int id, CancellationToken cancellationToken)
        {
            DeletedSupplierId = id;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NoContent));
        }

        private static HttpResponseMessage Json(HttpStatusCode status, object value) =>
            new(status) { Content = JsonContent.Create(value) };
    }
}
