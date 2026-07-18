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
using InvoiceDetailAggregator = Bff::Legacy.Maliev.Intranet.Bff.Accounting.InvoiceDetailAggregator;
using InvoiceDetailProxy = Bff::Legacy.Maliev.Intranet.Bff.Accounting.InvoiceDetailProxy;
using InvoiceFileProxy = Bff::Legacy.Maliev.Intranet.Bff.Accounting.InvoiceFileProxy;

namespace Legacy.Maliev.Intranet.Tests;

public sealed class BffInvoiceDetailContractTests
{
    [Fact]
    public async Task AuthorizedRead_UsesServerTokenAndReturnsBrowserSafeSignedLinks()
    {
        var accounting = new AccountingHandler();
        var files = new FileHandler();
        await using var factory = new Factory(accounting, files, ReadPermissions);
        using var client = CreateClient(factory);
        await SignInAsync(client);

        using var response = await client.GetAsync("/bff/invoices/7");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.All(accounting.Requests, request => Assert.Equal("Bearer signed-service-token", request.Authorization));
        Assert.All(files.Requests, request => Assert.Equal("Bearer signed-service-token", request.Authorization));
        Assert.Contains("https://storage.test/signed", body, StringComparison.Ordinal);
        Assert.DoesNotContain("maliev.com", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("signed-service-token", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task MissingFilesRead_IsForbiddenBeforeAnyDownstreamCall()
    {
        var accounting = new AccountingHandler();
        var files = new FileHandler();
        await using var factory = new Factory(accounting, files, [LegacyEmployeePermissions.AccountingRead]);
        using var client = CreateClient(factory);
        await SignInAsync(client);

        using var response = await client.GetAsync("/bff/invoices/7");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Empty(accounting.Requests);
        Assert.Empty(files.Requests);
    }

    [Fact]
    public async Task UpdateWithoutCsrf_IsRejectedBeforeAccountingWrite()
    {
        var accounting = new AccountingHandler();
        await using var factory = new Factory(accounting, new FileHandler(), [.. ReadPermissions, LegacyEmployeePermissions.AccountingUpdate]);
        using var client = CreateClient(factory);
        await SignInAsync(client);

        using var response = await client.PutAsJsonAsync("/bff/invoices/7", new { isPaid = true, paymentDate = "2030-07-18T00:00:00Z", internalComment = "paid", modifiedDate = "2030-07-18T00:00:00Z" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.DoesNotContain(accounting.Requests, request => request.Method == "PUT");
    }

    [Fact]
    public async Task StaleUpdate_IsConflictAndForwardsConcurrencyTimestamp()
    {
        var accounting = new AccountingHandler { PutStatus = HttpStatusCode.PreconditionFailed };
        await using var factory = new Factory(accounting, new FileHandler(), [.. ReadPermissions, LegacyEmployeePermissions.AccountingUpdate]);
        using var client = CreateClient(factory);
        var csrf = await SignInAsync(client);
        using var request = new HttpRequestMessage(HttpMethod.Put, "/bff/invoices/7")
        {
            Content = JsonContent.Create(new { isPaid = true, paymentDate = "2030-07-18T00:00:00Z", internalComment = "paid", modifiedDate = "2030-07-18T00:00:00Z" }),
        };
        request.Headers.Add("X-CSRF-TOKEN", csrf);

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var put = Assert.Single(accounting.Requests, item => item.Method == "PUT");
        Assert.Equal("2030-07-18T00:00:00.0000000Z", put.Concurrency);
    }

    private static readonly string[] ReadPermissions =
    [
        LegacyEmployeePermissions.AccountingRead,
        LegacyEmployeePermissions.AccountingFilesRead,
        LegacyEmployeePermissions.FileUploadsRead,
    ];

    private static HttpClient CreateClient(WebApplicationFactory<BffProgram> factory) => factory.CreateClient(new() { AllowAutoRedirect = false, BaseAddress = new("https://localhost"), HandleCookies = true });

    private static async Task<string> SignInAsync(HttpClient client)
    {
        using var sessionResponse = await client.GetAsync("/bff/session");
        var session = await sessionResponse.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        var csrf = session.GetProperty("csrfToken").GetString()!;
        using var request = new HttpRequestMessage(HttpMethod.Post, "/bff/login") { Content = JsonContent.Create(new { email = "employee@maliev.com", password = "password", returnUrl = "/Invoices/View?id=7" }) };
        request.Headers.Add("X-CSRF-TOKEN", csrf);
        using var response = await client.SendAsync(request);
        response.EnsureSuccessStatusCode();
        return (await (await client.GetAsync("/bff/session")).Content.ReadFromJsonAsync<System.Text.Json.JsonElement>()).GetProperty("csrfToken").GetString()!;
    }

    private sealed class Factory(AccountingHandler accounting, FileHandler files, IReadOnlyList<string> permissions) : WebApplicationFactory<BffProgram>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing");
            TestJwtConfiguration.Configure(builder);
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<ILegacyAuthClient>();
                services.AddSingleton<ILegacyAuthClient>(new AuthClient(permissions));
                services.RemoveAll<IServiceAccessTokenProvider>();
                var token = new TokenProvider();
                services.AddSingleton<IServiceAccessTokenProvider>(token);
                services.RemoveAll<InvoiceDetailProxy>();
                services.RemoveAll<InvoiceFileProxy>();
                services.RemoveAll<InvoiceDetailAggregator>();
                var accountingAuth = new LegacyServiceAuthenticationHandler(token) { InnerHandler = accounting };
                var fileAuth = new LegacyServiceAuthenticationHandler(token) { InnerHandler = files };
                var invoiceProxy = new InvoiceDetailProxy(new HttpClient(accountingAuth) { BaseAddress = new("http://accounting") });
                var fileProxy = new InvoiceFileProxy(new HttpClient(fileAuth) { BaseAddress = new("http://files") });
                services.AddSingleton(invoiceProxy);
                services.AddSingleton(fileProxy);
                services.AddSingleton(new InvoiceDetailAggregator(invoiceProxy, fileProxy));
            });
        }
    }

    private sealed class TokenProvider : IServiceAccessTokenProvider
    {
        public ValueTask<string?> GetAccessTokenAsync(CancellationToken cancellationToken) => ValueTask.FromResult<string?>("signed-service-token");
        public void Invalidate(string token) { }
    }

    private sealed class AuthClient(IReadOnlyList<string> permissions) : ILegacyAuthClient
    {
        public Task<EmployeeLoginResult> LoginAsync(string email, string password, CancellationToken cancellationToken) => Task.FromResult(new EmployeeLoginResult(true, new("server-token", "refresh-token", "Bearer", 900, DateTimeOffset.UtcNow.AddDays(1)), new("employee-id", email, email, permissions, 7)));
        public Task<EmployeeRefreshResult?> RefreshAsync(string refreshToken, CancellationToken cancellationToken) => Task.FromResult<EmployeeRefreshResult?>(null);
        public Task RevokeAsync(string refreshToken, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<CustomerIdentityResponse?> CreateCustomerIdentityAsync(int databaseId, CreateCustomerIdentityRequest request, string accessToken, CancellationToken cancellationToken) => Task.FromResult<CustomerIdentityResponse?>(null);
        public Task<EmployeeIdentityResponse?> CreateEmployeeIdentityAsync(int databaseId, CreateEmployeeIdentityRequest request, string accessToken, CancellationToken cancellationToken) => Task.FromResult<EmployeeIdentityResponse?>(null);
    }

    private sealed class AccountingHandler : HttpMessageHandler
    {
        public HttpStatusCode PutStatus { get; init; } = HttpStatusCode.NoContent;
        public List<RequestRecord> Requests { get; } = [];
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            request.Headers.TryGetValues("If-Unmodified-Since", out var concurrency);
            Requests.Add(new(request.Method.Method, request.RequestUri?.PathAndQuery, request.Headers.Authorization?.ToString(), concurrency?.SingleOrDefault()));
            if (request.Method == HttpMethod.Put) return Task.FromResult(new HttpResponseMessage(PutStatus));
            var json = request.RequestUri?.AbsolutePath switch
            {
                "/invoices/7" => InvoiceJson,
                "/invoices/7/orderitems" => "[]",
                "/invoices/7/files" => """[{"id":4,"invoiceId":7,"bucket":"maliev.com","objectName":"accounting/invoices/7/invoice.pdf"}]""",
                "/receipts/9/files" => "[]",
                _ => null,
            };
            return Task.FromResult(json is null ? new HttpResponseMessage(HttpStatusCode.NotFound) : new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") });
        }
    }

    private sealed class FileHandler : HttpMessageHandler
    {
        public List<RequestRecord> Requests { get; } = [];
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(new(request.Method.Method, request.RequestUri?.PathAndQuery, request.Headers.Authorization?.ToString(), null));
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("\"https://storage.test/signed\"", Encoding.UTF8, "application/json") });
        }
    }

    private sealed record RequestRecord(string Method, string? Path, string? Authorization, string? Concurrency);
    private const string InvoiceJson = """{"id":7,"customerId":3,"number":"INV-7","currency":"THB","subtotal":1000.25,"vat":70.02,"total":1070.27,"withholdingTax":30.00,"outstanding":1040.27,"isPaid":true,"receiptId":9,"paymentDate":"2030-07-18T00:00:00Z","createdDate":"2030-07-18T00:00:00Z","modifiedDate":"2030-07-18T00:00:00Z"}""";
}
