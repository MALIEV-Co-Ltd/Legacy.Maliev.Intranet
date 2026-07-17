extern alias Bff;

using System.Net;
using System.Net.Http.Json;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Legacy.Maliev.Intranet.Auth;
using Legacy.Maliev.Intranet.Contracts;
using Legacy.Maliev.Intranet.Customers;
using Maliev.Aspire.ServiceDefaults;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.IdentityModel.Tokens;
using BffProgram = Bff::Program;
using CustomersProxy = Bff::Legacy.Maliev.Intranet.Bff.Customers.CustomersProxy;

namespace Legacy.Maliev.Intranet.Tests;

public sealed class BffCustomersProxyContractTests
{
    [Fact]
    public async Task AuthorizedEmployee_ForwardsExactQueryAndServerOnlyBearerToken()
    {
        var downstream = new RecordingCustomerHandler(HttpStatusCode.OK, CustomerPageJson);
        await using var factory = new CustomersBffFactory(downstream, hasPermission: true);
        using var client = CreateClient(factory);
        await SignInAsync(client);

        using var response = await client.GetAsync(
            "/bff/customers?sort=CustomerEmail_Ascending&search=ada%20lovelace&index=2&size=25");
        var json = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("/customers?sort=CustomerEmail_Ascending&search=ada%20lovelace&index=2&size=25", downstream.PathAndQuery);
        Assert.Equal("Bearer signed-service-token", downstream.Authorization);
        Assert.Contains("\"pageIndex\":2", json, StringComparison.Ordinal);
        Assert.Contains("\"fullName\":\"Ada Lovelace\"", json, StringComparison.Ordinal);
        Assert.DoesNotContain("server-only-access-token", json, StringComparison.Ordinal);
    }

    [Fact]
    public async Task EmployeeWithoutExactPermission_IsForbiddenBeforeCustomerServiceCall()
    {
        var downstream = new RecordingCustomerHandler(HttpStatusCode.OK, CustomerPageJson);
        await using var factory = new CustomersBffFactory(downstream, hasPermission: false);
        using var client = CreateClient(factory);
        await SignInAsync(client);

        using var response = await client.GetAsync("/bff/customers");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Null(downstream.PathAndQuery);
    }

    [Fact]
    public async Task AnonymousRequest_IsUnauthorizedBeforeCustomerServiceCall()
    {
        var downstream = new RecordingCustomerHandler(HttpStatusCode.OK, CustomerPageJson);
        await using var factory = new CustomersBffFactory(downstream, hasPermission: true);
        using var client = CreateClient(factory);

        using var response = await client.GetAsync("/bff/customers");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Null(downstream.PathAndQuery);
    }

