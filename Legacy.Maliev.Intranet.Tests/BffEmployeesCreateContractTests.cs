extern alias Bff;

using System.Net;
using System.Net.Http.Json;
using System.Text;
using Legacy.Maliev.Intranet.Auth;
using Legacy.Maliev.Intranet.Contracts;
using Legacy.Maliev.Intranet.Employees;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using BffProgram = Bff::Program;

namespace Legacy.Maliev.Intranet.Tests;

public sealed class BffEmployeesCreateContractTests
{
    [Fact]
    public async Task AuthorizedEmployee_WithCsrf_UsesServerTokenAndReturnsOnlyCreatedId()
    {
        var profiles = new RecordingHandler((HttpStatusCode.Created, "{\"Id\":42}"));
        var identities = new RecordingHandler((HttpStatusCode.Created, "{\"databaseID\":42}"));
        await using var factory = new EmployeesCreateBffFactory(profiles, identities, hasCreatePermission: true);
        using var client = CreateClient(factory);
        await SignInAsync(client);

        using var response = await SendCreateAsync(client, includeCsrf: true);
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal("/employees", profiles.Requests.Single().PathAndQuery);
        Assert.Equal("Bearer signed-service-token", profiles.Requests.Single().Authorization);
        Assert.Contains("\"FirstName\":\"Ada\"", profiles.Requests.Single().Body, StringComparison.Ordinal);
        Assert.DoesNotContain("password", profiles.Requests.Single().Body, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("/auth/v1/employee-identities/42", identities.Requests.Single().PathAndQuery);
        Assert.Equal("Bearer signed-service-token", identities.Requests.Single().Authorization);
        Assert.Contains("\"password\":\"correct horse battery staple\"", identities.Requests.Single().Body, StringComparison.Ordinal);
        Assert.Contains("\"id\":42", body, StringComparison.Ordinal);
        Assert.DoesNotContain("password", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task MissingCsrf_IsRejectedBeforeDownstreamCalls()
    {
        var profiles = new RecordingHandler((HttpStatusCode.Created, "{\"Id\":42}"));
        var identities = new RecordingHandler((HttpStatusCode.Created, "{\"databaseID\":42}"));
        await using var factory = new EmployeesCreateBffFactory(profiles, identities, hasCreatePermission: true);
        using var client = CreateClient(factory);
        await SignInAsync(client);

        using var response = await SendCreateAsync(client, includeCsrf: false);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(profiles.Requests);
        Assert.Empty(identities.Requests);
    }

    [Fact]
    public async Task EmployeeWithoutCreatePermission_IsForbiddenBeforeDownstreamCalls()
    {
        var profiles = new RecordingHandler((HttpStatusCode.Created, "{\"Id\":42}"));
        var identities = new RecordingHandler((HttpStatusCode.Created, "{\"databaseID\":42}"));
        await using var factory = new EmployeesCreateBffFactory(profiles, identities, hasCreatePermission: false);
        using var client = CreateClient(factory);
        await SignInAsync(client);

        using var response = await SendCreateAsync(client, includeCsrf: true);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Empty(profiles.Requests);
        Assert.Empty(identities.Requests);
    }

    [Fact]
    public async Task AnonymousRequest_IsUnauthorizedBeforeDownstreamCalls()
    {
        var profiles = new RecordingHandler((HttpStatusCode.Created, "{\"Id\":42}"));
        var identities = new RecordingHandler((HttpStatusCode.Created, "{\"databaseID\":42}"));
        await using var factory = new EmployeesCreateBffFactory(profiles, identities, hasCreatePermission: true);
        using var client = CreateClient(factory);

        using var response = await SendCreateAsync(client, includeCsrf: false);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Empty(profiles.Requests);
        Assert.Empty(identities.Requests);
    }

    private static HttpClient CreateClient(WebApplicationFactory<BffProgram> factory) =>
        factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            BaseAddress = new Uri("https://localhost"),
            HandleCookies = true,
        });

    private static async Task SignInAsync(HttpClient client)
    {
        using var sessionResponse = await client.GetAsync("/bff/session");
        var session = await sessionResponse.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/bff/login")
        {
            Content = JsonContent.Create(new { email = "employee@maliev.com", password = "password", returnUrl = "/Employees/Create" }),
        };
        request.Headers.Add("X-CSRF-TOKEN", session.GetProperty("csrfToken").GetString());
        using var response = await client.SendAsync(request);
        response.EnsureSuccessStatusCode();
    }

    private static async Task<HttpResponseMessage> SendCreateAsync(HttpClient client, bool includeCsrf)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/bff/employees") { Content = JsonContent.Create(ValidRequest()) };
        if (includeCsrf)
        {
            using var sessionResponse = await client.GetAsync("/bff/session");
            var session = await sessionResponse.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
            request.Headers.Add("X-CSRF-TOKEN", session.GetProperty("csrfToken").GetString());
        }
        return await client.SendAsync(request);
    }

