extern alias Bff;
extern alias CatalogApi;
extern alias ProcurementApi;
extern alias DocumentApi;
extern alias FileApi;
extern alias EmployeeApi;

using System.Collections.Concurrent;
using System.Globalization;
using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using Legacy.Maliev.CatalogService.Data;
using Legacy.Maliev.CatalogService.Domain;
using Legacy.Maliev.EmployeeService.Data;
using Legacy.Maliev.EmployeeService.Domain;
using Legacy.Maliev.FileService.Data;
using Legacy.Maliev.Intranet.Auth;
using Legacy.Maliev.ProcurementService.Data;
using Legacy.Maliev.ProcurementService.Domain;
using Maliev.Aspire.ServiceDefaults.IAM;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;
using Npgsql;
using NSubstitute;

namespace SupplierCatalogPersistence.Acceptance;

/// <summary>Actual pinned Programs and disposable databases; authentication authority and external provider are synthetic.</summary>
internal sealed class PoCompanyRuntime
{
    private const string Issuer = "https://disposable-po-authority.test";
    private const string Audience = "disposable-po-company-proof";
    internal static readonly string[] Permissions =
    [
        "legacy-catalog.companies.read", "legacy-catalog.countries.read",
        "legacy-procurement.suppliers.read", "legacy-procurement.supplier-addresses.read",
        "legacy-procurement.purchase-orders.read", "legacy-procurement.purchase-orders.create",
        "legacy-procurement.purchase-order-addresses.read", "legacy-procurement.order-items.read",
        "legacy-procurement.order-items.write", "legacy-procurement.files.read", "legacy-procurement.files.write",
        "legacy-employee.employees.read", "legacy-employee.employees.list", "legacy.documents.render", "legacy-file.uploads.create", "legacy-file.uploads.read",
    ];
    internal HttpClient Catalog { get; private set; } = null!;
    internal HttpClient Procurement { get; private set; } = null!;
    internal HttpClient File { get; private set; } = null!;
    internal string Origin { get; private set; } = null!;
    internal string ServiceToken { get; private set; } = null!;
    internal string MissingCompanyServiceToken { get; private set; } = null!;
    internal string MissingCreateServiceToken { get; private set; } = null!;
    internal string ForeignServiceToken { get; private set; } = null!;
    internal PoCompanyProvider Provider { get; private set; } = null!;
    internal PoOwnedFileFixtures ExternalFile { get; private set; } = null!;
    internal string OrderConnection { get; private set; } = null!;
    internal string FileConnection { get; private set; } = null!;
    internal int SupplierId { get; private set; }
    internal int EmployeeId { get; private set; }
    internal int ShippingAddressId { get; private set; }
    internal int BillingAddressId { get; private set; }
    internal ConcurrentQueue<PoDocumentWitness> Documents { get; } = new();
    internal ConcurrentQueue<PoIamWitness> IamDecisions { get; } = new();
    private string procurementSecret = null!;
    private byte[] authorityCertificate = null!;

