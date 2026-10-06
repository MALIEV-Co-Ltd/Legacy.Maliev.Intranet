extern alias Bff;

using System.Collections.Concurrent;
using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Json;
using Legacy.Maliev.Intranet.Contracts;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Http;
using Microsoft.IdentityModel.Tokens;
using BffProgram = Bff::Program;

namespace Legacy.Maliev.Intranet.Tests;

// Normal BFF composition with controlled external HTTP only. No producer database/race proof.
public sealed class BffEmployeeAdministrationContractTests
{
    private const string Route = "/bff/employees/72/edit";
    private static readonly string IdentityVersion = '"' + new string('A', 64) + '"';
    private static readonly string ProfileVersion = '"' + new string('b', 64) + '"';
    private static readonly string AddressVersion = '"' + new string('c', 64) + '"';

    [Fact]
    public async Task NormalEdit_ReadPreservesIndependentVersionsAndNeverExposesCredentials()
    {
        using var scenario = new Scenario();
        await using var factory = new Factory(scenario);
        using var client = Client(factory);
        await LoginAsync(client);
        using var response = await client.GetAsync(Route);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore);
        var body = await response.Content.ReadAsStringAsync();
        var edit = JsonSerializer.Deserialize<EmployeeAdministrationEdit>(body, JsonSerializerOptions.Web)!;
        Assert.Equal(ProfileVersion, edit.ProfileVersion);
        Assert.Equal(IdentityVersion, edit.IdentityVersion);
        Assert.Equal(AddressVersion, edit.AddressVersion);
        Assert.Equal(17, edit.Address!.Id);
        Assert.DoesNotContain(scenario.EmployeeToken, body, StringComparison.Ordinal);
        Assert.DoesNotContain("securityStamp", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("passwordHash", body, StringComparison.OrdinalIgnoreCase);
        Assert.All(scenario.AdministrationCalls, call => Assert.True(call.EmployeeCredential));
        var countryRead = Assert.Single(scenario.Calls, call => call.Path == "/Countries");
        Assert.False(countryRead.HasAuthorization);
        Assert.Equal(1, Assert.Single(edit.Countries).Id);
    }

    [Fact]
    public async Task NormalSave_UsesCapturedVersionsExactFieldsAndSerialIdentityAddressProfile()
    {
        using var scenario = new Scenario();
        await using var factory = new Factory(scenario);
        using var client = Client(factory);
        var csrf = await LoginAsync(client);
        using var response = await SaveAsync(client, csrf, Input());
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = (await response.Content.ReadFromJsonAsync<EmployeeAdministrationSaveResult>())!;
        Assert.Equal(new[] { "identity", "address", "profile" }, result.CompletedStages);
        Assert.False(result.OutcomeUnknown);
        var writes = scenario.Writes;
        Assert.Equal(new[] { "/auth/v1/employee-identities/72/versioned", "/employees/72/home-address/versioned", "/employees/72/versioned" }, writes.Select(call => call.Path));
        Assert.Equal(new[] { IdentityVersion, AddressVersion, ProfileVersion }, writes.Select(call => call.Version));
        Assert.All(writes, call => Assert.True(call.EmployeeCredential));
        using var identity = JsonDocument.Parse(writes[0].Body);
        Assert.Equal(new[] { "email", "emailConfirmed", "lockoutEnabled", "lockoutEnd", "phoneNumber", "phoneNumberConfirmed", "twoFactorEnabled", "userName" }, Names(identity));
        Assert.Equal("changed@example.invalid", identity.RootElement.GetProperty("userName").GetString());
        Assert.True(identity.RootElement.GetProperty("twoFactorEnabled").GetBoolean());
        Assert.Equal("2030-01-01T00:00:00+00:00", identity.RootElement.GetProperty("lockoutEnd").GetString());
        using var address = JsonDocument.Parse(writes[1].Body);
        Assert.Equal(new[] { "AddressId", "AddressLine1", "AddressLine2", "Building", "City", "CountryId", "PostalCode", "State" }, Names(address));
        Assert.Equal(17, address.RootElement.GetProperty("AddressId").GetInt32());
        using var profile = JsonDocument.Parse(writes[2].Body);
        Assert.Equal(new[] { "DateOfBirth", "Email", "FirstName", "HomeAddressId", "LastName", "PhoneNumber", "RoleId" }, Names(profile));
        Assert.Equal(17, profile.RootElement.GetProperty("HomeAddressId").GetInt32());
    }

