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
public sealed class SupplierCompanyCatalogPersistenceTests
{
    private static readonly string[] Permissions =
    [
        "legacy-catalog.locations.read", "legacy-catalog.companies.read", "legacy-procurement.suppliers.create",
        "legacy-procurement.suppliers.update", "legacy-procurement.suppliers.read", "legacy-procurement.supplier-addresses.write",
        "legacy-procurement.supplier-addresses.read",
    ];
    private const string Issuer = "https://disposable-supplier-authority.test";
    private const string Audience = "disposable-supplier-acceptance";

    /// <summary>Proves existing supplier Catalog reselection through the ordinary update, API readback and reload.</summary>
    [Fact]
    public async Task ExistingSupplier_RealCompanySelection_OrdinaryUpdateReadbackAndReload()
    {
        var owner = new SupplierResourceScope(TimeSpan.FromMinutes(6));
        Exception? executionFailure = null;
        SupplierCompanyBrowserObserver? bodyObserver = null;
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
            var readonlyEmployee = Token(signingKey, service: false,
                Permissions.Where(permission => permission != "legacy-catalog.companies.read").ToArray());
            var updateDeniedEmployee = Token(signingKey, service: false,
                Permissions.Where(permission => permission != "legacy-procurement.suppliers.update").ToArray());
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

            var authority = await AuthorityAsync(owner, employeeToken, readonlyEmployee, updateDeniedEmployee, serviceToken, secret);
            var provider = await SupplierSyntheticProvider.StartAsync(owner);
            var catalog = owner.Acquire("catalog-factory", () => new RuntimeFactory<CatalogApi::Program>(owner, settings, "Production", catalog: true, transport: provider.Transport), (value, _) => value.DisposeAsync().AsTask(), phase: 3);
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
            Assert.Empty(provider.Observations);
            var deniedLookupStatus = 0;
            await using (var readOnly = await browser!.NewContextAsync(new() { IgnoreHTTPSErrors = true }))
            {
                await SignInAsync(readOnly, origin, "readonly-proof@maliev.com");
                var deniedLookup = await readOnly.APIRequest.GetAsync(origin + "/bff/lookups/companies/search?q=Synthetic%20supplier%20company&queryType=name&language=en&limit=20");
                Assert.Equal(403, deniedLookup.Status);
                deniedLookupStatus = deniedLookup.Status;
                Assert.Empty(provider.Observations);
            }
            catalogClient.DefaultRequestHeaders.Authorization = new("Bearer", Token(signingKey, true,
                Permissions.Where(permission => permission != "legacy-catalog.companies.read").ToArray()));
            using var deniedCatalogCompany = await catalogClient.GetAsync("/api/v1/companies/search?q=Synthetic%20supplier%20company&queryType=name&language=en&limit=20");
            Assert.Equal(HttpStatusCode.Forbidden, deniedCatalogCompany.StatusCode);
            Assert.Empty(provider.Observations);
            catalogClient.DefaultRequestHeaders.Authorization = new("Bearer", serviceToken);
            var deniedLookupPhysicalAttempts = provider.Observations.Count;
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
            var addressId = persisted.GetProperty("Id").GetInt32();
            var rejectedUpdateBody = new
            {
                name = "Rejected synthetic update",
                address1 = "Rejected replacement",
                countryId = 66,
            };
            var noEditCsrf = await context.APIRequest.PutAsync(origin + $"/bff/suppliers/{id}", new()
            { DataObject = rejectedUpdateBody });
            Assert.Equal(400, noEditCsrf.Status);
            var employeeUpdateDeniedStatus = 0;
            await using (var readOnlyEdit = await browser!.NewContextAsync(new() { IgnoreHTTPSErrors = true }))
            {
                await SignInAsync(readOnlyEdit, origin, "update-denied-proof@maliev.com");
                using var session = JsonDocument.Parse(await (await readOnlyEdit.APIRequest.GetAsync(origin + "/bff/session")).TextAsync());
                var deniedUpdate = await readOnlyEdit.APIRequest.PutAsync(origin + $"/bff/suppliers/{id}", new()
                {
                    Headers = new Dictionary<string, string> { ["X-CSRF-TOKEN"] = session.RootElement.GetProperty("csrfToken").GetString()! },
                    DataObject = rejectedUpdateBody,
                });
                Assert.Equal(403, deniedUpdate.Status);
                employeeUpdateDeniedStatus = deniedUpdate.Status;
            }
            procurementClient.DefaultRequestHeaders.Authorization = new("Bearer", Token(signingKey, true,
                Permissions.Where(permission => permission != "legacy-procurement.suppliers.update").ToArray()));
            using var deniedDomainUpdate = await procurementClient.PutAsJsonAsync($"/Suppliers/{id}", rejectedUpdateBody);
            Assert.Equal(HttpStatusCode.Forbidden, deniedDomainUpdate.StatusCode);
            procurementClient.DefaultRequestHeaders.Authorization = new("Bearer", serviceToken);
            using var unchangedRead = await procurementClient.GetAsync($"/suppliers/{id}/addresses");
            unchangedRead.EnsureSuccessStatusCode();
            var unchanged = await unchangedRead.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal(addressId, unchanged.GetProperty("Id").GetInt32());
            Assert.Equal("Synthetic manual street", unchanged.GetProperty("Address1").GetString());
            Assert.Equal("Synthetic floor 2", unchanged.GetProperty("Address2").GetString());
            Assert.Equal(66, unchanged.GetProperty("CountryId").GetInt32());
            Assert.Equal(city, unchanged.GetProperty("City").GetString());
            Assert.Equal(state, unchanged.GetProperty("State").GetString());
            Assert.Equal("10110", unchanged.GetProperty("PostalCode").GetString());
            using var unchangedProfileRead = await procurementClient.GetAsync($"/suppliers/{id}");
            unchangedProfileRead.EnsureSuccessStatusCode();
            var unchangedProfile = await unchangedProfileRead.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal(id, unchangedProfile.GetProperty("Id").GetInt32());
            Assert.Equal(addressId, unchangedProfile.GetProperty("AddressId").GetInt32());
            Assert.Equal("Synthetic Catalog persistence supplier", unchangedProfile.GetProperty("Name").GetString());
            Assert.Equal("0123456789012", unchangedProfile.GetProperty("TaxNumber").GetString());

            bodyObserver = new SupplierCompanyBrowserObserver(page, origin + "/bff/lookups/companies/search?q=Synthetic%20supplier%20company&queryType=name&language=en&limit=20");
            var observerReceiptWritten = false;
            var observerLease = owner.Register("company-browser-response-observer", async token =>
            {
                await bodyObserver.DisposeAsync(token);
                if (!observerReceiptWritten)
                {
                    await owner.WriteReceiptAsync("browser-observer.jsonl", bodyObserver.ReleaseReceipt(), token);
                    observerReceiptWritten = true;
                }
            });
            await owner.StartAsync(observerLease, bodyObserver.InstallAsync);
            var editLookupResponse = await page.RunAndWaitForResponseAsync(
                () => page.Locator("#supplier-edit-company-lookup").FillAsync("Synthetic supplier company"),
                response => response.Url.Contains("/bff/lookups/companies/search", StringComparison.Ordinal));
            Assert.Equal(200, editLookupResponse.Status);
            var bodyObservation = await bodyObserver.ReadAndCloseAsync(timeout.Token);
            using var observedCompany = JsonDocument.Parse(bodyObservation.GetProperty("body").GetString()!);
            var actualCompanyPage = observedCompany.RootElement;
            Assert.Equal("matches", actualCompanyPage.GetProperty("outcome").GetString());
            Assert.Equal("creden", actualCompanyPage.GetProperty("provider").GetString());
            Assert.Equal("suggestion", actualCompanyPage.GetProperty("capability").GetString());
            var actualCompany = Assert.Single(actualCompanyPage.GetProperty("items").EnumerateArray());
            Assert.Equal("Synthetic Company Limited", actualCompany.GetProperty("nameEn").GetString());
            Assert.Equal("1234567890123", actualCompany.GetProperty("taxId").GetString());
            var editResults = page.Locator("#supplier-edit-company-lookup-results");
            await Assertions.Expect(editResults).ToBeEnabledAsync();
            await editResults.FocusAsync();
            await editResults.PressAsync("ArrowDown");
            await editResults.PressAsync("Enter");
            await Assertions.Expect(page.Locator("#supplier-edit-name")).ToHaveValueAsync("Synthetic Company Limited");
            await Assertions.Expect(page.Locator("#supplier-edit-tax-number")).ToHaveValueAsync("1234567890123");
            await Assertions.Expect(page.Locator("#supplier-edit-address-1")).ToHaveValueAsync("Synthetic manual street");
            await Assertions.Expect(page.Locator("#supplier-edit-address-2")).ToHaveValueAsync("Synthetic floor 2");
            await Assertions.Expect(page.Locator("#supplier-edit-country-id")).ToHaveValueAsync("66");
            await Assertions.Expect(page.Locator("#supplier-edit-city")).ToHaveValueAsync(city);
            await Assertions.Expect(page.Locator("#supplier-edit-state")).ToHaveValueAsync(state);
            await Assertions.Expect(page.Locator("#supplier-edit-postal-code")).ToHaveValueAsync("10110");
            Assert.Single(provider.Observations);
            var updated = await page.RunAndWaitForResponseAsync(
                () => page.Locator("button[type='submit']").ClickAsync(),
                response => response.Request.Method == "PUT" && response.Url.EndsWith($"/bff/suppliers/{id}", StringComparison.Ordinal));
            Assert.Equal(204, updated.Status);
            using var editedAddressRead = await procurementClient.GetAsync($"/suppliers/{id}/addresses");
            editedAddressRead.EnsureSuccessStatusCode();
            var editedAddress = await editedAddressRead.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal(addressId, editedAddress.GetProperty("Id").GetInt32());
            Assert.Equal("Synthetic manual street", editedAddress.GetProperty("Address1").GetString());
            Assert.Equal("Synthetic floor 2", editedAddress.GetProperty("Address2").GetString());
            Assert.Equal(66, editedAddress.GetProperty("CountryId").GetInt32());
            Assert.Equal(city, editedAddress.GetProperty("City").GetString());
            Assert.Equal(state, editedAddress.GetProperty("State").GetString());
            Assert.Equal("10110", editedAddress.GetProperty("PostalCode").GetString());
            using var editedProfileRead = await procurementClient.GetAsync($"/suppliers/{id}");
            editedProfileRead.EnsureSuccessStatusCode();
            var editedProfile = await editedProfileRead.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal(id, editedProfile.GetProperty("Id").GetInt32());
            Assert.Equal(addressId, editedProfile.GetProperty("AddressId").GetInt32());
            Assert.Equal("Synthetic Company Limited", editedProfile.GetProperty("Name").GetString());
            Assert.Equal("1234567890123", editedProfile.GetProperty("TaxNumber").GetString());
            await page.ReloadAsync();
            await Assertions.Expect(page.Locator("#supplier-edit-name")).ToHaveValueAsync("Synthetic Company Limited");
            await Assertions.Expect(page.Locator("#supplier-edit-address-1")).ToHaveValueAsync("Synthetic manual street");
            await Assertions.Expect(page.Locator("#supplier-edit-address-2")).ToHaveValueAsync("Synthetic floor 2");
            await Assertions.Expect(page.Locator("#supplier-edit-country-id")).ToHaveValueAsync("66");
            await Assertions.Expect(page.Locator("#supplier-edit-city")).ToHaveValueAsync(city);
            await Assertions.Expect(page.Locator("#supplier-edit-state")).ToHaveValueAsync(state);
            await Assertions.Expect(page.Locator("#supplier-edit-postal-code")).ToHaveValueAsync("10110");
            await Assertions.Expect(page.Locator("#supplier-edit-tax-number")).ToHaveValueAsync("1234567890123");
            await Assertions.Expect(page.Locator("#blazor-error-ui")).ToBeHiddenAsync();
            await using var readback = new SupplierDbContext(new DbContextOptionsBuilder<SupplierDbContext>()
                .UseNpgsql(postgres.GetConnectionString()).Options);
            Assert.Single(await readback.Suppliers.AsNoTracking().ToListAsync(timeout.Token));
            Assert.Single(await readback.Addresses.AsNoTracking().ToListAsync(timeout.Token));
            var evidenceDirectory = Environment.GetEnvironmentVariable("SUPPLIER_RESOURCE_EVIDENCE")
                ?? throw new InvalidOperationException("Hosted EDIT evidence directory required.");
            Directory.CreateDirectory(evidenceDirectory);
            await File.WriteAllTextAsync(Path.Combine(evidenceDirectory, "company-journey.json"), JsonSerializer.Serialize(new
            {
                schema = 1,
                owner = owner.Id.ToString("N"),
                runId = Environment.GetEnvironmentVariable("GITHUB_RUN_ID"),
                runAttempt = Environment.GetEnvironmentVariable("GITHUB_RUN_ATTEMPT"),
                supplierId = id,
                originalAddressId = addressId,
                persistedSupplierId = editedProfile.GetProperty("Id").GetInt32(),
                persistedAddressId = editedAddress.GetProperty("Id").GetInt32(),
                profileAddressId = editedProfile.GetProperty("AddressId").GetInt32(),
                selectedName = "Synthetic Company Limited",
                selectedTaxNumber = "1234567890123",
                persistedName = editedProfile.GetProperty("Name").GetString(),
                persistedTaxNumber = editedProfile.GetProperty("TaxNumber").GetString(),
                catalogName = actualCompany.GetProperty("nameEn").GetString(),
                catalogTaxNumber = actualCompany.GetProperty("taxId").GetString(),
                catalogOutcome = actualCompanyPage.GetProperty("outcome").GetString(),
                catalogProvider = actualCompanyPage.GetProperty("provider").GetString(),
                catalogCapability = actualCompanyPage.GetProperty("capability").GetString(),
                browserBodyObservation = bodyObservation.GetProperty("receipt"),
                lookupStatus = editLookupResponse.Status,
                updateStatus = updated.Status,
                addressReadStatus = (int)editedAddressRead.StatusCode,
                profileReadStatus = (int)editedProfileRead.StatusCode,
                csrfDeniedStatus = noEditCsrf.Status,
                employeeUpdateDeniedStatus,
                workloadUpdateDeniedStatus = (int)deniedDomainUpdate.StatusCode,
                employeeLookupDeniedStatus = deniedLookupStatus,
                workloadLookupDeniedStatus = (int)deniedCatalogCompany.StatusCode,
                deniedLookupPhysicalAttempts,
                observations = provider.Observations.ToArray(),
                manualAddressPreserved = true,
                countryPreserved = true,
                addressTuplePreserved = true,
                deniedUpdatePreservedOriginal = true,
                reloadMatched = true,
                singleSupplierAndAddress = true,
            }), timeout.Token);

        }
        catch (Exception error) { executionFailure = error; }
        finally
        {
            try { await owner.DisposeAsync(); }
            catch (Exception firstCleanup) when (executionFailure is not null && bodyObserver?.RequiresRealmRecovery == true)
            {
                // Same phase attempted context/browser shutdown after the bounded observer join failed.
                // Retry only after the exact retained Evaluate task settled and its owned page closed.
                try
                {
                    await bodyObserver.AwaitExactEvaluationAfterRealmClosureAsync();
                    if (!bodyObserver.CanRetryAfterRealmClosure) throw new InvalidOperationException("Exact observer evaluation has not settled for retry.");
                    await owner.DisposeAsync();
                }
                catch (Exception retryCleanup)
                { throw new AggregateException("Supplier execution and settled-realm cleanup retry failed.", executionFailure, firstCleanup, retryCleanup); }
            }
            catch (Exception cleanupFailure) when (executionFailure is not null)
            { throw new AggregateException("Supplier execution and exact owned cleanup failed.", executionFailure, cleanupFailure); }
        }
        if (executionFailure is not null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(executionFailure).Throw();
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

    private static async Task<WebApplication> AuthorityAsync(SupplierResourceScope owner, string employee, string readOnly, string updateDenied, string workload, string secret)
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
                accessToken = body.UserName switch
                {
                    "readonly-proof@maliev.com" => readOnly,
                    "update-denied-proof@maliev.com" => updateDenied,
                    _ => employee,
                },
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

    private sealed class RuntimeFactory<T>(SupplierResourceScope owner, Dictionary<string, string?> settings, string environment, bool catalog = false, bool bff = false, OwnedSyntheticTransport? transport = null)
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
            if (catalog)
            {
                builder.UseSetting("Creden:Enabled", "true");
                builder.UseSetting("Creden:BaseUrl", "https://data.creden.co/");
                builder.UseSetting("Creden:AccessReviewReference", "Synthetic disposable acceptance transport only");
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
                    services.AddHttpClient("catalog-creden").ConfigurePrimaryHttpMessageHandler(() => transport
                        ?? throw new InvalidOperationException("Owned synthetic transport required."));
                    // This lane reads the pinned local dataset. Catalog material seeding is out of scope.
                    foreach (var descriptor in services.Where(descriptor => descriptor.ServiceType == typeof(IHostedService) &&
                        descriptor.ImplementationType?.Name == "InstantQuotationCatalogStartupService").ToArray()) services.Remove(descriptor);
                }
            });
        }
    }
}

