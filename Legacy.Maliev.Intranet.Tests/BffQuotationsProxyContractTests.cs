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
using QuotationsProxy = Bff::Legacy.Maliev.Intranet.Bff.Quotations.QuotationsProxy;

namespace Legacy.Maliev.Intranet.Tests;

public sealed class BffQuotationsProxyContractTests
{
    [Fact]
    public async Task AuthorizedEmployee_ForwardsBoundedQueryAndServerOnlyToken()
    {
        var downstream = new RecordingHandler(QuotationPageJson);
        await using var factory = new QuotationBffFactory(downstream, quotationRead: true);
        using var client = CreateClient(factory);
        await SignInAsync(client);

        using var response = await client.GetAsync("/bff/quotations?sort=QuotationModifiedDate_Descending&search=Thai%20fixture&index=-4&size=999");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("/quotations?sort=QuotationModifiedDate_Descending&search=Thai%20fixture&index=1&size=250", downstream.PathAndQuery);
        Assert.Equal("Bearer signed-service-token", downstream.Authorization);
        Assert.Contains("\"quotedAmount\":1040.27", body, StringComparison.Ordinal);
        Assert.DoesNotContain("orderItems", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("signed-service-token", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Stats_UsesTheExactServiceOwnedRoute()
    {
        var downstream = new RecordingHandler("""{"Accepted":4,"Declined":2,"Open":1}""");
        await using var factory = new QuotationBffFactory(downstream, quotationRead: true);
        using var client = CreateClient(factory);
        await SignInAsync(client);

        using var response = await client.GetAsync("/bff/quotations/stats");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("/quotations/stats", downstream.PathAndQuery);
    }

    [Fact]
    public async Task MissingQuotationRead_IsForbiddenBeforeDownstream()
    {
        var downstream = new RecordingHandler(QuotationPageJson);
        await using var factory = new QuotationBffFactory(downstream, quotationRead: false);
        using var client = CreateClient(factory);
        await SignInAsync(client);

        using var response = await client.GetAsync("/bff/quotations");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Null(downstream.PathAndQuery);
    }

    [Fact]
    public async Task InvalidQuotationPayload_IsBadGatewayWithoutPayloadLeak()
    {
        var downstream = new RecordingHandler("quotation-secret-not-json");
        await using var factory = new QuotationBffFactory(downstream, quotationRead: true);
        using var client = CreateClient(factory);
        await SignInAsync(client);

        using var response = await client.GetAsync("/bff/quotations");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
        Assert.DoesNotContain("quotation-secret-not-json", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Detail_MissingSupportingReadPermissions_IsForbiddenBeforeDownstream()
    {
        var downstream = new RecordingHandler(QuotationPageJson);
        await using var factory = new QuotationBffFactory(downstream, quotationRead: true);
        using var client = CreateClient(factory);
        await SignInAsync(client);

        using var response = await client.GetAsync("/bff/quotations/84");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Null(downstream.PathAndQuery);
    }

    [Fact]
    public async Task AnonymousDetail_IsUnauthorizedBeforeDownstream()
    {
        var downstream = new RecordingHandler(QuotationPageJson);
        await using var factory = new QuotationBffFactory(downstream, quotationRead: true);
        using var client = CreateClient(factory);

        using var response = await client.GetAsync("/bff/quotations/84");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
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
            Content = JsonContent.Create(new { email = "employee@maliev.com", password = "password", returnUrl = "/Quotations/Index" }),
        };
        request.Headers.Add("X-CSRF-TOKEN", session.GetProperty("csrfToken").GetString());
        using var response = await client.SendAsync(request);
        response.EnsureSuccessStatusCode();
    }

    private sealed class QuotationBffFactory(HttpMessageHandler downstream, bool quotationRead) : WebApplicationFactory<BffProgram>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing");
            TestJwtConfiguration.Configure(builder);
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<ILegacyAuthClient>();
                services.AddSingleton<ILegacyAuthClient>(new QuotationAuthClient(quotationRead));
                services.RemoveAll<IServiceAccessTokenProvider>();
                var tokenProvider = new QuotationServiceTokenProvider();
                services.AddSingleton<IServiceAccessTokenProvider>(tokenProvider);
                services.RemoveAll<QuotationsProxy>();
                var authHandler = new LegacyServiceAuthenticationHandler(tokenProvider) { InnerHandler = downstream };
                services.AddSingleton(new QuotationsProxy(new HttpClient(authHandler)
                {
                    BaseAddress = new Uri("http://quotation/"),
                    Timeout = TimeSpan.FromSeconds(10),
                }));
            });
        }
    }

    private sealed class QuotationServiceTokenProvider : IServiceAccessTokenProvider
    {
        public ValueTask<string?> GetAccessTokenAsync(CancellationToken cancellationToken) => ValueTask.FromResult<string?>("signed-service-token");
        public void Invalidate(string token) { }
    }

    private sealed class QuotationAuthClient(bool quotationRead) : ILegacyAuthClient
    {
        public Task<EmployeeLoginResult> LoginAsync(string email, string password, CancellationToken cancellationToken) =>
            Task.FromResult(new EmployeeLoginResult(
                true,
                new AuthTokenResponse("server-only-access-token", "server-only-refresh-token", "Bearer", 900, DateTimeOffset.UtcNow.AddDays(1)),
                new EmployeeIdentity("employee-id", email, email, quotationRead ? [LegacyEmployeePermissions.QuotationsRead] : [], 7)));
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
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json"),
            });
        }
    }

    private const string QuotationPageJson =
        """{"Items":[{"Id":7,"CustomerId":3,"EmployeeId":2,"InvoiceId":null,"Period":14,"ExpirationDate":"2030-08-01T00:00:00Z","Subtotal":1000.25,"Vat":70.02,"Total":1070.27,"WithholdingTax":30.00,"QuotedAmount":1040.27,"CurrencyId":1,"Comment":"Thai fixture","Fob":"Bangkok","ShippedVia":"Courier","Terms":"Net 7","Accepted":null,"CreatedDate":"2030-07-18T00:00:00Z","ModifiedDate":null}],"PageIndex":1,"TotalPages":1,"TotalRecords":1,"HasNextPage":false,"HasPreviousPage":false}""";
}
