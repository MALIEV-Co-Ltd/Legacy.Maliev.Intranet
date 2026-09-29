using Legacy.Maliev.Intranet.Auth;
using Legacy.Maliev.Intranet.Employees;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using System.Net;
using System.Net.Http.Json;
using System.Text.RegularExpressions;

namespace Legacy.Maliev.Intranet.Tests;

public sealed partial class EmployeePageContractTests
{
    [Fact]
    public async Task AuthenticatedEmployeePages_RenderTypedEmployeeResults()
    {
        var employees = new StubEmployeeClient();
        var auth = new StubAuthClient();
        await using var factory = new EmployeeIntranetFactory(employees, auth);
        using var client = factory.CreateClient(new() { AllowAutoRedirect = false, HandleCookies = true, BaseAddress = new Uri("https://localhost") });
        await LoginAsync(client);

        var index = await client.GetAsync("/Employees/Index?search=ada&index=1&size=25");
        var indexHtml = await index.Content.ReadAsStringAsync();
        var view = await client.GetAsync("/Employees/View?id=42");
        var viewHtml = await view.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, index.StatusCode);
        Assert.Contains("Ada Lovelace", indexHtml, StringComparison.Ordinal);
        Assert.Contains("ada@example.com", indexHtml, StringComparison.Ordinal);
        Assert.Equal("employee-access-token", employees.LastAccessToken);
        Assert.Equal(HttpStatusCode.OK, view.StatusCode);
        Assert.Contains("Employee #42", viewHtml, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CreateEmployee_UsesAuthIdentityBoundaryAndRollsBackProfileOnConflict()
    {
        var employees = new StubEmployeeClient();
        var auth = new StubAuthClient { IdentityConflict = true };
        await using var factory = new EmployeeIntranetFactory(employees, auth);
        using var client = factory.CreateClient(new() { AllowAutoRedirect = false, HandleCookies = true, BaseAddress = new Uri("https://localhost") });
        await LoginAsync(client);
        var createPage = await client.GetStringAsync("/Employees/Create");
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

        var response = await client.PostAsync("/Employees/Create", form);
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("secret / ?", auth.EmployeeIdentityRequest?.Password);
        Assert.False(auth.EmployeeIdentityRequest?.EmailConfirmed);
        Assert.Equal("employee-access-token", auth.IdentityAccessToken);
        Assert.Equal(42, employees.DeletedEmployeeId);
        Assert.DoesNotContain("secret / ?", html, StringComparison.Ordinal);
        Assert.Contains("could not be created", html, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CreateEmployee_NotificationFailurePreservesCreatedIdentityAndShowsWarning()
    {
        var employees = new StubEmployeeClient();
        var auth = new StubAuthClient();
        await using var factory = new EmployeeIntranetFactory(employees, auth, HttpStatusCode.ServiceUnavailable);
        using var client = factory.CreateClient(new() { AllowAutoRedirect = false, HandleCookies = true, BaseAddress = new Uri("https://localhost") });
        await LoginAsync(client);
        var createPage = await client.GetStringAsync("/Employees/Create");
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

        using var response = await client.PostAsync("/Employees/Create", form);
        using var view = await client.GetAsync(response.Headers.Location);
        var html = await view.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.False(auth.EmployeeIdentityRequest?.EmailConfirmed);
        Assert.Null(employees.DeletedEmployeeId);
        Assert.Contains("created, but the confirmation email could not be sent", html, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CreateEmployee_MissingTrustedOrigin_DoesNotCreateProfileOrIdentity()
    {
        var employees = new StubEmployeeClient();
        var auth = new StubAuthClient();
        await using var factory = new EmployeeIntranetFactory(employees, auth, publicOrigin: null);
        using var client = factory.CreateClient(new() { AllowAutoRedirect = false, HandleCookies = true, BaseAddress = new Uri("https://localhost") });
        await LoginAsync(client);
        var createPage = await client.GetStringAsync("/Employees/Create");
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

        using var response = await client.PostAsync("/Employees/Create", form);
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("email confirmation is unavailable", html, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, employees.CreatedEmployeeCount);
        Assert.Null(auth.EmployeeIdentityRequest);
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

    private sealed class EmployeeIntranetFactory(ILegacyEmployeeClient employees, ILegacyAuthClient auth, HttpStatusCode notificationStatus = HttpStatusCode.Accepted, string? publicOrigin = "https://intranet.example.com/") : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing");
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<ILegacyEmployeeClient>();
                services.RemoveAll<ILegacyAuthClient>();
                services.AddSingleton(employees);
                services.AddSingleton(auth);
                services.RemoveAll<LegacyEmployeeConfirmationDelivery>();
                var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["EmployeeConfirmation:PublicOrigin"] = publicOrigin,
                }).Build();
                services.AddSingleton(new LegacyEmployeeConfirmationDelivery(
                    new ConfirmationClientFactory(notificationStatus), configuration,
                    NullLogger<LegacyEmployeeConfirmationDelivery>.Instance));
            });
        }
    }

