using System.Net;
using System.Collections.Concurrent;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Legacy.Maliev.Intranet.Contracts;
using Legacy.Maliev.Intranet.Tests;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Playwright;
using Xunit;

namespace Legacy.Maliev.Intranet.BrowserTests;

/// <summary>Eight real WASM lifecycle cases over the hosted isolated fixture and actual BFF, with synthetic current session/downstream authority.</summary>
[Collection("Customer document switch diagnostic")]
public sealed class CustomerDocumentsLifecycleBrowserTests
{
    [Theory]
    [InlineData("en")]
    [InlineData("th")]
    public async Task UploadUsesImmutableCommercialAssociationSnapshotAndCurrentServerToken(string culture)
    {
        var owner = new OwnerBoundary(); await using var app = await HostAsync(owner);
        using var runtime = await Playwright.CreateAsync(); await using var browser = await runtime.Chromium.LaunchAsync(new() { Headless = true });
        await using var context = await BrowserContext(browser); var page = await context.NewPageAsync(); using var barriers = owner.ReleaseAtEnd(); var diagnostics = Observe(page);
        try
        {
            await Load(page, app, culture);
            await page.Locator("#customer-document-title").FillAsync("uploaded-billing-evidence");
            await SelectNamedOption(page, "#customer-document-kind", Label(culture, "BillingInstruction"));
            await page.Locator("#upload-order-ids").FillAsync("11,12"); await page.Locator("#upload-quotation-id").FillAsync("21"); await page.Locator("#upload-replacement-ids").FillAsync("31");
            await SelectPdf(page); owner.DelaySession = true;
            await page.Locator("#customer-document-upload").ClickAsync(); await owner.SessionEntered.Task.WaitAsync(TimeSpan.FromSeconds(15));
            await page.Locator("#upload-order-ids").FillAsync("99"); await page.Locator("#customer-document-title").ClickAsync(); owner.ReleaseSession.TrySetResult();
            await owner.UploadReceived.Task.WaitAsync(TimeSpan.FromSeconds(15));
            Assert.Equal(42, owner.UploadCustomer); Assert.Equal("Bearer synthetic-acting-token", owner.UploadAuthorization);
            Assert.True(owner.SessionTokenIssued); Assert.Equal("BillingInstruction", owner.UploadKind);
            using var associations = JsonDocument.Parse(owner.UploadAssociations!);
            Assert.Equal(new[] { "Order:11", "Order:12", "Quotation:21", "Replacement:31" }, associations.RootElement.EnumerateArray().Select(x => x.GetProperty("Kind").GetString() + ":" + x.GetProperty("ResourceId").GetInt32()));
            await page.Locator("article h3").Filter(new() { HasText = "uploaded-billing-evidence" }).WaitForAsync();
            Assert.Contains(culture == "th" ? "รอเจ้าหน้าที่ตรวจสอบ" : "PendingVerification", await page.Locator("article").Last.InnerTextAsync());
            Assert.DoesNotContain("synthetic-acting-token", await page.ContentAsync());
            await Evidence(page, $"interactive-staff-{culture}-upload");
        }
        catch { await CaptureFailure(page, diagnostics, owner, culture); throw; }
    }