/// <summary>Observes a clone of the actual browser fetch; never replaces its promise, response or wire request.</summary>
internal sealed class SupplierCompanyBrowserObserver(IPage page, string url, bool heldInstallationControl = false)
{
    private readonly string id = Guid.NewGuid().ToString("N");
    private bool closed;
    private bool realmClosed;
    private Task? installing;
    private Task<string>? closing;
    private JsonElement? joinedReceipt;
    internal bool InstallationEvaluationSettled => installing?.IsCompleted == true;
    internal bool ClosingWasDispatched => closing is not null;
    internal bool CanRetryAfterRealmClosure => !closed && page.IsClosed && installing is { IsCompleted: true } && (closing is null || closing.IsCompleted);
    internal bool RequiresRealmRecovery => !closed && page.IsClosed && installing is not null;

    internal async Task AwaitExactEvaluationAfterRealmClosureAsync()
    {
        if (!page.IsClosed || installing is null) throw new InvalidOperationException("Owned realm must close before evaluation recovery.");
        var exact = closing is null ? installing : Task.WhenAll(installing, closing);
        try { await exact.WaitAsync(TimeSpan.FromSeconds(2)); }
        catch (Exception) when (exact.IsCompleted) { _ = exact.Exception; }
    }

    internal Task InstallAsync(CancellationToken token)
    {
        var expression = heldInstallationControl
            ? "async options => { await window.__supplierObserverInstallationControlGate; return (" + InstallScript + ")(options); }"
            : InstallScript;
        installing ??= page.EvaluateAsync(expression, new { url, id, maxBytes = 65536, deadlineMs = 5000 });
        return installing.WaitAsync(token);
    }