    [Theory]
    [InlineData("profile")]
    [InlineData("identity")]
    [InlineData("address")]
    public async Task NormalSave_StaleCapturedVersionStopsBeforeEveryWrite(string resource)
    {
        using var scenario = new Scenario();
        await using var factory = new Factory(scenario);
        using var client = Client(factory);
        var csrf = await LoginAsync(client);
        var input = Input();
        var stale = '"' + new string(resource == "identity" ? 'D' : 'd', 64) + '"';
        input = resource switch
        {
            "profile" => input with { ProfileVersion = stale },
            "identity" => input with { IdentityVersion = stale },
            _ => input with { AddressVersion = stale },
        };
        using var response = await SaveAsync(client, csrf, input);
        Assert.Equal(HttpStatusCode.PreconditionFailed, response.StatusCode);
        Assert.Empty(scenario.Writes);
    }

    [Theory]
    [InlineData("identity", 400, 0)]
    [InlineData("identity", 412, 0)]
    [InlineData("address", 400, 1)]
    [InlineData("address", 412, 1)]
    [InlineData("profile", 412, 2)]
    public async Task NormalSave_KnownFailureStopsLaterStepsAndRetainsConfirmedStages(string stage, int status, int confirmed)
    {
        using var scenario = new Scenario { FailureStage = stage, FailureStatus = (HttpStatusCode)status };
        await using var factory = new Factory(scenario);
        using var client = Client(factory);
        var csrf = await LoginAsync(client);
        using var response = await SaveAsync(client, csrf, Input());
        Assert.Equal((HttpStatusCode)status, response.StatusCode);
        var result = (await response.Content.ReadFromJsonAsync<EmployeeAdministrationSaveResult>())!;
        Assert.Equal(stage, result.Stage);
        Assert.Equal(new[] { "identity", "address", "profile" }.Take(confirmed), result.CompletedStages);
        Assert.False(result.OutcomeUnknown);
        Assert.Equal(confirmed + 1, scenario.Writes.Length);
    }

    [Theory]
    [InlineData("identity", 0)]
    [InlineData("address", 1)]
    [InlineData("profile", 2)]
    public async Task NormalSave_LostAcknowledgementIsUnknownAndNeverRetriesOrRunsLaterSteps(string stage, int confirmed)
    {
        using var scenario = new Scenario { FailureStage = stage, LoseAcknowledgement = true };
        await using var factory = new Factory(scenario);
        using var client = Client(factory);
        var csrf = await LoginAsync(client);
        using var response = await SaveAsync(client, csrf, Input());
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        var result = (await response.Content.ReadFromJsonAsync<EmployeeAdministrationSaveResult>())!;
        Assert.True(result.OutcomeUnknown);
        Assert.Equal(stage, result.Stage);
        Assert.Equal(confirmed, result.CompletedStages.Count);
        Assert.Equal(confirmed + 1, scenario.Writes.Length);
        Assert.Single(scenario.Writes, call => Stage(call.Path) == stage);
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("changed")]
    [InlineData("multiple")]
    public async Task NormalSave_InvalidEmployeeFenceReceiptStopsBeforeFinalProfileWithoutReplay(string variant)
    {
        using var scenario = new Scenario { InvalidEmployeeReceipt = variant };
        await using var factory = new Factory(scenario);
        using var client = Client(factory);
        var csrf = await LoginAsync(client);
        using var response = await SaveAsync(client, csrf, Input());
        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
        var result = (await response.Content.ReadFromJsonAsync<EmployeeAdministrationSaveResult>())!;
        Assert.Equal(new[] { "identity", "address" }, result.CompletedStages);
        Assert.False(result.OutcomeUnknown);
        Assert.Equal(2, scenario.Writes.Length);
        Assert.DoesNotContain(scenario.Writes, call => Stage(call.Path) == "profile");
    }

