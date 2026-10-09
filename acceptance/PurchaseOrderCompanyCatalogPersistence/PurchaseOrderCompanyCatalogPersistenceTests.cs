using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using Legacy.Maliev.FileService.Data;
using Legacy.Maliev.ProcurementService.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Playwright;
using UglyToad.PdfPig;

namespace SupplierCatalogPersistence.Acceptance;

/// <summary>Real joined PO create, document rendering, File persistence and readback; external cloud/provider/scan protocol are synthetic.</summary>
public sealed class PurchaseOrderCompanyCatalogPersistenceTests
{
    [Fact]
    public async Task ShippingAndBilling_EnglishAndThai_CatalogSelectionOrdinarySavePdfFileReadbackAndReload()
    {
        var owner = new SupplierResourceScope(TimeSpan.FromMinutes(12));
        Exception? failure = null;
        try
        {
            var runtime = await PoCompanyRuntime.StartAsync(owner, owner.Token);
            IPlaywright? driver = null;
            await owner.StartAsync(owner.Register("playwright-driver", _ => { driver?.Dispose(); return Task.CompletedTask; }, phase: 1),
                async _ => driver = await Playwright.CreateAsync());
            IBrowser? browser = null;
            await owner.StartAsync(owner.Register("chromium", _ => browser?.DisposeAsync().AsTask() ?? Task.CompletedTask),
                async _ => browser = await driver!.Chromium.LaunchAsync(new() { Headless = true, Timeout = 30_000 }));

            // All negative requests precede successful creates; independent owning database must remain empty.
            var deniedCreate = await ContextAsync(owner, browser!, "create-denied", "en-US");
            await SignInAsync(deniedCreate, runtime.Origin, "create-denied@maliev.com");
            var deniedCsrf = await CsrfAsync(deniedCreate, runtime.Origin);
            var deniedEmployee = await deniedCreate.APIRequest.PostAsync(runtime.Origin + "/bff/purchase-orders", new()
            {
                Headers = new Dictionary<string, string> { ["X-CSRF-TOKEN"] = deniedCsrf, ["Idempotency-Key"] = Guid.NewGuid().ToString("D") },
                DataObject = RequestBody(runtime),
            });
            Assert.Equal(403, deniedEmployee.Status);
            runtime.Procurement.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", runtime.MissingCreateServiceToken);
            using var deniedDomain = await runtime.Procurement.PostAsJsonAsync("/PurchaseOrders", new
            {
                runtime.SupplierId,
                runtime.EmployeeId,
                runtime.ShippingAddressId,
                runtime.BillingAddressId,
            }, owner.Token);
            Assert.Equal(HttpStatusCode.Forbidden, deniedDomain.StatusCode);
            runtime.Procurement.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", runtime.ForeignServiceToken);
            using var deniedForeignToken = await runtime.Procurement.PostAsJsonAsync("/PurchaseOrders", new { runtime.SupplierId, runtime.EmployeeId, runtime.ShippingAddressId, runtime.BillingAddressId }, owner.Token);
            Assert.Equal(HttpStatusCode.Forbidden, deniedForeignToken.StatusCode);
            runtime.Procurement.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", runtime.ServiceToken);
            using var deniedForeignResource = await runtime.Procurement.PostAsync("/purchaseorders/999999/files?bucket=maliev.com&objectName=synthetic-foreign.pdf", null, owner.Token);
            Assert.Equal(HttpStatusCode.Forbidden, deniedForeignResource.StatusCode);
            await using (var original = OrderDb(runtime)) Assert.Equal(0, await original.PurchaseOrders.CountAsync(owner.Token));
            Assert.Empty(runtime.Documents);
            Assert.Empty(runtime.ExternalFile.ScannedBytes);

            var manual = await ContextAsync(owner, browser!, "manual-denied", "en-US");
            await SignInAsync(manual, runtime.Origin, "company-denied@maliev.com");
            var noCsrf = await manual.APIRequest.PostAsync(runtime.Origin + "/bff/purchase-orders", new() { DataObject = RequestBody(runtime) });
            Assert.Equal(400, noCsrf.Status);
            var lookupDenied = await manual.APIRequest.GetAsync(runtime.Origin + "/bff/lookups/companies/search?q=Synthetic%20PO%20shipping%20en&queryType=name&language=en&limit=20");
            Assert.Equal(403, lookupDenied.Status);
            runtime.Catalog.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", runtime.MissingCompanyServiceToken);
            using var directDenied = await runtime.Catalog.GetAsync("/api/v1/companies/search?q=Synthetic%20PO%20shipping%20en&queryType=name&language=en&limit=20", owner.Token);
            Assert.Equal(HttpStatusCode.Forbidden, directDenied.StatusCode);
            runtime.Catalog.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", runtime.ServiceToken);
            Assert.Empty(runtime.Provider.Observations);
            await using (var original = OrderDb(runtime)) Assert.Equal(0, await original.PurchaseOrders.CountAsync(owner.Token));

            var manualPage = await PageAsync(owner, manual, "manual-denied");
            await FillAsync(manualPage, runtime, "manual");
            var manualLookup = await manualPage.RunAndWaitForResponseAsync(
                () => manualPage.Locator("#purchase-order-shipping-company-lookup").FillAsync(PoCompanyProvider.Query("shipping", "en")),
                response => response.Url.Contains("/bff/lookups/companies/search", StringComparison.Ordinal));
            Assert.Equal(403, manualLookup.Status);
            Assert.Empty(runtime.Provider.Observations);
            await Assertions.Expect(manualPage.Locator("#purchase-order-shipping-company")).ToHaveValueAsync("Synthetic manual shipping");
            var receipts = new List<CaseReceipt>();
            receipts.Add(await SaveReadbackAsync(manualPage, manual, runtime, "manual", "en", "Synthetic manual shipping", "Synthetic manual billing", owner.Token));

            foreach (var party in new[] { "shipping", "billing" })
            {
                foreach (var language in new[] { "en", "th" })
                {
                    var key = party + "-" + language;
                    var context = await ContextAsync(owner, browser!, "case-" + key, language == "th" ? "th-TH" : "en-US");
                    await SignInAsync(context, runtime.Origin, "po-company-proof@maliev.com");
                    var page = await PageAsync(owner, context, "case-" + key);
                    await FillAsync(page, runtime, key);
                    var query = PoCompanyProvider.Query(party, language);
                    using var independentLookup = await runtime.Catalog.GetAsync("/api/v1/companies/search?q=" + Uri.EscapeDataString(query) + "&queryType=name&language=" + language + "&limit=20", owner.Token);
                    Assert.Equal(HttpStatusCode.OK, independentLookup.StatusCode);
                    var actual = await independentLookup.Content.ReadFromJsonAsync<JsonElement>(owner.Token);
                    Assert.Equal(PoCompanyProvider.Name(party, language), actual.GetProperty("items")[0].GetProperty(language == "th" ? "nameTh" : "nameEn").GetString());
                    var lookup = await page.RunAndWaitForResponseAsync(
                        () => page.Locator("#purchase-order-" + party + "-company-lookup").FillAsync(query),
                        response => response.Url.Contains("/bff/lookups/companies/search", StringComparison.Ordinal));
                    Assert.Equal(200, lookup.Status);
                    // HTTP search must not mutate either manual company field before deliberate selection.
                    await Assertions.Expect(page.Locator("#purchase-order-shipping-company")).ToHaveValueAsync("Synthetic manual shipping");
                    await Assertions.Expect(page.Locator("#purchase-order-billing-company")).ToHaveValueAsync("Synthetic manual billing");
                    var selection = page.Locator("#purchase-order-" + party + "-company-lookup-results");
                    await Assertions.Expect(selection).ToBeEnabledAsync();
                    await selection.FocusAsync();
                    await selection.PressAsync("ArrowDown");
                    await selection.PressAsync("Enter");
                    var selected = PoCompanyProvider.Name(party, language);
                    await Assertions.Expect(page.Locator("#purchase-order-" + party + "-company")).ToHaveValueAsync(selected);
                    var other = party == "shipping" ? "billing" : "shipping";
                    await Assertions.Expect(page.Locator("#purchase-order-" + other + "-company")).ToHaveValueAsync("Synthetic manual " + other);
                    await AssertManualFieldsAsync(page, key);
                    var reviewed = selected + " reviewed";
                    await page.Locator("#purchase-order-" + party + "-company").FillAsync(reviewed);
                    await page.Locator("#purchase-order-notes").FocusAsync();
                    receipts.Add(await SaveReadbackAsync(page, context, runtime, party, language,
                        party == "shipping" ? reviewed : "Synthetic manual shipping",
                        party == "billing" ? reviewed : "Synthetic manual billing", owner.Token));
                }
            }
            Assert.Equal(5, runtime.Documents.Count);
            await using (var final = OrderDb(runtime))
            {
                Assert.Equal(5, await final.PurchaseOrders.CountAsync(owner.Token));
                Assert.Equal(5, await final.OrderItems.CountAsync(owner.Token));
                Assert.Equal(5, await final.Files.CountAsync(owner.Token));
                Assert.Equal(2, await final.Addresses.CountAsync(owner.Token));
            }
            var provider = runtime.Provider.Observations.ToArray();
            foreach (var party in new[] { "shipping", "billing" })
                foreach (var language in new[] { "en", "th" })
                    Assert.Contains(provider, row => row.Party == party && row.Language == language && row.Query == PoCompanyProvider.Query(party, language) && row.Status == 200);
            Assert.All(provider, row => Assert.Equal(200, row.Status));
            await owner.WriteReceiptAsync("po-company-journey.jsonl", new
            {
                schema = 1,
                run = Environment.GetEnvironmentVariable("GITHUB_RUN_ID"),
                runAttempt = Environment.GetEnvironmentVariable("GITHUB_RUN_ATTEMPT"),
                source = Environment.GetEnvironmentVariable("PO_COMPANY_PROOF_HEAD"),
                managedClientCount = 51,
                csrfDenied = noCsrf.Status,
                employeeCreateDenied = deniedEmployee.Status,
                domainCreateDenied = (int)deniedDomain.StatusCode,
                foreignTokenDenied = (int)deniedForeignToken.StatusCode,
                foreignResourceDenied = (int)deniedForeignResource.StatusCode,
                companyDenied = lookupDenied.Status,
                directCompanyDenied = (int)directDenied.StatusCode,
                manualLookupDenied = manualLookup.Status,
                deniedRequestsPreservedEmptyDatabase = true,
                externalFile = runtime.ExternalFile.SourceReceipt(),
                provider = provider.Select(row => new { row.Party, row.Language, row.Query, row.Status }).ToArray(),
                iam = runtime.IamDecisions.ToArray(),
                cases = receipts,
                scope = "Synthetic external company/GCS SDK/INSTREAM only; actual PO/Document/File Programs, QuestPDF, PostgreSQL, signed read; no live IAM/GCS/ClamAV claim",
            }, owner.Token);
        }
        catch (Exception exception) { failure = exception; }
        finally
        {
            try { await owner.DisposeAsync(); }
            catch (Exception cleanup) { failure = failure is null ? cleanup : new AggregateException(failure, cleanup); }
        }
        if (failure is not null) throw failure;
        Assert.True(owner.Released);
        Assert.DoesNotContain(owner, SupplierResourceScope.Unresolved);
    }

