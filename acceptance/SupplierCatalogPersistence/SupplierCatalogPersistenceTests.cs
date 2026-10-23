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
using Legacy.Maliev.Intranet.Auth;
using Legacy.Maliev.Intranet.Contracts;
using Legacy.Maliev.ProcurementService.Data;
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
using Microsoft.IdentityModel.Tokens;
using Microsoft.Playwright;
using NSubstitute;

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

    /// <summary>Proves the bounded supplier postcode selection through real storage and a full editor reload.</summary>
    [Fact]
    public async Task CatalogPostcodeSelection_RealSupplierSave_ApiReadbackAndReloadPreserveAddress()
    {
        var owner = new SupplierResourceScope(TimeSpan.FromMinutes(6));
        Exception? executionFailure = null;
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(owner.Token);
            timeout.CancelAfter(TimeSpan.FromMinutes(4));
            var backends = owner.AcquireBackend("postgres-and-redis", () => new SupplierBackends(owner), (value, token) => value.ReleaseAsync(token));
            var backendLease = owner.RegisterBackend("backend-startup", _ => Task.CompletedTask);
            await owner.StartAsync(backendLease, backends.StartAsync);
            var postgres = backends.Postgres;
            var redis = backends.Redis;
            var signingKey = owner.Acquire("signing-key", () => RSA.Create(2048), (key, _) => { key.Dispose(); return Task.CompletedTask; }, phase: 5);
            var protectionKey = owner.Acquire("protection-key", () => RSA.Create(2048), (key, _) => { key.Dispose(); return Task.CompletedTask; }, phase: 5);
            var certificate = owner.Acquire("certificate", () => new CertificateRequest("CN=disposable-supplier-proof", protectionKey,
                HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1).CreateSelfSigned(
                    DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddHours(1)),
                (value, _) => { value.Dispose(); return Task.CompletedTask; }, phase: 5);
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

            var authority = await AuthorityAsync(owner, employeeToken, readonlyEmployee, serviceToken, secret);
            var catalog = owner.Acquire("catalog-factory", () => new RuntimeFactory<CatalogApi::Program>(owner, settings, "Production", catalog: true), (value, _) => value.DisposeAsync().AsTask(), phase: 3);
            var catalogLease = owner.Register("catalog-startup", _ => Task.CompletedTask);
            catalog.UseKestrel(0);
            HttpClient catalogClient = null!;
            owner.Register("catalog-http", _ => { catalogClient?.Dispose(); return Task.CompletedTask; });
            await owner.StartAsync(catalogLease, token => Task.Run(() => catalogClient = catalog.CreateClient(), token));
            catalogClient.Timeout = TimeSpan.FromSeconds(20);
            var procurement = owner.Acquire("procurement-factory", () => new RuntimeFactory<ProcurementApi::Program>(owner, settings, "Production"), (value, _) => value.DisposeAsync().AsTask(), phase: 3);
            var procurementLease = owner.Register("procurement-startup", _ => Task.CompletedTask);
            procurement.UseKestrel(0);
            HttpClient procurementClient = null!;
            owner.Register("procurement-http", _ => { procurementClient?.Dispose(); return Task.CompletedTask; });
            await owner.StartAsync(procurementLease, token => Task.Run(() => procurementClient = procurement.CreateClient(), token));
            procurementClient.Timeout = TimeSpan.FromSeconds(20);
            settings["Services:Catalog"] = catalogClient.BaseAddress!.AbsoluteUri;
            settings["Services:Procurement"] = procurementClient.BaseAddress!.AbsoluteUri;
            settings["Services:Auth"] = authority.Urls.Single();
            foreach (var service in new[] { "Customer", "Employee", "Order" }) settings["Services:" + service] = authority.Urls.Single();
            settings["ServiceAuthentication:ClientId"] = "supplier-proof";
            settings["ServiceAuthentication:ClientSecret"] = secret;
            var bff = owner.Acquire("bff-factory", () => new RuntimeFactory<Bff::Program>(owner, settings, "Development", bff: true), (value, _) => value.DisposeAsync().AsTask(), phase: 3);
            var bffLease = owner.Register("bff-startup", _ => Task.CompletedTask);
            bff.UseKestrel(options => options.Listen(IPAddress.Loopback, 0, listen => listen.UseHttps(certificate)));
            HttpClient bffClient = null!;
            owner.Register("bff-http", _ => { bffClient?.Dispose(); return Task.CompletedTask; });
            await owner.StartAsync(bffLease, token => Task.Run(() => bffClient = bff.CreateClient(), token));
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

            IPlaywright? playwright = null;
            var driverLease = owner.Register("playwright-driver", _ => { playwright?.Dispose(); return Task.CompletedTask; }, phase: 1);
            await owner.StartAsync(driverLease, async _ => playwright = await Playwright.CreateAsync());
            IBrowser? browser = null;
            var browserLease = owner.Register("chromium", _ => browser?.DisposeAsync().AsTask() ?? Task.CompletedTask);
            await owner.StartAsync(browserLease, async _ => browser = await playwright!.Chromium.LaunchAsync(new() { Headless = true, Timeout = 30_000 }));
            IBrowserContext context = null!;
            var contextLease = owner.Register("browser-context", _ => context is null ? Task.CompletedTask : context.DisposeAsync().AsTask());
            await owner.StartAsync(contextLease, async _ => context = await browser!.NewContextAsync(new() { Locale = "en-US", IgnoreHTTPSErrors = true }));
            await SignInAsync(context, origin, "supplier-proof@maliev.com");
            var noCsrf = await context.APIRequest.PostAsync(origin + "/bff/suppliers", new()
            { DataObject = new { name = "Rejected synthetic supplier", address1 = "Rejected street", countryId = 66 } });
            Assert.Equal(400, noCsrf.Status);
            await using (var readOnly = await browser!.NewContextAsync(new() { IgnoreHTTPSErrors = true }))
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
        catch (Exception error) { executionFailure = error; }
        finally
        {
            try { await owner.DisposeAsync(); }
            catch (Exception cleanupFailure) when (executionFailure is not null)
            { throw new AggregateException("Supplier execution and exact owned cleanup failed.", executionFailure, cleanupFailure); }
        }
        if (executionFailure is not null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(executionFailure).Throw();
    }

    /// <summary>Joins the real consumer and pinned producer; Auth/IAM are exact synthetic test dependencies.</summary>
    [Fact]
    public async Task PurchaseOrderReplay_RealProducerRowsSurviveLostAcknowledgementAndLaterChildFailure()
    {
        const string actor = "service:supplier-proof";
        const string createPermission = "legacy-procurement.purchase-orders.create";
        const string itemPermission = "legacy-procurement.order-items.write";
        var owner = new SupplierResourceScope(TimeSpan.FromMinutes(6));
        Exception? executionFailure = null;
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(owner.Token);
            timeout.CancelAfter(TimeSpan.FromMinutes(4));
            var backends = owner.AcquireBackend("postgres-and-redis", () => new SupplierBackends(owner), (value, token) => value.ReleaseAsync(token));
            var backendLease = owner.RegisterBackend("backend-startup", _ => Task.CompletedTask);
            await owner.StartAsync(backendLease, backends.StartAsync);
            var signingKey = owner.Acquire("signing-key", () => RSA.Create(2048), (key, _) => { key.Dispose(); return Task.CompletedTask; }, phase: 5);
            var protectionKey = owner.Acquire("protection-key", () => RSA.Create(2048), (key, _) => { key.Dispose(); return Task.CompletedTask; }, phase: 5);
            var certificate = owner.Acquire("certificate", () => new CertificateRequest("CN=disposable-supplier-proof", protectionKey,
                HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1).CreateSelfSigned(
                    DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddHours(1)),
                (value, _) => { value.Dispose(); return Task.CompletedTask; }, phase: 5);
            var password = Convert.ToHexString(RandomNumberGenerator.GetBytes(24));
            var settings = new Dictionary<string, string?>
            {
                ["Jwt:Issuer"] = Issuer,
                ["Jwt:Audience"] = Audience,
                ["Jwt:PublicKey"] = Convert.ToBase64String(Encoding.UTF8.GetBytes(signingKey.ExportSubjectPublicKeyInfoPem())),
                ["ConnectionStrings:redis"] = backends.Redis.GetConnectionString(),
                ["ConnectionStrings:SupplierDbContext"] = backends.Postgres.GetConnectionString(),
                ["ConnectionStrings:PurchaseOrderDbContext"] = backends.Postgres.GetConnectionString(),
                ["Services:Auth"] = "https://127.0.0.1/",
                ["Services:IAM"] = "https://127.0.0.1/",
                ["DataProtection:CertificatePfxBase64"] = Convert.ToBase64String(certificate.Export(X509ContentType.Pfx, password)),
                ["DataProtection:CertificatePassword"] = password,
            };
            var options = new DbContextOptionsBuilder<PurchaseOrderDbContext>().UseNpgsql(backends.Postgres.GetConnectionString()).Options;
            int addressId;
            await using (var schema = new PurchaseOrderDbContext(options))
            {
                await schema.Database.EnsureCreatedAsync(timeout.Token); // This owner allocated a fresh disposable database.
                var address = new Legacy.Maliev.ProcurementService.Domain.PurchaseOrderAddress { AddressLine1 = "owned replay address" };
                schema.Addresses.Add(address);
                await schema.SaveChangesAsync(timeout.Token);
                addressId = address.Id;
            }

            // No authorization handler or production permission is replaced. Unmatched live checks deny.
            var iam = Substitute.For<IIamServiceClient>();
            var allowRoot = false;
            iam.CheckPermissionLiveAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<CancellationToken>()).Returns(Task.FromResult(false));
            iam.CheckPermissionLiveAsync(actor, createPermission, "global", Arg.Any<CancellationToken>()).Returns(_ => Task.FromResult(allowRoot));
            iam.CheckPermissionLiveAsync(actor, itemPermission, "global", Arg.Any<CancellationToken>()).Returns(Task.FromResult(true));
            var procurement = owner.Acquire("procurement-factory", () => new RuntimeFactory<ProcurementApi::Program>(owner, settings, "Production", liveIam: iam), (value, _) => value.DisposeAsync().AsTask(), phase: 3);
            var procurementLease = owner.Register("procurement-startup", _ => Task.CompletedTask);
            procurement.UseKestrel(0);
            HttpClient producer = null!;
            owner.Register("procurement-http", _ => { producer?.Dispose(); return Task.CompletedTask; });
            await owner.StartAsync(procurementLease, token => Task.Run(() => producer = procurement.CreateClient(), token));
            producer.Timeout = TimeSpan.FromSeconds(20);
            var request = new PurchaseOrderCreateRequest
            {
                SupplierId = 4,
                EmployeeId = 7,
                ShippingAddressId = addressId,
                BillingAddressId = addressId,
                Notes = " literal replay notes ",
                Items = [new() { PartNumber = " P-1 ", Description = "first", Quantity = 2, UnitPrice = 3m },
                    new() { Description = "second", Quantity = 1, UnitPrice = 4m }],
            };

            producer.DefaultRequestHeaders.Authorization = new("Bearer", Token(signingKey, true, []));
            using (var denied = await producer.PostAsJsonAsync("/PurchaseOrders", new { request.SupplierId, request.EmployeeId, request.Notes }, timeout.Token))
                Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
            allowRoot = true;
            producer.DefaultRequestHeaders.Authorization = new("Bearer", Token(signingKey, false, []));
            using (var wrongActor = await producer.PostAsJsonAsync("/PurchaseOrders", new { request.SupplierId, request.EmployeeId, request.Notes }, timeout.Token))
                Assert.Equal(HttpStatusCode.Forbidden, wrongActor.StatusCode);
            await using (var deniedReadback = new PurchaseOrderDbContext(options))
            {
                Assert.Empty(await deniedReadback.PurchaseOrders.AsNoTracking().ToListAsync(timeout.Token));
                Assert.Empty(await deniedReadback.OrderItems.AsNoTracking().ToListAsync(timeout.Token));
            }
            producer.DefaultRequestHeaders.Authorization = new("Bearer", Token(signingKey, true, []));

            int foreignOrderId;
            int foreignItemId;
            await using (var seed = new PurchaseOrderDbContext(options))
            {
                var foreign = new Legacy.Maliev.ProcurementService.Domain.PurchaseOrder { Notes = "foreign invocation" };
                seed.PurchaseOrders.Add(foreign);
                await seed.SaveChangesAsync(timeout.Token);
                var foreignItem = new Legacy.Maliev.ProcurementService.Domain.OrderItem { PurchaseOrderId = foreign.Id, Description = "foreign child", Quantity = 1, UnitPrice = 8m };
                seed.OrderItems.Add(foreignItem);
                await seed.SaveChangesAsync(timeout.Token);
                foreignOrderId = foreign.Id;
                foreignItemId = foreignItem.Id;
            }

            var transport = new PurchaseOrderJoinTransport(producer);
            var consumerHttp = owner.Acquire("purchase-order-consumer-http", () => new HttpClient(transport)
            { BaseAddress = producer.BaseAddress, Timeout = TimeSpan.FromSeconds(30) }, (value, _) => { value.Dispose(); return Task.CompletedTask; });
            var service = new Legacy.Maliev.Intranet.PurchaseOrders.PurchaseOrderCreationService(
                new Bff::Legacy.Maliev.Intranet.Bff.Procurement.PurchaseOrderCreationGateway(new PurchaseOrderJoinClients(consumerHttp)),
                Microsoft.Extensions.Logging.Abstractions.NullLogger<Legacy.Maliev.Intranet.PurchaseOrders.PurchaseOrderCreationService>.Instance);
            var attempt = Guid.NewGuid().ToString("D");
            var first = await service.CreateAsync(request, attempt, timeout.Token);
            Assert.Equal(Legacy.Maliev.Intranet.PurchaseOrders.PurchaseOrderCreationStatus.OutcomeUnknown, first.Status);
            Assert.Equal(2, transport.ItemIds.Count);
            var originalIds = transport.ItemIds.ToArray();
            await using (var committed = new PurchaseOrderDbContext(options))
            {
                Assert.Equal(2, await committed.PurchaseOrders.CountAsync(timeout.Token));
                Assert.Equal(originalIds.Order(), await committed.OrderItems.Where(value => value.PurchaseOrderId == transport.RootIds[0]).OrderBy(value => value.Id).Select(value => value.Id).ToArrayAsync(timeout.Token));
            }

            var second = await service.CreateAsync(request, attempt, timeout.Token);
            Assert.Equal(Legacy.Maliev.Intranet.PurchaseOrders.PurchaseOrderCreationStatus.OutcomeUnknown, second.Status);
            Assert.Equal(0, transport.Deletes);
            Assert.Equal(2, transport.RootIds.Count);
            Assert.Equal(transport.RootIds[0], transport.RootIds[1]);
            Assert.NotEqual(foreignOrderId, transport.RootIds[0]);
            // The pinned producer ignores child idempotency keys: the repeated first child is a NEW row.
            Assert.Equal(3, transport.ItemIds.Distinct().Count());
            Assert.Equal(6, transport.Keys.Count);
            Assert.Equal(attempt, transport.Keys[0]);
            Assert.Equal(attempt, transport.Keys[3]);
            for (var index = 0; index < 3; index++)
            {
                Assert.Equal(transport.Keys[index], transport.Keys[index + 3]);
                Assert.Equal(transport.Bodies[index], transport.Bodies[index + 3]);
            }
            Assert.NotEqual(transport.Keys[1], transport.Keys[2]);
            Assert.Contains(" literal replay notes ", transport.Bodies[0], StringComparison.Ordinal);
            Assert.Contains(" P-1 ", transport.Bodies[1], StringComparison.Ordinal);
            await using (var readback = new PurchaseOrderDbContext(options))
            {
                Assert.Equal(2, await readback.PurchaseOrders.CountAsync(timeout.Token));
                var root = await readback.PurchaseOrders.AsNoTracking().SingleAsync(value => value.Id == transport.RootIds[0], timeout.Token);
                Assert.Equal(request.Notes, root.Notes);
                Assert.Equal(addressId, root.ShippingAddressId);
                Assert.Equal(addressId, root.BillingAddressId);
                Assert.Equal("owned replay address", (await readback.Addresses.AsNoTracking().SingleAsync(timeout.Token)).AddressLine1);
                Assert.NotNull(root.CreatedDate);
                var children = await readback.OrderItems.AsNoTracking().Where(value => value.PurchaseOrderId == root.Id).OrderBy(value => value.Id).ToListAsync(timeout.Token);
                Assert.Equal(transport.ItemIds.Order(), children.Select(value => value.Id));
                Assert.All(originalIds, id => Assert.Contains(children, value => value.Id == id));
                Assert.Equal(2, children.Count(value => value.PartNumber == " P-1 " && value.Description == "first" && value.Quantity == 2 && value.UnitPrice == 3m && value.Subtotal == 6m));
                Assert.Single(children, value => value.Description == "second" && value.Quantity == 1 && value.UnitPrice == 4m && value.Subtotal == 4m);
                Assert.Equal("foreign invocation", (await readback.PurchaseOrders.AsNoTracking().SingleAsync(value => value.Id == foreignOrderId, timeout.Token)).Notes);
                var foreignItem = await readback.OrderItems.AsNoTracking().SingleAsync(value => value.Id == foreignItemId, timeout.Token);
                Assert.Equal(foreignOrderId, foreignItem.PurchaseOrderId);
                Assert.Equal("foreign child", foreignItem.Description);
                Assert.Equal(4, await readback.OrderItems.CountAsync(timeout.Token));
            }
            var checks = iam.ReceivedCalls().ToArray();
            Assert.Equal(7, checks.Length);
            Assert.All(checks, call =>
            {
                Assert.Equal(nameof(IIamServiceClient.CheckPermissionLiveAsync), call.GetMethodInfo().Name);
                var arguments = call.GetArguments();
                Assert.Equal("global", arguments[2]);
                Assert.True((Equals(arguments[0], actor) && (Equals(arguments[1], createPermission) || Equals(arguments[1], itemPermission))) ||
                    (Equals(arguments[0], "supplier-proof-employee") && Equals(arguments[1], createPermission)));
            });
            Assert.Equal(3, checks.Count(call => Equals(call.GetArguments()[0], actor) && Equals(call.GetArguments()[1], createPermission)));
            Assert.Equal(3, checks.Count(call => Equals(call.GetArguments()[1], itemPermission)));
        }
        catch (Exception error) { executionFailure = error; }
        finally
        {
            try { await owner.DisposeAsync(); }
            catch (Exception cleanupFailure) when (executionFailure is not null)
            { throw new AggregateException("Purchase-order join and exact owned cleanup failed.", executionFailure, cleanupFailure); }
        }
        if (executionFailure is not null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(executionFailure).Throw();
    }

    private sealed class PurchaseOrderJoinClients(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name)
        {
            Assert.Equal(Bff::Legacy.Maliev.Intranet.Bff.Procurement.PurchaseOrderCreationGateway.ProcurementClient, name);
            return client;
        }
    }

    private sealed class PurchaseOrderJoinTransport(HttpClient producer) : HttpMessageHandler
    {
        internal List<string> Keys { get; } = [];
        internal List<string> Bodies { get; } = [];
        internal List<int> RootIds { get; } = [];
        internal List<int> ItemIds { get; } = [];
        internal int Deletes { get; private set; }
        private int itemCalls;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.Method == HttpMethod.Delete)
            {
                Deletes++;
                throw new InvalidOperationException("Automatic compensation attempted a forbidden DELETE.");
            }
            Assert.Equal(HttpMethod.Post, request.Method);
            var path = request.RequestUri!.AbsolutePath;
            Assert.Contains(path, new[] { "/PurchaseOrders", "/purchaseorders/orderitems" });
            Keys.Add(Assert.Single(request.Headers.GetValues("Idempotency-Key")));
            Bodies.Add(await request.Content!.ReadAsStringAsync(cancellationToken));
            if (path == "/purchaseorders/orderitems" && ++itemCalls == 4)
                return new(HttpStatusCode.ServiceUnavailable); // A later child is rejected before dispatch.
            using var forwarded = new HttpRequestMessage(request.Method, new Uri(producer.BaseAddress!, path))
            { Content = new StringContent(Bodies[^1], Encoding.UTF8, "application/json") };
            forwarded.Headers.Add("Idempotency-Key", Keys[^1]);
            var response = await producer.SendAsync(forwarded, cancellationToken);
            try
            {
                Assert.Equal(HttpStatusCode.Created, response.StatusCode);
                using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
                var id = body.RootElement.GetProperty("Id").GetInt32();
                Assert.True(id > 0);
                if (path == "/PurchaseOrders") RootIds.Add(id);
                else ItemIds.Add(id);
                if (path == "/purchaseorders/orderitems" && itemCalls == 2)
                    throw new HttpRequestException("Synthetic acknowledgement loss after the actual producer committed its child.");
                return response;
            }
            catch { response.Dispose(); throw; }
        }
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

    private static async Task<WebApplication> AuthorityAsync(SupplierResourceScope owner, string employee, string readOnly, string workload, string secret)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        var app = owner.Acquire("authority-host", builder.Build, async (value, token) =>
        {
            await value.StopAsync(token);
            await value.DisposeAsync();
        }, phase: 4);
        var lease = owner.Register("authority-startup", _ => Task.CompletedTask);
        app.MapPost("/auth/v1/login", (EmployeeLoginRequest body) => body.Password == "synthetic-disposable"
            ? Results.Ok(new
            {
                accessToken = body.UserName == "readonly-proof@maliev.com" ? readOnly : employee,
                refreshToken = Guid.NewGuid().ToString("N"),
                tokenType = "Bearer",
                expiresIn = 900,
                refreshExpiresAt = DateTimeOffset.UtcNow.AddHours(1)
            })
            : Results.Unauthorized());
        app.MapPost("/auth/v1/service/login", (JsonElement body) => body.GetProperty("clientId").GetString() == "supplier-proof" && body.GetProperty("clientSecret").GetString() == secret
            ? Results.Ok(new { accessToken = workload, expiresIn = 900 }) : Results.Unauthorized());
        await owner.StartAsync(lease, app.StartAsync);
        return app;
    }

    private sealed class RuntimeFactory<T>(SupplierResourceScope owner, Dictionary<string, string?> settings, string environment, bool catalog = false, bool bff = false, IIamServiceClient? liveIam = null)
        : WebApplicationFactory<T> where T : class
    {
        protected override IHost CreateHost(IHostBuilder builder)
        {
            // Register the actual host before Start; a factory's disposed flag is not exit proof.
            var host = owner.Acquire(typeof(T).Assembly.GetName().Name! + "-actual-host", builder.Build, async (value, token) =>
            {
                await value.StopAsync(token);
                await Task.Run(value.Dispose);
            }, phase: 2);
            if (!bff)
            {
                // Match WebApplicationFactory.CreateHost port setup, which this ownership override replaces.
                var addresses = host.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()
                    ?? throw new InvalidOperationException("Kestrel address feature unavailable.");
                addresses.Addresses.Clear();
                addresses.Addresses.Add("http://127.0.0.1:0");
                addresses.PreferHostingUrls = true;
            }
            host.Start(); // The outer registered startup task bounds/retains this synchronous call.
            return host;
        }

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
                    services.AddScoped<IIamServiceClient>(_ => liveIam ?? iam);
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