    [Fact]
    public async Task NormalSave_MissingCsrfStopsBeforeDownstreamAdmission()
    {
        using var scenario = new Scenario();
        await using var factory = new Factory(scenario);
        using var client = Client(factory);
        await LoginAsync(client);
        using var response = await SaveAsync(client, null, Input());
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(scenario.AdministrationCalls);
    }

    [Theory]
    [InlineData("GET")]
    [InlineData("PUT")]
    public async Task NormalEdit_AnonymousCookieBoundaryNeverContactsProducer(string method)
    {
        using var scenario = new Scenario();
        await using var factory = new Factory(scenario);
        using var client = Client(factory);
        using var response = method == "GET" ? await client.GetAsync(Route) : await SaveAsync(client, null, Input());
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Empty(scenario.Calls);
    }

    [Theory]
    [InlineData("id")]
    [InlineData("passwordHash")]
    [InlineData("securityStamp")]
    [InlineData("twoFactorEnabled")]
    public async Task NormalSave_ExpandedBrowserWriteRejectedBeforeProducer(string member)
    {
        using var scenario = new Scenario();
        await using var factory = new Factory(scenario);
        using var client = Client(factory);
        var csrf = await LoginAsync(client);
        var input = System.Text.Json.Nodes.JsonNode.Parse(JsonSerializer.Serialize(Input(), JsonSerializerOptions.Web))!.AsObject();
        input[member] = "forbidden-browser-field";
        using var request = new HttpRequestMessage(HttpMethod.Put, Route) { Content = JsonContent.Create(input) };
        request.Headers.Add("X-CSRF-TOKEN", csrf);
        using var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(scenario.AdministrationCalls);
    }

    [Theory]
    [InlineData("legacy-auth.employee-identities.update")]
    [InlineData("legacy-employee.employees.update")]
    [InlineData("legacy-employee.addresses.update")]
    public async Task NormalSave_MissingActorPermissionNeverUsesServiceFallback(string permission)
    {
        using var scenario = new Scenario { MissingPermission = permission };
        await using var factory = new Factory(scenario);
        using var client = Client(factory);
        var csrf = await LoginAsync(client);
        using var response = await SaveAsync(client, csrf, Input());
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Empty(scenario.AdministrationCalls);
    }

    [Fact]
    public async Task NormalSave_RefreshRemovedPermissionStopsBeforeMutation()
    {
        using var scenario = new Scenario { RefreshMissingPermission = "legacy-employee.addresses.update" };
        await using var factory = new Factory(scenario);
        using var client = Client(factory);
        var csrf = await LoginAsync(client);
        scenario.Clock.Offset = TimeSpan.FromMinutes(14);
        using var response = await SaveAsync(client, csrf, Input());
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(1, scenario.RefreshCalls);
        Assert.Empty(scenario.AdministrationCalls);
    }

    [Theory]
    [InlineData("email")]
    [InlineData("address")]
    public async Task NormalSave_SharedPersistenceLimitRejectsBeforeFirstWrite(string field)
    {
        using var scenario = new Scenario();
        await using var factory = new Factory(scenario);
        using var client = Client(factory);
        var csrf = await LoginAsync(client);
        var input = Input();
        input = field == "email"
            ? input with { Email = new string('a', 241) + "@example.invalid" }
            : input with { Address = input.Address! with { AddressLine1 = new string('a', 257) } };
        using var response = await SaveAsync(client, csrf, input);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(scenario.Writes);
    }