    private static PurchaseOrderDbContext OrderDb(PoCompanyRuntime runtime) => new(new DbContextOptionsBuilder<PurchaseOrderDbContext>().UseNpgsql(runtime.OrderConnection).Options);

    private static async Task<IBrowserContext> ContextAsync(SupplierResourceScope owner, IBrowser browser, string name, string locale)
    {
        IBrowserContext? value = null;
        await owner.StartAsync(owner.Register(name + "-context", _ => value?.DisposeAsync().AsTask() ?? Task.CompletedTask),
            async _ => value = await browser.NewContextAsync(new() { IgnoreHTTPSErrors = true, Locale = locale }));
        value!.SetDefaultTimeout(25_000);
        value.SetDefaultNavigationTimeout(40_000);
        // Set the existing user preference before the real WASM bootstrap; browser locale alone is not authority for UI culture.
        await value.AddInitScriptAsync("localStorage.setItem('maliev_culture', " + JsonSerializer.Serialize(locale == "th-TH" ? "th-TH" : "en-TH") + ");");
        return value;
    }

    private static async Task<IPage> PageAsync(SupplierResourceScope owner, IBrowserContext context, string name)
    {
        IPage? value = null;
        await owner.StartAsync(owner.Register(name + "-page", _ => value?.CloseAsync() ?? Task.CompletedTask), async _ => value = await context.NewPageAsync());
        return value!;
    }

