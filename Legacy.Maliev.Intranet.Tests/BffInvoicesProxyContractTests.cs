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
using InvoicesProxy = Bff::Legacy.Maliev.Intranet.Bff.Accounting.InvoicesProxy;

namespace Legacy.Maliev.Intranet.Tests;

public sealed class BffInvoicesProxyContractTests
{
    [Fact]
    public async Task AuthorizedEmployee_ForwardsBoundedLegacyQueryAndServerOnlyToken()
    {
        var downstream = new RecordingHandler(InvoicePageJson);
        await using var factory = new InvoiceBffFactory(downstream, accountingRead: true);
        using var client = CreateClient(factory);
        await SignInAsync(client);

        using var response = await client.GetAsync("/bff/invoices?sort=InvoicePaymentDate_Descending&search=INV%207&index=-4&size=999&paid=true");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("/invoices?sort=InvoicePaymentDate_Descending&search=INV%207&index=1&size=150&paid=true", downstream.PathAndQuery);
        Assert.Equal("Bearer signed-service-token", downstream.Authorization);
        Assert.Contains("\"outstanding\":1040.27", body, StringComparison.Ordinal);
        Assert.DoesNotContain("invoiceFiles", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("signed-service-token", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task MissingAccountingRead_IsForbiddenBeforeDownstream()
    {
        var downstream = new RecordingHandler(InvoicePageJson);
        await using var factory = new InvoiceBffFactory(downstream, accountingRead: false);
        using var client = CreateClient(factory);
        await SignInAsync(client);

        using var response = await client.GetAsync("/bff/invoices");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Null(downstream.PathAndQuery);
    }

    [Fact]
    public async Task AnonymousRequest_IsUnauthorizedBeforeDownstream()
    {
        var downstream = new RecordingHandler(InvoicePageJson);
        await using var factory = new InvoiceBffFactory(downstream, accountingRead: true);
        using var client = CreateClient(factory);

        using var response = await client.GetAsync("/bff/invoices");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Null(downstream.PathAndQuery);
    }

    [Fact]
    public async Task InvalidAccountingPayload_IsBadGatewayWithoutPayloadLeak()
    {
        var downstream = new RecordingHandler("accounting-secret-not-json");
        await using var factory = new InvoiceBffFactory(downstream, accountingRead: true);
        using var client = CreateClient(factory);
        await SignInAsync(client);

        using var response = await client.GetAsync("/bff/invoices");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
        Assert.DoesNotContain("accounting-secret-not-json", body, StringComparison.Ordinal);
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
            Content = JsonContent.Create(new { email = "employee@maliev.com", password = "password", returnUrl = "/Invoices/Index" }),
        };
        request.Headers.Add("X-CSRF-TOKEN", session.GetProperty("csrfToken").GetString());
        using var response = await client.SendAsync(request);
        response.EnsureSuccessStatusCode();
    }

    private sealed class InvoiceBffFactory(HttpMessageHandler downstream, bool accountingRead) : WebApplicationFactory<BffProgram>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing");
            TestJwtConfiguration.Configure(builder);
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<ILegacyAuthClient>();
                services.AddSingleton<ILegacyAuthClient>(new InvoiceAuthClient(accountingRead));
                services.RemoveAll<IServiceAccessTokenProvider>();
                var tokenProvider = new InvoiceServiceTokenProvider();
                services.AddSingleton<IServiceAccessTokenProvider>(tokenProvider);
                services.RemoveAll<InvoicesProxy>();
                var authHandler = new LegacyServiceAuthenticationHandler(tokenProvider) { InnerHandler = downstream };
                services.AddSingleton(new InvoicesProxy(new HttpClient(authHandler)
                {
                    BaseAddress = new Uri("http://accounting/"),
                    Timeout = TimeSpan.FromSeconds(10),
                }));
            });
        }
    }

    private sealed class InvoiceServiceTokenProvider : IServiceAccessTokenProvider
    {
        public ValueTask<string?> GetAccessTokenAsync(CancellationToken cancellationToken) =>
            ValueTask.FromResult<string?>("signed-service-token");

        public void Invalidate(string token)
        {
        }
    }

    private sealed class InvoiceAuthClient(bool accountingRead) : ILegacyAuthClient
    {
        public Task<EmployeeLoginResult> LoginAsync(string email, string password, CancellationToken cancellationToken) =>
            Task.FromResult(new EmployeeLoginResult(
                true,
                new AuthTokenResponse("server-only-access-token", "server-only-refresh-token", "Bearer", 900, DateTimeOffset.UtcNow.AddDays(1)),
                new EmployeeIdentity("employee-id", email, email, accountingRead ? [LegacyEmployeePermissions.AccountingRead] : [], 7)));

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

    private const string InvoicePageJson =
        """{"Items":[{"Id":7,"CustomerId":3,"Number":"INV-7","Currency":"THB","PurchaseOrderNumber":"PO-7","Subtotal":1000.25,"Vat":70.02,"Total":1070.27,"WithholdingTax":30.00,"Outstanding":1040.27,"IsPaid":false,"ReceiptId":null,"PaymentDate":null,"CreatedDate":"2030-07-18T00:00:00Z","InvoiceFiles":[{"Id":99}]}],"PageIndex":1,"TotalPages":1,"TotalRecords":1,"HasNextPage":false,"HasPreviousPage":false}""";
}