    [Theory]
    [InlineData("twoFactorEnabled")]
    [InlineData("lockoutEnd")]
    public async Task NormalSave_MissingProtectedIdentityProjectionNeverWritesDefaultSecuritySettings(string member)
    {
        using var scenario = new Scenario { MissingIdentityMember = member };
        await using var factory = new Factory(scenario);
        using var client = Client(factory);
        var csrf = await LoginAsync(client);
        using var response = await SaveAsync(client, csrf, Input());
        Assert.Contains(response.StatusCode, new[] { HttpStatusCode.BadGateway, HttpStatusCode.ServiceUnavailable });
        Assert.Empty(scenario.Writes);
        var result = (await response.Content.ReadFromJsonAsync<EmployeeAdministrationSaveResult>())!;
        Assert.Empty(result.CompletedStages);
        Assert.False(result.OutcomeUnknown);
    }

    [Theory]
    [InlineData("null")]
    [InlineData("duplicate")]
    public async Task NormalSave_InvalidRoleProjectionStopsBeforeFirstWrite(string variant)
    {
        using var scenario = new Scenario { InvalidRoles = variant };
        await using var factory = new Factory(scenario);
        using var client = Client(factory);
        var csrf = await LoginAsync(client);
        using var response = await SaveAsync(client, csrf, Input());
        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
        Assert.Empty(scenario.Writes);
    }

    [Theory]
    [InlineData("null", 502)]
    [InlineData("duplicate", 502)]
    [InlineData("malformed", 503)]
    public async Task NormalSave_InvalidPublicCountryProjectionStopsBeforeFirstWrite(string variant, int status)
    {
        using var scenario = new Scenario { InvalidCountries = variant };
        await using var factory = new Factory(scenario);
        using var client = Client(factory);
        var csrf = await LoginAsync(client);
        using var response = await SaveAsync(client, csrf, Input());
        Assert.Equal((HttpStatusCode)status, response.StatusCode);
        Assert.Empty(scenario.Writes);
    }

    [Fact]
    public async Task NormalEdit_EmptyPublicCountriesMaps404ToEmptyWithoutBearer()
    {
        using var scenario = new Scenario { EmptyCountries = true };
        await using var factory = new Factory(scenario);
        using var client = Client(factory);
        await LoginAsync(client);
        using var response = await client.GetAsync(Route);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var edit = (await response.Content.ReadFromJsonAsync<EmployeeAdministrationEdit>())!;
        Assert.Empty(edit.Countries);
        Assert.False(Assert.Single(scenario.Calls, call => call.Path == "/Countries").HasAuthorization);
    }

    [Fact]
    public async Task NormalSave_UnknownSelectedCountryRejectedBeforeIdentityWrite()
    {
        using var scenario = new Scenario();
        await using var factory = new Factory(scenario);
        using var client = Client(factory);
        var csrf = await LoginAsync(client);
        var input = Input() with { Address = Input().Address! with { CountryId = 999 } };
        using var response = await SaveAsync(client, csrf, input);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(scenario.Writes);
    }

    [Theory]
    [InlineData("identity", 503, 0)]
    [InlineData("address", 500, 1)]
    [InlineData("profile", 503, 2)]
    public async Task NormalSave_ActualUpstreamServerFailureIsUnknownWithoutRetry(string stage, int status, int confirmed)
    {
        using var scenario = new Scenario { FailureStage = stage, FailureStatus = (HttpStatusCode)status };
        await using var factory = new Factory(scenario);
        using var client = Client(factory);
        var csrf = await LoginAsync(client);
        using var response = await SaveAsync(client, csrf, Input());
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        var result = (await response.Content.ReadFromJsonAsync<EmployeeAdministrationSaveResult>())!;
        Assert.True(result.OutcomeUnknown);
        Assert.Equal(confirmed, result.CompletedStages.Count);
        Assert.Equal(confirmed + 1, scenario.Writes.Length);
    }

