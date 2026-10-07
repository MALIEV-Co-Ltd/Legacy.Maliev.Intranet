extern alias Bff;
extern alias CatalogApi;
extern alias ProcurementApi;

using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using Legacy.Maliev.Intranet.Contracts;
using Legacy.Maliev.ProcurementService.Data;
using Maliev.Aspire.ServiceDefaults.IAM;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.IdentityModel.Tokens;
using Microsoft.Playwright;
using NSubstitute;
using Testcontainers.PostgreSql;
using Testcontainers.Redis;

namespace SupplierCatalogPersistence.Acceptance;

/// <summary>Joined real HTTP, WASM, BFF and domain persistence with disposable synthetic authorities.</summary>
public sealed class SupplierCatalogPersistenceTests
{
    private static readonly string[] Permissions =
    [
        "legacy-catalog.locations.read", "legacy-procurement.suppliers.create",
        "legacy-procurement.suppliers.read", "legacy-procurement.supplier-addresses.write",
        "legacy-procurement.supplier-addresses.read",
    ];
    private const string Issuer = "https://disposable-supplier-authority.test";
    private const string Audience = "disposable-supplier-acceptance";

    [Fact]
    public async Task CatalogPostcodeSelection_RealSupplierSave_ApiReadbackAndReloadPreserveAddress()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(4));
        await using var postgres = new PostgreSqlBuilder("postgres:18-alpine").Build();
        await using var redis = new RedisBuilder("redis:7-alpine").Build();
        await postgres.StartAsync(timeout.Token);
        await redis.StartAsync(timeout.Token);
        using var signingKey = RSA.Create(2048);
        using var protectionKey = RSA.Create(2048);
        using var certificate = new CertificateRequest("CN=disposable-supplier-proof", protectionKey,
            HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1).CreateSelfSigned(
                DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddHours(1));
        var secret = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        var certificatePassword = Convert.ToHexString(RandomNumberGenerator.GetBytes(24));
        var serviceToken = Token(signingKey, service: true, Permissions);
        var employeeToken = Token(signingKey, service: false, Permissions);
        var readonlyEmployee = Token(signingKey, service: false, ["legacy-catalog.locations.read"]);
        var settings = new Dictionary<string, string?>
        {
            ["Jwt:Issuer"] = Issuer,
            ["Jwt:Audience"] = Audience,
            ["Jwt:PublicKey"] = Convert.ToBase64String(Encoding.UTF8.GetBytes(signingKey.ExportSubjectPublicKeyInfoPem())),
            ["ConnectionStrings:redis"] = redis.GetConnectionString(),
            ["ConnectionStrings:SupplierDbContext"] = postgres.GetConnectionString(),
            ["ConnectionStrings:PurchaseOrderDbContext"] = postgres.GetConnectionString(),
            ["ConnectionStrings:CatalogDbContext"] = postgres.GetConnectionString(),
            ["ConnectionStrings:CountryDbContext"] = postgres.GetConnectionString(),
            ["ConnectionStrings:CurrencyDbContext"] = postgres.GetConnectionString(),
            ["Services:Auth"] = "https://127.0.0.1/", // Procurement composition stays fail closed; these non-live routes use exact signed claims.
            ["Services:IAM"] = "https://127.0.0.1/",
            ["DataProtection:CertificatePfxBase64"] = Convert.ToBase64String(certificate.Export(X509ContentType.Pfx, certificatePassword)),
            ["DataProtection:CertificatePassword"] = certificatePassword,
        };
        await using (var db = new SupplierDbContext(new DbContextOptionsBuilder<SupplierDbContext>()
            .UseNpgsql(postgres.GetConnectionString()).Options))
            await db.Database.EnsureCreatedAsync(timeout.Token); // Disposable schema only, never an existing database.

        await using var authority = await AuthorityAsync(employeeToken, readonlyEmployee, serviceToken, secret);
        await using var catalog = new RuntimeFactory<CatalogApi::Program>(settings, "Production", catalog: true);
        catalog.UseKestrel(0);
        using var catalogClient = catalog.CreateClient();
        catalogClient.Timeout = TimeSpan.FromSeconds(20);
        await using var procurement = new RuntimeFactory<ProcurementApi::Program>(settings, "Production");
        procurement.UseKestrel(0);
        using var procurementClient = procurement.CreateClient();
        procurementClient.Timeout = TimeSpan.FromSeconds(20);
        settings["Services:Catalog"] = catalogClient.BaseAddress!.AbsoluteUri;
        settings["Services:Procurement"] = procurementClient.BaseAddress!.AbsoluteUri;
        settings["Services:Auth"] = authority.Urls.Single();
        foreach (var service in new[] { "Customer", "Employee", "Order" }) settings["Services:" + service] = authority.Urls.Single();
        settings["ServiceAuthentication:ClientId"] = "supplier-proof";
        settings["ServiceAuthentication:ClientSecret"] = secret;
        await using var bff = new RuntimeFactory<Bff::Program>(settings, "Development", bff: true);
        bff.UseKestrel(options => options.Listen(IPAddress.Loopback, 0, listen => listen.UseHttps(certificate)));
        using var bffClient = bff.CreateClient();
        bffClient.Timeout = TimeSpan.FromSeconds(20);
        var origin = bffClient.BaseAddress!.AbsoluteUri.TrimEnd('/');

        // Production validators must reject anonymous, forged and permissionless workloads.
        using var anonymous = await catalogClient.GetAsync("/api/v1/thai-addresses/provinces");
        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
        catalogClient.DefaultRequestHeaders.Authorization = new("Bearer", Token(signingKey, true, []));
        using var noCatalogGrant = await catalogClient.GetAsync("/api/v1/thai-addresses/provinces");
        Assert.Equal(HttpStatusCode.Forbidden, noCatalogGrant.StatusCode);
        using var wrongKey = RSA.Create(2048);
        procurementClient.DefaultRequestHeaders.Authorization = new("Bearer", Token(wrongKey, true, Permissions));
        using var forged = await procurementClient.GetAsync("/suppliers/1");
        Assert.Equal(HttpStatusCode.Unauthorized, forged.StatusCode);
        procurementClient.DefaultRequestHeaders.Authorization = new("Bearer", Token(signingKey, true, []));
        using var noDomainGrant = await procurementClient.PostAsJsonAsync("/suppliers", new { Name = "Rejected synthetic supplier" });
        Assert.Equal(HttpStatusCode.Forbidden, noDomainGrant.StatusCode);
        procurementClient.DefaultRequestHeaders.Authorization = new("Bearer", serviceToken);
        catalogClient.DefaultRequestHeaders.Authorization = new("Bearer", serviceToken);
        using var combinations = await catalogClient.GetAsync("/api/v1/thai-addresses/autocomplete?postcode=10110&limit=20");
        combinations.EnsureSuccessStatusCode();
        var actual = await combinations.Content.ReadFromJsonAsync<JsonElement>();
        Assert.NotEmpty(actual.GetProperty("items").EnumerateArray());
        Assert.StartsWith("thailand-geography-json:", actual.GetProperty("datasetVersion").GetString());

        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync(new() { Headless = true });
        await using var context = await browser.NewContextAsync(new() { Locale = "en-US", IgnoreHTTPSErrors = true });
        await SignInAsync(context, origin, "supplier-proof@maliev.com");
        var noCsrf = await context.APIRequest.PostAsync(origin + "/bff/suppliers", new()
        { DataObject = new { name = "Rejected synthetic supplier", address1 = "Rejected street", countryId = 66 } });
        Assert.Equal(400, noCsrf.Status);
        await using (var readOnly = await browser.NewContextAsync(new() { IgnoreHTTPSErrors = true }))
        {
            await SignInAsync(readOnly, origin, "readonly-proof@maliev.com");
            using var session = JsonDocument.Parse(await (await readOnly.APIRequest.GetAsync(origin + "/bff/session")).TextAsync());
            var denied = await readOnly.APIRequest.PostAsync(origin + "/bff/suppliers", new()
            {
                Headers = new Dictionary<string, string> { ["X-CSRF-TOKEN"] = session.RootElement.GetProperty("csrfToken").GetString()! },
                DataObject = new { name = "Rejected synthetic supplier", address1 = "Rejected street", countryId = 66 },
            });
            Assert.Equal(403, denied.Status);
        }
        context.SetDefaultTimeout(20_000);
        context.SetDefaultNavigationTimeout(30_000);
        var page = await context.NewPageAsync();
        // No RouteAsync, proxy bridge, fabricated lookup, or fabricated domain response.
        await page.GotoAsync(origin + "/Suppliers/Create");
        await page.Locator("#supplier-name").FillAsync("Synthetic Catalog persistence supplier");
        await page.Locator("#supplier-tax-number").FillAsync("0123456789012");
        await page.Locator("#supplier-address-1").FillAsync("Synthetic manual street");
        await page.Locator("#supplier-address-2").FillAsync("Synthetic floor 2");
        await page.Locator("#supplier-country-id").FillAsync("66");
        await page.Locator("#supplier-address-lookup-enabled").ClickAsync();
        var lookupResponse = await page.RunAndWaitForResponseAsync(
            () => page.Locator("#supplier-address-lookup-postcode-first").FillAsync("10110"),
            response => response.Url.Contains("/bff/lookups/thai-addresses/autocomplete", StringComparison.Ordinal));
        Assert.Equal(200, lookupResponse.Status);
        var result = page.Locator("#supplier-address-lookup-combination-results");
        await Assertions.Expect(result).ToBeEnabledAsync();
        await result.FocusAsync();
        await result.PressAsync("ArrowDown");
        await result.PressAsync("Enter");
        await Assertions.Expect(page.Locator("#supplier-postal-code")).ToHaveValueAsync("10110");
        var city = await page.Locator("#supplier-city").InputValueAsync();
        var state = await page.Locator("#supplier-state").InputValueAsync();
        Assert.False(string.IsNullOrWhiteSpace(city));
        Assert.False(string.IsNullOrWhiteSpace(state));
        Assert.Contains(actual.GetProperty("items").EnumerateArray(), row =>
            row.GetProperty("province").GetProperty("nameTh").GetString() == state &&
            city == string.Join(", ", row.GetProperty("subdistrict").GetProperty("nameTh").GetString(), row.GetProperty("district").GetProperty("nameTh").GetString()));
        await Assertions.Expect(page.Locator("#supplier-address-1")).ToHaveValueAsync("Synthetic manual street");
        await Assertions.Expect(page.Locator("#supplier-address-2")).ToHaveValueAsync("Synthetic floor 2");
        await Assertions.Expect(page.Locator("#supplier-country-id")).ToHaveValueAsync("66");
        var saved = await page.RunAndWaitForResponseAsync(
            () => page.Locator("button[type='submit']").ClickAsync(),
            response => response.Request.Method == "POST" && response.Url.EndsWith("/bff/suppliers", StringComparison.Ordinal));
        Assert.Equal(201, saved.Status);
        var id = (await saved.JsonAsync())!.Value.GetProperty("id").GetInt32();
        Assert.True(id > 0);
        using var domainRead = await procurementClient.GetAsync($"/suppliers/{id}/addresses");
        domainRead.EnsureSuccessStatusCode();
        var persisted = await domainRead.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Synthetic manual street", persisted.GetProperty("Address1").GetString());
        Assert.Equal("Synthetic floor 2", persisted.GetProperty("Address2").GetString());
        Assert.Equal(66, persisted.GetProperty("CountryId").GetInt32());
        Assert.Equal(city, persisted.GetProperty("City").GetString());
        Assert.Equal(state, persisted.GetProperty("State").GetString());
        Assert.Equal("10110", persisted.GetProperty("PostalCode").GetString());
        using var profileRead = await procurementClient.GetAsync($"/suppliers/{id}");
        profileRead.EnsureSuccessStatusCode();
        var profile = await profileRead.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Synthetic Catalog persistence supplier", profile.GetProperty("Name").GetString());
        Assert.Equal("0123456789012", profile.GetProperty("TaxNumber").GetString());
        Assert.Equal(persisted.GetProperty("Id").GetInt32(), profile.GetProperty("AddressId").GetInt32());
        await page.WaitForURLAsync($"**/Suppliers/View?id={id}");
        await page.ReloadAsync();
        await Assertions.Expect(page.Locator("#supplier-edit-address-1")).ToHaveValueAsync("Synthetic manual street");
        await Assertions.Expect(page.Locator("#supplier-edit-address-2")).ToHaveValueAsync("Synthetic floor 2");
        await Assertions.Expect(page.Locator("#supplier-edit-country-id")).ToHaveValueAsync("66");
        await Assertions.Expect(page.Locator("#supplier-edit-city")).ToHaveValueAsync(city);
        await Assertions.Expect(page.Locator("#supplier-edit-state")).ToHaveValueAsync(state);
        await Assertions.Expect(page.Locator("#supplier-edit-postal-code")).ToHaveValueAsync("10110");
        await Assertions.Expect(page.Locator("#supplier-edit-tax-number")).ToHaveValueAsync("0123456789012");
        await Assertions.Expect(page.Locator("#blazor-error-ui")).ToBeHiddenAsync();
        await using var readback = new SupplierDbContext(new DbContextOptionsBuilder<SupplierDbContext>()
            .UseNpgsql(postgres.GetConnectionString()).Options);
        Assert.Single(await readback.Suppliers.AsNoTracking().ToListAsync(timeout.Token));
        Assert.Single(await readback.Addresses.AsNoTracking().ToListAsync(timeout.Token));
    }

    private static async Task SignInAsync(IBrowserContext context, string origin, string email)
    {
        using var session = JsonDocument.Parse(await (await context.APIRequest.GetAsync(origin + "/bff/session")).TextAsync());
        var response = await context.APIRequest.PostAsync(origin + "/bff/login", new()
        {
            Headers = new Dictionary<string, string> { ["X-CSRF-TOKEN"] = session.RootElement.GetProperty("csrfToken").GetString()! },
            DataObject = new { email, password = "synthetic-disposable", returnUrl = "/Suppliers/Create" },
        });
        Assert.Equal(200, response.Status);
    }

    private static string Token(RSA key, bool service, string[] permissions)
    {
        var claims = new List<Claim>
        {
            new("sub", service ? "service:supplier-proof" : "supplier-proof-employee"),
            new("identity_kind", service ? "service" : "employee"),
            new("name", "Synthetic supplier proof"), new("email", "supplier-proof@maliev.com"),
            new("legacy_database_id", "7"), new("role", service ? "service-account" : "Employee"),
        };
        claims.AddRange(permissions.Select(permission => new Claim("permissions", permission)));
        return new JwtSecurityTokenHandler().WriteToken(new JwtSecurityToken(Issuer, Audience, claims,
            DateTime.UtcNow.AddMinutes(-1), DateTime.UtcNow.AddMinutes(15),
            new SigningCredentials(new RsaSecurityKey(key), SecurityAlgorithms.RsaSha256)));
    }

    private static async Task<WebApplication> AuthorityAsync(string employee, string readOnly, string workload, string secret)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        var app = builder.Build();
        app.MapPost("/auth/v1/login", (JsonElement body) => body.GetProperty("password").GetString() == "synthetic-disposable"
            ? Results.Ok(new
            {
                accessToken = body.GetProperty("email").GetString() == "readonly-proof@maliev.com" ? readOnly : employee,
                refreshToken = Guid.NewGuid().ToString("N"),
                tokenType = "Bearer",
                expiresIn = 900,
                refreshExpiresAt = DateTimeOffset.UtcNow.AddHours(1)
            })
            : Results.Unauthorized());
        app.MapPost("/auth/v1/service/login", (JsonElement body) => body.GetProperty("clientId").GetString() == "supplier-proof" && body.GetProperty("clientSecret").GetString() == secret
            ? Results.Ok(new { accessToken = workload, expiresIn = 900 }) : Results.Unauthorized());
        await app.StartAsync();
        return app;
    }

    private sealed class RuntimeFactory<T>(Dictionary<string, string?> settings, string environment, bool catalog = false, bool bff = false)
        : WebApplicationFactory<T> where T : class
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment(environment);
            if (!bff)
            {
                var assembly = typeof(T).Assembly.GetName().Name!;
                var repository = assembly[..assembly.LastIndexOf('.')];
                builder.UseContentRoot(Path.Combine(Environment.GetEnvironmentVariable("MalievWorkspaceRoot")!, repository, assembly));
            }
            foreach (var setting in settings) builder.UseSetting(setting.Key, setting.Value);
            builder.ConfigureServices(services =>
            {
                if (!bff)
                {
                    // Preserve real JWT and RequirePermission handlers. Standard non-live IAM unavailability
                    // falls back to exact signed permission claims; no authorization handler is replaced.
                    services.RemoveAll<IIamServiceClient>();
                    var iam = Substitute.For<IIamServiceClient>();
                    iam.CheckPermissionAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<CancellationToken>()).Returns(Task.FromResult(false));
                    services.AddScoped<IIamServiceClient>(_ => iam);
                }
                if (catalog)
                {
                    // This lane reads the pinned local dataset. Catalog material seeding is out of scope.
                    foreach (var descriptor in services.Where(descriptor => descriptor.ServiceType == typeof(IHostedService) &&
                        descriptor.ImplementationType?.Name == "InstantQuotationCatalogStartupService").ToArray()) services.Remove(descriptor);
                }
            });
        }
    }
}