    [Theory]
    [InlineData("en")]
    [InlineData("th")]
    public async Task SecondNdaSaveUsesAuthoritativeRefreshedParentEpoch(string culture)
    {
        var owner = new OwnerBoundary { DistinctParentEpoch = true }; await using var app = await HostAsync(owner);
        using var runtime = await Playwright.CreateAsync(); await using var browser = await runtime.Chromium.LaunchAsync(new() { Headless = true });
        await using var context = await BrowserContext(browser); var page = await context.NewPageAsync(); using var barriers = owner.ReleaseAtEnd(); var diagnostics = Observe(page);
        try
        {
            await Load(page, app, culture);
            await page.Locator("article").GetByRole(AriaRole.Button, new() { Name = Label(culture, "Verify externally signed NDA"), Exact = true }).ClickAsync();
            string Field(string name) => $"#nda-{owner.Version:D}-{name}";
            await page.Locator(Field("party-one")).FillAsync("synthetic first party"); await page.Locator(Field("party-two")).FillAsync("synthetic second party");
            await SelectNamedOption(page, Field("coverage"), Label(culture, "Customer-wide coverage")); await page.Locator(Field("responsible")).FillAsync("synthetic-employee"); await page.Locator(Field("reason")).FillAsync("synthetic exact signed evidence");
            await page.Locator(Field("verify")).ClickAsync(); await owner.FirstNdaSave.Task.WaitAsync(TimeSpan.FromSeconds(15));
            await owner.ParentRefreshEntered.Task.WaitAsync(TimeSpan.FromSeconds(15));
            Assert.Equal(2, owner.FirstNdaReceiptEpoch); Assert.Equal(5, owner.Revision);
            owner.ReleaseParentRefresh.TrySetResult();
            await page.Locator("article ul li").Filter(new() { HasText = culture == "th" ? "ตรวจสอบแล้ว" : "Verified" }).WaitForAsync();
            await Assertions.Expect(page.Locator(Field("verify"))).ToBeEnabledAsync();
            await page.Locator(Field("verify")).ClickAsync(); await owner.SecondNdaSave.Task.WaitAsync(TimeSpan.FromSeconds(15));
            await Assertions.Expect(page.Locator(Field("verify"))).ToBeEnabledAsync();
            Assert.Equal(new long[] { 1, 5 }, owner.NdaEpochs); Assert.Equal(6, owner.Revision);
            Assert.DoesNotContain("synthetic-acting-token", await page.ContentAsync());
            await Evidence(page, $"interactive-staff-{culture}-nda");
        }
        catch { await CaptureFailure(page, diagnostics, owner, culture); throw; }
    }

    [Theory]
    [InlineData("en")]
    [InlineData("th")]
    public async Task VerifyRejectReadsExactReceiptEpochAndReloadsAfterConflict(string culture)
    {
        RecordPhase(culture, "before-owner-host");
        var owner = new OwnerBoundary { DocumentKind = "Evidence", Revision = 101, EvidenceEpoch = 7, ConflictFirstEvidence = true }; await using var app = await HostAsync(owner);
        RecordPhase(culture, "before-playwright");
        using var runtime = await Playwright.CreateAsync();
        RecordPhase(culture, "before-browser-launch");
        await using var browser = await runtime.Chromium.LaunchAsync(new() { Headless = true });
        RecordPhase(culture, "before-context");
        await using var context = await BrowserContext(browser);
        RecordPhase(culture, "before-page");
        var page = await context.NewPageAsync(); using var barriers = owner.ReleaseAtEnd(); var diagnostics = Observe(page);
        try
        {
            RecordPhase(culture, "before-load", owner);
            await Load(page, app, culture);
            RecordPhase(culture, "loaded", owner);
            await page.Locator($"#verification-{owner.Version:D}").FillAsync("synthetic verification reason");
            using var conflictCompletion = new CustomerDocumentRequestCompletion(page);
            var conflict = page.WaitForResponseAsync(response => response.Url.EndsWith($"/versions/{owner.Version:D}/verification", StringComparison.Ordinal) && response.Status == 409);
            RecordPhase(culture, "before-verify-click", owner);
            await page.Locator("article").GetByRole(AriaRole.Button, new() { Name = Label(culture, "Verify exact version"), Exact = true }).ClickAsync();
            RecordPhase(culture, "verify-click-completed", owner);
            var conflictResponse = await conflict;
            RecordPhase(culture, "conflict-headers-observed", owner);
            Assert.Equal(409, conflictResponse.Status);
            await conflictCompletion.WaitAsync(conflictResponse);
            RecordPhase(culture, "conflict-request-terminal", owner);
            await owner.EvidenceConflictRefreshEntered.Task.WaitAsync(TimeSpan.FromSeconds(15));
            Assert.Equal(109, owner.Revision); Assert.Equal(17, owner.ReceiptEpoch);
            RecordPhase(culture, "before-release-refresh", owner);
            owner.ReleaseEvidenceConflictRefresh.TrySetResult();
            await page.GetByRole(AriaRole.Alert).WaitForAsync(); await Assertions.Expect(page.GetByRole(AriaRole.Button, new() { Name = Label(culture, "Reload"), Exact = true })).ToBeEnabledAsync();
            await page.Locator("article").GetByRole(AriaRole.Button, new() { Name = Label(culture, "Reject exact version"), Exact = true }).ClickAsync();
            await owner.EvidenceRejected.Task.WaitAsync(TimeSpan.FromSeconds(15));
            await Assertions.Expect(page.GetByRole(AriaRole.Button, new() { Name = Label(culture, "Reload"), Exact = true })).ToBeEnabledAsync();
            Assert.Equal(new long[] { 7, 17 }, owner.EvidenceRequests.Select(x => x.ExpectedVerificationRevision));
            Assert.Equal(new[] { "Verified", "Rejected" }, owner.EvidenceRequests.Select(x => x.Status));
            Assert.Equal(2, owner.ReceiptReads); Assert.Equal("Rejected", owner.EvidenceStatus);
            Assert.All(owner.EvidenceAuthorizations, value => Assert.Equal("Bearer synthetic-acting-token", value));
            Assert.DoesNotContain("synthetic-acting-token", await page.ContentAsync());
            RecordPhase(culture, "assertions-passed-before-evidence", owner);
            await Evidence(page, $"interactive-staff-{culture}-evidence");
        }
        catch { RecordPhase(culture, "failure-before-capture", owner); await CaptureFailure(page, diagnostics, owner, culture); RecordPhase(culture, "failure-after-capture-before-cleanup", owner); throw; }
    }