    [Theory]
    [InlineData(503)]
    [InlineData(403)]
    public async Task NormalSave_LocalRenewedCredentialRejectionIsKnownAndNeverDispatchesWrite(int status)
    {
        using var scenario = new Scenario
        {
            RefreshAfterPreflight = true,
            RefreshStatus = status == 503 ? HttpStatusCode.ServiceUnavailable : HttpStatusCode.OK,
            RefreshMissingPermission = status == 403 ? "legacy-auth.employee-identities.update" : null,
        };
        await using var factory = new Factory(scenario);
        using var client = Client(factory);
        var csrf = await LoginAsync(client);
        using var response = await SaveAsync(client, csrf, Input());
        Assert.Equal((HttpStatusCode)status, response.StatusCode);
        var result = (await response.Content.ReadFromJsonAsync<EmployeeAdministrationSaveResult>())!;
        Assert.Equal("identity", result.Stage);
        Assert.False(result.OutcomeUnknown);
        Assert.Empty(result.CompletedStages);
        Assert.Empty(scenario.Writes);
        Assert.True(scenario.RefreshCalls > 0);
    }

    [Theory]
    [InlineData(201, true)]
    [InlineData(201, false)]
    [InlineData(200, true)]
    public async Task NormalSave_NewAddressLinksOnlyConfirmedCreatedPositiveId(int status, bool positiveId)
    {
        using var scenario = new Scenario { HasAddress = false, CreateStatus = (HttpStatusCode)status, CreatedId = positiveId ? 91 : 0 };
        await using var factory = new Factory(scenario);
        using var client = Client(factory);
        var csrf = await LoginAsync(client);
        using var response = await SaveAsync(client, csrf, Input(false));
        var result = (await response.Content.ReadFromJsonAsync<EmployeeAdministrationSaveResult>())!;
        Assert.Single(scenario.Writes, call => call.Method == HttpMethod.Post && call.Path == "/employees/addresses");
        if (status == 201 && positiveId)
        {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal(91, result.CreatedAddressId);
            using var profile = JsonDocument.Parse(scenario.Writes.Last().Body);
            Assert.Equal(91, profile.RootElement.GetProperty("HomeAddressId").GetInt32());
        }
        else
        {
            Assert.False(response.IsSuccessStatusCode);
            Assert.True(result.OutcomeUnknown);
            Assert.DoesNotContain(scenario.Writes, call => Stage(call.Path) == "profile");
        }
    }

    [Theory]
    [InlineData("", 1)]
    [InlineData("LINE", 0)]
    public async Task NormalSave_AbsentOptionalNewAddressPreservesSourceNoOp(string line, int countryId)
    {
        using var scenario = new Scenario { HasAddress = false };
        await using var factory = new Factory(scenario);
        using var client = Client(factory);
        var csrf = await LoginAsync(client);
        var input = Input(false) with { Address = new(null, line, null, null, null, null, countryId) };
        using var response = await SaveAsync(client, csrf, input);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(new[] { "identity", "profile" }, scenario.Writes.Select(call => Stage(call.Path)));
    }

    private static EmployeeAdministrationSaveRequest Input(bool existing = true) => new(
        "FIRST", "LAST", "changed@example.invalid", null, null, 7, true, false, true,
        existing ? 17 : null, ProfileVersion, IdentityVersion, existing ? AddressVersion : null,
        new(null, "LINE", null, null, null, null, 1));

    private static string[] Names(JsonDocument document) => document.RootElement.EnumerateObject()
        .Select(property => property.Name).Order(StringComparer.Ordinal).ToArray();

    private static string Stage(string path) => path.Contains("employee-identities", StringComparison.Ordinal)
        ? "identity" : path.Contains("addresses", StringComparison.Ordinal)
            || path.Contains("home-address", StringComparison.Ordinal) ? "address" : "profile";