    internal static async Task<PoCompanyRuntime> StartAsync(SupplierResourceScope owner, CancellationToken token)
    {
        var result = new PoCompanyRuntime();
        var backends = owner.AcquireBackend("postgres-and-redis", () => new SupplierBackends(owner), (value, cleanup) => value.ReleaseAsync(cleanup));
        var backendStartup = owner.RegisterBackend("backend-startup", _ => Task.CompletedTask);
        await owner.StartAsync(backendStartup, backends.StartAsync);
        var signingKey = owner.Acquire("signing-key", () => RSA.Create(2048), (value, _) => { value.Dispose(); return Task.CompletedTask; }, phase: 5);
        var protectionKey = owner.Acquire("protection-key", () => RSA.Create(2048), (value, _) => { value.Dispose(); return Task.CompletedTask; }, phase: 5);
        var certificate = owner.Acquire("certificate", () => new CertificateRequest("CN=disposable-po-proof", protectionKey,
            HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1).CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddHours(1)),
            (value, _) => { value.Dispose(); return Task.CompletedTask; }, phase: 5);
        var secret = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        result.procurementSecret = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        var liveKey = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        result.authorityCertificate = certificate.RawData;
        var certificatePassword = Convert.ToHexString(RandomNumberGenerator.GetBytes(24));
        result.ServiceToken = Token(signingKey, true, Permissions);
        result.MissingCompanyServiceToken = Token(signingKey, true, Permissions.Where(value => value != "legacy-catalog.companies.read").ToArray());
        result.MissingCreateServiceToken = Token(signingKey, true, Permissions.Where(value => value != "legacy-procurement.purchase-orders.create").ToArray());
        result.ForeignServiceToken = Token(signingKey, true, Permissions, "service:foreign-disposable-proof");
        var authority = await result.AuthorityAsync(owner, certificate, signingKey, secret, liveKey, result.ServiceToken,
            Token(signingKey, false, Permissions),
            Token(signingKey, false, Permissions.Where(value => value != "legacy-catalog.companies.read").ToArray()),
            Token(signingKey, false, Permissions.Where(value => value != "legacy-procurement.purchase-orders.create").ToArray()));
        result.Provider = await PoCompanyProvider.StartAsync(owner);
        result.ExternalFile = await PoOwnedFileFixtures.StartAsync(owner);
        var connections = await CreateDatabasesAsync(backends.Postgres.GetConnectionString(), token);
        result.OrderConnection = connections["po_order"];
        result.FileConnection = connections["po_file"];
        await result.SeedAsync(connections, token);
        var settings = new Dictionary<string, string?>
        {
            ["Jwt:Issuer"] = Issuer,
            ["Jwt:Audience"] = Audience,
            ["Jwt:PublicKey"] = Convert.ToBase64String(Encoding.UTF8.GetBytes(signingKey.ExportSubjectPublicKeyInfoPem())),
            ["ConnectionStrings:redis"] = backends.Redis.GetConnectionString(),
            ["ConnectionStrings:SupplierDbContext"] = connections["po_supplier"],
            ["ConnectionStrings:PurchaseOrderDbContext"] = connections["po_order"],
            ["ConnectionStrings:EmployeeDbContext"] = connections["po_employee"],
            ["ConnectionStrings:FileDbContext"] = connections["po_file"],
            ["ConnectionStrings:CountryDbContext"] = connections["po_country"],
            ["ConnectionStrings:CatalogDbContext"] = connections["po_catalog"],
            ["ConnectionStrings:CurrencyDbContext"] = connections["po_currency"],
            ["Services:Auth"] = authority.Urls.Single(),
            ["Services:IAM"] = authority.Urls.Single(),
            ["IAM:LivePermissionChecks:Credential"] = liveKey,
            ["Features:ResourceScopedAuthEnabled"] = "true",
            ["ServiceAuthentication:ClientId"] = "po-company-proof",
            ["ServiceAuthentication:ClientSecret"] = secret,
            ["DataProtection:CertificatePfxBase64"] = Convert.ToBase64String(certificate.Export(X509ContentType.Pfx, certificatePassword)),
            ["DataProtection:CertificatePassword"] = certificatePassword,
            ["FileStorage:Enabled"] = "true",
            ["FileStorage:WritesEnabled"] = "true",
            ["FileStorage:AllowedBuckets:0"] = "maliev.com",
            ["FileStorage:SignedUrlHours"] = "1",
            ["InstantQuoteFiles:Enabled"] = "false",
            ["InstantQuoteFiles:WritesEnabled"] = "false",
            ["InstantQuoteFiles:CleanupEnabled"] = "false",
            ["MalwareScanner:Host"] = "127.0.0.1",
            ["MalwareScanner:Port"] = result.ExternalFile.ScannerPort.ToString(CultureInfo.InvariantCulture),
            ["MalwareScanner:TimeoutSeconds"] = "10",
        };
        result.Catalog = await StartFactoryAsync<CatalogApi::Program>(owner, "catalog", settings, result, catalog: true);
        result.Procurement = await StartFactoryAsync<ProcurementApi::Program>(owner, "procurement", settings, result);
        var employee = await StartFactoryAsync<EmployeeApi::Program>(owner, "employee", settings, result);
        var document = await StartFactoryAsync<DocumentApi::Program>(owner, "document", settings, result, document: true);
        result.File = await StartFactoryAsync<FileApi::Program>(owner, "file", settings, result, file: true);
        settings["Services:Catalog"] = result.Catalog.BaseAddress!.AbsoluteUri;
        settings["Services:Procurement"] = result.Procurement.BaseAddress!.AbsoluteUri;
        settings["Services:Employee"] = employee.BaseAddress!.AbsoluteUri;
        settings["Services:Document"] = document.BaseAddress!.AbsoluteUri;
        settings["Services:File"] = result.File.BaseAddress!.AbsoluteUri;
        foreach (var service in new[] { "Customer", "Order" }) settings["Services:" + service] = authority.Urls.Single();
        var bff = await StartFactoryAsync<Bff::Program>(owner, "bff", settings, result, bff: true, certificate: certificate);
        result.Origin = bff.BaseAddress!.AbsoluteUri.TrimEnd('/');
        foreach (var client in new[] { result.Catalog, result.Procurement, result.File })
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", result.ServiceToken);
        return result;
    }

    private static async Task<Dictionary<string, string>> CreateDatabasesAsync(string original, CancellationToken token)
    {
        var result = new Dictionary<string, string>();
        await using var connection = new NpgsqlConnection(original);
        await connection.OpenAsync(token);
        foreach (var name in new[] { "po_supplier", "po_order", "po_employee", "po_file", "po_country", "po_catalog", "po_currency" })
        {
            await using var command = connection.CreateCommand();
            command.CommandText = $"CREATE DATABASE \"{name}\""; // Fixed synthetic names inside the two original owned backend IDs only.
            await command.ExecuteNonQueryAsync(token);
            result.Add(name, new NpgsqlConnectionStringBuilder(original) { Database = name }.ConnectionString);
        }
        return result;
    }

    private async Task SeedAsync(IReadOnlyDictionary<string, string> connections, CancellationToken token)
    {
        await using var supplier = new SupplierDbContext(new DbContextOptionsBuilder<SupplierDbContext>().UseNpgsql(connections["po_supplier"]).Options);
        await supplier.Database.EnsureCreatedAsync(token);
        var supplierAddress = new SupplierAddress { Address1 = "Synthetic supplier street", Address2 = "Synthetic supplier suite", CountryId = 66 };
        supplier.Addresses.Add(supplierAddress);
        await supplier.SaveChangesAsync(token);
        var row = new Supplier { Name = "Synthetic supplier", AddressId = supplierAddress.Id, Telephone = "020000041" };
        supplier.Suppliers.Add(row);
        await supplier.SaveChangesAsync(token);
        SupplierId = row.Id;
        await using var order = new PurchaseOrderDbContext(new DbContextOptionsBuilder<PurchaseOrderDbContext>().UseNpgsql(connections["po_order"]).Options);
        await order.Database.EnsureCreatedAsync(token);
        var shipping = new PurchaseOrderAddress { AddressLine1 = "Synthetic shipping street", AddressLine2 = "Synthetic shipping suite", Building = "Synthetic shipping building", City = "Bangkok", State = "Synthetic shipping state", PostalCode = "10110", CountryId = 66 };
        var billing = new PurchaseOrderAddress { AddressLine1 = "Synthetic billing street", AddressLine2 = "Synthetic billing suite", Building = "Synthetic billing building", City = "Nonthaburi", State = "Synthetic billing state", PostalCode = "11120", CountryId = 66 };
        order.Addresses.AddRange(shipping, billing);
        await order.SaveChangesAsync(token);
        ShippingAddressId = shipping.Id;
        BillingAddressId = billing.Id;
        await using var employee = new EmployeeDbContext(new DbContextOptionsBuilder<EmployeeDbContext>().UseNpgsql(connections["po_employee"]).Options);
        await employee.Database.EnsureCreatedAsync(token);
        var staff = new Employee { FirstName = "Synthetic", LastName = "Employee", Email = "po-fixture@example.invalid" };
        employee.Employees.Add(staff);
        await employee.SaveChangesAsync(token);
        EmployeeId = staff.Id;
        await using var file = new FileDbContext(new DbContextOptionsBuilder<FileDbContext>().UseNpgsql(connections["po_file"]).Options);
        await file.Database.EnsureCreatedAsync(token);
        await using var countries = new CatalogCountryDbContext(new DbContextOptionsBuilder<CatalogCountryDbContext>().UseNpgsql(connections["po_country"]).Options);
        await countries.Database.EnsureCreatedAsync(token);
        countries.Countries.Add(new Country { Id = 66, Name = "Thailand", Iso2 = "TH", Iso3 = "THA" });
        await countries.SaveChangesAsync(token);
        await using var catalog = new CatalogDbContext(new DbContextOptionsBuilder<CatalogDbContext>().UseNpgsql(connections["po_catalog"]).Options);
        await catalog.Database.EnsureCreatedAsync(token);
        await using var currencies = new CatalogCurrencyDbContext(new DbContextOptionsBuilder<CatalogCurrencyDbContext>().UseNpgsql(connections["po_currency"]).Options);
        await currencies.Database.EnsureCreatedAsync(token);
    }

    private static async Task<HttpClient> StartFactoryAsync<T>(SupplierResourceScope owner, string name,
        Dictionary<string, string?> settings, PoCompanyRuntime runtime, bool catalog = false, bool document = false,
        bool file = false, bool bff = false, X509Certificate2? certificate = null) where T : class
    {
        var factory = owner.Acquire(name + "-factory", () => new RuntimeFactory<T>(owner, settings, runtime, catalog, document, file, bff),
            (value, _) => value.DisposeAsync().AsTask(), phase: 3);
        if (bff) factory.UseKestrel(options => options.Listen(IPAddress.Loopback, 0, listen => listen.UseHttps(certificate!)));
        else factory.UseKestrel(0);
        var startup = owner.Register(name + "-startup", _ => Task.CompletedTask);
        HttpClient client = null!;
        owner.Register(name + "-http", _ => { client?.Dispose(); return Task.CompletedTask; });
        await owner.StartAsync(startup, token => Task.Run(() => client = factory.CreateClient(), token));
        client.Timeout = TimeSpan.FromSeconds(30);
        return client;
    }

    private static string Token(RSA key, bool service, string[] permissions, string? subject = null)
    {
        var claims = new List<Claim>
        {
            new("sub", subject ?? (service ? permissions.Contains("legacy-procurement.purchase-orders.create", StringComparer.Ordinal)
                ? permissions.Contains("legacy-catalog.companies.read", StringComparer.Ordinal) ? "service:po-company-proof" : "service:po-company-denied-company"
                : "service:po-company-denied-create" : "po-company-proof-employee")),
            new("identity_kind", service ? "service" : "employee"), new("name", "Synthetic PO proof"),
            new("email", "po-company-proof@maliev.com"), new("legacy_database_id", "7"),
            new("role", service ? "service-account" : "Employee"),
        };
        claims.AddRange(permissions.Select(value => new Claim("permissions", value)));
        return new JwtSecurityTokenHandler().WriteToken(new JwtSecurityToken(Issuer, Audience, claims,
            DateTime.UtcNow.AddMinutes(-1), DateTime.UtcNow.AddMinutes(15),
            new SigningCredentials(new RsaSecurityKey(key), SecurityAlgorithms.RsaSha256)));
    }

    private async Task<WebApplication> AuthorityAsync(SupplierResourceScope owner, X509Certificate2 certificate, RSA signingKey, string secret, string liveKey,
        string workload, string employee, string companyDenied, string createDenied)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.ConfigureKestrel(options => options.Listen(IPAddress.Loopback, 0, listen => listen.UseHttps(certificate)));
        var iamWorkload = Token(signingKey, true, ["iam.auth.check-permission"], "service:po-procurement-fixture");
        var app = owner.Acquire("authority-host", builder.Build, async (value, token) =>
        {
            await value.StopAsync(token);
            await value.DisposeAsync();
        }, phase: 4);
        var startup = owner.Register("authority-startup", _ => Task.CompletedTask);
        app.MapPost("/auth/v1/login", (EmployeeLoginRequest body) => body.Password == "synthetic-disposable"
            ? Results.Ok(new
            {
                accessToken = body.UserName switch
                {
                    "company-denied@maliev.com" => companyDenied,
                    "create-denied@maliev.com" => createDenied,
                    _ => employee,
                },
                refreshToken = Guid.NewGuid().ToString("N"), tokenType = "Bearer", expiresIn = 900,
                refreshExpiresAt = DateTimeOffset.UtcNow.AddHours(1),
            }) : Results.Unauthorized());
        app.MapPost("/auth/v1/service/login", (HttpContext context, JsonElement body) =>
        {
            if (context.Request.Headers.Authorization.Count != 0 || body.EnumerateObject().Select(value => value.Name).Order().SequenceEqual(new[] { "clientId", "clientSecret" }) is false)
                return Results.Unauthorized();
            var client = body.GetProperty("clientId").GetString();
            var supplied = body.GetProperty("clientSecret").GetString();
            if (client == "po-company-proof" && supplied == secret) return Results.Ok(new { accessToken = workload, expiresIn = 900 });
            if (client == "po-procurement-fixture" && supplied == procurementSecret) return Results.Ok(new { accessToken = iamWorkload, expiresIn = 900 });
            return Results.Unauthorized();
        });
        // Contract reused from ProcurementAuthenticatedIamTests.cs614a Authority174–309.
        // Real IamServiceClient/LegacyServiceAuthenticationHandler/HTTPS guard execute unchanged.
        app.MapPost("/iam/v1/auth/check-permission", async (HttpContext context, JsonElement body) =>
        {
            if (context.Request.Headers.Authorization.ToString() != "Bearer " + iamWorkload ||
                context.Request.Headers["X-Maliev-IAM-Live-Check-Key"].ToString() != liveKey ||
                !body.EnumerateObject().Select(value => value.Name).Order().SequenceEqual(new[] { "bypassCache", "permissionId", "principalId", "resourcePath" }))
                return Results.Unauthorized();
            var validated = new JwtSecurityTokenHandler().ValidateToken(iamWorkload, new TokenValidationParameters
            {
                ValidIssuer = Issuer,
                ValidAudience = Audience,
                IssuerSigningKey = new RsaSecurityKey(signingKey),
                ValidateIssuerSigningKey = true,
                ValidateLifetime = true,
                ClockSkew = TimeSpan.Zero,
            }, out _);
            if (validated.FindFirst("identity_kind")?.Value != "service" || !validated.HasClaim("permissions", "iam.auth.check-permission"))
                return Results.Unauthorized();
            var principal = body.GetProperty("principalId").GetString();
            var permission = body.GetProperty("permissionId").GetString();
            var resource = body.GetProperty("resourcePath").GetString();
            var bypass = body.GetProperty("bypassCache").GetBoolean();
            var allowed = principal == "service:po-company-proof" && permission is not null && Permissions.Contains(permission, StringComparer.Ordinal);
            var critical = permission is "legacy-procurement.purchase-orders.create" or "legacy-procurement.order-items.write" or "legacy-procurement.files.write";
            if (critical && !bypass) allowed = false;
            if (resource != "global")
            {
                var prefix = "/purchaseorders/";
                if (resource is null || !resource.StartsWith(prefix, StringComparison.Ordinal) || !int.TryParse(resource[prefix.Length..], out var id) || id <= 0)
                    allowed = false;
                else
                {
                    await using var db = new PurchaseOrderDbContext(new DbContextOptionsBuilder<PurchaseOrderDbContext>().UseNpgsql(OrderConnection).Options);
                    allowed &= await db.PurchaseOrders.AnyAsync(value => value.Id == id, context.RequestAborted);
                }
            }
            IamDecisions.Enqueue(new(permission ?? "", resource ?? "", bypass, allowed, principal == "service:po-company-proof"));
            return Results.Ok(new { principalId = principal, permissionId = permission, resourcePath = resource, allowed, fromCache = false, latencyMs = 0 });
        });
        await owner.StartAsync(startup, app.StartAsync);
        return app;
    }

    private sealed class RuntimeFactory<T>(SupplierResourceScope owner, Dictionary<string, string?> settings,
        PoCompanyRuntime runtime, bool catalog, bool document, bool file, bool bff) : WebApplicationFactory<T> where T : class
    {
        protected override IHost CreateHost(IHostBuilder builder)
        {
            var host = owner.Acquire(typeof(T).Assembly.GetName().Name! + "-actual-host", builder.Build, async (value, token) =>
            {
                await value.StopAsync(token);
                await Task.Run(value.Dispose);
            }, phase: 2);
            if (!bff)
            {
                var addresses = host.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()
                    ?? throw new InvalidOperationException("Actual Kestrel address feature absent.");
                addresses.Addresses.Clear();
                addresses.Addresses.Add("http://127.0.0.1:0");
                addresses.PreferHostingUrls = true;
            }
            host.Start();
            return host;
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment(bff ? "Development" : "Production");
            if (!bff)
            {
                var assembly = typeof(T).Assembly.GetName().Name!;
                builder.UseContentRoot(Path.Combine(Environment.GetEnvironmentVariable("MalievWorkspaceRoot")!, assembly[..assembly.LastIndexOf('.')], assembly));
            }
            if (catalog)
            {
                builder.UseSetting("Creden:Enabled", "true");
                builder.UseSetting("Creden:BaseUrl", "https://data.creden.co/");
                builder.UseSetting("Creden:AccessReviewReference", "Finite synthetic PO company protocol fixture only");
            }
            foreach (var setting in settings) builder.UseSetting(setting.Key, setting.Value);
            var procurement = typeof(T) == typeof(ProcurementApi::Program);
            if (procurement)
            {
                builder.UseSetting("ServiceAuthentication:ClientId", "po-procurement-fixture");
                builder.UseSetting("ServiceAuthentication:ClientSecret", runtime.procurementSecret);
            }
            builder.ConfigureLogging(logging => logging.ClearProviders());
            builder.ConfigureServices(services =>
            {
                if (!bff && !procurement)
                {
                    // Same disclosed non-live IAM signed-claim fallback as the accepted supplier bootstrap.
                    // Keep production JWT/RequirePermission handlers, including critical/live attributes, intact.
                    services.RemoveAll<IIamServiceClient>();
                    var iam = Substitute.For<IIamServiceClient>();
                    iam.CheckPermissionAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<CancellationToken>()).Returns(Task.FromResult(false));
                    services.AddScoped<IIamServiceClient>(_ => iam);
                }
                // Trust exactly this owner-generated certificate at the original bounded authority endpoint;
                // leave redirect policy and production clients/handlers intact. Never accept arbitrary TLS certificates.
                foreach (var name in procurement ? new[] { "LegacyAuthServiceTokenExchange", "IAMService" } : bff ? new[] { "service-auth" } : Array.Empty<string>())
                {
                    services.AddHttpClient(name).ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
                    {
                        AllowAutoRedirect = false,
                        ServerCertificateCustomValidationCallback = (request, remote, _, _) =>
                            request.RequestUri is { IsLoopback: true, Scheme: "https" } &&
                            request.RequestUri.Authority == new Uri(settings["Services:Auth"]!).Authority &&
                            (request.RequestUri.AbsolutePath is "/auth/v1/login" or "/auth/v1/service/login" or "/iam/v1/auth/check-permission") &&
                            remote is not null && remote.RawData.SequenceEqual(runtime.authorityCertificate),
                    });
                }
                if (bff) services.AddHttpClient<ILegacyAuthClient, LegacyAuthClient>().ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
                {
                    AllowAutoRedirect = false,
                    ServerCertificateCustomValidationCallback = (request, remote, _, _) =>
                        request.RequestUri is { IsLoopback: true, Scheme: "https", AbsolutePath: "/auth/v1/login" } &&
                        request.RequestUri.Authority == new Uri(settings["Services:Auth"]!).Authority &&
                        remote is not null && remote.RawData.SequenceEqual(runtime.authorityCertificate),
                });
                if (catalog)
                {
                    services.AddHttpClient("catalog-creden").ConfigurePrimaryHttpMessageHandler(() => runtime.Provider.Transport);
                    foreach (var descriptor in services.Where(value => value.ServiceType == typeof(IHostedService) && value.ImplementationType?.Name == "InstantQuotationCatalogStartupService").ToArray())
                        services.Remove(descriptor);
                }
                if (file)
                {
                    // External SDK seam only. Normal File controller/application/repository/journals,
                    // GoogleCloudObjectStorage and ClamAvFileSafetyScanner are not replaced.
                    services.RemoveAll<Google.Cloud.Storage.V1.StorageClient>();
                    services.RemoveAll<Google.Cloud.Storage.V1.UrlSigner>();
                    services.AddSingleton(runtime.ExternalFile.Client);
                    services.AddSingleton(runtime.ExternalFile.Signer);
                    services.AddSingleton(runtime.ExternalFile.SignedOrigin);
                }
                if (document) services.AddSingleton<IStartupFilter>(new DocumentWitnessFilter(runtime));
            });
        }
    }

    private sealed class DocumentWitnessFilter(PoCompanyRuntime runtime) : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            app.Use(async (context, proceed) =>
            {
                if (context.Request.Method == "POST" && context.Request.Path == "/Pdfs/purchaseorder")
                {
                    context.Request.EnableBuffering();
                    using var body = await JsonDocument.ParseAsync(context.Request.Body, cancellationToken: context.RequestAborted);
                    context.Request.Body.Position = 0;
                    var document = body.RootElement;
                    runtime.Documents.Enqueue(new(document.GetProperty("referenceNumber").GetInt32(),
                        document.GetProperty("shipping").GetProperty("companyName").GetString()!,
                        document.GetProperty("billing").GetProperty("companyName").GetString()!));
                }
                await proceed(context); // Original body and normal MVC/QuestPDF response remain untouched.
            });
            next(app);
        };
    }

    internal sealed record PoDocumentWitness(int ReferenceNumber, string ShippingName, string BillingName);
    internal sealed record PoIamWitness(string Permission, string Resource, bool BypassCache, bool Allowed, bool KnownPrincipal);
}