    private sealed class ConfirmationClientFactory(HttpStatusCode notificationStatus) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(new ConfirmationHandler(notificationStatus), disposeHandler: true)
        {
            BaseAddress = new(name == "employee-confirmation-auth" ? "http://auth/" : "http://notification/"),
        };
    }

    private sealed class ConfirmationHandler(HttpStatusCode notificationStatus) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(request.RequestUri!.AbsolutePath == "/auth/v1/employee-self-service/email-confirmation/request"
                ? new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = JsonContent.Create(new { accepted = true, token = "opaque-confirm-token-0123456789012345678901" }),
                }
                : new HttpResponseMessage(notificationStatus));
    }

    private sealed class StubEmployeeClient : ILegacyEmployeeClient
    {
        private static readonly EmployeeResponse Ada = new(
            42, null, "Ada", "Lovelace", "Ada Lovelace", null, "ada@example.com", null,
            null, null, null, null, new RoleResponse(1, "Engineer", null, null, null));

        public string? LastAccessToken { get; private set; }
        public int? DeletedEmployeeId { get; private set; }
        public int CreatedEmployeeCount { get; private set; }

        public Task<PaginatedResponse<EmployeeResponse>?> GetEmployeesAsync(EmployeeSortType sort, string? search, int index, int size, string accessToken, CancellationToken cancellationToken)
        {
            LastAccessToken = accessToken;
            return Task.FromResult<PaginatedResponse<EmployeeResponse>?>(new([Ada], index, 1, 1));
        }

        public Task<EmployeeResponse?> GetEmployeeAsync(int id, string accessToken, CancellationToken cancellationToken)
        {
            LastAccessToken = accessToken;
            return Task.FromResult<EmployeeResponse?>(id == Ada.Id ? Ada : null);
        }

        public Task<EmployeeResponse> CreateEmployeeAsync(UpsertEmployeeRequest request, string accessToken, CancellationToken cancellationToken)
        {
            CreatedEmployeeCount++;
            LastAccessToken = accessToken;
            return Task.FromResult(Ada);
        }

        public Task DeleteEmployeeAsync(int id, string accessToken, CancellationToken cancellationToken)
        {
            DeletedEmployeeId = id;
            LastAccessToken = accessToken;
            return Task.CompletedTask;
        }
    }

    private sealed class StubAuthClient : ILegacyAuthClient
    {
        public bool IdentityConflict { get; init; }
        public CreateEmployeeIdentityRequest? EmployeeIdentityRequest { get; private set; }
        public string? IdentityAccessToken { get; private set; }

        public Task<EmployeeLoginResult> LoginAsync(string email, string password, CancellationToken cancellationToken) =>
            Task.FromResult(new EmployeeLoginResult(true,
                new AuthTokenResponse("employee-access-token", "employee-refresh-token", "Bearer", 900, DateTimeOffset.UtcNow.AddDays(14)),
                new EmployeeIdentity("employee-id", email, email)));

        public Task<EmployeeRefreshResult?> RefreshAsync(string refreshToken, CancellationToken cancellationToken) => Task.FromResult<EmployeeRefreshResult?>(null);
        public Task RevokeAsync(string refreshToken, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<CustomerIdentityResponse?> CreateCustomerIdentityAsync(int databaseId, CreateCustomerIdentityRequest request, string accessToken, CancellationToken cancellationToken) => Task.FromResult<CustomerIdentityResponse?>(null);

        public Task<EmployeeIdentityResponse?> CreateEmployeeIdentityAsync(int databaseId, CreateEmployeeIdentityRequest request, string accessToken, CancellationToken cancellationToken)
        {
            EmployeeIdentityRequest = request;
            IdentityAccessToken = accessToken;
            return Task.FromResult<EmployeeIdentityResponse?>(IdentityConflict
                ? null
                : new("identity-id", request.UserName, request.Email, true, request.PhoneNumber, false, false, null, true, 0, databaseId));
        }
    }
}