    private static CreateEmployeeAccountRequest ValidRequest() => new()
    {
        FirstName = "Ada",
        LastName = "Lovelace",
        Email = "ada@example.com",
        Password = "correct horse battery staple",
        ConfirmPassword = "correct horse battery staple",
        PhoneNumber = "+66 81 234 5678",
        RoleId = 7,
        DateOfBirth = new DateTime(1815, 12, 10),
    };

    private sealed class EmployeesCreateBffFactory(RecordingHandler profiles, RecordingHandler identities, bool hasCreatePermission)
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
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<ILegacyAuthClient>();
                services.AddSingleton<ILegacyAuthClient>(new EmployeeAuthClient(hasCreatePermission));
                services.RemoveAll<IServiceAccessTokenProvider>();
                services.AddSingleton<IServiceAccessTokenProvider>(new ServiceTokenProvider());
                services.RemoveAll<IEmployeeProfileCreationClient>();
                services.AddHttpClient<IEmployeeProfileCreationClient, EmployeeProfileCreationClient>()
                    .ConfigurePrimaryHttpMessageHandler(() => profiles);
                services.RemoveAll<IEmployeeIdentityCreationClient>();
                services.AddHttpClient<IEmployeeIdentityCreationClient, EmployeeIdentityCreationClient>()
                    .ConfigurePrimaryHttpMessageHandler(() => identities);
            });
        }
    }

    private sealed class ServiceTokenProvider : IServiceAccessTokenProvider
    {
        public ValueTask<string?> GetAccessTokenAsync(CancellationToken cancellationToken) => ValueTask.FromResult<string?>("signed-service-token");
        public void Invalidate(string token) { }
    }

    private sealed class EmployeeAuthClient(bool hasCreatePermission) : ILegacyAuthClient
    {
        public Task<EmployeeLoginResult> LoginAsync(string email, string password, CancellationToken cancellationToken) => Task.FromResult(new EmployeeLoginResult(
            true,
            new AuthTokenResponse("server-only-access-token", "server-only-refresh-token", "Bearer", 900, DateTimeOffset.UtcNow.AddDays(1)),
            new EmployeeIdentity("employee-id", email, email, hasCreatePermission ? [LegacyEmployeePermissions.EmployeesCreate] : [])));
        public Task<EmployeeRefreshResult?> RefreshAsync(string refreshToken, CancellationToken cancellationToken) => Task.FromResult<EmployeeRefreshResult?>(null);
        public Task RevokeAsync(string refreshToken, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<CustomerIdentityResponse?> CreateCustomerIdentityAsync(int databaseId, CreateCustomerIdentityRequest request, string accessToken, CancellationToken cancellationToken) => Task.FromResult<CustomerIdentityResponse?>(null);
        public Task<EmployeeIdentityResponse?> CreateEmployeeIdentityAsync(int databaseId, CreateEmployeeIdentityRequest request, string accessToken, CancellationToken cancellationToken) => Task.FromResult<EmployeeIdentityResponse?>(null);
    }

    private sealed class RecordingHandler(params (HttpStatusCode Status, string Body)[] responses) : HttpMessageHandler
    {
        private readonly Queue<(HttpStatusCode Status, string Body)> queue = new(responses);
        public List<RecordedRequest> Requests { get; } = [];
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(new(request.RequestUri?.PathAndQuery, request.Headers.Authorization?.ToString(), request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken)));
            var response = queue.Dequeue();
            return new HttpResponseMessage(response.Status) { Content = new StringContent(response.Body, Encoding.UTF8, "application/json") };
        }
    }

    private sealed record RecordedRequest(string? PathAndQuery, string? Authorization, string? Body);
}