    internal async Task<JsonElement> ReadAndCloseAsync(CancellationToken token)
    {
        var value = await CloseAsync(token);
        var receipt = value.GetProperty("receipt");
        Assert.Equal(1, receipt.GetProperty("matchingRequests").GetInt32());
        Assert.Equal(1, receipt.GetProperty("capturedResponses").GetInt32());
        Assert.Equal(200, receipt.GetProperty("status").GetInt32());
        Assert.True(receipt.GetProperty("captureSucceeded").GetBoolean());
        Assert.Equal(JsonValueKind.Null, value.GetProperty("error").ValueKind);
        Assert.InRange(receipt.GetProperty("capturedBytes").GetInt32(), 1, 65536);
        return value;
    }

    internal async Task DisposeAsync(CancellationToken token)
    {
        if (installing is null) closed = true; // No installation RPC was admitted or dispatched.
        if (CanRetryAfterRealmClosure)
        {
            _ = installing!.Exception;
            _ = closing?.Exception; // Observe exact RPC failures; never call realm destruction a joined reader.
            realmClosed = closed = true;
        }
        if (!closed) await CloseAsync(token);
    }

    internal object ReleaseReceipt() => new
    {
        schema = 1,
        state = installing is null ? "observer-never-dispatched" : realmClosed ? "owned-realm-destroyed-after-settled-evaluation" : "reader-joined-fetch-restored",
        observerId = id,
        installationDispatched = installing is not null,
        installationEvaluationSettled = installing?.IsCompleted == true,
        retainedEvaluationSettled = (installing is null || installing.IsCompleted) && (closing is null || closing.IsCompleted),
        ownedPageClosed = realmClosed && page.IsClosed,
        joinedReaderReceipt = realmClosed ? (JsonElement?)null : joinedReceipt,
        runId = Environment.GetEnvironmentVariable("GITHUB_RUN_ID"),
        runAttempt = Environment.GetEnvironmentVariable("GITHUB_RUN_ATTEMPT"),
    };

