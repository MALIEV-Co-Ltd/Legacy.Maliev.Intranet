extern alias Bff;

using System.Collections.Concurrent;
using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Legacy.Maliev.Intranet.Contracts;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http;
using Microsoft.IdentityModel.Tokens;
using BffProgram = Bff::Program;
using Coordinator = Bff::Legacy.Maliev.Intranet.Bff.Customers.CustomerAdministrationCoordinator;

namespace Legacy.Maliev.Intranet.Tests;

/// <summary>Normal BFF cookie/CSRF admission and coordinator with controlled external HTTP, not real producer persistence.</summary>
public sealed class BffCustomerAdministrationCoordinatorTests
{
    private const string Route = "/bff/customers/42/edit";
    private static readonly string AuthVersion = '"' + new string('A', 64) + '"';
    private const string ProfileVersion = "\"abcdef01\"";

    [Theory]
    [InlineData("profile")]
    [InlineData("identity")]
    public async Task Preflight_OriginalStaleVersionReturns412BeforeAnyWrite(string resource)
    {
        using var scenario = new Scenario();
        await using var factory = new Factory(scenario);
        using var client = Client(factory);
        var session = await LoginAsync(client);
        var input = resource == "profile" ? Input() with { ProfileVersion = "\"abcdef02\"" }
            : Input() with { IdentityVersion = '"' + new string('B', 64) + '"' };
        using var response = await SaveAsync(client, session.Csrf, input);
        Assert.Equal(HttpStatusCode.PreconditionFailed, response.StatusCode);
        var result = (await response.Content.ReadFromJsonAsync<CustomerAdministrationSaveResult>())!;
        Assert.Equal("preflight", result.Stage);
        Assert.Empty(result.CompletedStages);
        Assert.False(result.OutcomeUnknown);
        Assert.Empty(scenario.Writes);
    }

    [Fact]
    public async Task Profile_Explicit400RejectsWithoutIdentityWriteOrFalseCompletion()
    {
        using var scenario = new Scenario { FailureStage = "profile", Failure = "400" };
        await using var factory = new Factory(scenario);
        using var client = Client(factory);
        var session = await LoginAsync(client);
        using var response = await SaveAsync(client, session.Csrf, Input());
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var result = (await response.Content.ReadFromJsonAsync<CustomerAdministrationSaveResult>())!;
        Assert.Equal("profile", result.Stage);
        Assert.Empty(result.CompletedStages);
        Assert.False(result.OutcomeUnknown);
        Assert.Equal("/customers/42/versioned", Assert.Single(scenario.Writes).Path);
    }

    [Theory]
    [InlineData("503")]
    [InlineData("http-loss")]
    [InlineData("io-loss")]
    public async Task Profile_UncertainAcknowledgementStopsWithoutRetryOrIdentityWrite(string failure)
    {
        using var scenario = new Scenario { FailureStage = "profile", Failure = failure };
        await using var factory = new Factory(scenario);
        using var client = Client(factory);
        var session = await LoginAsync(client);
        using var response = await SaveAsync(client, session.Csrf, Input());
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        var result = (await response.Content.ReadFromJsonAsync<CustomerAdministrationSaveResult>())!;
        Assert.Equal("profile", result.Stage);
        Assert.Empty(result.CompletedStages);
        Assert.True(result.OutcomeUnknown);
        Assert.Equal("/customers/42/versioned", Assert.Single(scenario.Writes).Path);
    }

    [Fact]
    public async Task Identity_Stale412ReportsConfirmedProfileOnlyWithoutReplay()
    {
        using var scenario = new Scenario { FailureStage = "identity", Failure = "412" };
        await using var factory = new Factory(scenario);
        using var client = Client(factory);
        var session = await LoginAsync(client);
        using var response = await SaveAsync(client, session.Csrf, Input());
        Assert.Equal(HttpStatusCode.PreconditionFailed, response.StatusCode);
        var result = (await response.Content.ReadFromJsonAsync<CustomerAdministrationSaveResult>())!;
        Assert.Equal("identity", result.Stage);
        Assert.Equal(new[] { "profile" }, result.CompletedStages);
        Assert.False(result.OutcomeUnknown);
        Assert.Equal(2, scenario.Writes.Length);
        Assert.Equal(AuthVersion, scenario.Writes[1].Version);
    }

