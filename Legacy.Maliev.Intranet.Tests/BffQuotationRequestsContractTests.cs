extern alias Bff;

using System.Net;
using System.Net.Http.Json;
using System.Text;
using Legacy.Maliev.Intranet.Auth;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using BffProgram = Bff::Program;
using QuotationRequestsProxy = Bff::Legacy.Maliev.Intranet.Bff.Quotations.QuotationRequestsProxy;

namespace Legacy.Maliev.Intranet.Tests;

public sealed class BffQuotationRequestsContractTests
{
    [Fact]
    public async Task AuthorizedEmployee_ReceivesOwnedPageAndBoundedDownstreamQuery()
    {
        var downstream = new RecordingHandler(PageJson);
        await using var factory = new Factory(downstream, canRead: true);
        using var client = CreateClient(factory);
        await SignInAsync(client);

        using var response = await client.GetAsync("/bff/quotation-requests?sort=RequestModifiedDate_Descending&search=Thai%20fixture&index=-4&size=999");
        var body = await response.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("/quotationrequests?sort=RequestModifiedDate_Descending&search=Thai%20fixture&index=1&size=250", downstream.PathAndQuery);
        Assert.Equal("Bearer signed-service-token", downstream.Authorization);
        Assert.True(body.GetProperty("hasNextPage").GetBoolean());
        Assert.False(body.GetProperty("hasPreviousPage").GetBoolean());
        Assert.DoesNotContain("signed-service-token", body.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task MissingReadPermission_IsForbiddenBeforeDownstream()
    {
        var downstream = new RecordingHandler(PageJson);
        await using var factory = new Factory(downstream, canRead: false);
        using var client = CreateClient(factory);
        await SignInAsync(client);

        using var response = await client.GetAsync("/bff/quotation-requests");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Null(downstream.PathAndQuery);
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
            Content = JsonContent.Create(new { email = "employee@maliev.com", password = "password", returnUrl = "/QuotationRequests/Index" }),
        };
        request.Headers.Add("X-CSRF-TOKEN", session.GetProperty("csrfToken").GetString());
        using var response = await client.SendAsync(request);
        response.EnsureSuccessStatusCode();
    }

    private sealed class Factory(HttpMessageHandler downstream, bool canRead) : WebApplicationFactory<BffProgram>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing");
            TestJwtConfiguration.Configure(builder);
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<ILegacyAuthClient>();
                services.AddSingleton<ILegacyAuthClient>(new AuthClient(canRead));
                services.RemoveAll<IServiceAccessTokenProvider>();
                var tokenProvider = new ServiceTokenProvider();
                services.AddSingleton<IServiceAccessTokenProvider>(tokenProvider);
                services.RemoveAll<QuotationRequestsProxy>();
                var authHandler = new LegacyServiceAuthenticationHandler(tokenProvider) { InnerHandler = downstream };
                services.AddSingleton(new QuotationRequestsProxy(new HttpClient(authHandler)
                {
                    BaseAddress = new Uri("http://quotation/"),
                    Timeout = TimeSpan.FromSeconds(10),
                }));
            });
        }
    }

    private sealed class ServiceTokenProvider : IServiceAccessTokenProvider
    {
        public ValueTask<string?> GetAccessTokenAsync(CancellationToken cancellationToken) => ValueTask.FromResult<string?>("signed-service-token");
        public void Invalidate(string token) { }
    }

    private sealed class AuthClient(bool canRead) : ILegacyAuthClient
    {
        public Task<EmployeeLoginResult> LoginAsync(string email, string password, CancellationToken cancellationToken) => Task.FromResult(new EmployeeLoginResult(true, new AuthTokenResponse("server-only-access", "server-only-refresh", "Bearer", 900, DateTimeOffset.UtcNow.AddDays(1)), new EmployeeIdentity("employee-id", email, email, canRead ? [LegacyEmployeePermissions.QuotationRequestsRead] : [], 7)));
        public Task<EmployeeRefreshResult?> RefreshAsync(string refreshToken, CancellationToken cancellationToken) => Task.FromResult<EmployeeRefreshResult?>(null);
        public Task RevokeAsync(string refreshToken, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<CustomerIdentityResponse?> CreateCustomerIdentityAsync(int databaseId, CreateCustomerIdentityRequest request, string accessToken, CancellationToken cancellationToken) => Task.FromResult<CustomerIdentityResponse?>(null);
        public Task<EmployeeIdentityResponse?> CreateEmployeeIdentityAsync(int databaseId, CreateEmployeeIdentityRequest request, string accessToken, CancellationToken cancellationToken) => Task.FromResult<EmployeeIdentityResponse?>(null);
    }

    private sealed class RecordingHandler(string body) : HttpMessageHandler
    {
        public string? PathAndQuery { get; private set; }
        public string? Authorization { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            PathAndQuery = request.RequestUri?.PathAndQuery;
            Authorization = request.Headers.Authorization?.ToString();
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") });
        }
    }

    private const string PageJson = """{"Items":[{"Id":7,"FirstName":"Nat","LastName":"T","Email":"employee@maliev.com","Done":false,"CreatedDate":"2030-07-18T00:00:00Z"}],"PageIndex":1,"TotalPages":3,"TotalRecords":51}""";
}