    private async Task<JsonElement> CloseAsync(CancellationToken token)
    {
        if (installing is null) throw new InvalidOperationException("Observer installation was never dispatched.");
        await installing.WaitAsync(token); // The cancellation wrapper is not original RPC settlement.
        closing ??= page.EvaluateAsync<string>("async id => { const state = window.__supplierCompanyObserver; if (!state || state.id !== id) throw new Error('Owned observer identity missing'); return JSON.stringify(await state.close()); }", id);
        using var actualWire = JsonDocument.Parse(await closing.WaitAsync(token));
        var value = actualWire.RootElement.Clone();
        RequireExactKeys(value, "body", "error", "receipt");
        var receipt = value.GetProperty("receipt");
        RequireExactKeys(receipt, "schema", "source", "observerId", "maxBytes", "deadlineMs", "matchingRequests", "capturedResponses", "capturedBytes", "status",
            "captureSucceeded", "exactRequestAndResponseUrl", "method", "closed", "fetchIdentityRestored", "activeTasks", "activeTimers", "readSettled", "cancelSettled");
        Assert.True(receipt.GetProperty("closed").GetBoolean());
        Assert.True(receipt.GetProperty("fetchIdentityRestored").GetBoolean());
        Assert.Equal(0, receipt.GetProperty("activeTasks").GetInt32());
        Assert.Equal(0, receipt.GetProperty("activeTimers").GetInt32());
        Assert.True(receipt.GetProperty("readSettled").GetBoolean());
        Assert.True(receipt.GetProperty("cancelSettled").GetBoolean());
        joinedReceipt = receipt;
        closed = true;
        return value;
    }

