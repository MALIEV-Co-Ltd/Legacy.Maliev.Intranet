extern alias Bff;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Legacy.Maliev.Intranet.Client.Features.Customers.Components;
using System.Text;
using System.Security.Claims;
using System.Text.Encodings.Web;
using Legacy.Maliev.Intranet.Auth;
using Legacy.Maliev.Intranet.Contracts;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using CustomerDocumentProxy = Bff::Legacy.Maliev.Intranet.Bff.CustomerDocuments.CustomerDocumentProxy;
using CustomerDocumentEndpointMapper = Bff::Legacy.Maliev.Intranet.Bff.CustomerDocuments.CustomerDocumentEndpointMapper;

namespace Legacy.Maliev.Intranet.Tests;

/// <summary>Transport boundary tests; synthetic HTTP handler does not replace joined registry acceptance.</summary>
public sealed class CustomerDocumentBffTests
{
    [Theory]
    [InlineData("[{\"Kind\":\"Order\",\"ResourceId\":0}]", 400)]
    [InlineData("[{\"Kind\":0,\"ResourceId\":11}]", 400)]
    [InlineData("[{\"Kind\":\"Order\",\"ResourceId\":11},{\"Kind\":\"Order\",\"ResourceId\":11}]", 400)]
    [InlineData("[{\"Kind\":\"Quotation\",\"ResourceId\":21},{\"Kind\":\"Quotation\",\"ResourceId\":22}]", 400)]
    [InlineData("[{\"Kind\":\"Customer\",\"ResourceId\":999}]", 400)]
    [InlineData("over-limit", 400)]
    [InlineData("[{\"Kind\":\"Order\",\"ResourceId\":11},{\"Kind\":\"Quotation\",\"ResourceId\":21},{\"Kind\":\"Replacement\",\"ResourceId\":31}]", 200)]
    public async Task ActualMappedUploadBoundsNamedCommercialAssociationsBeforeDownstream(string associations, int status)
    {
        if (associations == "over-limit") associations = JsonSerializer.Serialize(Enumerable.Range(1, 101).Select(id => new { Kind = "Order", ResourceId = id }));
        var receipt = new Legacy.Maliev.Intranet.Contracts.CustomerDocumentVersionReceipt(Guid.NewGuid(), Guid.NewGuid(), 42, 1, new string('a', 64), 1);
        var handler = new RecordingHandler(JsonSerializer.Serialize(receipt)); await using var app = await HostAsync(handler);
        using var http = app.GetTestClient(); http.DefaultRequestHeaders.Add("Synthetic-Employee", "yes");
        using var csrf = await http.GetAsync("/fixture/csrf");
        http.DefaultRequestHeaders.Add("Cookie", csrf.Headers.GetValues("Set-Cookie").First().Split(';')[0]);
        http.DefaultRequestHeaders.Add("RequestVerificationToken", await csrf.Content.ReadFromJsonAsync<string>());
        http.DefaultRequestHeaders.Add("Idempotency-Key", Guid.NewGuid().ToString("D"));
        using var form = new MultipartFormDataContent(); form.Add(new StringContent("BillingInstruction"), "Kind"); form.Add(new StringContent("synthetic"), "Title"); form.Add(new StringContent("Internal"), "Visibility"); form.Add(new StringContent(associations), "Associations");
        using var file = new ByteArrayContent([1]); file.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/pdf"); form.Add(file, "files", "synthetic.pdf");
        using var response = await http.PostAsync("/bff/customers/42/documents?customerId=999", form);
        Assert.Equal(status, (int)response.StatusCode);
        if (status == 400) Assert.Null(handler.Path);
        else { Assert.Equal("/customers/42/documents", handler.Path); Assert.Contains(associations, handler.Body!); Assert.Equal("Bearer synthetic-acting-token", handler.Authorization); }
    }
    [Fact]
    public async Task ActualMappedEmployeeReadKeepsCredentialServerSide()
    {
        var id = Guid.NewGuid(); var handler = new RecordingHandler($"[{{\"DocumentId\":\"{id}\",\"CustomerId\":42,\"Kind\":\"Nda\",\"Title\":\"synthetic\",\"Visibility\":\"Internal\",\"Revision\":1}}]");
        await using var app = await HostAsync(handler); using var http = app.GetTestClient(); http.DefaultRequestHeaders.Add("Synthetic-Employee", "yes");
        using var response = await http.GetAsync("/bff/customers/42/documents");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode); Assert.Equal("Bearer synthetic-acting-token", handler.Authorization);
        Assert.DoesNotContain("synthetic-acting-token", await response.Content.ReadAsStringAsync()); Assert.Contains("no-store", response.Headers.CacheControl!.ToString());
    }
    [Fact]
    public async Task ActualMappedMutationRejectsMissingCsrfBeforeRegistry()
    {
        var handler = new RecordingHandler("{}"); await using var app = await HostAsync(handler);
        using var http = app.GetTestClient(); http.DefaultRequestHeaders.Add("Synthetic-Employee", "yes");
        using var form = new MultipartFormDataContent(); form.Add(new StringContent("Nda"), "Kind");
        using var response = await http.PostAsync("/bff/customers/42/documents", form);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode); Assert.Null(handler.Path);
    }
    [Fact]
    public async Task ActualMappedAnonymousReadRejectsBeforeRegistry()
    {
        var handler = new RecordingHandler("{}"); await using var app = await HostAsync(handler);
        using var http = app.GetTestClient(); using var response = await http.GetAsync("/bff/customers/42/documents");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode); Assert.Null(handler.Path);
    }
    [Fact]
    public async Task ReceiptUsesActingCredentialAndExactVersionPath()
    {
        var documentId = Guid.NewGuid(); var versionId = Guid.NewGuid(); var handler = new RecordingHandler("{}");
        using var http = new HttpClient(handler) { BaseAddress = new Uri("http://synthetic-registry/") };
        var proxy = new CustomerDocumentProxy(http);
        await proxy.ReceiptAsync(42, documentId, versionId, "synthetic-acting-token", CancellationToken.None);
        Assert.Equal($"/customers/42/documents/{documentId:D}/versions/{versionId:D}/receipt", handler.Path);
        Assert.Equal("Bearer synthetic-acting-token", handler.Authorization);
    }
    [Fact]
    public async Task ProviderRedirectIsUnavailableAndNeverExposesBearerUrl()
    {
        var handler = new RecordingHandler("https://unsafe.example/signed?secret=synthetic", HttpStatusCode.Redirect);
        using var http = new HttpClient(handler) { BaseAddress = new Uri("http://synthetic-registry/") };
        var result = await new CustomerDocumentProxy(http).DownloadAsync(42, Guid.NewGuid(), Guid.NewGuid(), "synthetic-acting-token", CancellationToken.None);
        Assert.Equal(503, result.StatusCode); Assert.Null(result.Value);
    }
    [Theory]
    [InlineData("Verified", 200)]
    [InlineData("Rejected", 200)]
    [InlineData("1", 400)]
    public async Task ActualMappedExactVersionVerificationUsesNamedStatusAndEvidenceRevision(string status, int expectedStatus)
    {
        var document = Guid.NewGuid(); var version = Guid.NewGuid();
        var receipt = new CustomerDocumentReceipt(document, version, 42, "BillingInstruction", new string('a', 64), null, [], status, "synthetic-employee", DateTimeOffset.UtcNow, 8);
        var handler = new RecordingHandler(JsonSerializer.Serialize(receipt)); await using var app = await HostAsync(handler);
        using var http = app.GetTestClient(); http.DefaultRequestHeaders.Add("Synthetic-Employee", "yes");
        using var csrf = await http.GetAsync("/fixture/csrf");
        http.DefaultRequestHeaders.Add("Cookie", csrf.Headers.GetValues("Set-Cookie").First().Split(';')[0]);
        http.DefaultRequestHeaders.Add("RequestVerificationToken", await csrf.Content.ReadFromJsonAsync<string>());
        using var response = await http.PostAsJsonAsync($"/bff/customers/42/documents/{document:D}/versions/{version:D}/verification", new CustomerDocumentVerificationRequest(7, status, "synthetic staff decision"));
        Assert.Equal(expectedStatus, (int)response.StatusCode);
        if (expectedStatus == 400) { Assert.Null(handler.Path); return; }
        Assert.Equal($"/customers/42/documents/{document:D}/versions/{version:D}/verification", handler.Path);
        Assert.Equal("Bearer synthetic-acting-token", handler.Authorization);
        using var payload = JsonDocument.Parse(handler.Body!);
        Assert.Equal(7, payload.RootElement.GetProperty("ExpectedVerificationRevision").GetInt64());
        Assert.Equal(status, payload.RootElement.GetProperty("Status").GetString());
        Assert.DoesNotContain("synthetic-acting-token", await response.Content.ReadAsStringAsync());
    }
    [Fact]
    public async Task ActualMappedVerificationRefusesMissingCsrfBeforeDownstream()
    {
        var handler = new RecordingHandler("{}"); await using var app = await HostAsync(handler);
        using var http = app.GetTestClient(); http.DefaultRequestHeaders.Add("Synthetic-Employee", "yes");
        using var response = await http.PostAsJsonAsync($"/bff/customers/42/documents/{Guid.NewGuid():D}/versions/{Guid.NewGuid():D}/verification", new CustomerDocumentVerificationRequest(7, "Verified", "synthetic reason"));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode); Assert.Null(handler.Path);
    }
    [Fact]
    public async Task ActualMappedReceiptRejectsVerifiedWithoutActorAndUtcEvidence()
    {
        var document = Guid.NewGuid(); var version = Guid.NewGuid();
        var receipt = new CustomerDocumentReceipt(document, version, 42, "Evidence", new string('a', 64), null, [], "Verified", null, null, 1);
        var handler = new RecordingHandler(JsonSerializer.Serialize(receipt)); await using var app = await HostAsync(handler);
        using var http = app.GetTestClient(); http.DefaultRequestHeaders.Add("Synthetic-Employee", "yes");
        using var response = await http.GetAsync($"/bff/customers/42/documents/{document:D}/versions/{version:D}/receipt");
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.DoesNotContain(new string('a', 64), await response.Content.ReadAsStringAsync());
    }
    [Fact]
    public async Task ActualMappedUploadRejectsMalformedAcknowledgement()
    {
        var receipt = new CustomerDocumentVersionReceipt(Guid.Empty, Guid.NewGuid(), 42, 1, new string('a', 64), 1);
        var handler = new RecordingHandler(JsonSerializer.Serialize(receipt)); await using var app = await HostAsync(handler);
        using var http = app.GetTestClient(); http.DefaultRequestHeaders.Add("Synthetic-Employee", "yes");
        using var csrf = await http.GetAsync("/fixture/csrf");
        http.DefaultRequestHeaders.Add("Cookie", csrf.Headers.GetValues("Set-Cookie").First().Split(';')[0]); http.DefaultRequestHeaders.Add("RequestVerificationToken", await csrf.Content.ReadFromJsonAsync<string>()); http.DefaultRequestHeaders.Add("Idempotency-Key", Guid.NewGuid().ToString("D"));
        using var form = new MultipartFormDataContent(); form.Add(new StringContent("Corporate"), "Kind"); form.Add(new StringContent("synthetic-title"), "Title"); form.Add(new StringContent("Internal"), "Visibility"); form.Add(new StringContent("[]"), "Associations");
        using var file = new ByteArrayContent([1]); file.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/pdf"); form.Add(file, "files", "synthetic.pdf");
        using var response = await http.PostAsync("/bff/customers/42/documents", form);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode); Assert.Equal("/customers/42/documents", handler.Path);
        Assert.DoesNotContain(new string('a', 64), await response.Content.ReadAsStringAsync());
    }
    [Theory]
    [InlineData("state=1")]
    [InlineData("state=Unknown")]
    [InlineData("limit=101")]
    [InlineData("dueFromUtc=2026-10-08T00%3A00%3A00%2B07%3A00")]
    [InlineData("dueFromUtc=2026-01-01T00%3A00%3A00Z&dueThroughUtc=2027-01-03T00%3A00%3A00Z")]
    public async Task ActualMappedWorklistRejectsUnboundedOrUndefinedFilters(string query)
    {
        var handler = new RecordingHandler("[]"); await using var app = await HostAsync(handler);
        using var http = app.GetTestClient(); http.DefaultRequestHeaders.Add("Synthetic-Employee", "yes");
        using var response = await http.GetAsync("/bff/staff/nda-reminders?" + query);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode); Assert.Null(handler.Path);
    }
    [Theory]
    [InlineData("synthetic-employee", "Due", 200)]
    [InlineData("synthetic-employee", "due", 200)]
    [InlineData("other-employee", "Due", 503)]
    public async Task ActualMappedFilteredWorklistKeepsCurrentResponsibleRecipient(string recipient, string state, int expectedStatus)
    {
        var reminder = new CustomerNdaReminder(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 1, 7, recipient, new DateTimeOffset(2026, 10, 8, 0, 0, 0, TimeSpan.Zero), "Due");
        var handler = new RecordingHandler(JsonSerializer.Serialize(new[] { reminder })); await using var app = await HostAsync(handler);
        using var http = app.GetTestClient(); http.DefaultRequestHeaders.Add("Synthetic-Employee", "yes");
        using var response = await http.GetAsync("/bff/staff/nda-reminders?state=" + state + "&limit=20&dueFromUtc=2026-10-08T00%3A00%3A00Z&dueThroughUtc=2026-10-08T00%3A00%3A00Z");
        Assert.Equal(expectedStatus, (int)response.StatusCode);
        Assert.Equal("/staff/nda-reminders", handler.Path); Assert.Contains("state=Due", handler.Query); Assert.Contains("limit=20", handler.Query);
        if (expectedStatus != 200) Assert.DoesNotContain(recipient, await response.Content.ReadAsStringAsync());
    }
    [Fact]
    public async Task ActualMappedWorklistAcceptsOwnerSupported730DayLead()
    {
        var reminder = new CustomerNdaReminder(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 1, 730, "synthetic-employee", DateTimeOffset.UtcNow, "Due");
        var handler = new RecordingHandler(JsonSerializer.Serialize(new[] { reminder })); await using var app = await HostAsync(handler);
        using var http = app.GetTestClient(); http.DefaultRequestHeaders.Add("Synthetic-Employee", "yes");
        using var response = await http.GetAsync("/bff/staff/nda-reminders"); Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
    [Theory]
    [InlineData("archive", false)]
    [InlineData("archive", true)]
    [InlineData("nda/verification", false)]
    [InlineData("nda/verification", true)]
    [InlineData("versions/evidence/verification", false)]
    [InlineData("versions/evidence/verification", true)]
    public async Task ActualMappedJsonMutationsBoundBodyBeforeDeserialization(string route, bool chunked)
    {
        var document = Guid.NewGuid(); var version = Guid.NewGuid();
        var handler = new RecordingHandler("{}"); await using var app = await HostAsync(handler);
        using var http = app.GetTestClient(); http.DefaultRequestHeaders.Add("Synthetic-Employee", "yes");
        using var csrf = await http.GetAsync("/fixture/csrf");
        http.DefaultRequestHeaders.Add("Cookie", csrf.Headers.GetValues("Set-Cookie").First().Split(';')[0]); http.DefaultRequestHeaders.Add("RequestVerificationToken", await csrf.Content.ReadFromJsonAsync<string>());
        var padding = new string('x', 70 * 1024);
        var payload = route == "archive" ? JsonSerializer.Serialize(new { ExpectedRevision = 1, Reason = "synthetic", Padding = padding }) : route == "nda/verification" ? JsonSerializer.Serialize(new { VersionId = version, ExpectedRevision = 1, PartyOne = "synthetic one", PartyTwo = "synthetic two", EffectiveAtUtc = DateTimeOffset.UtcNow, SurvivalKind = "Unknown", ResponsibleEmployeeSubject = "synthetic-employee", Coverage = new[] { new { Kind = "Customer", ResourceId = 42 } }, Reason = "synthetic", Padding = padding }) : JsonSerializer.Serialize(new { ExpectedVerificationRevision = 1, Status = "Verified", Reason = "synthetic", Padding = padding });
        var path = route == "versions/evidence/verification" ? $"versions/{version:D}/verification" : route;
        using HttpContent content = chunked ? new UnboundedLengthContent(payload) : new StringContent(payload, Encoding.UTF8, "application/json");
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/bff/customers/42/documents/{document:D}/{path}") { Content = content };
        if (chunked) request.Headers.TransferEncodingChunked = true;
        using var response = await http.SendAsync(request);
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode); Assert.Null(handler.Path);
    }
    private sealed class UnboundedLengthContent : HttpContent
    {
        private readonly byte[] bytes;
        public UnboundedLengthContent(string payload) { bytes = Encoding.UTF8.GetBytes(payload); Headers.ContentType = new("application/json"); }
        protected override bool TryComputeLength(out long length) { length = 0; return false; }
        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) => stream.WriteAsync(bytes).AsTask();
    }
    public sealed class RecordingHandler(string body, HttpStatusCode status = HttpStatusCode.OK) : HttpMessageHandler
    {
        public string? Path { get; private set; }
        public string? Authorization { get; private set; }
        public string? Body { get; private set; }
        public string? Query { get; private set; }
        public Func<string, string>? ResponseBody { get; init; }
        public Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>>? ResponseFactory { get; init; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            var path = request.RequestUri!.AbsolutePath; Path = path; Query = request.RequestUri.Query; Authorization = request.Headers.Authorization?.ToString();
            Body = request.Content is null ? null : await request.Content.ReadAsStringAsync(token);
            if (ResponseFactory is not null) return await ResponseFactory(request, token);
            return new HttpResponseMessage(status) { Content = new StringContent(ResponseBody?.Invoke(path) ?? body, Encoding.UTF8, "application/json") };
        }
    }
    public static async Task<WebApplication> HostAsync(RecordingHandler handler, bool loopbackBrowser = false, Action<WebApplication>? configure = null)
    {
        var builder = WebApplication.CreateBuilder();
        if (loopbackBrowser) builder.WebHost.UseUrls("http://127.0.0.1:0"); else builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders(); builder.Services.AddDataProtection().UseEphemeralDataProtectionProvider();
        builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme).AddScheme<AuthenticationSchemeOptions, FixtureAuthentication>(CookieAuthenticationDefaults.AuthenticationScheme, _ => { });
        builder.Services.AddAuthorization(); builder.Services.AddAntiforgery(); builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.AddLocalization(); builder.Services.AddRazorComponents();
        var componentHttp = new HttpClient(); componentHttp.DefaultRequestHeaders.Add("Synthetic-Employee", "yes"); builder.Services.AddSingleton(componentHttp);
        builder.Services.Configure<LegacyEmployeeCompatibilityOptions>(_ => { }); builder.Services.AddSingleton<ILegacyAuthClient, UnusedAuth>(); builder.Services.AddScoped<EmployeeSessionService>();
        builder.Services.AddSingleton(new CustomerDocumentProxy(new HttpClient(handler) { BaseAddress = new Uri("http://synthetic-registry/") }));
        var app = builder.Build(); app.UseRequestLocalization(new RequestLocalizationOptions().SetDefaultCulture("en").AddSupportedCultures("en", "th").AddSupportedUICultures("en", "th")); app.UseAuthentication(); app.UseAuthorization(); CustomerDocumentEndpointMapper.MapCustomerDocumentEndpoints(app);
        app.MapGet("/fixture/csrf", (HttpContext context, IAntiforgery antiforgery) => Results.Json(antiforgery.GetAndStoreTokens(context).RequestToken));
        app.MapGet("/fixture/page", async (HttpContext context) =>
        {
            await using var renderer = new HtmlRenderer(context.RequestServices, context.RequestServices.GetRequiredService<ILoggerFactory>());
            var html = await renderer.Dispatcher.InvokeAsync(async () => (await renderer.RenderComponentAsync<CustomerDocuments>(ParameterView.FromDictionary(new Dictionary<string, object?> { ["CustomerId"] = 42 }))).ToHtmlString());
            return Results.Content(html, "text/html; charset=utf-8");
        }).RequireAuthorization();
        configure?.Invoke(app);
        await app.StartAsync(); componentHttp.BaseAddress = loopbackBrowser ? new Uri(app.Urls.Single()) : new Uri("http://localhost");
        return app;
    }
    private sealed class FixtureAuthentication(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (!Request.Headers.ContainsKey("Synthetic-Employee")) return Task.FromResult(AuthenticateResult.NoResult());
            var properties = new AuthenticationProperties(); properties.StoreTokens([new AuthenticationToken { Name = "legacy_access_token", Value = "synthetic-acting-token" }, new AuthenticationToken { Name = "legacy_access_expires_at", Value = DateTimeOffset.UtcNow.AddHours(1).ToString("O") }]);
            var principal = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "synthetic-employee"), new Claim("identity_kind", "employee")], Scheme.Name));
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(principal, properties, Scheme.Name)));
        }
    }
    private sealed class UnusedAuth : ILegacyAuthClient
    {
        public Task<EmployeeLoginResult> LoginAsync(string email, string password, CancellationToken token) => throw new NotSupportedException();
        public Task<EmployeeRefreshResult?> RefreshAsync(string refreshToken, CancellationToken token) => throw new NotSupportedException();
        public Task RevokeAsync(string refreshToken, CancellationToken token) => throw new NotSupportedException();
        public Task<CustomerIdentityResponse?> CreateCustomerIdentityAsync(int databaseId, CreateCustomerIdentityRequest request, string accessToken, CancellationToken token) => throw new NotSupportedException();
        public Task<EmployeeIdentityResponse?> CreateEmployeeIdentityAsync(int databaseId, CreateEmployeeIdentityRequest request, string accessToken, CancellationToken token) => throw new NotSupportedException();
    }
}
