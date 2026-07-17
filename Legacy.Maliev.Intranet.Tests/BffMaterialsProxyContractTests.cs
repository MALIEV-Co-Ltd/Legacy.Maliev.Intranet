extern alias Bff;

using System.Net;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Net.Http.Json;
using System.IdentityModel.Tokens.Jwt;
using Legacy.Maliev.Intranet.Auth;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using BffProgram = Bff::Program;
using CatalogMaterialsProxy = Bff::Legacy.Maliev.Intranet.Bff.Catalog.CatalogMaterialsProxy;

namespace Legacy.Maliev.Intranet.Tests;

public sealed class BffMaterialsProxyContractTests
{
    [Theory]
    [InlineData("/bff/catalog/material-groups", "/materials/MaterialGroups", "Steel")]
    [InlineData("/bff/catalog/currencies", "/Currencies", "THB")]
    public async Task AuthorizedEmployee_LookupsStayServerAuthenticated(
        string bffPath,
        string catalogPath,
        string expectedValue)
    {
        var downstream = new RecordingCatalogHandler(
            HttpStatusCode.OK,
            expectedValue == "Steel"
                ? "[{\"Id\":7,\"Name\":\"Steel\",\"Description\":\"Metal\"}]"
                : "[{\"Id\":1,\"ShortName\":\"THB\",\"LongName\":\"Thai Baht\"}]");
        await using var factory = new MaterialsBffFactory(downstream, hasPermission: true, compatibilityGrant: false);
        using var client = CreateClient(factory);
        await SignInAsync(client);

        using var response = await client.GetAsync(bffPath);
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(catalogPath, downstream.PathAndQuery);
        Assert.Equal("Bearer signed-service-token", downstream.Authorization);
        Assert.Contains(expectedValue, body, StringComparison.Ordinal);
        Assert.DoesNotContain("Description", body, StringComparison.Ordinal);
        Assert.DoesNotContain("LongName", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CreateWithoutCsrf_IsRejectedBeforeCatalogCall()
    {
        var downstream = new RecordingCatalogHandler(HttpStatusCode.OK, "{\"Id\":42}");
        await using var factory = new MaterialsBffFactory(
            downstream,
            hasPermission: true,
            compatibilityGrant: false,
            hasCreatePermission: true);
        using var client = CreateClient(factory);
        await SignInAsync(client);

        using var response = await client.PostAsJsonAsync("/bff/catalog/materials", ValidCreateRequest);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(0, downstream.RequestCount);
    }

    [Fact]
    public async Task ReadOnlyEmployee_CreateIsForbiddenBeforeCatalogCall()
    {
        var downstream = new RecordingCatalogHandler(HttpStatusCode.OK, "{\"Id\":42}");
        await using var factory = new MaterialsBffFactory(
            downstream,
            hasPermission: true,
            compatibilityGrant: false,
            hasCreatePermission: false);
        using var client = CreateClient(factory);
        await SignInAsync(client);
        var csrf = await GetCsrfAsync(client);
        using var request = new HttpRequestMessage(HttpMethod.Post, "/bff/catalog/materials")
        {
            Content = JsonContent.Create(ValidCreateRequest),
        };
        request.Headers.Add("X-CSRF-TOKEN", csrf);

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(0, downstream.RequestCount);
    }

    [Fact]
    public async Task InvalidCreate_IsRejectedByServerValidationBeforeCatalogCall()
    {
        var downstream = new RecordingCatalogHandler(HttpStatusCode.OK, "{\"Id\":42}");
        await using var factory = new MaterialsBffFactory(
            downstream,
            hasPermission: true,
            compatibilityGrant: false,
            hasCreatePermission: true);
        using var client = CreateClient(factory);
        await SignInAsync(client);
        var csrf = await GetCsrfAsync(client);
        using var request = new HttpRequestMessage(HttpMethod.Post, "/bff/catalog/materials")
        {
            Content = JsonContent.Create(new Legacy.Maliev.Intranet.Contracts.CatalogMaterialUpsertRequest
            {
                Name = string.Empty,
                MaterialGroupId = 0,
            }),
        };
        request.Headers.Add("X-CSRF-TOKEN", csrf);

        using var response = await client.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(0, downstream.RequestCount);
        Assert.Contains("materialGroupId", body, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("name", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ValidCreate_ForwardsCompleteJsonWithServerTokenAndReturnsCreatedId()
    {
        var downstream = new RecordingCatalogHandler(HttpStatusCode.OK, "{\"Id\":42}");
        await using var factory = new MaterialsBffFactory(
            downstream,
            hasPermission: true,
            compatibilityGrant: false,
            hasCreatePermission: true);
        using var client = CreateClient(factory);
        await SignInAsync(client);
        var csrf = await GetCsrfAsync(client);
        using var request = new HttpRequestMessage(HttpMethod.Post, "/bff/catalog/materials")
        {
            Content = JsonContent.Create(ValidCreateRequest),
        };
        request.Headers.Add("X-CSRF-TOKEN", csrf);

        using var response = await client.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(HttpMethod.Post, downstream.Method);
        Assert.Equal("/Materials", downstream.PathAndQuery);
        Assert.Equal("Bearer signed-service-token", downstream.Authorization);
        Assert.Contains("\"materialGroupId\":7", downstream.Body, StringComparison.Ordinal);
        Assert.Contains("\"name\":\"4140\"", downstream.Body, StringComparison.Ordinal);
        Assert.Contains("\"thermalConductivityWattPerMeterKelvin\":42", downstream.Body, StringComparison.Ordinal);
        Assert.Contains("\"id\":42", body, StringComparison.Ordinal);
        Assert.DoesNotContain("server-only", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task InvalidCreateResponse_IsBadGatewayWithoutLeakingCatalogPayload()
    {
        var downstream = new RecordingCatalogHandler(HttpStatusCode.OK, "not-json");
        await using var factory = new MaterialsBffFactory(
            downstream,
            hasPermission: true,
            compatibilityGrant: false,
            hasCreatePermission: true);
        using var client = CreateClient(factory);
        await SignInAsync(client);
        var csrf = await GetCsrfAsync(client);
        using var request = new HttpRequestMessage(HttpMethod.Post, "/bff/catalog/materials")
        {
            Content = JsonContent.Create(ValidCreateRequest),
        };
        request.Headers.Add("X-CSRF-TOKEN", csrf);

        using var response = await client.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
        Assert.DoesNotContain("not-json", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CreateWithSignedCatalogServiceToken_PassesCatalogPermissionPipeline()
    {
        using var signingKey = RSA.Create(2048);
        await using var catalog = await StartCatalogPermissionPipelineAsync(signingKey);
        var serviceToken = CreateSignedToken(
            signingKey,
            "service",
            includeCatalogPermission: true,
            includeCatalogCreatePermission: true);
        await using var factory = new MaterialsBffFactory(
            catalog.GetTestServer().CreateHandler(),
            hasPermission: true,
            compatibilityGrant: false,
            serviceToken,
            hasCreatePermission: true);
        using var client = CreateClient(factory);
        await SignInAsync(client);
        var csrf = await GetCsrfAsync(client);
        using var request = new HttpRequestMessage(HttpMethod.Post, "/bff/catalog/materials")
        {
            Content = JsonContent.Create(ValidCreateRequest),
        };
        request.Headers.Add("X-CSRF-TOKEN", csrf);

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task AuthorizedEmployee_DetailForwardsExactIdAndServerOnlyBearerToken()
    {
        var downstream = new RecordingCatalogHandler(HttpStatusCode.OK, MaterialDetailJson);
        await using var factory = new MaterialsBffFactory(downstream, hasPermission: true, compatibilityGrant: false);
        using var client = CreateClient(factory);
        await SignInAsync(client);

        using var response = await client.GetAsync("/bff/catalog/materials/42");
        var json = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("/Materials/42", downstream.PathAndQuery);
        Assert.Equal("Bearer signed-service-token", downstream.Authorization);
        Assert.Contains("\"id\":42", json, StringComparison.Ordinal);
        Assert.Contains("\"materialGroup\":{\"id\":7,\"name\":\"Steel\"}", json, StringComparison.Ordinal);
        Assert.DoesNotContain("server-only-access-token", json, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DetailEmployeeWithoutExactPermission_IsForbiddenBeforeCatalogCall()
    {
        var downstream = new RecordingCatalogHandler(HttpStatusCode.OK, MaterialDetailJson);
        await using var factory = new MaterialsBffFactory(downstream, hasPermission: false, compatibilityGrant: false);
        using var client = CreateClient(factory);
        await SignInAsync(client);

        using var response = await client.GetAsync("/bff/catalog/materials/42");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Null(downstream.PathAndQuery);
    }

    [Fact]
    public async Task AnonymousDetailRequest_IsUnauthorizedBeforeCatalogCall()
    {
        var downstream = new RecordingCatalogHandler(HttpStatusCode.OK, MaterialDetailJson);
        await using var factory = new MaterialsBffFactory(downstream, hasPermission: true);
        using var client = CreateClient(factory);

        using var response = await client.GetAsync("/bff/catalog/materials/42");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Null(downstream.PathAndQuery);
    }

    [Fact]
    public async Task DetailNotFound_IsPreserved()
    {
        var downstream = new RecordingCatalogHandler(HttpStatusCode.NotFound, "{}");
        await using var factory = new MaterialsBffFactory(downstream, hasPermission: true);
        using var client = CreateClient(factory);
        await SignInAsync(client);

        using var response = await client.GetAsync("/bff/catalog/materials/404");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("/Materials/404", downstream.PathAndQuery);
    }

    [Fact]
    public async Task InvalidDetailId_IsNotFoundWithoutCallingCatalog()
    {
        var downstream = new RecordingCatalogHandler(HttpStatusCode.OK, MaterialDetailJson);
        await using var factory = new MaterialsBffFactory(downstream, hasPermission: true);
        using var client = CreateClient(factory);
        await SignInAsync(client);

        using var response = await client.GetAsync("/bff/catalog/materials/0");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(0, downstream.RequestCount);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    public async Task DetailDownstreamAuthFailure_IsPreserved(HttpStatusCode statusCode)
    {
        var downstream = new RecordingCatalogHandler(statusCode, "{}");
        await using var factory = new MaterialsBffFactory(downstream, hasPermission: true);
        using var client = CreateClient(factory);
        await SignInAsync(client);

        using var response = await client.GetAsync("/bff/catalog/materials/42");

        Assert.Equal(statusCode, response.StatusCode);
        Assert.Equal(1, downstream.RequestCount);
    }

    [Fact]
    public async Task DetailRateLimit_PreservesStatusAndBoundedRetryAfter()
    {
        var downstream = new RecordingCatalogHandler(HttpStatusCode.TooManyRequests, "{}", retryAfterSeconds: 1);
        await using var factory = new MaterialsBffFactory(downstream, hasPermission: true);
        using var client = CreateClient(factory);
        await SignInAsync(client);

        using var response = await client.GetAsync("/bff/catalog/materials/42");

        Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode);
        Assert.Equal(TimeSpan.FromSeconds(1), response.Headers.RetryAfter?.Delta);
    }

    [Fact]
    public async Task InvalidDetailPayload_IsMappedToBadGatewayWithoutLeakingThePayload()
    {
        var downstream = new RecordingCatalogHandler(HttpStatusCode.OK, "not-json");
        await using var factory = new MaterialsBffFactory(downstream, hasPermission: true);
        using var client = CreateClient(factory);
        await SignInAsync(client);

        using var response = await client.GetAsync("/bff/catalog/materials/42");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
        Assert.DoesNotContain("not-json", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DetailTransportFailure_IsMappedToServiceUnavailable()
    {
        var downstream = new RecordingCatalogHandler(
            HttpStatusCode.OK,
            "{}",
            exception: new HttpRequestException("catalog unavailable"));
        await using var factory = new MaterialsBffFactory(downstream, hasPermission: true, compatibilityGrant: false);
        using var client = CreateClient(factory);
        await SignInAsync(client);

        using var response = await client.GetAsync("/bff/catalog/materials/42");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
    }

    [Fact]
    public async Task DetailWithSignedCatalogServiceToken_PassesCatalogPermissionPipeline()
    {
        using var signingKey = RSA.Create(2048);
        await using var catalog = await StartCatalogPermissionPipelineAsync(signingKey);
        var serviceToken = CreateSignedToken(signingKey, "service", includeCatalogPermission: true);
        await using var factory = new MaterialsBffFactory(
            catalog.GetTestServer().CreateHandler(),
            hasPermission: true,
            compatibilityGrant: false,
            serviceToken);
        using var client = CreateClient(factory);
        await SignInAsync(client);

        using var response = await client.GetAsync("/bff/catalog/materials/42");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("\"id\":42", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AuthorizedEmployee_ForwardsExactQueryAndServerOnlyBearerToken()
    {
        var downstream = new RecordingCatalogHandler(HttpStatusCode.OK, MaterialPageJson);
        await using var factory = new MaterialsBffFactory(downstream, hasPermission: true, compatibilityGrant: false);
        using var client = CreateClient(factory);
        await SignInAsync(client);

        using var response = await client.GetAsync(
            "/bff/catalog/materials?sort=MaterialName_Ascending&search=tool%20steel&index=2&size=25");
        var json = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("/Materials?sort=MaterialName_Ascending&search=tool%20steel&index=2&size=25", downstream.PathAndQuery);
        Assert.Equal("Bearer signed-service-token", downstream.Authorization);
        Assert.Contains("\"pageIndex\":2", json, StringComparison.Ordinal);
        Assert.Contains("\"name\":\"4140\"", json, StringComparison.Ordinal);
        Assert.DoesNotContain("server-only-access-token", json, StringComparison.Ordinal);
    }

    [Fact]
    public async Task EmployeeWithoutExactPermission_IsForbiddenBeforeCatalogCall()
    {
        var downstream = new RecordingCatalogHandler(HttpStatusCode.OK, MaterialPageJson);
        await using var factory = new MaterialsBffFactory(downstream, hasPermission: false, compatibilityGrant: false);
        using var client = CreateClient(factory);
        await SignInAsync(client);

        using var response = await client.GetAsync("/bff/catalog/materials");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Null(downstream.PathAndQuery);
    }

    [Fact]
    public async Task LegacyEmployeeWithoutTokenPermission_CompatibilityGrantAllowsCatalogRead()
    {
        var downstream = new RecordingCatalogHandler(HttpStatusCode.OK, MaterialPageJson);
        await using var factory = new MaterialsBffFactory(downstream, hasPermission: false, compatibilityGrant: true);
        using var client = CreateClient(factory);
        await SignInAsync(client);

        using var response = await client.GetAsync("/bff/catalog/materials");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(downstream.PathAndQuery);
    }

    [Fact]
    public async Task AnonymousRequest_IsUnauthorizedBeforeCatalogCall()
    {
        var downstream = new RecordingCatalogHandler(HttpStatusCode.OK, MaterialPageJson);
        await using var factory = new MaterialsBffFactory(downstream, hasPermission: true);
        using var client = CreateClient(factory);

        using var response = await client.GetAsync("/bff/catalog/materials");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Null(downstream.PathAndQuery);
    }

    [Fact]
    public async Task InvalidPaging_IsClampedAndLegacyNotFoundBecomesAnEmptyPage()
    {
        var downstream = new RecordingCatalogHandler(HttpStatusCode.NotFound, "{}");
        await using var factory = new MaterialsBffFactory(downstream, hasPermission: true);
        using var client = CreateClient(factory);
        await SignInAsync(client);

        using var response = await client.GetAsync("/bff/catalog/materials?index=-5&size=999");
        var json = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("/Materials?sort=MaterialId_Descending&search=&index=1&size=250", downstream.PathAndQuery);
        Assert.Contains("\"items\":[]", json, StringComparison.Ordinal);
        Assert.Contains("\"pageIndex\":1", json, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CatalogRateLimit_PreservesStatusAndBoundedRetryAfter()
    {
        var downstream = new RecordingCatalogHandler(HttpStatusCode.TooManyRequests, "{}", retryAfterSeconds: 1);
        await using var factory = new MaterialsBffFactory(downstream, hasPermission: true);
        using var client = CreateClient(factory);
        await SignInAsync(client);

        using var response = await client.GetAsync("/bff/catalog/materials");

        Assert.Equal(1, downstream.RequestCount);
        Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode);
        Assert.Equal(TimeSpan.FromSeconds(1), response.Headers.RetryAfter?.Delta);
    }

    [Fact]
    public async Task InvalidCatalogPayload_IsMappedToBadGatewayWithoutLeakingThePayload()
    {
        var downstream = new RecordingCatalogHandler(HttpStatusCode.OK, "not-json");
        await using var factory = new MaterialsBffFactory(downstream, hasPermission: true);
        using var client = CreateClient(factory);
        await SignInAsync(client);

        using var response = await client.GetAsync("/bff/catalog/materials");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
        Assert.DoesNotContain("not-json", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CatalogTransportFailure_IsMappedToServiceUnavailable()
    {
        var downstream = new RecordingCatalogHandler(
            HttpStatusCode.OK,
            "{}",
            exception: new HttpRequestException("catalog unavailable"));
        await using var factory = new MaterialsBffFactory(downstream, hasPermission: true, compatibilityGrant: false);
        using var client = CreateClient(factory);
        await SignInAsync(client);

        using var response = await client.GetAsync("/bff/catalog/materials");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
    }

    [Fact]
    public async Task CatalogNonCallerTimeout_IsMappedToServiceUnavailable()
    {
        var downstream = new RecordingCatalogHandler(
            HttpStatusCode.OK,
            "{}",
            exception: new TaskCanceledException("catalog timeout"));
        await using var factory = new MaterialsBffFactory(downstream, hasPermission: true, compatibilityGrant: false);
        using var client = CreateClient(factory);
        await SignInAsync(client);

        using var response = await client.GetAsync("/bff/catalog/materials");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
    }

    [Fact]
    public async Task CallerCancellation_IsNotTranslatedIntoAServiceUnavailableResponse()
    {
        await using var factory = new MaterialsBffFactory(
            new CallerCancellationHandler(),
            hasPermission: true,
            compatibilityGrant: false);
        using var client = CreateClient(factory);
        await SignInAsync(client);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(250));

        var exception = await Record.ExceptionAsync(() =>
            client.GetAsync("/bff/catalog/materials", cancellation.Token));

        Assert.IsAssignableFrom<OperationCanceledException>(exception);
        Assert.True(cancellation.IsCancellationRequested);
    }

    [Fact]
    public async Task AuthorizedCookie_WithSignedCatalogServiceToken_PassesCatalogPermissionPipeline()
    {
        using var signingKey = RSA.Create(2048);
        await using var catalog = await StartCatalogPermissionPipelineAsync(signingKey);
        var serviceToken = CreateSignedToken(signingKey, "service", includeCatalogPermission: true);
        await using var factory = new MaterialsBffFactory(
            catalog.GetTestServer().CreateHandler(),
            hasPermission: true,
            compatibilityGrant: false,
            serviceToken);
        using var client = CreateClient(factory);
        await SignInAsync(client);

        using var response = await client.GetAsync("/bff/catalog/materials");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task AuthorizedCookie_WithEmployeeTokenWithoutCatalogPermission_IsRejectedByCatalogPipeline()
    {
        using var signingKey = RSA.Create(2048);
        await using var catalog = await StartCatalogPermissionPipelineAsync(signingKey);
        var employeeToken = CreateSignedToken(signingKey, "employee", includeCatalogPermission: false);
        await using var factory = new MaterialsBffFactory(
            catalog.GetTestServer().CreateHandler(),
            hasPermission: true,
            compatibilityGrant: false,
            employeeToken);
        using var client = CreateClient(factory);
        await SignInAsync(client);

        using var response = await client.GetAsync("/bff/catalog/materials");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
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
        var csrf = session.GetProperty("csrfToken").GetString();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/bff/login")
        {
            Content = JsonContent.Create(new { email = "employee@maliev.com", password = "password", returnUrl = "/Materials/Index" }),
        };
        request.Headers.Add("X-CSRF-TOKEN", csrf);
        using var response = await client.SendAsync(request);
        response.EnsureSuccessStatusCode();
    }

    private static async Task<string> GetCsrfAsync(HttpClient client)
    {
        using var response = await client.GetAsync("/bff/session");
        response.EnsureSuccessStatusCode();
        var session = await response.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        return session.GetProperty("csrfToken").GetString()
            ?? throw new InvalidOperationException("The BFF did not issue an antiforgery request token.");
    }

    private sealed class MaterialsBffFactory(
        HttpMessageHandler downstream,
        bool hasPermission,
        bool compatibilityGrant = false,
        string serviceToken = "signed-service-token",
        bool hasCreatePermission = false)
        : WebApplicationFactory<BffProgram>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing");
            TestJwtConfiguration.Configure(builder);
            builder.UseSetting("Services:Auth", "http://auth/");
            builder.UseSetting("Services:Catalog", "http://catalog/");
            builder.UseSetting(
                "LegacyEmployeeCompatibility:GrantCatalogMaterialsRead",
                compatibilityGrant.ToString());
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<ILegacyAuthClient>();
                services.AddSingleton<ILegacyAuthClient>(new MaterialsAuthClient(hasPermission, hasCreatePermission));
                services.RemoveAll<IServiceAccessTokenProvider>();
                services.AddSingleton<IServiceAccessTokenProvider>(new MaterialsServiceTokenProvider(serviceToken));
                services.AddHttpClient<CatalogMaterialsProxy>()
                    .ConfigurePrimaryHttpMessageHandler(() => downstream);
            });
        }
    }

    private sealed class MaterialsServiceTokenProvider(string serviceToken) : IServiceAccessTokenProvider
    {
        public ValueTask<string?> GetAccessTokenAsync(CancellationToken cancellationToken) =>
            ValueTask.FromResult<string?>(serviceToken);

        public void Invalidate(string token)
        {
        }
    }

    private sealed class MaterialsAuthClient(bool hasPermission, bool hasCreatePermission) : ILegacyAuthClient
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
                        .. hasPermission ? ["legacy-catalog.materials.read"] : Array.Empty<string>(),
                        .. hasCreatePermission ? ["legacy-catalog.materials.create"] : Array.Empty<string>(),
                    ])));
        public Task<EmployeeRefreshResult?> RefreshAsync(string refreshToken, CancellationToken cancellationToken) => Task.FromResult<EmployeeRefreshResult?>(null);
        public Task RevokeAsync(string refreshToken, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<CustomerIdentityResponse?> CreateCustomerIdentityAsync(int databaseId, CreateCustomerIdentityRequest request, string accessToken, CancellationToken cancellationToken) => Task.FromResult<CustomerIdentityResponse?>(null);
        public Task<EmployeeIdentityResponse?> CreateEmployeeIdentityAsync(int databaseId, CreateEmployeeIdentityRequest request, string accessToken, CancellationToken cancellationToken) => Task.FromResult<EmployeeIdentityResponse?>(null);
    }

    private sealed class RecordingCatalogHandler(
        HttpStatusCode statusCode,
        string body,
        int? retryAfterSeconds = null,
        Exception? exception = null) : HttpMessageHandler
    {
        public string? PathAndQuery { get; private set; }
        public string? Authorization { get; private set; }
        public HttpMethod? Method { get; private set; }
        public string? Body { get; private set; }
        public int RequestCount { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestCount++;
            PathAndQuery = request.RequestUri?.PathAndQuery;
            Authorization = request.Headers.Authorization?.ToString();
            Method = request.Method;
            Body = request.Content is null
                ? null
                : await request.Content.ReadAsStringAsync(cancellationToken);
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
                response.Headers.RetryAfter = new(retryAfterSeconds.Value switch
                {
                    var seconds => TimeSpan.FromSeconds(seconds),
                });
            }

            return response;
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

    private const string MaterialPageJson =
        """{"Items":[{"Id":42,"MaterialGroupId":1,"Machinable":true,"Printable":false,"Name":"4140","MaterialNumber":"4140","DensityKilogramPerCubicMeter":7850,"MaterialGroup":{"Id":1,"Name":"Steel"}}],"PageIndex":2,"TotalPages":4,"TotalRecords":75,"HasNextPage":true,"HasPreviousPage":true}""";

    private const string MaterialDetailJson =
        """{"Id":42,"MaterialGroupId":7,"Machinable":true,"Printable":false,"Name":"4140","MaterialNumber":"AISI 4140","DensityKilogramPerCubicMeter":7850,"MaterialGroup":{"Id":7,"Name":"Steel"}}""";

    private static readonly Legacy.Maliev.Intranet.Contracts.CatalogMaterialUpsertRequest ValidCreateRequest = new()
    {
        MaterialGroupId = 7,
        Machinable = true,
        Printable = false,
        Name = "4140",
        MaterialNumber = "AISI 4140",
        DensityKilogramPerCubicMeter = 7850m,
        ThermalConductivityWattPerMeterKelvin = 42m,
        CurrencyId = 1,
        PricePerKilogram = 100m,
    };

    private static async Task<WebApplication> StartCatalogPermissionPipelineAsync(RSA signingKey)
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
        app.MapGet("/Materials", () => Results.Text(MaterialPageJson, "application/json"))
            .RequireAuthorization($"Permission:{LegacyEmployeePermissions.CatalogMaterialsRead}");
        app.MapGet("/Materials/{id:int}", (int id) => Results.Text(MaterialDetailJson, "application/json"))
            .RequireAuthorization($"Permission:{LegacyEmployeePermissions.CatalogMaterialsRead}");
        app.MapPost("/Materials", () => Results.Text("{\"Id\":42}", "application/json"))
            .RequireAuthorization("Permission:legacy-catalog.materials.create");
        await app.StartAsync();
        return app;
    }

    private static string CreateSignedToken(
        RSA signingKey,
        string identityKind,
        bool includeCatalogPermission,
        bool includeCatalogCreatePermission = false)
    {
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, "subject-id"),
            new("identity_kind", identityKind),
        };
        if (includeCatalogPermission)
        {
            claims.Add(new Claim("permissions", LegacyEmployeePermissions.CatalogMaterialsRead));
        }
        if (includeCatalogCreatePermission)
        {
            claims.Add(new Claim("permissions", "legacy-catalog.materials.create"));
        }

        var key = new RsaSecurityKey(signingKey) { KeyId = "catalog-contract-key" };
        var token = new JwtSecurityToken(
            "https://auth.test",
            "legacy-test",
            claims,
            DateTime.UtcNow.AddMinutes(-1),
            DateTime.UtcNow.AddMinutes(10),
            new SigningCredentials(key, SecurityAlgorithms.RsaSha256));
        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