    internal static void RequireExactKeys(JsonElement value, params string[] expected)
        => Assert.Equal(expected.Order(), value.EnumerateObject().Select(property => property.Name).Order());

    internal const string InstallScript = """
        options => {
          const target = options.target || window;
          if (target.__supplierCompanyObserver) throw new Error('Observer already installed');
          const expected = new URL(options.url);
          const original = target.fetch;
          if (typeof original !== 'function' || !Number.isInteger(options.maxBytes) || options.maxBytes < 1 || options.maxBytes > 65536 ||
              !Number.isInteger(options.deadlineMs) || options.deadlineMs < 1 || options.deadlineMs > 5000) throw new Error('Observer bounds invalid');
          const state = { id: options.id, count: 0, captures: 0, active: 0, timers: 0, bytes: 0, status: 0,
            readSettled: true, cancelSettled: true, error: null, body: null, closed: false, tasks: [] };
          function matches(value, method) {
            try {
            const u = new URL(value, expected.origin);
            const pairs = [...u.searchParams.entries()];
            return method === 'GET' && u.origin === expected.origin && u.pathname === expected.pathname && !u.hash && !u.username && !u.password &&
              pairs.length === 4 && new Set(pairs.map(p => p[0])).size === 4 &&
              ['q','queryType','language','limit'].every(k => u.searchParams.get(k) === expected.searchParams.get(k));
            } catch { return false; }
          }
          async function capture(response) {
            state.active++;
            let reader, reading, cancel, alarm;
            try {
              state.status = response.status;
              if (response.status !== 200 || !matches(response.url, 'GET')) throw new Error('Response contract differs');
              const clone = response.clone();
              if (!clone.body) throw new Error('Response stream missing');
              reader = clone.body.getReader();
              state.readSettled = false; state.cancelSettled = false;
              const chunks = [];
              const deadline = new Promise((_, reject) => {
                state.timers++;
                alarm = setTimeout(() => reject(new Error('Clone deadline exceeded')), options.deadlineMs);
              });
              for (;;) {
                reading = reader.read();
                const part = await Promise.race([reading, deadline]);
                if (part.done) break;
                state.bytes += part.value.byteLength;
                if (state.bytes > options.maxBytes) throw new Error('Clone byte cap exceeded');
                chunks.push(part.value);
              }
              const bytes = new Uint8Array(state.bytes);
              let offset = 0;
              for (const chunk of chunks) { bytes.set(chunk, offset); offset += chunk.byteLength; }
              state.body = new TextDecoder('utf-8', { fatal: true }).decode(bytes);
              JSON.parse(state.body);
              state.captures++;
            } catch (error) { state.error = String(error.message || error); }
            finally {
              if (alarm !== undefined) { clearTimeout(alarm); state.timers--; }
              if (reader) {
                try { cancel = reader.cancel(); await cancel; }
                catch (error) { state.error = String(error.message || error); }
                finally { state.cancelSettled = true; }
                if (reading) {
                  try { await reading; }
                  catch (error) { state.error = state.error || String(error.message || error); }
                }
                state.readSettled = true;
                try { reader.releaseLock(); }
                catch (error) { state.error = state.error || String(error.message || error); }
              }
              state.active--;
            }
          }
          function wrapper(...args) {
            const promise = original.apply(this, args);
            const input = args[0], init = args[1];
            const requestUrl = input instanceof Request ? input.url : String(input);
            const method = String(init?.method || (input instanceof Request ? input.method : 'GET')).toUpperCase();
            if (!state.closed && matches(requestUrl, method)) {
              state.count++;
              if (state.count > 1) state.error = 'Duplicate exact request';
              else state.tasks.push(promise.then(capture, error => { state.error = String(error.message || error); }));
            }
            return promise;
          }
          state.close = async () => {
            state.closed = true;
            const identity = target.fetch === wrapper || target.fetch === original;
            if (target.fetch === wrapper) target.fetch = original;
            await Promise.all(state.tasks);
            return { body: state.body, error: state.error, receipt: { schema: 1, source: 'actual-browser-fetch-clone',
              observerId: state.id, maxBytes: options.maxBytes, deadlineMs: options.deadlineMs,
              matchingRequests: state.count, capturedResponses: state.captures, capturedBytes: state.bytes, status: state.status,
              captureSucceeded: state.error === null && state.captures === 1, exactRequestAndResponseUrl: state.captures === 1,
              method: 'GET', closed: state.closed, fetchIdentityRestored: identity && target.fetch === original,
              activeTasks: state.active, activeTimers: state.timers, readSettled: state.readSettled, cancelSettled: state.cancelSettled } };
          };
          target.__supplierCompanyObserver = state;
          target.fetch = wrapper;
        }
        """;