    private static async Task<string> CsrfAsync(IBrowserContext context, string origin)
    {
        using var session = JsonDocument.Parse(await (await context.APIRequest.GetAsync(origin + "/bff/session")).TextAsync());
        return session.RootElement.GetProperty("csrfToken").GetString()!;
    }

    private static async Task SignInAsync(IBrowserContext context, string origin, string email)
    {
        var response = await context.APIRequest.PostAsync(origin + "/bff/login", new()
        {
            Headers = new Dictionary<string, string> { ["X-CSRF-TOKEN"] = await CsrfAsync(context, origin) },
            DataObject = new { email, password = "synthetic-disposable", returnUrl = "/PurchaseOrders/Create" },
        });
        Assert.Equal(200, response.Status);
    }

    private static object RequestBody(PoCompanyRuntime runtime) => new
    {
        runtime.SupplierId,
        runtime.EmployeeId,
        runtime.ShippingAddressId,
        runtime.BillingAddressId,
        shippingCompanyName = "Synthetic manual shipping",
        billingCompanyName = "Synthetic manual billing",
        items = new[] { new { description = "Synthetic rejected item", quantity = 2, unitPrice = 12.5m } },
    };

    private static async Task FillAsync(IPage page, PoCompanyRuntime runtime, string key)
    {
        await page.GotoAsync(runtime.Origin + "/PurchaseOrders/Create");
        await Assertions.Expect(page.Locator("#purchase-order-shipping-company")).ToBeEnabledAsync();
        // Initial selectors choose the sole supplier/employee and first shipping address. Billing must choose the distinct second address.
        var billing = page.Locator("#purchase-order-billing-address");
        await billing.ClickAsync();
        await page.GetByRole(AriaRole.Option, new() { Name = "Synthetic billing street, Nonthaburi", Exact = true }).ClickAsync();
        foreach (var party in new[] { "shipping", "billing" })
        {
            await page.Locator("#purchase-order-" + party + "-company").FillAsync("Synthetic manual " + party);
            foreach (var field in new[] { "contact", "telephone", "mobile", "fax" })
                await page.Locator("#purchase-order-" + party + "-" + field).FillAsync(Manual(party, field, key));
        }
        await page.Locator("#purchase-order-supplier-contact").FillAsync("Synthetic supplier contact " + key);
        await page.Locator("#purchase-order-fob").FillAsync("Synthetic FOB " + key);
        await page.Locator("#purchase-order-terms").FillAsync("Synthetic terms " + key);
        await page.Locator("#purchase-order-shipping-method").FillAsync("Synthetic transport " + key);
        await page.Locator("#purchase-order-notes").FillAsync("Synthetic PO notes " + key);
        await page.Locator("#purchase-order-line-0-part").FillAsync("SYN-" + key);
        await page.Locator("#purchase-order-line-0-description").FillAsync("Synthetic item " + key);
        await page.Locator("#purchase-order-line-0-quantity").FillAsync("2");
        await page.Locator("#purchase-order-line-0-price").FillAsync("12.50");
        await page.Locator("#purchase-order-notes").FocusAsync();
    }

