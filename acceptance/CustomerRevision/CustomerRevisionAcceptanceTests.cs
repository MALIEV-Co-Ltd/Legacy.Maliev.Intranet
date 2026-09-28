extern alias Bff;
extern alias CustomerApi;

using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using BffProgram = Bff::Program;
using CustomerProgram = CustomerApi::Program;
using CustomersProxy = Bff::Legacy.Maliev.Intranet.Bff.Customers.CustomersProxy;
using CustomerUpdateProxy = Bff::Legacy.Maliev.Intranet.Bff.Customers.CustomerUpdateProxy;
using Legacy.Maliev.CustomerService.Application.Interfaces;
using Legacy.Maliev.CustomerService.Application.Models;
using Legacy.Maliev.CustomerService.Application.Services;
using Legacy.Maliev.CustomerService.Data;
using Legacy.Maliev.Intranet.Auth;
using Legacy.Maliev.Intranet.Contracts;
using Maliev.Aspire.ServiceDefaults;
using Maliev.Aspire.ServiceDefaults.Authorization;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Testcontainers.PostgreSql;

namespace CustomerRevision.Acceptance;

public sealed partial class CustomerRevisionAcceptanceTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer postgres = new PostgreSqlBuilder("postgres:18-alpine").Build();

    public async Task InitializeAsync()
    {
        await postgres.StartAsync();
        await using var db = CreateDb();
        await db.Database.MigrateAsync();
    }

    public Task DisposeAsync() => postgres.DisposeAsync().AsTask();

    [Fact]
    public async Task TwoEmployeeSessions_RejectStaleWriteAndAcceptFreshRevision()
    {
        await using var customerApp = await StartCustomerAsync();
        using var customerClient = customerApp.GetTestClient();
        customerClient.DefaultRequestHeaders.Add("Test-Identity", "allowed");
        using var created = await customerClient.PostAsJsonAsync("/customers", InitialProfile());
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var id = (await created.Content.ReadFromJsonAsync<CustomerResponse>())!.Id;

        await using var bff = new IntranetFactory(customerApp, canRead: true, canUpdate: true);
        using var first = CreateEmployeeClient(bff);
        using var second = CreateEmployeeClient(bff);
        await SignInAsync(first, "integration-first@maliev.com");
        await SignInAsync(second, "integration-second@maliev.com");

        using var firstRead = await first.GetAsync($"/bff/customers/{id}/versioned");
        using var secondRead = await second.GetAsync($"/bff/customers/{id}/versioned");
        Assert.Equal(HttpStatusCode.OK, firstRead.StatusCode);
        Assert.Equal(HttpStatusCode.OK, secondRead.StatusCode);
        var originalRevision = firstRead.Headers.ETag?.ToString();
        Assert.NotNull(originalRevision);
        Assert.Equal(originalRevision, secondRead.Headers.ETag?.ToString());

        using var winner = await UpdateAsync(first, id, Profile("Winner"), originalRevision, includeCsrf: true);
        using var stale = await UpdateAsync(second, id, Profile("Loser"), originalRevision, includeCsrf: true);
        Assert.Equal(HttpStatusCode.NoContent, winner.StatusCode);
        Assert.Equal(HttpStatusCode.PreconditionFailed, stale.StatusCode);
        Assert.Equal("Winner", (await ReadPersistedAsync(customerClient, id)).FirstName);

        using var refreshed = await second.GetAsync($"/bff/customers/{id}/versioned");
        Assert.Equal(HttpStatusCode.OK, refreshed.StatusCode);
        var freshRevision = refreshed.Headers.ETag?.ToString();
        Assert.NotNull(freshRevision);
        Assert.NotEqual(originalRevision, freshRevision);
        using var freshWrite = await UpdateAsync(second, id, Profile("Fresh"), freshRevision, includeCsrf: true);
        Assert.Equal(HttpStatusCode.NoContent, freshWrite.StatusCode);
        Assert.Equal("Fresh", (await ReadPersistedAsync(customerClient, id)).FirstName);

        using var noCsrf = await UpdateAsync(first, id, Profile("Rejected"), freshRevision, includeCsrf: false);
        using var missingRevision = await UpdateAsync(first, id, Profile("Rejected"), null, includeCsrf: true);
        using var malformedRevision = await UpdateAsync(first, id, Profile("Rejected"), "W/\"00000001\"", includeCsrf: true);
        Assert.Equal(HttpStatusCode.BadRequest, noCsrf.StatusCode);
        Assert.Equal((HttpStatusCode)428, missingRevision.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, malformedRevision.StatusCode);
        Assert.Equal("Fresh", (await ReadPersistedAsync(customerClient, id)).FirstName);

        await using var forbiddenBff = new IntranetFactory(customerApp, canRead: false, canUpdate: false);
        using var forbidden = CreateEmployeeClient(forbiddenBff);
        await SignInAsync(forbidden, "integration-forbidden@maliev.com");
        using var forbiddenRead = await forbidden.GetAsync($"/bff/customers/{id}/versioned");
        using var forbiddenWrite = await UpdateAsync(forbidden, id, Profile("Rejected"), freshRevision, includeCsrf: true);
        Assert.Equal(HttpStatusCode.Forbidden, forbiddenRead.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, forbiddenWrite.StatusCode);
        Assert.Equal("Fresh", (await ReadPersistedAsync(customerClient, id)).FirstName);

        using var anonymous = CreateEmployeeClient(bff);
        using var anonymousRead = await anonymous.GetAsync($"/bff/customers/{id}/versioned");
        Assert.Equal(HttpStatusCode.Unauthorized, anonymousRead.StatusCode);
    }

    private CustomerDbContext CreateDb() => new(new DbContextOptionsBuilder<CustomerDbContext>()
        .UseNpgsql(postgres.GetConnectionString()).Options);

    private async Task<WebApplication> StartCustomerAsync(string? servicePublicKey = null)
    {
        // The real-auth lane must not use ServiceDefaults' permissive Testing JWT validator.
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = servicePublicKey is null ? "Testing" : "Production",
        });
        builder.WebHost.UseTestServer();
        builder.Services.AddDbContext<CustomerDbContext>(options => options.UseNpgsql(postgres.GetConnectionString()));
        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.AddScoped<ICustomerRepository, CustomerRepository>();
        builder.Services.AddScoped<ICustomerCache, NoOpCustomerCache>();
        builder.Services.AddScoped<ICustomerService, CustomerApplicationService>();
        builder.Services.AddScoped<CustomerApi::Legacy.Maliev.CustomerService.Api.CustomerCreateReplayService>();
        builder.Services.AddControllers().AddApplicationPart(typeof(CustomerProgram).Assembly).AddJsonOptions(options =>
        {
            options.JsonSerializerOptions.PropertyNamingPolicy = null;
            options.JsonSerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
        });
        if (servicePublicKey is not null)
        {
            builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Jwt:PublicKey"] = Convert.ToBase64String(Encoding.UTF8.GetBytes(servicePublicKey)),
                ["Jwt:Issuer"] = "https://disposable-auth.test",
                ["Jwt:Audience"] = "disposable-intranet",
            });
            builder.AddJwtAuthentication();
        }
        else
        {
            builder.Services.AddAuthentication("Test")
                .AddScheme<AuthenticationSchemeOptions, TestAuthenticationHandler>("Test", _ => { });
            builder.Services.AddAuthorization(options =>
            {
                foreach (var permission in typeof(CustomerApi::Legacy.Maliev.CustomerService.Api.Controllers.CustomersController)
                    .GetMethods().SelectMany(method => method.GetCustomAttributes<RequirePermissionAttribute>()))
                {
                    options.AddPolicy(permission.Policy!, policy => policy.RequireAuthenticatedUser()
                        .RequireClaim("test-access", "allowed"));
                }
            });
        }
        var app = builder.Build();
        app.UseAuthentication();
        app.UseAuthorization();
        app.MapControllers();
        await app.StartAsync();
        return app;
    }

    private static HttpClient CreateEmployeeClient(WebApplicationFactory<BffProgram> factory) =>
        factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            BaseAddress = new Uri("https://localhost"),
            HandleCookies = true,
        });

    private static async Task SignInAsync(HttpClient client, string email)
    {
        var csrf = await CsrfAsync(client);
        using var request = new HttpRequestMessage(HttpMethod.Post, "/bff/login")
        {
            Content = JsonContent.Create(new { email, password = "disposable", returnUrl = "/Customers/Index" }),
        };
        request.Headers.Add("X-CSRF-TOKEN", csrf);
        using var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private static async Task<HttpResponseMessage> UpdateAsync(
        HttpClient client, int id, CustomerUpdateRequest profile, string? revision, bool includeCsrf)
    {
        using var request = new HttpRequestMessage(HttpMethod.Put, $"/bff/customers/{id}/versioned")
        {
            Content = JsonContent.Create(profile),
        };
        if (revision is not null) request.Headers.TryAddWithoutValidation("If-Match", revision);
        if (includeCsrf) request.Headers.Add("X-CSRF-TOKEN", await CsrfAsync(client));
        return await client.SendAsync(request);
    }

    private static async Task<string> CsrfAsync(HttpClient client)
    {
        using var session = await client.GetAsync("/bff/session");
        session.EnsureSuccessStatusCode();
        using var body = JsonDocument.Parse(await session.Content.ReadAsStringAsync());
        return body.RootElement.GetProperty("csrfToken").GetString()!;
    }

    private static async Task<CustomerResponse> ReadPersistedAsync(HttpClient client, int id) =>
        (await client.GetFromJsonAsync<CustomerResponse>($"/customers/{id}/versioned"))!;

    private static UpsertCustomerRequest InitialProfile() =>
        new("Initial", "Employee", null, null, null, "revision@maliev.test", null, null, null, null);

    private static CustomerUpdateRequest Profile(string firstName) => new()
    {
        FirstName = firstName,
        LastName = "Employee",
        Email = "revision@maliev.test",
    };

    private sealed class IntranetFactory(WebApplication customerApp, bool canRead, bool canUpdate)
        : WebApplicationFactory<BffProgram>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing");
            using var rsa = RSA.Create(2048);
            builder.UseSetting("Jwt:Issuer", "https://auth.test");
            builder.UseSetting("Jwt:Audience", "legacy-test");
            builder.UseSetting("Jwt:PublicKeyPem", rsa.ExportSubjectPublicKeyInfoPem());
            builder.UseSetting("Services:Auth", "http://auth/");
            builder.UseSetting("Services:Catalog", "http://catalog/");
            builder.UseSetting("Services:Customer", "http://customer/");
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<ILegacyAuthClient>();
                services.AddSingleton<ILegacyAuthClient>(new TestAuthClient(canRead, canUpdate));
                services.RemoveAll<IServiceAccessTokenProvider>();
                services.AddSingleton<IServiceAccessTokenProvider>(new TestServiceTokenProvider());
                services.AddHttpClient<CustomersProxy>()
                    .ConfigurePrimaryHttpMessageHandler(() => customerApp.GetTestServer().CreateHandler());
                services.AddHttpClient<CustomerUpdateProxy>()
                    .ConfigurePrimaryHttpMessageHandler(() => customerApp.GetTestServer().CreateHandler());
            });
        }
    }

    private sealed class TestAuthClient(bool canRead, bool canUpdate) : ILegacyAuthClient
    {
        public Task<EmployeeLoginResult> LoginAsync(string email, string password, CancellationToken cancellationToken) =>
            Task.FromResult(new EmployeeLoginResult(true,
                new AuthTokenResponse("test-access", "test-refresh", "Bearer", 900, DateTimeOffset.UtcNow.AddDays(1)),
                new EmployeeIdentity(email, email, email,
                [.. canRead ? ["legacy-customer.customers.read"] : Array.Empty<string>(),
                 .. canUpdate ? ["legacy-customer.customers.update"] : Array.Empty<string>()])));

        public Task<EmployeeRefreshResult?> RefreshAsync(string refreshToken, CancellationToken cancellationToken) =>
            Task.FromResult<EmployeeRefreshResult?>(null);
        public Task RevokeAsync(string refreshToken, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<CustomerIdentityResponse?> CreateCustomerIdentityAsync(int databaseId, CreateCustomerIdentityRequest request,
            string accessToken, CancellationToken cancellationToken) => Task.FromResult<CustomerIdentityResponse?>(null);
        public Task<EmployeeIdentityResponse?> CreateEmployeeIdentityAsync(int databaseId, CreateEmployeeIdentityRequest request,
            string accessToken, CancellationToken cancellationToken) => Task.FromResult<EmployeeIdentityResponse?>(null);
    }

    private sealed class TestServiceTokenProvider : IServiceAccessTokenProvider
    {
        public ValueTask<string?> GetAccessTokenAsync(CancellationToken cancellationToken) =>
            ValueTask.FromResult<string?>("integration-service-token");
        public void Invalidate(string token) { }
    }

    private sealed class TestAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var identity = Request.Headers["Test-Identity"].ToString();
            var authorization = Request.Headers.Authorization.ToString();
            if (string.IsNullOrEmpty(identity) && authorization == "Bearer integration-service-token")
                identity = "allowed";
            return Task.FromResult(string.IsNullOrEmpty(identity) ? AuthenticateResult.NoResult() :
                AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(new ClaimsIdentity(
                    [new Claim("test-access", identity), new Claim(ClaimTypes.NameIdentifier, "test-employee")],
                    Scheme.Name)), Scheme.Name)));
        }
    }

    private sealed class NoOpCustomerCache : ICustomerCache
    {
        public Task<CustomerResponse?> GetAsync(int id, CancellationToken cancellationToken) =>
            Task.FromResult<CustomerResponse?>(null);
        public Task SetAsync(CustomerResponse customer, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task RemoveAsync(int id, CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
