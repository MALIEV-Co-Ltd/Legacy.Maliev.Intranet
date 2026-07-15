using System.Net;
using System.Text.RegularExpressions;
using Legacy.Maliev.Intranet.Auth;
using Legacy.Maliev.Intranet.Customers;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Legacy.Maliev.Intranet.Tests;

public sealed partial class CustomerPageContractTests
{
    [Fact]
    public async Task AuthenticatedCustomerPages_RenderTypedCustomerResults()
    {
        var customers = new StubCustomerClient();
        var auth = new StubAuthClient();
        await using var factory = new CustomerIntranetFactory(customers, auth);
        using var client = factory.CreateClient(new() { AllowAutoRedirect = false, HandleCookies = true });
        await LoginAsync(client);

        var index = await client.GetAsync("/Customers/Index?search=ada&index=1&size=25");
        var indexHtml = await index.Content.ReadAsStringAsync();
        var view = await client.GetAsync("/Customers/View?id=42");
        var viewHtml = await view.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, index.StatusCode);
        Assert.Contains("Ada Lovelace", indexHtml, StringComparison.Ordinal);
        Assert.Contains("ada@example.com", indexHtml, StringComparison.Ordinal);
        Assert.Equal("employee-access-token", customers.LastAccessToken);
        Assert.Equal(HttpStatusCode.OK, view.StatusCode);
        Assert.Contains("Customer #42", viewHtml, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CreateCustomer_UsesAuthIdentityBoundaryAndRollsBackProfileOnConflict()
    {
        var customers = new StubCustomerClient();
        var auth = new StubAuthClient { IdentityConflict = true };
        await using var factory = new CustomerIntranetFactory(customers, auth);
        using var client = factory.CreateClient(new() { AllowAutoRedirect = false, HandleCookies = true });
        await LoginAsync(client);
        var createPage = await client.GetStringAsync("/Customers/Create");
        var antiForgery = AntiForgeryToken().Match(createPage).Groups[1].Value;
        using var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Input.FirstName"] = "Ada",
            ["Input.LastName"] = "Lovelace",
            ["Input.Email"] = "ada@example.com",
            ["Input.Password"] = "secret / ?",
            ["Input.ConfirmPassword"] = "secret / ?",
            ["__RequestVerificationToken"] = antiForgery,
        });

        var response = await client.PostAsync("/Customers/Create", form);
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("secret / ?", auth.IdentityRequest?.Password);
        Assert.Equal("employee-access-token", auth.IdentityAccessToken);
        Assert.Equal(42, customers.DeletedCustomerId);
        Assert.DoesNotContain("secret / ?", html, StringComparison.Ordinal);
        Assert.Contains("could not be created", html, StringComparison.OrdinalIgnoreCase);
    }

    private static async Task LoginAsync(HttpClient client)
    {
        var loginPage = await client.GetStringAsync("/Login");
        var antiForgery = AntiForgeryToken().Match(loginPage).Groups[1].Value;
        using var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Email"] = "employee@maliev.com",
            ["Password"] = "employee-password",
            ["__RequestVerificationToken"] = antiForgery,
        });
        var response = await client.PostAsync("/Login", form);
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
    }

    [GeneratedRegex("name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"", RegexOptions.CultureInvariant)]
    private static partial Regex AntiForgeryToken();

    private sealed class CustomerIntranetFactory(ILegacyCustomerClient customers, ILegacyAuthClient auth) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing");
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<ILegacyCustomerClient>();
                services.RemoveAll<ILegacyAuthClient>();
                services.AddSingleton(customers);
                services.AddSingleton(auth);
            });
        }
    }

    private sealed class StubCustomerClient : ILegacyCustomerClient
    {
        private static readonly CustomerResponse Ada = new(
            42, "Ada", "Lovelace", "Ada Lovelace", null, null, null, "ada@example.com", null,
            null, null, null, null, null, null, null, null);

        public string? LastAccessToken { get; private set; }
        public int? DeletedCustomerId { get; private set; }

        public Task<PaginatedResponse<CustomerResponse>?> GetCustomersAsync(CustomerSortType sort, string? search, int index, int size, string accessToken, CancellationToken cancellationToken)
        {
            LastAccessToken = accessToken;
            return Task.FromResult<PaginatedResponse<CustomerResponse>?>(new([Ada], index, 1, 1));
        }

        public Task<CustomerResponse?> GetCustomerAsync(int id, string accessToken, CancellationToken cancellationToken)
        {
            LastAccessToken = accessToken;
            return Task.FromResult<CustomerResponse?>(id == Ada.Id ? Ada : null);
        }

        public Task<CustomerResponse> CreateCustomerAsync(UpsertCustomerRequest request, string accessToken, CancellationToken cancellationToken)
        {
            LastAccessToken = accessToken;
            return Task.FromResult(Ada);
        }

        public Task DeleteCustomerAsync(int id, string accessToken, CancellationToken cancellationToken)
        {
            DeletedCustomerId = id;
            LastAccessToken = accessToken;
            return Task.CompletedTask;
        }
    }

    private sealed class StubAuthClient : ILegacyAuthClient
    {
        public bool IdentityConflict { get; init; }
        public CreateCustomerIdentityRequest? IdentityRequest { get; private set; }
        public string? IdentityAccessToken { get; private set; }

        public Task<EmployeeLoginResult> LoginAsync(string email, string password, CancellationToken cancellationToken) =>
            Task.FromResult(new EmployeeLoginResult(
                true,
                new AuthTokenResponse("employee-access-token", "employee-refresh-token", "Bearer", 900, DateTimeOffset.UtcNow.AddDays(14)),
                new EmployeeIdentity("employee-id", email, email)));

        public Task<AuthTokenResponse?> RefreshAsync(string refreshToken, CancellationToken cancellationToken) =>
            Task.FromResult<AuthTokenResponse?>(null);

        public Task RevokeAsync(string refreshToken, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task<CustomerIdentityResponse?> CreateCustomerIdentityAsync(
            int databaseId,
            CreateCustomerIdentityRequest request,
            string accessToken,
            CancellationToken cancellationToken)
        {
            IdentityRequest = request;
            IdentityAccessToken = accessToken;
            return Task.FromResult<CustomerIdentityResponse?>(IdentityConflict
                ? null
                : new("identity-id", request.UserName, request.Email, true, request.PhoneNumber, false, false, null, true, 0, databaseId, request.FaxNumber, request.MobileNumber));
        }
    }
}
