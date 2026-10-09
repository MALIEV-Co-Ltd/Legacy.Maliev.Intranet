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

[CollectionDefinition("Customer document switch diagnostic", DisableParallelization = true)]
public sealed class CustomerDocumentSwitchDiagnosticCollection { }

/// <summary>Controlled synthetic session/downstream; real WASM events and same-origin actual BFF.</summary>
[Collection("Customer document switch diagnostic")]
public sealed class CustomerDocumentsCustomerSwitchBrowserTests
{
    [Theory]
    [InlineData("en")]
    [InlineData("th")]
    public async Task LateCustomerResponseCannotRestorePriorInputsOrMetadata(string culture)
    {
        var owner = new OwnerBoundary(); await using var app = await HostAsync(owner);
        using var runtime = await Playwright.CreateAsync(); await using var browser = await runtime.Chromium.LaunchAsync(new() { Headless = true });
        await using var context = await BrowserContext(browser); var page = await context.NewPageAsync(); using var barriers = owner.ReleaseAtEnd();
        var diagnostics = Observe(page); await Load(page, app, culture);
        await page.Locator("#customer-document-title").FillAsync("old customer title"); await page.Locator("#upload-order-ids").FillAsync("11"); await page.Locator("#upload-quotation-id").FillAsync("21"); await page.Locator("#upload-replacement-ids").FillAsync("31"); await SelectPdf(page);
        owner.DelayCustomer42 = true; await page.Locator("section > button").ClickAsync(); await owner.ListEntered.Task.WaitAsync(TimeSpan.FromSeconds(15));
        await page.Locator("#fixture-customer-43").ClickAsync();
        try { await page.Locator("article h3").Filter(new() { HasText = "current-customer-43" }).WaitForAsync(); }
        catch
        {
            using var captureDeadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            try { await DiagnosticEvidence(page, diagnostics, owner, $"interactive-staff-{culture}-switch-failure", captureDeadline.Token).WaitAsync(TimeSpan.FromSeconds(10)); }
            catch { /* Best-effort diagnostics must preserve the original test failure. */ }
            finally { try { captureDeadline.Cancel(); } catch { /* Preserve the original failure. */ } owner.ReleaseAll(); }
            throw;
        }
        var late = page.WaitForResponseAsync(response => response.Url.EndsWith("/bff/customers/42/documents", StringComparison.Ordinal));
        owner.ReleaseList.TrySetResult(); var lateResponse = await late; await lateResponse.FinishedAsync().WaitAsync(TimeSpan.FromSeconds(10));
        await page.EvaluateAsync("() => new Promise(resolve => requestAnimationFrame(() => requestAnimationFrame(resolve)))").WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal("", await page.Locator("#customer-document-title").InputValueAsync());
        foreach (var id in new[] { "upload-order-ids", "upload-quotation-id", "upload-replacement-ids" }) Assert.Equal("", await page.Locator("#" + id).InputValueAsync());
        Assert.Equal(0, await page.Locator("#customer-document-file").EvaluateAsync<int>("element => element.files.length"));
        Assert.Contains("current-customer-43", await page.Locator("section").InnerTextAsync()); Assert.DoesNotContain("old-customer-42", await page.Locator("section").InnerTextAsync());
        Assert.Equal(0, owner.Mutations); await Evidence(page, $"interactive-staff-{culture}-switch");
    }

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
            app.MapGet("/bff/session", (HttpContext context, IAntiforgery antiforgery) =>
            {
                var token = antiforgery.GetAndStoreTokens(context).RequestToken; owner.SessionTokenIssued = !string.IsNullOrWhiteSpace(token);
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
        public readonly Guid Document = Guid.NewGuid(), Version = Guid.NewGuid();
        public bool DelayCustomer42, SessionTokenIssued;
        public int Mutations;
        public readonly ConcurrentQueue<string> ObservedPaths = new();
        public readonly TaskCompletionSource ListEntered = new(TaskCreationOptions.RunContinuationsAsynchronously), ReleaseList = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public IDisposable ReleaseAtEnd() => new BarrierLease(ReleaseAll);
        public void ReleaseAll() => ReleaseList.TrySetResult();
        public async Task<HttpResponseMessage> ReadAsync(HttpRequestMessage request, CancellationToken token)
        {
            var path = request.RequestUri!.AbsolutePath;
            ObservedPaths.Enqueue(request.Method + " " + path);
            if (request.Method != HttpMethod.Get) { Mutations++; return new(HttpStatusCode.MethodNotAllowed); }
            if (path == "/staff/nda-reminders") return Json(Array.Empty<CustomerNdaReminder>());
            var parts = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
            var customer = int.Parse(parts[1]);
            if (path.EndsWith("/nda", StringComparison.Ordinal)) return new(HttpStatusCode.NotFound);
            if (path.EndsWith("/versions", StringComparison.Ordinal))
                return Json(new[] { new CustomerDocumentVersionSummary(Document, Version, 1, "Nda", new string('a', 64), DateTimeOffset.UtcNow, "PendingVerification", null, null, 1) });
            if (DelayCustomer42 && customer == 42) { ListEntered.TrySetResult(); await ReleaseList.Task.WaitAsync(token); }
            return Json(new[] { new CustomerDocumentSummary(Document, customer, "Nda", customer == 42 ? "old-customer-42" : "current-customer-43", "Internal", 1) });
        }
        private static HttpResponseMessage Json<T>(T body) => new(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json") };
    }
}