    internal static string ControlsScript => "async () => { const install = " + InstallScript + "; " + ControlsBody + " }";
    // A primitive JSON string avoids Playwright's JsonElement reference-preservation metadata.
    internal static string ControlsWireScript => "async () => JSON.stringify(await (" + ControlsScript + ")())";
    private const string ControlsBody = """
        const url = 'https://owned.synthetic.test/bff/lookups/companies/search?q=Synthetic%20supplier%20company&queryType=name&language=en&limit=20';
        const body = JSON.stringify({ marker: 'owned-synthetic-observer-control' });
        const passed = [];
        function check(condition, name) { if (!condition) throw new Error(name); }
        function response(text = body, status = 200, responseUrl = url) {
          const value = new Response(text, { status });
          Object.defineProperty(value, 'url', { value: responseUrl });
          return value;
        }
        function fixture(factory = () => response(), maxBytes = 65536, deadlineMs = 80) {
          let calls = 0, originalPromise, receiver, args;
          const target = { fetch: function(...values) {
            calls++; receiver = this; args = values; originalPromise = factory();
            if (!(originalPromise instanceof Promise)) originalPromise = Promise.resolve(originalPromise);
            return originalPromise;
          } };
          const original = target.fetch;
          install({ target, url, id: 'a'.repeat(32), maxBytes, deadlineMs });
          return { target, original, get calls() { return calls; }, get promise() { return originalPromise; },
            get receiver() { return receiver; }, get args() { return args; }, state: target.__supplierCompanyObserver };
        }
        function settled(result) {
          const r = result.receipt;
          return r.closed && r.fetchIdentityRestored && r.activeTasks === 0 && r.activeTimers === 0 && r.readSettled && r.cancelSettled;
        }
        {
          const expectedResponse = response();
          const f = fixture(() => expectedResponse), options = { method: 'GET' };
          const promise = f.target.fetch(url, options);
          check(promise === f.promise && f.calls === 1 && f.receiver === f.target && f.args[0] === url && f.args[1] === options, 'identity');
          const originalResponse = await promise;
          check(originalResponse === expectedResponse, 'same original response');
          check(await originalResponse.text() === body, 'original body preserved');
          const result = await f.state.close();
          check(settled(result) && result.body === body && result.receipt.captureSucceeded && f.target.fetch === f.original, 'clone joins');
          passed.push('same-promise-response-args-receiver');
          await f.target.fetch(url);
          check(f.calls === 2 && f.state.count === 1 && f.state.tasks.length === 1, 'restored pass through');
          passed.push('restored-pass-through-no-new-task');
        }
        for (const [name, changed, method] of [
          ['wrong-origin', url.replace('owned.synthetic.test', 'other.synthetic.test'), 'GET'],
          ['wrong-path', url.replace('/companies/search', '/companies/other'), 'GET'],
          ['wrong-query', url.replace('language=en', 'language=th'), 'GET'],
          ['wrong-method', url, 'POST'],
          ['missing-query-key', url.replace('&limit=20', ''), 'GET'],
          ['duplicate-query-key', url + '&q=second', 'GET']]) {
          const f = fixture(); const p = f.target.fetch(changed, { method });
          check(p === f.promise, name + ' promise'); await p;
          const result = await f.state.close();
          check(settled(result) && f.calls === 1 && result.receipt.matchingRequests === 0 && result.receipt.capturedResponses === 0, name);
          passed.push(name);
        }
        for (const [name, factory, cap] of [
          ['wrong-response-url', () => response(body, 200, url.replace('limit=20', 'limit=19')), 65536],
          ['wrong-response-status', () => response(body, 503), 65536],
          ['byte-cap', () => response(body), 8],
          ['invalid-json', () => response('{'), 65536],
          ['clone-read-error', () => { const s = new ReadableStream({ start(c) { c.error(new Error('synthetic stream failure')); } }); return response(s); }, 65536]]) {
          const f = fixture(factory, cap); const value = await f.target.fetch(url);
          try { await value.text(); } catch { /* Expected errored control stream; clone error is independently asserted. */ }
          const result = await f.state.close();
          check(settled(result) && result.error !== null && !result.receipt.captureSucceeded && f.calls === 1, name);
          passed.push(name);
        }
        {
          const f = fixture(); const values = await Promise.all([f.target.fetch(url), f.target.fetch(url)]);
          await Promise.all(values.map(value => value.text()));
          const result = await f.state.close();
          check(settled(result) && result.error !== null && result.receipt.matchingRequests === 2 && !result.receipt.captureSucceeded && f.state.tasks.length === 1, 'cardinality');
          passed.push('duplicate-exact-request');
        }
        {
          const f = fixture(() => Promise.reject(new Error('synthetic original rejection')));
          const p = f.target.fetch(url); check(p === f.promise, 'rejected promise preserved');
          let rejected = false; try { await p; } catch { rejected = true; }
          const result = await f.state.close();
          check(rejected && settled(result) && result.error !== null && !result.receipt.captureSucceeded, 'original rejection surfaced');
          passed.push('original-fetch-rejection');
        }
        {
          const stream = new ReadableStream({ start(c) { c.enqueue(new TextEncoder().encode(body)); } });
          const originalResponse = response(stream);
          const f = fixture(() => originalResponse, 65536, 20);
          await f.target.fetch(url);
          let joined = false;
          const closing = f.state.close().then(r => { joined = true; return r; });
          await new Promise(resolve => setTimeout(resolve, 60));
          check(!joined && f.state.active === 1 && !f.state.cancelSettled && f.target.fetch === f.original, 'tee cancellation honestly pending');
          await originalResponse.body.cancel();
          const result = await closing;
          check(settled(result) && result.error !== null && !result.receipt.captureSucceeded, 'tee cancellation joined after original close');
          passed.push('deadline-pending-tee-until-original-close');
        }
        {
          const f = fixture(); await (await f.target.fetch(url)).text();
          const other = function() { throw new Error('other actor'); }; f.target.fetch = other;
          const result = await f.state.close();
          check(f.target.fetch === other && !result.receipt.fetchIdentityRestored && result.receipt.activeTasks === 0 && result.receipt.activeTimers === 0, 'detach does not clobber');
          passed.push('detach-identity-interference');
        }
        check(passed.length === 17 && new Set(passed).size === 17, 'exact controls');
        return { schema: 1, nativeBrowserObserverControls: true, cases: passed, realNetworkAllocated: false };
        """;
}