    [Theory]
    [InlineData("en")]
    [InlineData("th")]
    public async Task ReminderFiltersUseCurrentStaffAndClearMetadataOnDeniedAndUnavailable(string culture)
    {
        var owner = new OwnerBoundary { ShowReminders = true }; await using var app = await HostAsync(owner);
        using var runtime = await Playwright.CreateAsync(); await using var browser = await runtime.Chromium.LaunchAsync(new() { Headless = true });
        await using var context = await BrowserContext(browser); var page = await context.NewPageAsync(); using var barriers = owner.ReleaseAtEnd(); var diagnostics = Observe(page);
        try
        {
            await Load(page, app, culture);
            await page.Locator("#customer-nda-reminders").GetByRole(AriaRole.Button, new() { Name = Label(culture, "My NDA reminders"), Exact = true }).ClickAsync();
            await page.Locator("#nda-due-from").FillAsync("2026-10-01"); await page.Locator("#nda-due-through").FillAsync("2026-10-31");
            await SelectNamedOption(page, "#nda-reminder-state", Label(culture, "Missed")); await SelectNamedOption(page, "#nda-reminder-limit", "50");
            var filtered = page.WaitForResponseAsync(response => response.Url.Contains("/bff/staff/nda-reminders?", StringComparison.Ordinal) && response.Url.Contains("state=Missed", StringComparison.Ordinal));
            await page.Locator("#customer-nda-reminders").GetByRole(AriaRole.Button, new() { Name = Label(culture, "Apply reminder filters"), Exact = true }).ClickAsync(); Assert.Equal(200, (await filtered).Status);
            await Assertions.Expect(page.GetByRole(AriaRole.Button, new() { Name = Label(culture, "Reload"), Exact = true })).ToBeEnabledAsync();
            var query = QueryHelpers.ParseQuery(owner.ReminderQuery!);
            Assert.Equal("50", query["limit"].ToString()); Assert.Equal("Missed", query["state"].ToString());
            Assert.Equal(new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.Zero), DateTimeOffset.Parse(query["dueFromUtc"].ToString()));
            Assert.Equal(new DateTimeOffset(2026, 11, 1, 0, 0, 0, TimeSpan.Zero).AddTicks(-1), DateTimeOffset.Parse(query["dueThroughUtc"].ToString()));
            Assert.Equal("Bearer synthetic-acting-token", owner.ReminderAuthorization); Assert.Equal("synthetic-employee", owner.ReminderRecipient);
            Assert.Equal(1, await page.Locator("#customer-nda-reminders ul li").CountAsync());
            foreach (var failure in new[] { 403, 503 })
            {
                owner.ReminderStatus = failure;
                using var refusalCompletion = new CustomerDocumentRequestCompletion(page);
                var refused = page.WaitForResponseAsync(response => response.Url.Contains("/bff/staff/nda-reminders?", StringComparison.Ordinal) && response.Status == failure);
                await page.Locator("#customer-nda-reminders").GetByRole(AriaRole.Button, new() { Name = Label(culture, "Apply reminder filters"), Exact = true }).ClickAsync(); var refusalResponse = await refused; Assert.Equal(failure, refusalResponse.Status); await refusalCompletion.WaitAsync(refusalResponse);
                await page.GetByRole(AriaRole.Alert).WaitForAsync(); await Assertions.Expect(page.GetByRole(AriaRole.Button, new() { Name = Label(culture, "Reload"), Exact = true })).ToBeEnabledAsync();
                Assert.Equal(0, await page.Locator("article").CountAsync()); Assert.Equal(0, await page.Locator("#customer-nda-reminders ul li").CountAsync());
                Assert.DoesNotContain("old-customer-42", await page.Locator("section").InnerTextAsync());
                if (failure == 403) { owner.ReminderStatus = 200; await page.GetByRole(AriaRole.Button, new() { Name = Label(culture, "Reload"), Exact = true }).ClickAsync(); await page.Locator("article h3").Filter(new() { HasText = "old-customer-42" }).WaitForAsync(); Assert.Equal(1, await page.Locator("#customer-nda-reminders ul li").CountAsync()); }
            }
            Assert.Equal(0, owner.Mutations); Assert.DoesNotContain("synthetic-acting-token", await page.ContentAsync());
            await Evidence(page, $"interactive-staff-{culture}-reminders");
        }
        catch { await CaptureFailure(page, diagnostics, owner, culture); throw; }
    }

    private static async Task SelectNamedOption(IPage page, string id, string name)
    {
        var trigger = page.Locator(id);
        await Assertions.Expect(trigger).ToHaveAttributeAsync("role", "combobox");
        await trigger.ClickAsync();
        await page.GetByRole(AriaRole.Option, new() { Name = name, Exact = true }).ClickAsync();
    }
    private static void RecordPhase(string culture, string phase, OwnerBoundary? owner = null)
    {
        try
        {
            var root = Environment.GetEnvironmentVariable("TASK4_BROWSER_EVIDENCE");
            if (string.IsNullOrWhiteSpace(root)) return;
            Directory.CreateDirectory(root);
            File.WriteAllText(Path.Combine(root, $"interactive-staff-{culture}-evidence-phase.json"), JsonSerializer.Serialize(new { Phase = phase, OwnerPaths = owner?.ObservedPaths.ToArray() ?? [], ConflictRefreshEntered = owner?.EvidenceConflictRefreshEntered.Task.IsCompleted ?? false, ConflictRefreshReleased = owner?.ReleaseEvidenceConflictRefresh.Task.IsCompleted ?? false }));
        }
        catch { /* Diagnostic writes must preserve the original test outcome and capture path. */ }
    }
    private static async Task CaptureFailure(IPage page, ConcurrentQueue<string> diagnostics, OwnerBoundary owner, string culture)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        try { await DiagnosticEvidence(page, diagnostics, owner, $"interactive-staff-{culture}-lifecycle-failure-{Guid.NewGuid():N}", deadline.Token).WaitAsync(TimeSpan.FromSeconds(10)); }
        catch { /* Preserve the original test failure. */ }
        finally { try { deadline.Cancel(); } catch { /* Preserve the original failure. */ } owner.ReleaseAll(); }
    }
    private static string Label(string culture, string key) => culture != "th" ? key : key switch
    {
        "BillingInstruction" => "\u0e04\u0e33\u0e41\u0e19\u0e30\u0e19\u0e33\u0e01\u0e32\u0e23\u0e40\u0e23\u0e35\u0e22\u0e01\u0e40\u0e01\u0e47\u0e1a\u0e40\u0e07\u0e34\u0e19",
        "Missed" => "\u0e40\u0e25\u0e22\u0e01\u0e33\u0e2b\u0e19\u0e14",
        "My NDA reminders" => "\u0e07\u0e32\u0e19\u0e40\u0e15\u0e37\u0e2d\u0e19 NDA \u0e02\u0e2d\u0e07\u0e09\u0e31\u0e19",
        "Apply reminder filters" => "\u0e43\u0e0a\u0e49\u0e15\u0e31\u0e27\u0e01\u0e23\u0e2d\u0e07\u0e23\u0e32\u0e22\u0e01\u0e32\u0e23\u0e40\u0e15\u0e37\u0e2d\u0e19",
        "Verify exact version" => "\u0e15\u0e23\u0e27\u0e08\u0e2a\u0e2d\u0e1a\u0e23\u0e38\u0e48\u0e19\u0e19\u0e35\u0e49",
        "Reject exact version" => "\u0e1b\u0e0f\u0e34\u0e40\u0e2a\u0e18\u0e23\u0e38\u0e48\u0e19\u0e19\u0e35\u0e49",
        "Reload" => "\u0e42\u0e2b\u0e25\u0e14\u0e43\u0e2b\u0e21\u0e48",
        "Verify externally signed NDA" => "\u0e15\u0e23\u0e27\u0e08\u0e2a\u0e2d\u0e1a NDA \u0e17\u0e35\u0e48\u0e25\u0e07\u0e19\u0e32\u0e21\u0e20\u0e32\u0e22\u0e19\u0e2d\u0e01\u0e41\u0e25\u0e49\u0e27",
        "Customer-wide coverage" => "\u0e04\u0e23\u0e2d\u0e1a\u0e04\u0e25\u0e38\u0e21\u0e07\u0e32\u0e19\u0e17\u0e31\u0e49\u0e07\u0e2b\u0e21\u0e14\u0e02\u0e2d\u0e07\u0e25\u0e39\u0e01\u0e04\u0e49\u0e32",
        _ => throw new ArgumentOutOfRangeException(nameof(key), key, "Missing fixture translation")
    };
    private static async Task<FixtureHost> HostAsync(OwnerBoundary owner)
    {
        var configured = Environment.GetEnvironmentVariable("TASK4_INTERACTIVE_ASSET_ROOT");
        if (string.IsNullOrWhiteSpace(configured)) throw new InvalidOperationException("TASK4_INTERACTIVE_ASSET_ROOT must name the isolated published wwwroot; missing assets are a failure, never a skip.");
        var root = Path.GetFullPath(configured);
        if (!File.Exists(Path.Combine(root, "index.html")) || !File.Exists(Path.Combine(root, "_framework", "blazor.webassembly.js"))) throw new InvalidOperationException("Publish the isolated WASM fixture before this validation.");
        var handler = new CustomerDocumentBffTests.RecordingHandler("[]") { ResponseFactory = owner.ReadAsync };
        var app = await CustomerDocumentBffTests.HostAsync(handler, loopbackBrowser: true, configure: app =>
        {
            app.Services.GetRequiredService<IOptions<AntiforgeryOptions>>().Value.HeaderName = "X-CSRF-TOKEN";
            app.MapGet("/bff/session", async (HttpContext context, IAntiforgery antiforgery) =>
            {
                var token = antiforgery.GetAndStoreTokens(context).RequestToken; owner.SessionTokenIssued = !string.IsNullOrWhiteSpace(token);
                if (owner.DelaySession) { owner.SessionEntered.TrySetResult(); await owner.ReleaseSession.Task.WaitAsync(context.RequestAborted); }
                return Results.Json(new EmployeeSessionSummary(true, "synthetic-employee", null, [], token));
            }).RequireAuthorization();
            var mime = new FileExtensionContentTypeProvider(); mime.Mappings[".wasm"] = "application/wasm";
            app.MapGet("/{**asset}", (string? asset) =>
            {
                var relative = string.IsNullOrEmpty(asset) ? "index.html" : asset;
                var path = Path.GetFullPath(Path.Combine(root, relative));
                if (!path.StartsWith(root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) || !File.Exists(path)) return Results.NotFound();
                return Results.File(path, mime.TryGetContentType(path, out var type) ? type : "application/octet-stream");
            });
        });
        return new FixtureHost(app, owner);
    }
    private static Task<IBrowserContext> BrowserContext(IBrowser browser) => browser.NewContextAsync(new() { ExtraHTTPHeaders = new Dictionary<string, string> { ["Synthetic-Employee"] = "yes" } });
    private static async Task Load(IPage page, FixtureHost app, string culture) { await page.GotoAsync(app.Urls.Single() + "/?culture=" + culture); await Assertions.Expect(page.Locator("#fixture-customer-43")).ToHaveCSSAsync("display", "inline-flex"); await page.Locator("article h3").Filter(new() { HasText = "old-customer-42" }).WaitForAsync(); }
    private static Task SelectPdf(IPage page) => page.Locator("#customer-document-file").SetInputFilesAsync(new FilePayload { Name = "synthetic.pdf", MimeType = "application/pdf", Buffer = Encoding.ASCII.GetBytes("%PDF-1.4\n1 0 obj<</Type/Catalog>>endobj\ntrailer<</Root 1 0 R>>\n%%EOF\n") });
    private static ConcurrentQueue<string> Observe(IPage page)
    {
        var entries = new ConcurrentQueue<string>();
        static string PathOnly(string url) => Uri.TryCreate(url, UriKind.Absolute, out var uri) ? uri.AbsolutePath : "[invalid URL]";
        page.Request += (_, request) => entries.Enqueue("request " + request.Method + " " + PathOnly(request.Url));
        page.Response += (_, response) => entries.Enqueue("response " + response.Status + " " + PathOnly(response.Url));
        page.RequestFailed += (_, request) => entries.Enqueue("request-failed " + request.Method + " " + PathOnly(request.Url));
        page.Console += (_, message) => entries.Enqueue("console " + message.Type + " " + SafeDiagnostic(message.Text));
        page.PageError += (_, error) => entries.Enqueue("page-error " + SafeDiagnostic(error));
        return entries;
    }
    private static string SafeDiagnostic(string text)
    {
        if (text.Contains("token", StringComparison.OrdinalIgnoreCase) || text.Contains("authorization", StringComparison.OrdinalIgnoreCase) || text.Contains("csrf", StringComparison.OrdinalIgnoreCase)) return "[redacted sensitive diagnostic]";
        return text.Length <= 4096 ? text : text[..4096];
    }
    private static async Task DiagnosticEvidence(IPage page, ConcurrentQueue<string> diagnostics, OwnerBoundary owner, string name, CancellationToken token)
    {
        var root = Environment.GetEnvironmentVariable("TASK4_BROWSER_EVIDENCE") ?? throw new InvalidOperationException("TASK4_BROWSER_EVIDENCE is required.");
        Directory.CreateDirectory(root);
        await File.WriteAllTextAsync(Path.Combine(root, name + ".diagnostics.json"), JsonSerializer.Serialize(new { Browser = diagnostics.ToArray(), OwnerPaths = owner.ObservedPaths.ToArray(), ListBarrierEntered = owner.ListEntered.Task.IsCompleted, ListBarrierReleased = owner.ReleaseList.Task.IsCompleted, owner.Mutations }, new JsonSerializerOptions { WriteIndented = true }), token);
        var screenshot = await page.ScreenshotAsync(new() { FullPage = true, Timeout = 3000 }).WaitAsync(TimeSpan.FromSeconds(4), token);
        await File.WriteAllBytesAsync(Path.Combine(root, name + ".png"), screenshot, token);
        var html = await page.Locator("html").EvaluateAsync<string>("element => element.outerHTML", null, new() { Timeout = 2000 }).WaitAsync(TimeSpan.FromSeconds(3), token);
        await File.WriteAllTextAsync(Path.Combine(root, name + ".html"), html, token);
    }
    private static async Task Evidence(IPage page, string name)
    {
        var root = Environment.GetEnvironmentVariable("TASK4_BROWSER_EVIDENCE") ?? throw new InvalidOperationException("TASK4_BROWSER_EVIDENCE is required.");
        Directory.CreateDirectory(root);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        try
        {
            var screenshot = await page.ScreenshotAsync(new() { FullPage = true, Timeout = 3000 }).WaitAsync(TimeSpan.FromSeconds(4), deadline.Token);
            await File.WriteAllBytesAsync(Path.Combine(root, name + ".png"), screenshot, deadline.Token);
            var html = await page.Locator("html").EvaluateAsync<string>("element => element.outerHTML", null, new() { Timeout = 2000 }).WaitAsync(TimeSpan.FromSeconds(3), deadline.Token);
            await File.WriteAllTextAsync(Path.Combine(root, name + ".html"), html, deadline.Token);
        }
        finally { deadline.Cancel(); }
    }

    private sealed class FixtureHost(WebApplication app, OwnerBoundary owner) : IAsyncDisposable
    {
        public ICollection<string> Urls => app.Urls;
        public async ValueTask DisposeAsync()
        {
            owner.ReleaseAll();
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            try { await app.StopAsync(timeout.Token).WaitAsync(TimeSpan.FromSeconds(10)); }
            finally { await app.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(10)); }
        }
    }
    private sealed class BarrierLease(Action release) : IDisposable { public void Dispose() => release(); }
    private sealed class OwnerBoundary
    {
        public readonly Guid Document = Guid.NewGuid(), Version = Guid.NewGuid(), UploadedDocument = Guid.NewGuid();
        public long Revision = 1, EvidenceEpoch = 1, FirstNdaReceiptEpoch; public bool DelaySession, SessionTokenIssued, DistinctParentEpoch;
        public string DocumentKind = "Nda", EvidenceStatus = "PendingVerification"; public bool ConflictFirstEvidence, ShowReminders;
        public long ReceiptEpoch = 7; public int ReceiptReads, ReminderStatus = 200; public string? ReminderQuery, ReminderAuthorization, ReminderRecipient;
        public readonly List<CustomerDocumentVerificationRequest> EvidenceRequests = []; public readonly List<string?> EvidenceAuthorizations = [];
        public readonly ConcurrentQueue<string> ObservedPaths = new();
        public readonly TaskCompletionSource EvidenceConflictRefreshEntered = new(TaskCreationOptions.RunContinuationsAsynchronously), ReleaseEvidenceConflictRefresh = new(TaskCreationOptions.RunContinuationsAsynchronously), EvidenceRejected = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public readonly TaskCompletionSource ParentRefreshEntered = new(TaskCreationOptions.RunContinuationsAsynchronously), ReleaseParentRefresh = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int UploadCustomer, Mutations; public string? UploadAuthorization, UploadAssociations, UploadKind;
        public readonly List<long> NdaEpochs = [];
        public readonly TaskCompletionSource SessionEntered = new(TaskCreationOptions.RunContinuationsAsynchronously), ReleaseSession = new(TaskCreationOptions.RunContinuationsAsynchronously), UploadReceived = new(TaskCreationOptions.RunContinuationsAsynchronously), FirstNdaSave = new(TaskCreationOptions.RunContinuationsAsynchronously), SecondNdaSave = new(TaskCreationOptions.RunContinuationsAsynchronously), ListEntered = new(TaskCreationOptions.RunContinuationsAsynchronously), ReleaseList = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public IDisposable ReleaseAtEnd() => new BarrierLease(ReleaseAll);
        public void ReleaseAll() { ReleaseSession.TrySetResult(); ReleaseList.TrySetResult(); ReleaseParentRefresh.TrySetResult(); ReleaseEvidenceConflictRefresh.TrySetResult(); }
        public async Task<HttpResponseMessage> ReadAsync(HttpRequestMessage request, CancellationToken token)
        {
            var path = request.RequestUri!.AbsolutePath; var parts = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
            ObservedPaths.Enqueue(request.Method + " " + path);
            if (path == "/staff/nda-reminders")
            {
                ReminderQuery = request.RequestUri.Query; ReminderAuthorization = request.Headers.Authorization?.ToString(); ReminderRecipient = "synthetic-employee";
                if (ReminderStatus != 200) return new((HttpStatusCode)ReminderStatus);
                if (!ShowReminders) return Json(Array.Empty<CustomerNdaReminder>());
                var query = QueryHelpers.ParseQuery(ReminderQuery); var selectedState = query.TryGetValue("state", out var state) ? state.ToString() : "Due";
                return Json(new[] { new CustomerNdaReminder(Guid.NewGuid(), Guid.NewGuid(), Version, 1, 7, ReminderRecipient, new DateTimeOffset(2026, 10, 8, 0, 0, 0, TimeSpan.Zero), selectedState) });
            }
            var customer = int.Parse(parts[1]);
            if (path.EndsWith("/receipt", StringComparison.Ordinal)) { ReceiptReads++; return Json(EvidenceReceipt(customer)); }
            if (request.Method == HttpMethod.Post && path.EndsWith("/verification", StringComparison.Ordinal) && !path.EndsWith("/nda/verification", StringComparison.Ordinal))
            {
                var input = await request.Content!.ReadFromJsonAsync<CustomerDocumentVerificationRequest>(cancellationToken: token); Mutations++; EvidenceRequests.Add(input!); EvidenceAuthorizations.Add(request.Headers.Authorization?.ToString());
                if (ConflictFirstEvidence && EvidenceRequests.Count == 1) { Revision = 109; ReceiptEpoch = 17; return new(HttpStatusCode.Conflict); }
                if (input!.ExpectedVerificationRevision != ReceiptEpoch) { EvidenceRejected.TrySetResult(); return new(HttpStatusCode.Conflict); }
                ReceiptEpoch++; EvidenceEpoch = ReceiptEpoch; Revision++; EvidenceStatus = input.Status; var response = Json(EvidenceReceipt(customer)); EvidenceRejected.TrySetResult(); return response;
            }
            if (request.Method == HttpMethod.Post && path.EndsWith("/nda/verification", StringComparison.Ordinal))
            {
                var input = await request.Content!.ReadFromJsonAsync<CustomerNdaVerificationRequest>(cancellationToken: token); Mutations++; NdaEpochs.Add(input!.ExpectedRevision);
                if (NdaEpochs.Count == 2) SecondNdaSave.TrySetResult();
                if (input.ExpectedRevision != Revision) return new(HttpStatusCode.Conflict);
                Revision++; EvidenceEpoch = Revision; var receipt = new CustomerNdaVerificationReceipt(Guid.NewGuid(), Document, Version, EvidenceEpoch, "Active", "Protected");
                if (NdaEpochs.Count == 1) { FirstNdaReceiptEpoch = EvidenceEpoch; if (DistinctParentEpoch) Revision = 5; FirstNdaSave.TrySetResult(); }
                return Json(receipt);
            }
            if (request.Method == HttpMethod.Post)
            {
                Mutations++; var multipart = Assert.IsType<MultipartFormDataContent>(request.Content); var fields = new Dictionary<string, string>();
                foreach (var part in multipart) if (part.Headers.ContentDisposition?.FileName is null) fields[part.Headers.ContentDisposition!.Name!.Trim('"')] = await part.ReadAsStringAsync(token);
                UploadCustomer = customer; UploadAuthorization = request.Headers.Authorization?.ToString(); UploadAssociations = fields["Associations"]; UploadKind = fields["Kind"]; UploadReceived.TrySetResult();
                return Json(new CustomerDocumentVersionReceipt(UploadedDocument, Guid.NewGuid(), customer, 1, new string('a', 64), 1));
            }
            if (path.EndsWith("/nda", StringComparison.Ordinal)) return new(HttpStatusCode.NotFound);
            if (path.EndsWith("/versions", StringComparison.Ordinal))
            {
                var uploaded = parts[3] == UploadedDocument.ToString("D");
                var status = DocumentKind == "Nda" ? EvidenceEpoch > 1 ? "Verified" : "PendingVerification" : EvidenceStatus;
                return Json(new[] { new CustomerDocumentVersionSummary(uploaded ? UploadedDocument : Document, Version, 1, uploaded ? "BillingInstruction" : DocumentKind, new string('a', 64), DateTimeOffset.UtcNow, uploaded ? "PendingVerification" : status, !uploaded && status is "Verified" or "Rejected" ? "synthetic-employee" : null, !uploaded && status is "Verified" or "Rejected" ? DateTimeOffset.UtcNow : null, uploaded ? 1 : EvidenceEpoch) });
            }
            if (DistinctParentEpoch && NdaEpochs.Count == 1 && Revision == 5) { ParentRefreshEntered.TrySetResult(); await ReleaseParentRefresh.Task.WaitAsync(token); }
            if (ConflictFirstEvidence && EvidenceRequests.Count == 1 && Revision == 109) { EvidenceConflictRefreshEntered.TrySetResult(); await ReleaseEvidenceConflictRefresh.Task.WaitAsync(token); }
            var documents = new List<CustomerDocumentSummary> { new(Document, customer, DocumentKind, customer == 42 ? "old-customer-42" : "current-customer-43", "Internal", Revision) };
            if (UploadCustomer == customer) documents.Add(new(UploadedDocument, customer, "BillingInstruction", "uploaded-billing-evidence", "Internal", 1)); return Json(documents);
        }
        private CustomerDocumentReceipt EvidenceReceipt(int customer) => new(Document, Version, customer, DocumentKind, new string('a', 64), null, [], EvidenceStatus, EvidenceStatus is "Verified" or "Rejected" ? "synthetic-employee" : null, EvidenceStatus is "Verified" or "Rejected" ? DateTimeOffset.UtcNow : null, ReceiptEpoch);
        private static HttpResponseMessage Json<T>(T body) => new(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json") };
    }
}