    [Fact]
    public async Task InvalidPaging_IsClampedAndNotFoundBecomesAnEmptyPage()
    {
        var downstream = new RecordingCustomerHandler(HttpStatusCode.NotFound, "{}");
        await using var factory = new CustomersBffFactory(downstream, hasPermission: true);
        using var client = CreateClient(factory);
        await SignInAsync(client);

        using var response = await client.GetAsync("/bff/customers?index=-5&size=999");
        var json = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("/customers?sort=CustomerCreatedDate_Descending&search=&index=1&size=250", downstream.PathAndQuery);
        Assert.Contains("\"items\":[]", json, StringComparison.Ordinal);
        Assert.Contains("\"pageIndex\":1", json, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    public async Task DownstreamAuthorizationFailure_IsPreserved(HttpStatusCode statusCode)
    {
        var downstream = new RecordingCustomerHandler(statusCode, "{}");
        await using var factory = new CustomersBffFactory(downstream, hasPermission: true);
        using var client = CreateClient(factory);
        await SignInAsync(client);

        using var response = await client.GetAsync("/bff/customers");

        Assert.Equal(statusCode, response.StatusCode);
    }

    [Fact]
    public async Task RateLimit_PreservesStatusAndBoundedRetryAfterWithoutRetry()
    {
        var downstream = new RecordingCustomerHandler(HttpStatusCode.TooManyRequests, "{}", retryAfterSeconds: 1);
        await using var factory = new CustomersBffFactory(downstream, hasPermission: true);
        using var client = CreateClient(factory);
        await SignInAsync(client);

        using var response = await client.GetAsync("/bff/customers");

        Assert.Equal(1, downstream.RequestCount);
        Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode);
        Assert.Equal(TimeSpan.FromSeconds(1), response.Headers.RetryAfter?.Delta);
    }

    [Fact]
    public async Task InvalidPayload_IsMappedToBadGatewayWithoutLeakingPayload()
    {
        var downstream = new RecordingCustomerHandler(HttpStatusCode.OK, "not-json");
        await using var factory = new CustomersBffFactory(downstream, hasPermission: true);
        using var client = CreateClient(factory);
        await SignInAsync(client);

        using var response = await client.GetAsync("/bff/customers");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
        Assert.DoesNotContain("not-json", body, StringComparison.Ordinal);
    }

    [Theory]
    [MemberData(nameof(TransportFailures))]
    public async Task TransportFailure_IsMappedToServiceUnavailable(Exception exception)
    {
        var downstream = new RecordingCustomerHandler(HttpStatusCode.OK, "{}", exception: exception);
        await using var factory = new CustomersBffFactory(downstream, hasPermission: true);
        using var client = CreateClient(factory);
        await SignInAsync(client);

        using var response = await client.GetAsync("/bff/customers");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
    }

    [Fact]
    public async Task AuthorizedCookie_WithSignedCustomerListServiceToken_PassesCustomerPermissionPipeline()
    {
        using var signingKey = RSA.Create(2048);
        await using var customer = await StartCustomerPermissionPipelineAsync(signingKey);
        var serviceToken = CreateSignedToken(signingKey, includeCustomerListPermission: true);
        await using var factory = new CustomersBffFactory(
            customer.GetTestServer().CreateHandler(),
            hasPermission: true,
            serviceToken);
        using var client = CreateClient(factory);
        await SignInAsync(client);

        using var response = await client.GetAsync("/bff/customers");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task CallerCancellation_IsNotTranslatedIntoAServiceUnavailableResponse()
    {
        await using var factory = new CustomersBffFactory(new CallerCancellationHandler(), hasPermission: true);
        using var client = CreateClient(factory);
        await SignInAsync(client);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(250));

        var exception = await Record.ExceptionAsync(() =>
            client.GetAsync("/bff/customers", cancellation.Token));

        Assert.IsAssignableFrom<OperationCanceledException>(exception);
        Assert.True(cancellation.IsCancellationRequested);
    }

    [Fact]
    public async Task Create_ValidCsrfAndPermission_CreatesProfileAndIdentityWithServerToken()
    {
        var profile = new RecordingWorkflowHandler(
            (HttpStatusCode.Created, "{\"id\":42}"));
        var identity = new RecordingWorkflowHandler(
            (HttpStatusCode.Created, "{\"databaseID\":42}"));
        await using var factory = new CustomersBffFactory(
            new RecordingCustomerHandler(HttpStatusCode.OK, CustomerPageJson),
            hasPermission: true,
            hasCreatePermission: true,
            profileDownstream: profile,
            identityDownstream: identity);
        using var client = CreateClient(factory);
        await SignInAsync(client);

        using var response = await SendCreateAsync(client, ValidCreateRequest(), includeCsrf: true);
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal("/customers", profile.Requests.Single().PathAndQuery);
        Assert.Equal("Bearer signed-service-token", profile.Requests.Single().Authorization);
        Assert.Equal("/auth/v1/customer-identities/42", identity.Requests.Single().PathAndQuery);
        Assert.Equal("Bearer signed-service-token", identity.Requests.Single().Authorization);
        Assert.Contains("\"id\":42", body, StringComparison.Ordinal);
        Assert.DoesNotContain("password", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Create_MissingCsrf_IsRejectedBeforeAnyDownstreamCall()
    {
        var profile = new RecordingWorkflowHandler((HttpStatusCode.Created, "{\"id\":42}"));
        var identity = new RecordingWorkflowHandler((HttpStatusCode.Created, "{\"databaseID\":42}"));
        await using var factory = new CustomersBffFactory(
            new RecordingCustomerHandler(HttpStatusCode.OK, CustomerPageJson),
            hasPermission: true,
            hasCreatePermission: true,
            profileDownstream: profile,
            identityDownstream: identity);
        using var client = CreateClient(factory);
        await SignInAsync(client);

        using var response = await SendCreateAsync(client, ValidCreateRequest(), includeCsrf: false);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(profile.Requests);
        Assert.Empty(identity.Requests);
    }

    [Fact]
    public async Task Create_WithoutExactPermission_IsForbiddenBeforeAnyDownstreamCall()
    {
        var profile = new RecordingWorkflowHandler((HttpStatusCode.Created, "{\"id\":42}"));
        var identity = new RecordingWorkflowHandler((HttpStatusCode.Created, "{\"databaseID\":42}"));
        await using var factory = new CustomersBffFactory(
            new RecordingCustomerHandler(HttpStatusCode.OK, CustomerPageJson),
            hasPermission: true,
            hasCreatePermission: false,
            profileDownstream: profile,
            identityDownstream: identity);
        using var client = CreateClient(factory);
        await SignInAsync(client);

        using var response = await SendCreateAsync(client, ValidCreateRequest(), includeCsrf: true);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Empty(profile.Requests);
        Assert.Empty(identity.Requests);
    }

    [Fact]
    public async Task Create_InvalidRequest_ReturnsValidationProblemBeforeAnyDownstreamCall()
    {
        var profile = new RecordingWorkflowHandler((HttpStatusCode.Created, "{\"id\":42}"));
        var identity = new RecordingWorkflowHandler((HttpStatusCode.Created, "{\"databaseID\":42}"));
        await using var factory = new CustomersBffFactory(
            new RecordingCustomerHandler(HttpStatusCode.OK, CustomerPageJson),
            hasPermission: true,
            hasCreatePermission: true,
            profileDownstream: profile,
            identityDownstream: identity);
        using var client = CreateClient(factory);
        await SignInAsync(client);
        var invalid = ValidCreateRequest();
        invalid.Email = "not-an-email";
        invalid.ConfirmPassword = "does-not-match";

        using var response = await SendCreateAsync(client, invalid, includeCsrf: true);
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("email", body, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("confirmPassword", body, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(profile.Requests);
        Assert.Empty(identity.Requests);
    }

    [Fact]
    public async Task Create_IdentityConflict_CompensatesProfileAndPreservesConflict()
    {
        var profile = new RecordingWorkflowHandler(
            (HttpStatusCode.Created, "{\"id\":42}"),
            (HttpStatusCode.NoContent, string.Empty));
        var identity = new RecordingWorkflowHandler((HttpStatusCode.Conflict, "{}"));
        await using var factory = new CustomersBffFactory(
            new RecordingCustomerHandler(HttpStatusCode.OK, CustomerPageJson),
            hasPermission: true,
            hasCreatePermission: true,
            profileDownstream: profile,
            identityDownstream: identity);
        using var client = CreateClient(factory);
        await SignInAsync(client);

        using var response = await SendCreateAsync(client, ValidCreateRequest(), includeCsrf: true);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Collection(
            profile.Requests,
            request => Assert.Equal(HttpMethod.Post, request.Method),
            request =>
            {
                Assert.Equal(HttpMethod.Delete, request.Method);
                Assert.Equal("/customers/42", request.PathAndQuery);
            });
    }

    [Fact]
    public async Task Create_ProfileRateLimit_PreservesBoundedRetryAfterWithoutRetry()
    {
        var profile = new RecordingCustomerHandler(
            HttpStatusCode.TooManyRequests,
            "{}",
            retryAfterSeconds: 2);
        var identity = new RecordingWorkflowHandler((HttpStatusCode.Created, "{\"databaseID\":42}"));
        await using var factory = new CustomersBffFactory(
            new RecordingCustomerHandler(HttpStatusCode.OK, CustomerPageJson),
            hasPermission: true,
            hasCreatePermission: true,
            profileDownstream: profile,
            identityDownstream: identity);
        using var client = CreateClient(factory);
        await SignInAsync(client);

        using var response = await SendCreateAsync(client, ValidCreateRequest(), includeCsrf: true);

        Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode);
        Assert.Equal(TimeSpan.FromSeconds(2), response.Headers.RetryAfter?.Delta);
        Assert.Equal(1, profile.RequestCount);
        Assert.Empty(identity.Requests);
    }

    [Fact]
    public async Task Create_AnonymousRequest_IsUnauthorizedBeforeAnyDownstreamCall()
    {
        var profile = new RecordingWorkflowHandler((HttpStatusCode.Created, "{\"id\":42}"));
        var identity = new RecordingWorkflowHandler((HttpStatusCode.Created, "{\"databaseID\":42}"));
        await using var factory = new CustomersBffFactory(
            new RecordingCustomerHandler(HttpStatusCode.OK, CustomerPageJson),
            hasPermission: true,
            hasCreatePermission: true,
            profileDownstream: profile,
            identityDownstream: identity);
        using var client = CreateClient(factory);

        using var response = await SendCreateAsync(client, ValidCreateRequest(), includeCsrf: true);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Empty(profile.Requests);
        Assert.Empty(identity.Requests);
    }

    [Fact]
    public async Task Create_SignedLeastPrivilegeServiceToken_PassesBothPermissionPipelines()
    {
        using var signingKey = RSA.Create(2048);
        await using var customer = await StartCustomerCreationPermissionPipelineAsync(signingKey);
        await using var auth = await StartIdentityCreationPermissionPipelineAsync(signingKey);
        var serviceToken = CreateSignedToken(
            signingKey,
            includeCustomerCreatePermission: true,
            includeCustomerDeletePermission: true,
            includeIdentityCreatePermission: true);
        await using var factory = new CustomersBffFactory(
            new RecordingCustomerHandler(HttpStatusCode.OK, CustomerPageJson),
            hasPermission: true,
            serviceToken,
            hasCreatePermission: true,
            profileDownstream: customer.GetTestServer().CreateHandler(),
            identityDownstream: auth.GetTestServer().CreateHandler());
        using var client = CreateClient(factory);
        await SignInAsync(client);

        using var response = await SendCreateAsync(client, ValidCreateRequest(), includeCsrf: true);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Fact]
    public async Task Create_ServiceTokenWithoutIdentityPermission_IsForbiddenAndCompensated()
    {
        using var signingKey = RSA.Create(2048);
        await using var customer = await StartCustomerCreationPermissionPipelineAsync(signingKey);
        await using var auth = await StartIdentityCreationPermissionPipelineAsync(signingKey);
        var serviceToken = CreateSignedToken(
            signingKey,
            includeCustomerCreatePermission: true,
            includeCustomerDeletePermission: true,
            includeIdentityCreatePermission: false);
        await using var factory = new CustomersBffFactory(
            new RecordingCustomerHandler(HttpStatusCode.OK, CustomerPageJson),
            hasPermission: true,
            serviceToken,
            hasCreatePermission: true,
            profileDownstream: customer.GetTestServer().CreateHandler(),
            identityDownstream: auth.GetTestServer().CreateHandler());
        using var client = CreateClient(factory);
        await SignInAsync(client);

        using var response = await SendCreateAsync(client, ValidCreateRequest(), includeCsrf: true);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    public static TheoryData<Exception> TransportFailures => new()
    {
        new HttpRequestException("customer unavailable"),
        new TaskCanceledException("customer timeout"),
    };

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
        var csrf = session.GetProperty("csrfToken").GetString();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/bff/login")
        {
            Content = JsonContent.Create(new
            {
                email = "employee@maliev.com",
                password = "password",
                returnUrl = "/Customers/Index",
            }),
        };
        request.Headers.Add("X-CSRF-TOKEN", csrf);
        using var response = await client.SendAsync(request);
        response.EnsureSuccessStatusCode();
    }

    private static async Task<HttpResponseMessage> SendCreateAsync(
        HttpClient client,
        CreateCustomerAccountRequest requestBody,
        bool includeCsrf)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/bff/customers")
        {
            Content = JsonContent.Create(requestBody),
        };
        if (includeCsrf)
        {
            using var sessionResponse = await client.GetAsync("/bff/session");
            var session = await sessionResponse.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
            request.Headers.Add("X-CSRF-TOKEN", session.GetProperty("csrfToken").GetString());
        }

        return await client.SendAsync(request);
    }

    private static CreateCustomerAccountRequest ValidCreateRequest() => new()
    {
        FirstName = "Ada",
        LastName = "Lovelace",
        Email = "ada@example.com",
        Password = "correct horse battery staple",
        ConfirmPassword = "correct horse battery staple",
        Telephone = "+66 2 123 4567",
        Mobile = "+66 81 234 5678",
        Fax = "+66 2 765 4321",
        DateOfBirth = new DateTime(1815, 12, 10),
    };

    private sealed class CustomersBffFactory(
        HttpMessageHandler downstream,
        bool hasPermission,
        string serviceToken = "signed-service-token",
        bool hasCreatePermission = false,
        HttpMessageHandler? profileDownstream = null,
        HttpMessageHandler? identityDownstream = null)
        : WebApplicationFactory<BffProgram>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing");
            TestJwtConfiguration.Configure(builder);
            builder.UseSetting("Services:Auth", "http://auth/");
            builder.UseSetting("Services:Catalog", "http://catalog/");
            builder.UseSetting("Services:Customer", "http://customer/");
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<ILegacyAuthClient>();
                services.AddSingleton<ILegacyAuthClient>(new CustomersAuthClient(hasPermission, hasCreatePermission));
                services.RemoveAll<IServiceAccessTokenProvider>();
                services.AddSingleton<IServiceAccessTokenProvider>(new CustomersServiceTokenProvider(serviceToken));
                services.AddHttpClient<CustomersProxy>()
                    .ConfigurePrimaryHttpMessageHandler(() => downstream);
                services.RemoveAll<ICustomerProfileCreationClient>();
                services.AddHttpClient<ICustomerProfileCreationClient, CustomerProfileCreationClient>()
                    .ConfigurePrimaryHttpMessageHandler(() => profileDownstream ?? downstream);
                services.RemoveAll<ICustomerIdentityCreationClient>();
                services.AddHttpClient<ICustomerIdentityCreationClient, CustomerIdentityCreationClient>()
                    .ConfigurePrimaryHttpMessageHandler(() => identityDownstream ?? downstream);
            });
        }
    }

    private sealed class CustomersServiceTokenProvider(string serviceToken) : IServiceAccessTokenProvider
    {
        public ValueTask<string?> GetAccessTokenAsync(CancellationToken cancellationToken) =>
            ValueTask.FromResult<string?>(serviceToken);

        public void Invalidate(string token)
        {
        }
    }

    private sealed class CustomersAuthClient(bool hasPermission, bool hasCreatePermission) : ILegacyAuthClient
    {
        public Task<EmployeeLoginResult> LoginAsync(string email, string password, CancellationToken cancellationToken) =>
            Task.FromResult(new EmployeeLoginResult(
                true,
                new AuthTokenResponse("server-only-access-token", "server-only-refresh-token", "Bearer", 900, DateTimeOffset.UtcNow.AddDays(1)),
                new EmployeeIdentity(
                    "employee-id",
                    email,
                    email,
                    [
                        .. hasPermission ? ["legacy-customer.customers.list"] : Array.Empty<string>(),
                        .. hasCreatePermission ? ["legacy-customer.customers.create"] : Array.Empty<string>(),
                    ])));

        public Task<EmployeeRefreshResult?> RefreshAsync(string refreshToken, CancellationToken cancellationToken) => Task.FromResult<EmployeeRefreshResult?>(null);
        public Task RevokeAsync(string refreshToken, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<CustomerIdentityResponse?> CreateCustomerIdentityAsync(int databaseId, CreateCustomerIdentityRequest request, string accessToken, CancellationToken cancellationToken) => Task.FromResult<CustomerIdentityResponse?>(null);
        public Task<EmployeeIdentityResponse?> CreateEmployeeIdentityAsync(int databaseId, CreateEmployeeIdentityRequest request, string accessToken, CancellationToken cancellationToken) => Task.FromResult<EmployeeIdentityResponse?>(null);
    }

    private sealed class RecordingCustomerHandler(
        HttpStatusCode statusCode,
        string body,
        int? retryAfterSeconds = null,
        Exception? exception = null) : HttpMessageHandler
    {
        public string? PathAndQuery { get; private set; }
        public string? Authorization { get; private set; }
        public int RequestCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestCount++;
            PathAndQuery = request.RequestUri?.PathAndQuery;
            Authorization = request.Headers.Authorization?.ToString();
            if (exception is not null)
            {
                throw exception;
            }

            var response = new HttpResponseMessage(statusCode)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json"),
            };
            if (retryAfterSeconds is not null)
            {
                response.Headers.RetryAfter = new(TimeSpan.FromSeconds(retryAfterSeconds.Value));
            }

            return Task.FromResult(response);
        }
    }

    private sealed class CallerCancellationHandler : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            throw new InvalidOperationException("The caller cancellation should stop the request.");
        }
    }

    private sealed class RecordingWorkflowHandler(params (HttpStatusCode StatusCode, string Body)[] responses)
        : HttpMessageHandler
    {
        private readonly Queue<(HttpStatusCode StatusCode, string Body)> _responses = new(responses);

        public List<RecordedRequest> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Requests.Add(new RecordedRequest(
                request.Method,
                request.RequestUri?.PathAndQuery,
                request.Headers.Authorization?.ToString(),
                request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken)));
            var response = _responses.Dequeue();
            return new HttpResponseMessage(response.StatusCode)
            {
                Content = new StringContent(response.Body, Encoding.UTF8, "application/json"),
            };
        }
    }

    private sealed record RecordedRequest(HttpMethod Method, string? PathAndQuery, string? Authorization, string? Body);

    private const string CustomerPageJson =
        """{"Items":[{"Id":42,"FirstName":"Ada","LastName":"Lovelace","FullName":"Ada Lovelace","Email":"ada@example.com","Company":{"Id":7,"Name":"Analytical Engines Ltd"}}],"PageIndex":2,"TotalPages":4,"TotalRecords":75,"HasNextPage":true,"HasPreviousPage":true}""";

    private static async Task<WebApplication> StartCustomerPermissionPipelineAsync(RSA signingKey)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = "Production",
        });
        builder.WebHost.UseTestServer();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Jwt:Issuer"] = "https://auth.test",
            ["Jwt:Audience"] = "legacy-test",
            ["Jwt:PublicKey"] = Convert.ToBase64String(Encoding.UTF8.GetBytes(signingKey.ExportSubjectPublicKeyInfoPem())),
        });
        builder.AddJwtAuthentication();
        var app = builder.Build();
        app.UseAuthentication();
        app.UseAuthorization();
        app.MapGet("/customers", () => Results.Text(CustomerPageJson, "application/json"))
            .RequireAuthorization($"Permission:{LegacyEmployeePermissions.CustomersList}");
        await app.StartAsync();
        return app;
    }

    private static string CreateSignedToken(
        RSA signingKey,
        bool includeCustomerListPermission = false,
        bool includeCustomerCreatePermission = false,
        bool includeCustomerDeletePermission = false,
        bool includeIdentityCreatePermission = false)
    {
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, "legacy-intranet"),
            new("identity_kind", "service"),
        };
        if (includeCustomerListPermission)
        {
            claims.Add(new Claim("permissions", LegacyEmployeePermissions.CustomersList));
        }
        if (includeCustomerCreatePermission)
        {
            claims.Add(new Claim("permissions", LegacyEmployeePermissions.CustomersCreate));
        }
        if (includeCustomerDeletePermission)
        {
            claims.Add(new Claim("permissions", "legacy-customer.customers.delete"));
        }
        if (includeIdentityCreatePermission)
        {
            claims.Add(new Claim("permissions", "legacy-auth.customer-identities.create"));
        }

        var key = new RsaSecurityKey(signingKey) { KeyId = "customer-contract-key" };
        var token = new JwtSecurityToken(
            "https://auth.test",
            "legacy-test",
            claims,
            DateTime.UtcNow.AddMinutes(-1),
            DateTime.UtcNow.AddMinutes(10),
            new SigningCredentials(key, SecurityAlgorithms.RsaSha256));
        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    private static async Task<WebApplication> StartCustomerCreationPermissionPipelineAsync(RSA signingKey)
    {
        var app = BuildPermissionPipeline(signingKey);
        app.MapPost("/customers", () => Results.Text("{\"id\":42}", "application/json", statusCode: StatusCodes.Status201Created))
            .RequireAuthorization($"Permission:{LegacyEmployeePermissions.CustomersCreate}");
        app.MapDelete("/customers/{id:int}", () => Results.NoContent())
            .RequireAuthorization("Permission:legacy-customer.customers.delete");
        await app.StartAsync();
        return app;
    }

    private static async Task<WebApplication> StartIdentityCreationPermissionPipelineAsync(RSA signingKey)
    {
        var app = BuildPermissionPipeline(signingKey);
        app.MapPost("/auth/v1/customer-identities/{id:int}", (int id) =>
                Results.Text($"{{\"databaseID\":{id}}}", "application/json", statusCode: StatusCodes.Status201Created))
            .RequireAuthorization("Permission:legacy-auth.customer-identities.create");
        await app.StartAsync();
        return app;
    }

    private static WebApplication BuildPermissionPipeline(RSA signingKey)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = "Production",
        });
        builder.WebHost.UseTestServer();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Jwt:Issuer"] = "https://auth.test",
            ["Jwt:Audience"] = "legacy-test",
            ["Jwt:PublicKey"] = Convert.ToBase64String(Encoding.UTF8.GetBytes(signingKey.ExportSubjectPublicKeyInfoPem())),
        });
        builder.AddJwtAuthentication();
        var app = builder.Build();
        app.UseAuthentication();
        app.UseAuthorization();
        return app;
    }
}