    private static string Manual(string party, string field, string key) => field == "contact" ? "Synthetic " + party + " contact " + key : field switch
    {
        "telephone" => party == "shipping" ? "020000061" : "020000062",
        "mobile" => party == "shipping" ? "0800000061" : "0800000062",
        _ => party == "shipping" ? "020000071" : "020000072",
    };

    private static async Task AssertManualFieldsAsync(IPage page, string key)
    {
        foreach (var party in new[] { "shipping", "billing" })
            foreach (var field in new[] { "contact", "telephone", "mobile", "fax" })
                await Assertions.Expect(page.Locator("#purchase-order-" + party + "-" + field)).ToHaveValueAsync(Manual(party, field, key));
        await Assertions.Expect(page.Locator("#purchase-order-notes")).ToHaveValueAsync("Synthetic PO notes " + key);
        await Assertions.Expect(page.Locator("#purchase-order-line-0-description")).ToHaveValueAsync("Synthetic item " + key);
    }

    private static async Task<CaseReceipt> SaveReadbackAsync(IPage page, IBrowserContext context, PoCompanyRuntime runtime,
        string party, string language, string shippingName, string billingName, CancellationToken token)
    {
        var key = party == "manual" ? "manual" : party + "-" + language;
        var saved = await page.RunAndWaitForResponseAsync(() => page.Locator("button[type='submit']").ClickAsync(),
            response => response.Request.Method == "POST" && response.Url.EndsWith("/bff/purchase-orders", StringComparison.Ordinal));
        Assert.Equal(201, saved.Status);
        // Primitive metadata and actual result URL avoid Chromium response-body lifetime races; no copied response body or route interception.
        Assert.True(Guid.TryParse(saved.Request.Headers["idempotency-key"], out _));
        Assert.True(saved.Request.Headers.ContainsKey("x-csrf-token"));
        await page.WaitForURLAsync("**/PurchaseOrders/View?id=*");
        var id = int.Parse(new Uri(page.Url).Query.Split('=')[1], CultureInfo.InvariantCulture);
        Assert.True(id > 0);
        using var domain = await runtime.Procurement.GetAsync("/PurchaseOrders/" + id, token);
        Assert.Equal(HttpStatusCode.OK, domain.StatusCode);
        var actual = await domain.Content.ReadFromJsonAsync<JsonElement>(token);
        Assert.Equal(id, actual.GetProperty("Id").GetInt32());
        Assert.Equal(runtime.SupplierId, actual.GetProperty("SupplierId").GetInt32());
        Assert.Equal(runtime.EmployeeId, actual.GetProperty("EmployeeId").GetInt32());
        Assert.Equal(runtime.ShippingAddressId, actual.GetProperty("ShippingAddressId").GetInt32());
        Assert.Equal(runtime.BillingAddressId, actual.GetProperty("BillingAddressId").GetInt32());
        await using var db = OrderDb(runtime);
        var order = await db.PurchaseOrders.AsNoTracking().SingleAsync(row => row.Id == id, token);
        Assert.Equal("Synthetic PO notes " + key, order.Notes);
        Assert.Equal("Synthetic supplier contact " + key, order.SupplierContactPerson);
        Assert.Equal("Synthetic FOB " + key, order.Fob);
        Assert.Equal("Synthetic terms " + key, order.Terms);
        Assert.Equal("Synthetic transport " + key, order.ShippingMethod);
        Assert.Equal(Manual("shipping", "contact", key), order.ShippingContactPerson);
        Assert.Equal(Manual("billing", "contact", key), order.BillingContactPerson);
        Assert.Equal(Manual("shipping", "telephone", key), order.ShippingTelephone);
        Assert.Equal(Manual("billing", "telephone", key), order.BillingTelephone);
        Assert.Equal(Manual("shipping", "mobile", key), order.ShippingMobile);
        Assert.Equal(Manual("billing", "mobile", key), order.BillingMobile);
        Assert.Equal(Manual("shipping", "fax", key), order.ShippingFax);
        Assert.Equal(Manual("billing", "fax", key), order.BillingFax);
        var item = await db.OrderItems.AsNoTracking().SingleAsync(row => row.PurchaseOrderId == id, token);
        Assert.Equal("Synthetic item " + key, item.Description);
        Assert.Equal("SYN-" + key, item.PartNumber);
        Assert.Equal(2, item.Quantity);
        Assert.Equal(12.5m, item.UnitPrice);
        Assert.Equal(25m, item.Subtotal);
        var shipping = await db.Addresses.AsNoTracking().SingleAsync(row => row.Id == runtime.ShippingAddressId, token);
        var billing = await db.Addresses.AsNoTracking().SingleAsync(row => row.Id == runtime.BillingAddressId, token);
        Assert.Equal("Synthetic shipping suite", shipping.AddressLine2);
        Assert.Equal("Synthetic billing suite", billing.AddressLine2);
        Assert.Equal("Synthetic shipping building", shipping.Building);
        Assert.Equal("Synthetic billing building", billing.Building);
        Assert.Equal(66, shipping.CountryId);
        Assert.Equal(66, billing.CountryId);
        foreach (var address in new[] { shipping, billing })
        {
            var addressParty = address.Id == runtime.ShippingAddressId ? "shipping" : "billing";
            Assert.Equal("Synthetic " + addressParty + " street", address.AddressLine1);
            Assert.Equal(addressParty == "shipping" ? "Bangkok" : "Nonthaburi", address.City);
            Assert.Equal("Synthetic " + addressParty + " state", address.State);
            Assert.Equal(addressParty == "shipping" ? "10110" : "11120", address.PostalCode);
            using var independentAddressRead = await runtime.Procurement.GetAsync("/purchaseorders/addresses/" + address.Id, token);
            Assert.Equal(HttpStatusCode.OK, independentAddressRead.StatusCode);
            var independentAddress = await independentAddressRead.Content.ReadFromJsonAsync<JsonElement>(token);
            Assert.Equal(address.Id, independentAddress.GetProperty("Id").GetInt32());
            Assert.Equal(address.AddressLine1, independentAddress.GetProperty("AddressLine1").GetString());
            Assert.Equal(address.AddressLine2, independentAddress.GetProperty("AddressLine2").GetString());
            Assert.Equal(address.Building, independentAddress.GetProperty("Building").GetString());
            Assert.Equal(address.City, independentAddress.GetProperty("City").GetString());
            Assert.Equal(address.State, independentAddress.GetProperty("State").GetString());
            Assert.Equal(address.PostalCode, independentAddress.GetProperty("PostalCode").GetString());
            Assert.Equal(address.CountryId, independentAddress.GetProperty("CountryId").GetInt32());
        }
        var linked = await db.Files.AsNoTracking().SingleAsync(row => row.PurchaseOrderId == id, token);
        Assert.Equal("maliev.com", linked.Bucket);
        Assert.StartsWith("purchaseorders/" + id + "/", linked.ObjectName, StringComparison.Ordinal);
        using var independentFileRead = await runtime.Procurement.GetAsync("/purchaseorders/" + id + "/files", token);
        Assert.Equal(HttpStatusCode.OK, independentFileRead.StatusCode);
        var independentFile = await independentFileRead.Content.ReadFromJsonAsync<JsonElement>(token);
        Assert.Equal(1, independentFile.GetArrayLength());
        Assert.Equal(linked.Id, independentFile[0].GetProperty("Id").GetInt32());
        Assert.Equal(id, independentFile[0].GetProperty("PurchaseOrderId").GetInt32());
        Assert.Equal(linked.ObjectName, independentFile[0].GetProperty("ObjectName").GetString());
        await using var fileDb = new FileDbContext(new DbContextOptionsBuilder<FileDbContext>().UseNpgsql(runtime.FileConnection).Options);
        var uploaded = await fileDb.Uploads.AsNoTracking().SingleAsync(row => row.Bucket == linked.Bucket && row.Name == linked.ObjectName, token);
        Assert.Equal("application/pdf", uploaded.ContentType);
        var journal = await fileDb.StorageMoveJournals.AsNoTracking().SingleAsync(row => row.DestinationBucket == linked.Bucket && row.DestinationObjectName == linked.ObjectName, token);
        Assert.True(journal.ScanClean);
        Assert.Equal("MetadataCommitted", journal.State);
        Assert.True(journal.SourceGeneration > 0 && journal.DestinationGeneration > journal.SourceGeneration);
        var pdf = runtime.ExternalFile.ReadObject(linked.Bucket, linked.ObjectName);
        Assert.Equal(pdf.Length, uploaded.Size);
        Assert.Contains(runtime.ExternalFile.ScannedBytes, bytes => bytes.SequenceEqual(pdf));
        Assert.Equal(0, runtime.ExternalFile.ScannedInfected);
        using (var document = PdfDocument.Open(pdf))
        {
            Assert.NotEmpty(document.GetPages());
            var text = string.Join(" ", document.GetPages().Select(value => value.Text));
            var normalizedText = NormalizePdfText(text);
            Assert.Contains(NormalizePdfText(shippingName), normalizedText, StringComparison.Ordinal);
            Assert.Contains(NormalizePdfText(billingName), normalizedText, StringComparison.Ordinal);
        }
        var witness = Assert.Single(runtime.Documents.Where(row => row.ReferenceNumber == id));
        Assert.Equal(shippingName, witness.ShippingName);
        Assert.Equal(billingName, witness.BillingName);
        var detail = await context.APIRequest.GetAsync(runtime.Origin + "/bff/purchase-orders/" + id);
        Assert.Equal(200, detail.Status);
        using var result = JsonDocument.Parse(await detail.TextAsync());
        Assert.Equal(id, result.RootElement.GetProperty("id").GetInt32());
        await page.ReloadAsync();
        await Assertions.Expect(page.Locator(".purchase-order-summary")).ToContainTextAsync("Synthetic PO notes " + key);
        await Assertions.Expect(page.Locator(".purchase-order-lines")).ToContainTextAsync("Synthetic item " + key);
        var download = page.Locator(".purchase-order-downloads a").First;
        await Assertions.Expect(download).ToBeVisibleAsync();
        var href = await download.GetAttributeAsync("href");
        Assert.True(Uri.TryCreate(href, UriKind.Absolute, out var uri));
        Assert.Equal(runtime.ExternalFile.ReadOrigin.Authority, uri!.Authority);
        var downloadRead = await context.APIRequest.GetAsync(href!);
        Assert.Equal(200, downloadRead.Status);
        var downloaded = await downloadRead.BodyAsync();
        Assert.Equal(pdf, downloaded);
        await Assertions.Expect(page.Locator("#blazor-error-ui")).ToBeHiddenAsync();
        return new(party, language, id, runtime.SupplierId, runtime.EmployeeId, runtime.ShippingAddressId, runtime.BillingAddressId,
            shippingName, billingName, saved.Status, (int)domain.StatusCode, detail.Status, downloadRead.Status,
            pdf.Length, Convert.ToHexString(SHA256.HashData(pdf)).ToLowerInvariant(), item.Id, linked.Id,
            journal.SourceGeneration, journal.DestinationGeneration!.Value, true, true, true, true);
    }

    private sealed record CaseReceipt(string Party, string Language, int Id, int SupplierId, int EmployeeId,
        int ShippingAddressId, int BillingAddressId, string ShippingName, string BillingName,
        int SaveStatus, int DomainReadStatus, int BffReadStatus, int DownloadStatus, int PdfBytes, string PdfSha256,
        int ItemId, int LinkId, long SourceGeneration, long DestinationGeneration,
        bool ManualFieldsPreserved, bool AddressesPreserved, bool RealDocumentWitness, bool Reloaded);

    private static string NormalizePdfText(string value) => new(value.Where(character => !char.IsWhiteSpace(character)).ToArray());
}