    [Theory]
    [InlineData("http-loss")]
    [InlineData("io-loss")]
    public async Task Identity_LostAcknowledgementReportsUnknownAndNeverRepeatsEitherWrite(string failure)
    {
        using var scenario = new Scenario { FailureStage = "identity", Failure = failure };
        await using var factory = new Factory(scenario);
        using var client = Client(factory);
        var session = await LoginAsync(client);
        using var response = await SaveAsync(client, session.Csrf, Input());
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        var result = (await response.Content.ReadFromJsonAsync<CustomerAdministrationSaveResult>())!;
        Assert.Equal("identity", result.Stage);
        Assert.Equal(new[] { "profile" }, result.CompletedStages);
        Assert.True(result.OutcomeUnknown);
        Assert.Equal(new[] { "/customers/42/versioned", "/auth/v1/customer-identities/42/versioned" }, scenario.Writes.Select(call => call.Path));
    }

    [Fact]
    public async Task Cancellation_AfterConfirmedProfileBeforeIdentityKeepsNextUnattemptedStage()
    {
        using var scenario = new Scenario { CancelAfterProfileDispose = true };
        await using var factory = new Factory(scenario);
        using var client = Client(factory);
        var session = await LoginAsync(client);
        using var scope = factory.Services.CreateScope();
        var context = new DefaultHttpContext { RequestServices = scope.ServiceProvider };
        context.Request.Scheme = "https";
        context.Request.Host = new("localhost");
        context.Request.Headers.Cookie = session.Cookie;
        context.RequestAborted = scenario.Cancellation.Token;
        // Use the actual BFF cookie ticket produced by normal login, not a supplied principal/token.
        var authenticated = await context.AuthenticateAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        Assert.True(authenticated.Succeeded);
        context.User = authenticated.Principal!;
        var result = await scope.ServiceProvider.GetRequiredService<Coordinator>().SaveAsync(42, Input(), context, scenario.Cancellation.Token);
        Assert.True(scenario.Cancellation.IsCancellationRequested);
        Assert.Equal("identity", result.Stage);
        Assert.Equal(new[] { "profile" }, result.CompletedStages);
        Assert.False(result.OutcomeUnknown);
        Assert.Equal(StatusCodes.Status503ServiceUnavailable, result.StatusCode);
        Assert.Equal("/customers/42/versioned", Assert.Single(scenario.Writes).Path);
    }