public sealed class SupplierCompanyBrowserObserverControls
{
    [Fact]
    public Task PendingCloneRetainsExactEvaluationUntilOwnedRealmClosesAndSameOwnerRetries()
        => RunPendingControlAsync(false, "browser-observer-realm-control.json");

    [Fact]
    public Task CanceledInstallationRetainsOriginalRpcBeforeCloseAndOwnedRealmRetry()
        => RunPendingControlAsync(true, "browser-observer-installation-control.json");

    private static async Task RunPendingControlAsync(bool heldInstallation, string evidenceName)
    {
        var owner = new SupplierResourceScope(TimeSpan.FromMinutes(1), TimeSpan.FromSeconds(10));
        SupplierCompanyBrowserObserver? observer = null;
        try
        {
            IPlaywright? playwright = null;
            var driver = owner.Register("pending-control-driver", _ => { playwright?.Dispose(); return Task.CompletedTask; }, phase: 1);
            await owner.StartAsync(driver, async _ => playwright = await Playwright.CreateAsync());
            IBrowser? browser = null;
            var browserLease = owner.Register("pending-control-browser", _ => browser?.DisposeAsync().AsTask() ?? Task.CompletedTask);
            await owner.StartAsync(browserLease, async _ => browser = await playwright!.Chromium.LaunchAsync(new() { Headless = true, Timeout = 30_000 }));
            IBrowserContext? context = null;
            var contextLease = owner.Register("pending-control-context", _ => context?.DisposeAsync().AsTask() ?? Task.CompletedTask);
            await owner.StartAsync(contextLease, async _ => context = await browser!.NewContextAsync());
            var page = await context!.NewPageAsync();
            const string url = "https://owned.synthetic.test/bff/lookups/companies/search?q=Synthetic%20supplier%20company&queryType=name&language=en&limit=20";
            // Controlled original fetch only in this negative unit control, never in the business journey.
            await page.EvaluateAsync("url => { const response = new Response(new ReadableStream({ start(c) { c.enqueue(new TextEncoder().encode('{\"synthetic\":true}')); } })); Object.defineProperty(response, 'url', { value: url }); window.fetch = function() { return Promise.resolve(response); }; }", url);
            if (heldInstallation)
                await page.EvaluateAsync("() => { window.__supplierObserverInstallationControlGate = new Promise(() => {}); }");
            observer = new SupplierCompanyBrowserObserver(page, url, heldInstallation);
            var lease = owner.Register("pending-control-observer", observer.DisposeAsync);
            using var readWindow = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));
            if (heldInstallation)
            {
                await Assert.ThrowsAnyAsync<OperationCanceledException>(() => owner.StartAsync(lease, _ => observer.InstallAsync(readWindow.Token)));
                Assert.False(observer.InstallationEvaluationSettled);
                Assert.False(observer.ClosingWasDispatched);
            }
            else
            {
                await owner.StartAsync(lease, observer.InstallAsync);
                await page.EvaluateAsync("url => { void window.fetch(url); }", url);
                await page.WaitForFunctionAsync("window.__supplierCompanyObserver.active === 1");
                using var bodyWindow = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));
                await Assert.ThrowsAnyAsync<OperationCanceledException>(() => observer.ReadAndCloseAsync(bodyWindow.Token));
            }
            Assert.False(observer.CanRetryAfterRealmClosure);
            await Assert.ThrowsAsync<AggregateException>(() => owner.DisposeAsync().AsTask());
            Assert.False(owner.Released);
            Assert.True(page.IsClosed);
            await observer.AwaitExactEvaluationAfterRealmClosureAsync();
            Assert.True(observer.InstallationEvaluationSettled);
            if (heldInstallation) Assert.False(observer.ClosingWasDispatched);
            Assert.True(observer.CanRetryAfterRealmClosure);
            await owner.DisposeAsync();
            Assert.True(owner.Released);
            var receipt = JsonSerializer.SerializeToElement(observer.ReleaseReceipt());
            Assert.Equal("owned-realm-destroyed-after-settled-evaluation", receipt.GetProperty("state").GetString());
            Assert.True(receipt.GetProperty("retainedEvaluationSettled").GetBoolean());
            Assert.True(receipt.GetProperty("ownedPageClosed").GetBoolean());
            Assert.Equal(JsonValueKind.Null, receipt.GetProperty("joinedReaderReceipt").ValueKind);
            var directory = Environment.GetEnvironmentVariable("PROOF_RESULTS") ?? throw new InvalidOperationException("Explicit native control results required.");
            Directory.CreateDirectory(directory);
            await File.WriteAllTextAsync(Path.Combine(directory, evidenceName), receipt.GetRawText());
        }
        finally { await owner.DisposeAsync(); }
    }

    [Fact]
    public async Task ActualPassiveCloneContractsAndFailureSettlement()
    {
        var owner = new SupplierResourceScope(TimeSpan.FromMinutes(1));
        JsonElement result = default;
        Task<string>? evaluation = null;
        Exception? executionFailure = null;
        try
        {
            IPlaywright? playwright = null;
            var driver = owner.Register("observer-control-driver", _ => { playwright?.Dispose(); return Task.CompletedTask; }, phase: 1);
            await owner.StartAsync(driver, async _ => playwright = await Playwright.CreateAsync());
            IBrowser? browser = null;
            var browserLease = owner.Register("observer-control-browser", _ => browser?.DisposeAsync().AsTask() ?? Task.CompletedTask);
            await owner.StartAsync(browserLease, async _ => browser = await playwright!.Chromium.LaunchAsync(new() { Headless = true, Timeout = 30_000 }));
            IBrowserContext? context = null;
            var contextLease = owner.Register("observer-control-context", _ => context?.DisposeAsync().AsTask() ?? Task.CompletedTask);
            await owner.StartAsync(contextLease, async _ => context = await browser!.NewContextAsync());
            var page = await context!.NewPageAsync();
            evaluation = page.EvaluateAsync<string>(SupplierCompanyBrowserObserver.ControlsWireScript);
            using var actualWire = JsonDocument.Parse(await evaluation.WaitAsync(TimeSpan.FromSeconds(10)));
            result = actualWire.RootElement.Clone();
            SupplierCompanyBrowserObserver.RequireExactKeys(result, "schema", "nativeBrowserObserverControls", "cases", "realNetworkAllocated");
        }
        catch (Exception error) { executionFailure = error; }
        finally
        {
            var failures = new List<Exception>();
            if (executionFailure is not null) failures.Add(executionFailure);
            try { await owner.DisposeAsync(); }
            catch (Exception cleanup) { failures.Add(cleanup); }
            if (evaluation is not null)
            {
                try { await evaluation.WaitAsync(TimeSpan.FromSeconds(2)); }
                catch (Exception settlement)
                {
                    // Parent realm termination is separate from completion of the waiting wrapper.
                    if (evaluation.IsCompleted) _ = evaluation.Exception;
                    if (!evaluation.IsCompleted || executionFailure is null) failures.Add(settlement);
                }
            }
            if (failures.Count > 1) throw new AggregateException("Native observer execution and exact evaluation cleanup failed.", failures);
            if (failures.Count == 1) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failures[0]).Throw();
        }
        Assert.True(owner.Released);
        Assert.Equal(17, result.GetProperty("cases").GetArrayLength());
        var directory = Environment.GetEnvironmentVariable("PROOF_RESULTS") ?? throw new InvalidOperationException("Explicit native control results required.");
        Directory.CreateDirectory(directory);
        await File.WriteAllTextAsync(Path.Combine(directory, "browser-observer-controls.json"), result.GetRawText());
    }
}