    private static HttpClient Client(WebApplicationFactory<BffProgram> factory) => factory.CreateClient(new()
    {
        BaseAddress = new Uri("https://localhost"),
        AllowAutoRedirect = false,
        HandleCookies = true,
    });

    private static async Task<string> LoginAsync(HttpClient client)
    {
        using var sessionResponse = await client.GetAsync("/bff/session");
        var session = await sessionResponse.Content.ReadFromJsonAsync<JsonElement>();
        using var login = new HttpRequestMessage(HttpMethod.Post, "/bff/login")
        {
            Content = JsonContent.Create(new { email = "fixture.actor@maliev.com", password = "fixture-password", returnUrl = "/Employees/View?id=72" }),
        };
        login.Headers.Add("X-CSRF-TOKEN", session.GetProperty("csrfToken").GetString());
        using var loggedIn = await client.SendAsync(login);
        loggedIn.EnsureSuccessStatusCode();
        using var authenticated = await client.GetAsync("/bff/session");
        return (await authenticated.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("csrfToken").GetString()!;
    }

    private static Task<HttpResponseMessage> SaveAsync(HttpClient client, string? csrf, EmployeeAdministrationSaveRequest input)
    {
        var request = new HttpRequestMessage(HttpMethod.Put, Route) { Content = JsonContent.Create(input) };
        if (csrf is not null) request.Headers.Add("X-CSRF-TOKEN", csrf);
        return SendAndDisposeAsync(client, request);
    }

    private static async Task<HttpResponseMessage> SendAndDisposeAsync(HttpClient client, HttpRequestMessage request)
    {
        using (request) return await client.SendAsync(request);
    }

    private sealed class Factory(Scenario scenario) : WebApplicationFactory<BffProgram>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing");
            TestHostLifecycle.Configure(builder);
            builder.UseSetting("Jwt:Issuer", "https://administration-auth.test");
            builder.UseSetting("Jwt:Audience", "administration-test");
            builder.UseSetting("Jwt:PublicKeyPem", scenario.PublicKey);
            builder.UseSetting("Jwt:KeyId", "administration-test-key");
            foreach (var name in new[] { "Auth", "Employee", "Order", "Quotation", "Catalog", "Customer", "File", "Notification", "Document", "Procurement", "Accounting" })
                builder.UseSetting("Services:" + name, "https://" + name.ToLowerInvariant() + ".test");
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<TimeProvider>();
                services.AddSingleton<TimeProvider>(scenario.Clock);
                services.Configure<CookieAuthenticationOptions>(CookieAuthenticationDefaults.AuthenticationScheme,
                    options => options.TimeProvider = scenario.Clock);
                // Retain all production DI and delegating handlers; control the external transport only.
                services.AddSingleton<IHttpMessageHandlerBuilderFilter>(new TransportFilter(scenario));
            });
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

    private sealed class Clock : TimeProvider
    {
        public TimeSpan Offset { get; set; }
        public override DateTimeOffset GetUtcNow() => DateTimeOffset.UtcNow + Offset;
    }

    private sealed record Call(HttpMethod Method, string Path, string? Version, string Body, bool EmployeeCredential, bool HasAuthorization);

    private sealed class Scenario : IDisposable
    {
        private readonly RSA rsa = RSA.Create(2048);
        public Clock Clock { get; } = new();
        public ConcurrentQueue<Call> Calls { get; } = new();
        public string PublicKey => rsa.ExportSubjectPublicKeyInfoPem();
        public string EmployeeToken { get; private set; } = string.Empty;
        public bool HasAddress { get; set; } = true;
        public string? MissingPermission { get; set; }
        public string? RefreshMissingPermission { get; set; }
        public bool RefreshAfterPreflight { get; set; }
        public HttpStatusCode RefreshStatus { get; set; } = HttpStatusCode.OK;
        public int RefreshCalls { get; set; }
        public string? MissingIdentityMember { get; set; }
        public string? InvalidRoles { get; set; }
        public string? InvalidCountries { get; set; }
        public bool EmptyCountries { get; set; }
        public string? FailureStage { get; set; }
        public bool LoseAcknowledgement { get; set; }
        public HttpStatusCode FailureStatus { get; set; } = HttpStatusCode.BadRequest;
        public HttpStatusCode CreateStatus { get; set; } = HttpStatusCode.Created;
        public string? InvalidEmployeeReceipt { get; set; }
        public int CreatedId { get; set; } = 91;
        public Call[] AdministrationCalls => Calls.Where(call => call.Path != "/auth/v1/login" && call.Path != "/auth/v1/refresh" && call.Path != "/Countries").ToArray();
        public Call[] Writes => AdministrationCalls.Where(call => call.Method != HttpMethod.Get).ToArray();

        public object Envelope(bool refresh)
        {
            var now = DateTimeOffset.UtcNow;
            var claims = new List<Claim>
            {
                new("sub", "acting-administrator"), new("name", "fixture.actor@maliev.com"), new("email", "fixture.actor@maliev.com"),
                new("identity_kind", "employee"), new("legacy_database_id", "2"), new("sid", "administration-session"),
                new("jti", Guid.NewGuid().ToString("N")),
            };
            foreach (var permission in Permissions.Where(value => value != MissingPermission && (!refresh || value != RefreshMissingPermission)))
                claims.Add(new("permissions", permission));
            EmployeeToken = new JwtSecurityTokenHandler().WriteToken(new JwtSecurityToken(
                "https://administration-auth.test", "administration-test", claims, now.AddMinutes(-1).UtcDateTime,
                now.AddMinutes(15).UtcDateTime, new SigningCredentials(
                    new RsaSecurityKey(rsa) { KeyId = "administration-test-key" }, SecurityAlgorithms.RsaSha256)));
            return new { accessToken = EmployeeToken, refreshToken = "fixture-refresh", tokenType = "Bearer", expiresIn = 900, refreshExpiresAt = Clock.GetUtcNow().AddDays(1) };
        }

        public void Dispose() => rsa.Dispose();
    }

    private sealed class Transport(Scenario scenario) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.AbsolutePath;
            var body = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);
            scenario.Calls.Enqueue(new(request.Method, path, request.Headers.IfMatch.SingleOrDefault()?.ToString(), body,
                request.Headers.Authorization?.ToString() == "Bearer " + scenario.EmployeeToken, request.Headers.Authorization is not null));
            if (path == "/auth/v1/login") return Json(HttpStatusCode.OK, scenario.Envelope(false));
            if (path == "/auth/v1/refresh")
            {
                scenario.RefreshCalls++;
                if (scenario.RefreshStatus != HttpStatusCode.OK) return new(scenario.RefreshStatus);
                return Json(HttpStatusCode.OK, scenario.Envelope(true));
            }
            if (request.Method == HttpMethod.Get)
            {
                if (path == "/Countries")
                {
                    if (scenario.EmptyCountries) return new(HttpStatusCode.NotFound);
                    if (scenario.InvalidCountries == "malformed") return new(HttpStatusCode.OK)
                    {
                        Content = new StringContent("{", System.Text.Encoding.UTF8, "application/json"),
                    };
                    var country = new EmployeeAdministrationCountry(1, "COUNTRY");
                    return Json(HttpStatusCode.OK, scenario.InvalidCountries switch
                    {
                        "null" => new EmployeeAdministrationCountry?[] { null },
                        "duplicate" => new EmployeeAdministrationCountry?[] { country, country },
                        _ => new EmployeeAdministrationCountry?[] { country },
                    });
                }
                if (path == "/employees/72/edit") return Json(HttpStatusCode.OK,
                    new EmployeeAdministrationProfile(72, 7, "FIRST", "LAST", "FIRST LAST", null, "original@example.invalid", null, scenario.HasAddress ? 17 : null, null, null), ProfileVersion);
                if (path == "/auth/v1/employee-identities/72")
                {
                    var identity = new EmployeeAdministrationIdentity("target-identity", "original@example.invalid", "original@example.invalid", false, null, false,
                        true, new DateTimeOffset(2030, 1, 1, 0, 0, 0, TimeSpan.Zero), true, 0, 72, new string('A', 64));
                    var document = System.Text.Json.Nodes.JsonNode.Parse(JsonSerializer.Serialize(identity, JsonSerializerOptions.Web))!.AsObject();
                    if (scenario.MissingIdentityMember is { } missing) document.Remove(missing);
                    return Json(HttpStatusCode.OK, document, IdentityVersion, camel: true);
                }
                if (path == "/employees/addresses/17") return Json(HttpStatusCode.OK, Address(17), AddressVersion);
                if (path == "/employees/roles")
                {
                    if (scenario.RefreshAfterPreflight) scenario.Clock.Offset = TimeSpan.FromMinutes(14);
                    var role = new EmployeeRoleDetail(7, "ROLE", null, null, null);
                    return Json(HttpStatusCode.OK, scenario.InvalidRoles switch
                    {
                        "null" => new EmployeeRoleDetail?[] { null },
                        "duplicate" => new EmployeeRoleDetail?[] { role, role },
                        _ => new EmployeeRoleDetail?[] { role },
                    });
                }
            }
            else if (path is "/auth/v1/employee-identities/72/versioned" or "/employees/72/home-address/versioned" or "/employees/72/versioned" or "/employees/addresses")
            {
                if (Stage(path) == scenario.FailureStage)
                {
                    if (scenario.LoseAcknowledgement) throw new HttpRequestException("Controlled lost acknowledgement");
                    return new(scenario.FailureStatus);
                }
                if (request.Method == HttpMethod.Post) return Json(scenario.CreateStatus, Address(scenario.CreatedId));
                var response = new HttpResponseMessage(HttpStatusCode.NoContent);
                if (path == "/employees/72/home-address/versioned")
                {
                    Assert.Equal(ProfileVersion, Assert.Single(request.Headers.GetValues("X-Employee-If-Match")));
                    if (scenario.InvalidEmployeeReceipt != "missing")
                        response.Headers.Add("X-Employee-ETag", scenario.InvalidEmployeeReceipt == "changed"
                            ? '"' + new string('f', 64) + '"' : ProfileVersion);
                    if (scenario.InvalidEmployeeReceipt == "multiple")
                        response.Headers.Add("X-Employee-ETag", ProfileVersion);
                }
                if (!path.Contains("employee-identities", StringComparison.Ordinal))
                    response.Headers.ETag = EntityTagHeaderValue.Parse('"' + new string('e', 64) + '"');
                return response;
            }
            throw new InvalidOperationException("Unexpected external HTTP boundary: " + path);
        }
    }

    private static EmployeeAddressDetail Address(int id) => new(id, null, "LINE", null, null, null, null, 1, null, null);

    private static HttpResponseMessage Json(HttpStatusCode status, object value, string? etag = null, bool camel = false)
    {
        var response = new HttpResponseMessage(status)
        {
            Content = JsonContent.Create(value, options: new JsonSerializerOptions(camel ? JsonSerializerDefaults.Web : JsonSerializerDefaults.General)),
        };
        if (etag is not null) response.Headers.ETag = EntityTagHeaderValue.Parse(etag);
        return response;
    }

    private static readonly string[] Permissions =
    [
        "legacy-employee.employees.read", "legacy-employee.employees.update", "legacy-auth.employee-identities.read",
        "legacy-auth.employee-identities.update", "legacy-employee.addresses.read", "legacy-employee.addresses.update",
        "legacy-employee.addresses.create", "legacy-employee.roles.read",
    ];
}