    [Fact]
    public async Task Success_NormalCookieCsrfPreservesCapturedVersionsRelationsAndProtectedIdentity()
    {
        using var scenario = new Scenario();
        await using var factory = new Factory(scenario);
        using var client = Client(factory);
        var session = await LoginAsync(client);
        using var response = await SaveAsync(client, session.Csrf, Input());
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore);
        var result = (await response.Content.ReadFromJsonAsync<CustomerAdministrationSaveResult>())!;
        Assert.Equal("complete", result.Stage);
        Assert.Equal(new[] { "profile", "identity" }, result.CompletedStages);
        Assert.False(result.OutcomeUnknown);
        Assert.Equal(new[] { ProfileVersion, AuthVersion }, scenario.Writes.Select(call => call.Version));
        Assert.All(scenario.AdministrationCalls, call => Assert.True(call.EmployeeCredential));
        using var profile = JsonDocument.Parse(scenario.Writes[0].Body);
        Assert.Equal(71, profile.RootElement.GetProperty("companyId").GetInt32());
        Assert.Equal(72, profile.RootElement.GetProperty("billingAddressId").GetInt32());
        Assert.Equal(73, profile.RootElement.GetProperty("shippingAddressId").GetInt32());
        using var identity = JsonDocument.Parse(scenario.Writes[1].Body);
        Assert.Equal("changed@example.invalid", identity.RootElement.GetProperty("userName").GetString());
        Assert.True(identity.RootElement.GetProperty("twoFactorEnabled").GetBoolean());
        Assert.Equal("2030-01-01T00:00:00+00:00", identity.RootElement.GetProperty("lockoutEnd").GetString());
        Assert.Equal("FAX", identity.RootElement.GetProperty("faxNumber").GetString());
        Assert.Equal("MOBILE", identity.RootElement.GetProperty("mobileNumber").GetString());
    }

    [Theory]
    [InlineData("none")]
    [InlineData("some")]
    [InlineData("all")]
    public async Task ProducerExactPascalCaseNullOmission_EditAndSavePreserveActualRelationIds(string relations)
    {
        using var scenario = new Scenario { Relations = relations };
        await using var factory = new Factory(scenario);
        using var client = Client(factory);
        var session = await LoginAsync(client);
        using var read = await client.GetAsync(Route);
        Assert.Equal(HttpStatusCode.OK, read.StatusCode);
        var edit = (await read.Content.ReadFromJsonAsync<CustomerAdministrationEdit>())!;
        Assert.Equal(relations == "all" ? (int?)71 : null, edit.Profile.CompanyId);
        Assert.Equal(relations == "none" ? null : (int?)72, edit.Profile.BillingAddressId);
        Assert.Equal(relations == "all" ? (int?)73 : null, edit.Profile.ShippingAddressId);
        using var saved = await SaveAsync(client, session.Csrf, Input());
        Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
        using var payload = JsonDocument.Parse(scenario.Writes[0].Body);
        Assert.Equal(edit.Profile.CompanyId, NullableId(payload.RootElement.GetProperty("companyId")));
        Assert.Equal(edit.Profile.BillingAddressId, NullableId(payload.RootElement.GetProperty("billingAddressId")));
        Assert.Equal(edit.Profile.ShippingAddressId, NullableId(payload.RootElement.GetProperty("shippingAddressId")));
        Assert.Equal(new[] { "profile", "identity" }, (await saved.Content.ReadFromJsonAsync<CustomerAdministrationSaveResult>())!.CompletedStages);
        Assert.Equal(2, scenario.Writes.Length);
    }

    [Fact]
    public async Task MissingIdentityUpdatePermission_DeniedWithoutWorkloadFallbackOrProducerCalls()
    {
        using var scenario = new Scenario { MissingPermission = "legacy-auth.customer-identities.update" };
        await using var factory = new Factory(scenario);
        using var client = Client(factory);
        var session = await LoginAsync(client);
        using var response = await SaveAsync(client, session.Csrf, Input());
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Empty(scenario.AdministrationCalls);
    }

    [Theory]
    [InlineData("overlong")]
    [InlineData("disallowed-ascii")]
    [InlineData("non-ascii")]
    public async Task Email_InvalidSharedProducerBoundaryRejectsBeforeAdministrationCalls(string boundary)
    {
        using var scenario = new Scenario();
        await using var factory = new Factory(scenario);
        using var client = Client(factory);
        var session = await LoginAsync(client);
        var email = boundary switch
        {
            "overlong" => new string('a', 241) + "@example.invalid",
            "disallowed-ascii" => "fixture!name@example.invalid",
            _ => "é@example.invalid",
        };
        using var response = await SaveAsync(client, session.Csrf, Input() with { Email = email });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var result = (await response.Content.ReadFromJsonAsync<CustomerAdministrationSaveResult>())!;
        Assert.Equal("validation", result.Stage);
        Assert.Empty(result.CompletedStages);
        Assert.False(result.OutcomeUnknown);
        Assert.Empty(scenario.AdministrationCalls);
    }

    [Fact]
    public async Task Email_Exactly256AllowedCharactersReachesBothWritesUnchanged()
    {
        using var scenario = new Scenario();
        await using var factory = new Factory(scenario);
        using var client = Client(factory);
        var session = await LoginAsync(client);
        var email = new string('a', 240) + "@example.invalid";
        Assert.Equal(256, email.Length);
        using var response = await SaveAsync(client, session.Csrf, Input() with { Email = email });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = (await response.Content.ReadFromJsonAsync<CustomerAdministrationSaveResult>())!;
        Assert.Equal("complete", result.Stage);
        Assert.Equal(new[] { "profile", "identity" }, result.CompletedStages);
        Assert.False(result.OutcomeUnknown);
        Assert.Equal(new[] { "/customers/42/versioned", "/auth/v1/customer-identities/42/versioned" }, scenario.Writes.Select(call => call.Path));
        Assert.Equal(new[] { ProfileVersion, AuthVersion }, scenario.Writes.Select(call => call.Version));
        Assert.All(scenario.AdministrationCalls, call => Assert.True(call.EmployeeCredential));
        foreach (var call in scenario.Writes)
        {
            using var payload = JsonDocument.Parse(call.Body);
            Assert.Equal(email, payload.RootElement.GetProperty("email").GetString());
            if (call.Path.StartsWith("/auth/", StringComparison.Ordinal))
                Assert.Equal(email, payload.RootElement.GetProperty("userName").GetString());
        }
    }

    [Fact]
    public async Task MissingCsrf_NormalAuthenticatedSaveRejectsBeforeAdministrationCalls()
    {
        using var scenario = new Scenario();
        await using var factory = new Factory(scenario);
        using var client = Client(factory);
        await LoginAsync(client);
        using var request = new HttpRequestMessage(HttpMethod.Put, Route) { Content = JsonContent.Create(Input()) };
        using var response = await client.SendAsync(request);
        // The normal BFF antiforgery filter deliberately maps its rejection to 400, not permission-denied 403.
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(scenario.AdministrationCalls);
    }

    [Theory]
    [InlineData("GET")]
    [InlineData("PUT")]
    public async Task Anonymous_AdministrationRequiresNormalCookieBeforeAnyProducerCall(string method)
    {
        using var scenario = new Scenario();
        await using var factory = new Factory(scenario);
        using var client = Client(factory);
        using var request = new HttpRequestMessage(new HttpMethod(method), Route);
        if (method == "PUT") request.Content = JsonContent.Create(Input());
        using var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Empty(scenario.Calls);
    }

    [Theory]
    [InlineData("emailConfirmed", 502)]
    [InlineData("phoneNumberConfirmed", 502)]
    [InlineData("twoFactorEnabled", 502)]
    [InlineData("lockoutEnd", 502)]
    [InlineData("lockoutEnabled", 502)]
    [InlineData("owner", 502)]
    [InlineData("body-version", 502)]
    [InlineData("duplicate", 502)]
    [InlineData("malformed", 502)]
    [InlineData("oversized", 503)]
    public async Task Identity_MalformedProtectedProjectionFailsClosedBeforeEitherWrite(string defect, int status)
    {
        using var scenario = new Scenario { IdentityDefect = defect };
        await using var factory = new Factory(scenario);
        using var client = Client(factory);
        var session = await LoginAsync(client);
        using var response = await SaveAsync(client, session.Csrf, Input());
        Assert.Equal((HttpStatusCode)status, response.StatusCode);
        var result = (await response.Content.ReadFromJsonAsync<CustomerAdministrationSaveResult>())!;
        Assert.Equal("preflight", result.Stage);
        Assert.Empty(result.CompletedStages);
        Assert.False(result.OutcomeUnknown);
        Assert.Empty(scenario.Writes);
        Assert.Equal(new[] { "/customers/42/versioned", "/auth/v1/customer-identities/42" }, scenario.AdministrationCalls.Select(call => call.Path));
        Assert.All(scenario.AdministrationCalls, call =>
        {
            Assert.Equal(HttpMethod.Get, call.Method);
            Assert.True(call.EmployeeCredential);
        });
    }

    private static CustomerAdministrationSaveRequest Input() => new("FIRST", "LAST", "changed@example.invalid",
        "+66812345678", "MOBILE", "FAX", null, false, true, false, ProfileVersion, AuthVersion);

    private static HttpClient Client(WebApplicationFactory<BffProgram> factory) => factory.CreateClient(new()
    {
        BaseAddress = new("https://localhost"), AllowAutoRedirect = false, HandleCookies = true,
    });

    private sealed record Login(string Csrf, string Cookie);

    private static async Task<Login> LoginAsync(HttpClient client)
    {
        using var sessionResponse = await client.GetAsync("/bff/session");
        var session = await sessionResponse.Content.ReadFromJsonAsync<JsonElement>();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/bff/login")
        {
            Content = JsonContent.Create(new { email = "fixture.actor@maliev.com", password = "fixture-password", returnUrl = "/Customers/View?id=42" }),
        };
        request.Headers.Add("X-CSRF-TOKEN", session.GetProperty("csrfToken").GetString());
        using var login = await client.SendAsync(request);
        login.EnsureSuccessStatusCode();
        var cookie = string.Join("; ", login.Headers.GetValues("Set-Cookie").Select(value => value.Split(';', 2)[0]));
        using var authenticated = await client.GetAsync("/bff/session");
        return new((await authenticated.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("csrfToken").GetString()!, cookie);
    }

    private static async Task<HttpResponseMessage> SaveAsync(HttpClient client, string csrf, CustomerAdministrationSaveRequest input)
    {
        using var request = new HttpRequestMessage(HttpMethod.Put, Route) { Content = JsonContent.Create(input) };
        request.Headers.Add("X-CSRF-TOKEN", csrf);
        return await client.SendAsync(request);
    }

    private sealed class Factory(Scenario scenario) : WebApplicationFactory<BffProgram>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing");
            TestHostLifecycle.Configure(builder);
            builder.UseSetting("Jwt:Issuer", "https://customer-administration-auth.test");
            builder.UseSetting("Jwt:Audience", "customer-administration-test");
            builder.UseSetting("Jwt:PublicKeyPem", scenario.PublicKey);
            builder.UseSetting("Jwt:KeyId", "customer-administration-test-key");
            foreach (var name in new[] { "Auth", "Employee", "Order", "Quotation", "Catalog", "Customer", "File", "Notification", "Document", "Procurement", "Accounting" })
                builder.UseSetting("Services:" + name, "https://" + name.ToLowerInvariant() + ".test");
            // No replacement auth scheme, session service, permission policy or coordinator.
            builder.ConfigureServices(services => services.AddSingleton<IHttpMessageHandlerBuilderFilter>(new TransportFilter(scenario)));
        }
    }

    private sealed class TransportFilter(Scenario scenario) : IHttpMessageHandlerBuilderFilter
    {
        public Action<HttpMessageHandlerBuilder> Configure(Action<HttpMessageHandlerBuilder> next) => builder =>
        {
            next(builder);
            builder.PrimaryHandler = new Transport(scenario);
        };
    }

    private sealed record Call(HttpMethod Method, string Path, string? Version, string Body, bool EmployeeCredential);

    private sealed class Scenario : IDisposable
    {
        private readonly RSA rsa = RSA.Create(2048);
        public ConcurrentQueue<Call> Calls { get; } = new();
        public CancellationTokenSource Cancellation { get; } = new();
        public string PublicKey => rsa.ExportSubjectPublicKeyInfoPem();
        public string EmployeeToken { get; private set; } = string.Empty;
        public string? MissingPermission { get; init; }
        public string? FailureStage { get; init; }
        public string? Failure { get; init; }
        public bool CancelAfterProfileDispose { get; init; }
        public string Relations { get; init; } = "all";
        public string? IdentityDefect { get; init; }
        public Call[] AdministrationCalls => Calls.Where(call => call.Path != "/auth/v1/login").ToArray();
        public Call[] Writes => AdministrationCalls.Where(call => call.Method != HttpMethod.Get).ToArray();

        public object Envelope()
        {
            var now = DateTimeOffset.UtcNow;
            var claims = new List<Claim>
            {
                new("sub", "acting-administrator"), new("name", "fixture.actor@maliev.com"), new("email", "fixture.actor@maliev.com"),
                new("identity_kind", "employee"), new("legacy_database_id", "2"), new("sid", "customer-administration-session"),
                new("jti", Guid.NewGuid().ToString("N")),
            };
            // Exactly the four customer permissions already issued to employees by accepted Auth2d01.
            foreach (var permission in Permissions.Where(value => value != MissingPermission)) claims.Add(new("permissions", permission));
            EmployeeToken = new JwtSecurityTokenHandler().WriteToken(new JwtSecurityToken(
                "https://customer-administration-auth.test", "customer-administration-test", claims,
                now.AddMinutes(-1).UtcDateTime, now.AddMinutes(15).UtcDateTime,
                new SigningCredentials(new RsaSecurityKey(rsa) { KeyId = "customer-administration-test-key" }, SecurityAlgorithms.RsaSha256)));
            return new { accessToken = EmployeeToken, refreshToken = "fixture-refresh", tokenType = "Bearer", expiresIn = 900, refreshExpiresAt = now.AddDays(1) };
        }

        public void Dispose() { Cancellation.Dispose(); rsa.Dispose(); }
    }

    private sealed class Transport(Scenario scenario) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            var path = request.RequestUri!.AbsolutePath;
            var body = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(token);
            scenario.Calls.Enqueue(new(request.Method, path, request.Headers.IfMatch.SingleOrDefault()?.ToString(), body,
                request.Headers.Authorization?.ToString() == "Bearer " + scenario.EmployeeToken));
            if (path == "/auth/v1/login") return Json(scenario.Envelope());
            if (request.Method == HttpMethod.Get && path == "/customers/42/versioned")
                return Json(new CustomerDetail(42, "FIRST", "LAST", "FIRST LAST", null, null, null,
                    "original@example.invalid", null, scenario.Relations == "all" ? 71 : null,
                    scenario.Relations == "none" ? null : 72, scenario.Relations == "all" ? 73 : null,
                    null, null, null, null, null), ProfileVersion, producerWire: true);
            if (request.Method == HttpMethod.Get && path == "/auth/v1/customer-identities/42")
                return IdentityRead(scenario.IdentityDefect);
            if (request.Method != HttpMethod.Put || path is not ("/customers/42/versioned" or "/auth/v1/customer-identities/42/versioned"))
                throw new InvalidOperationException("Unexpected controlled Customer administration route.");
            var stage = path.StartsWith("/auth/", StringComparison.Ordinal) ? "identity" : "profile";
            if (scenario.FailureStage == stage)
            {
                if (scenario.Failure == "http-loss") throw new HttpRequestException("Controlled lost acknowledgement.");
                if (scenario.Failure == "io-loss") throw new IOException("Controlled incomplete transport.");
                return new((HttpStatusCode)int.Parse(scenario.Failure!, System.Globalization.CultureInfo.InvariantCulture));
            }
            return stage == "profile" && scenario.CancelAfterProfileDispose
                ? new CancelOnDisposeResponse(scenario.Cancellation) : new HttpResponseMessage(HttpStatusCode.NoContent);
        }
    }

    private sealed class CancelOnDisposeResponse(CancellationTokenSource cancellation) : HttpResponseMessage(HttpStatusCode.NoContent)
    {
        protected override void Dispose(bool disposing)
        {
            if (disposing) cancellation.Cancel();
            base.Dispose(disposing);
        }
    }

    private static int? NullableId(JsonElement value) => value.ValueKind == JsonValueKind.Null ? null : value.GetInt32();

    private static HttpResponseMessage IdentityRead(string? defect)
    {
        var identity = new CustomerAdministrationIdentity("target-identity", "original@example.invalid", "original@example.invalid",
            true, null, false, true, new(2030, 1, 1, 0, 0, 0, TimeSpan.Zero), true, 0, 42, null, null, new string('A', 64));
        if (defect is null) return Json(identity, AuthVersion);
        var document = JsonSerializer.SerializeToNode(identity, JsonSerializerOptions.Web)!.AsObject();
        if (defect == "owner") document["databaseID"] = 43;
        else if (defect == "body-version") document["version"] = new string('B', 64);
        else if (defect == "oversized") document["extra"] = new string('x', 64 * 1024);
        else if (defect is not ("duplicate" or "malformed")) Assert.True(document.Remove(defect));
        var body = document.ToJsonString();
        if (defect == "duplicate") body = body[..^1] + ",\"DATABASEID\":42}";
        if (defect == "malformed") body = "{";
        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };
        response.Headers.ETag = EntityTagHeaderValue.Parse(AuthVersion);
        return response;
    }

    private static HttpResponseMessage Json(object value, string? etag = null, bool producerWire = false)
    {
        var options = producerWire ? new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            PropertyNamingPolicy = null, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        } : JsonSerializerOptions.Web;
        var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(value, options: options) };
        if (etag is not null) response.Headers.ETag = EntityTagHeaderValue.Parse(etag);
        return response;
    }

    private static readonly string[] Permissions =
        ["legacy-customer.customers.read", "legacy-customer.customers.update",
            "legacy-auth.customer-identities.read", "legacy-auth.customer-identities.update"];
}
