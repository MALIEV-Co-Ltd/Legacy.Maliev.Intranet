extern alias Bff;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Legacy.Maliev.Intranet.Auth;
using Legacy.Maliev.Intranet.Contracts;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using BffProgram = Bff::Program;
using ReplacementProxy = Bff::Legacy.Maliev.Intranet.Bff.Orders.ReplacementProxy;
using CredentialSender = Bff::Legacy.Maliev.Intranet.Bff.Employees.EmployeeAdministrationCredentialSender;
namespace Legacy.Maliev.Intranet.Tests;

// Controlled downstream fixtures verify transport and binding only, not producer acceptance.
public sealed class ReplacementBffTests
{
    private static readonly string[] Permissions = [LegacyEmployeePermissions.OrdersRead, ReplacementPermissions.Read, ReplacementPermissions.Write, ReplacementPermissions.Approve];
    [Fact]
    public async Task Default_disabled_module_has_no_downstream_calls()
    {
        var handler = new Handler(); await using var factory = new Factory(handler, Permissions, false); using var client = Client(factory); await Login(client);
        using var response = await client.GetAsync("/bff/orders/94826/replacements"); Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode); Assert.Empty(handler.Requests);
    }
    [Fact]
    public async Task Report_requires_csrf_before_employee_or_original_reads()
    {
        var handler = new Handler(); await using var factory = new Factory(handler, Permissions); using var client = Client(factory); await Login(client);
        using var response = await client.PostAsJsonAsync("/bff/orders/94826/replacements", Input()); Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode); Assert.Empty(handler.Requests);
    }
    [Fact]
    public async Task Report_binds_customer_and_original_from_protected_order_and_forwards_staff_token_once()
    {
        var handler = new Handler(); await using var factory = new Factory(handler, Permissions); using var client = Client(factory); var csrf = await Login(client); var key = Guid.NewGuid().ToString("D");
        using var request = new HttpRequestMessage(HttpMethod.Post, "/bff/orders/94826/replacements") { Content = JsonContent.Create(Input()) }; request.Headers.Add("X-CSRF-TOKEN", csrf); request.Headers.Add("Idempotency-Key", key);
        using var response = await client.SendAsync(request); Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var post = Assert.Single(handler.Requests, x => x.Method == "POST"); using var body = JsonDocument.Parse(post.Body!);
        Assert.Equal(69797, body.RootElement.GetProperty("CustomerId").GetInt32()); Assert.Equal(94826, body.RootElement.GetProperty("Affected")[0].GetProperty("OrderId").GetInt32());
        Assert.Equal(key, post.Key); Assert.All(handler.Requests, x => Assert.Equal("Bearer employee-access", x.Authorization)); Assert.DoesNotContain("employee-access", await response.Content.ReadAsStringAsync());
    }
    [Fact]
    public async Task Case_from_unrelated_order_cannot_be_commanded()
    {
        var handler = new Handler { CaseOrder = 94876 }; await using var factory = new Factory(handler, Permissions); using var client = Client(factory); var csrf = await Login(client);
        using var request = new HttpRequestMessage(HttpMethod.Post, "/bff/orders/94826/replacements/3/commands") { Content = JsonContent.Create(new { ExpectedRevision = 2, Command = new { Kind = "StartAttempt", OrderId = 94826, Quantity = 1 } }, options: new JsonSerializerOptions(JsonSerializerDefaults.General)) }; request.Headers.Add("X-CSRF-TOKEN", csrf); request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString("D"));
        using var response = await client.SendAsync(request); Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode); Assert.DoesNotContain(handler.Requests, x => x.Method == "POST");
    }
    [Fact]
    public async Task Approval_cannot_use_operational_command_route()
    {
        var handler = new Handler(); await using var factory = new Factory(handler, Permissions); using var client = Client(factory); var csrf = await Login(client);
        using var request = new HttpRequestMessage(HttpMethod.Post, "/bff/orders/94826/replacements/3/commands") { Content = JsonContent.Create(new { ExpectedRevision = 1, Command = new { Kind = "Approve", ReturnDecision = "Waived", Reason = "Approved" } }, options: new JsonSerializerOptions(JsonSerializerDefaults.General)) }; request.Headers.Add("X-CSRF-TOKEN", csrf); request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString("D"));
        using var response = await client.SendAsync(request); Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode); Assert.Empty(handler.Requests);
    }
    [Fact]
    public async Task Case_id_must_match_even_when_customer_and_order_match()
    {
        var handler = new Handler { CaseId = 4 }; await using var factory = new Factory(handler, Permissions); using var client = Client(factory); var csrf = await Login(client);
        using var request = new HttpRequestMessage(HttpMethod.Post, "/bff/orders/94826/replacements/3/commands") { Content = JsonContent.Create(new { ExpectedRevision = 2, Command = new { Kind = "StartAttempt", OrderId = 94826, Quantity = 1 } }, options: new JsonSerializerOptions(JsonSerializerDefaults.General)) }; request.Headers.Add("X-CSRF-TOKEN", csrf); request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString("D"));
        using var response = await client.SendAsync(request); Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode); Assert.DoesNotContain(handler.Requests, x => x.Method == "POST");
    }
    [Fact]
    public async Task Oversized_upstream_response_is_bounded_before_buffering()
    {
        var handler = new Handler { Oversized = true }; await using var factory = new Factory(handler, Permissions); using var client = Client(factory); await Login(client);
        using var response = await client.GetAsync("/bff/orders/94826/replacements"); Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.InRange(handler.SentBytes, 1, 1048576 + 8192);
    }
    [Fact]
    public async Task Revision_rejection_is_preserved_only_for_bound_command_result()
    {
        var handler = new Handler { RejectRevision = true }; await using var factory = new Factory(handler, Permissions); using var client = Client(factory); var csrf = await Login(client);
        using var request = new HttpRequestMessage(HttpMethod.Post, "/bff/orders/94826/replacements/3/commands") { Content = JsonContent.Create(new { ExpectedRevision = 2, Command = new { Kind = "StartAttempt", OrderId = 94826, Quantity = 1 } }, options: new JsonSerializerOptions(JsonSerializerDefaults.General)) }; request.Headers.Add("X-CSRF-TOKEN", csrf); request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString("D"));
        using var response = await client.SendAsync(request); Assert.Equal(HttpStatusCode.Conflict, response.StatusCode); Assert.Equal("Revision", Assert.Single(response.Headers.GetValues("X-Replacement-Rejection")));
    }
    private static ReplacementReportInput Input() => new("ManufacturingNonconformance", 1, new(Guid.NewGuid(), Guid.NewGuid()));
    private static HttpClient Client(Factory f) => f.CreateClient(new() { AllowAutoRedirect = false, BaseAddress = new("https://localhost"), HandleCookies = true });
    private static async Task<string> Login(HttpClient client)
    {
        using var anonymous = await client.GetAsync("/bff/session"); var a = await anonymous.Content.ReadFromJsonAsync<JsonElement>();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/bff/login") { Content = JsonContent.Create(new { email = "employee@maliev.com", password = "password", returnUrl = "/Orders/View?id=94826" }) }; request.Headers.Add("X-CSRF-TOKEN", a.GetProperty("csrfToken").GetString());
        using var login = await client.SendAsync(request); login.EnsureSuccessStatusCode(); using var session = await client.GetAsync("/bff/session"); return (await session.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("csrfToken").GetString()!;
    }
    private sealed class Factory(Handler handler, IReadOnlyList<string> permissions, bool enabled = true) : WebApplicationFactory<BffProgram>
    {
        protected override void ConfigureWebHost(IWebHostBuilder b)
        {
            b.UseEnvironment("Testing"); TestJwtConfiguration.Configure(b); b.UseSetting("Services:Auth", "http://auth/"); b.UseSetting("ReplacementCases:Enabled", enabled.ToString());
            b.ConfigureServices(s => { s.RemoveAll<ILegacyAuthClient>(); s.AddSingleton<ILegacyAuthClient>(new Auth(permissions)); s.RemoveAll<ReplacementProxy>(); s.AddScoped(p => new ReplacementProxy(new HttpClient(handler) { BaseAddress = new("http://order/") }, p.GetRequiredService<CredentialSender>())); });
        }
    }
    private sealed class Auth(IReadOnlyList<string> permissions) : ILegacyAuthClient
    {
        public Task<EmployeeLoginResult> LoginAsync(string e, string p, CancellationToken c) => Task.FromResult(new EmployeeLoginResult(true, new("employee-access", "employee-refresh", "Bearer", 900, DateTimeOffset.UtcNow.AddDays(1)), new("employee-id", e, e, permissions, 7)));
        public Task<EmployeeRefreshResult?> RefreshAsync(string t, CancellationToken c) => Task.FromResult<EmployeeRefreshResult?>(null);
        public Task RevokeAsync(string t, CancellationToken c) => Task.CompletedTask;
        public Task<CustomerIdentityResponse?> CreateCustomerIdentityAsync(int i, CreateCustomerIdentityRequest r, string t, CancellationToken c) => Task.FromResult<CustomerIdentityResponse?>(null);
        public Task<EmployeeIdentityResponse?> CreateEmployeeIdentityAsync(int i, CreateEmployeeIdentityRequest r, string t, CancellationToken c) => Task.FromResult<EmployeeIdentityResponse?>(null);
    }
    private sealed record Sent(string Method, string? Authorization, string? Key, string? Body);
    private sealed class Handler : HttpMessageHandler
    {
        public int CaseOrder { get; set; } = 94826;
        public int CaseId { get; set; } = 3;
        public bool Oversized { get; set; }
        public bool RejectRevision { get; set; }
        public int SentBytes;
        public ConcurrentBag<Sent> Requests { get; } = [];
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken c)
        {
            Requests.Add(new(r.Method.Method, r.Headers.Authorization?.ToString(), r.Headers.TryGetValues("Idempotency-Key", out var k) ? k.Single() : null, r.Content is null ? null : await r.Content.ReadAsStringAsync(c)));
            if (r.RequestUri!.AbsolutePath == "/Orders/94826") return new(HttpStatusCode.OK) { Content = JsonContent.Create(new { Id = 94826, CustomerId = 69797 }) };
            if (RejectRevision && r.Method == HttpMethod.Post) { var rejected = new HttpResponseMessage(HttpStatusCode.Conflict); rejected.Headers.Add("X-Replacement-Rejection", "Revision"); return rejected; }
            if (Oversized) return new(HttpStatusCode.OK) { Content = new LargeContent(this) };
            var value = new ReplacementStoredCaseView(CaseId, new(69797, "ManufacturingNonconformance", "Reported", 1, new(Guid.NewGuid(), Guid.NewGuid()), [new(CaseOrder, 69797, 1, 1, 1, "LALAMOVE", new(2026, 9, 30), null, null)], [], [], [], [], [new(1, "Reported", 7, DateTimeOffset.UtcNow, "Nonconformance")], null, false, false, []));
            return new(r.Method == HttpMethod.Post ? HttpStatusCode.Created : HttpStatusCode.OK) { Content = JsonContent.Create(value) };
        }
    }
    private sealed class LargeContent(Handler owner) : HttpContent
    {
        protected override bool TryComputeLength(out long length) { length = 0; return false; }
        protected override async Task SerializeToStreamAsync(Stream stream, TransportContext? context)
        {
            var bytes = new byte[8192]; for (var i = 0; i < 256; i++) { await stream.WriteAsync(bytes); owner.SentBytes += bytes.Length; }
        }
    }

}
