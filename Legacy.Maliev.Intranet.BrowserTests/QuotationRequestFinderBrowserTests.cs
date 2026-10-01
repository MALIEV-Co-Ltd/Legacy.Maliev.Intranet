using System.Collections.Concurrent;
using System.Text.Json;
using Legacy.Maliev.Intranet.BrowserTests.Infrastructure;
using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace Legacy.Maliev.Intranet.BrowserTests;

/// <summary>Real Chromium DOM and captured UI wire behavior; intercepted BFF is not cookie-auth/PG acceptance.</summary>
[Collection(CustomerBrowserCollection.Name)]
public sealed class QuotationRequestFinderBrowserTests(IntranetClientServerFixture server, PlaywrightFixture playwright)
{
    private const string Timestamp = "2030-07-18T08:30:00";
    private const string UnsafeNote = "</textarea><img id=finder-injected src=x onerror=window.finderInjected=true> ไทย / English";
    private const string Envelope = """
        {"source":"service_finder","version":1,"answers":{"files":"files-real-part","service":"service-3d","material":"material-plastic","quantity":"quantity-1-10","end-use":"use-prototype","performance":"performance-strength","environment":"environment-outdoor","future_answer":{"keep":[1,true,null,"ไทย / English"]}},"recommended_service_ids":["scanning","design"],"finder_path":["scanning","design"],"operator_comment":"original staff note","future_context":{"keep":"unchanged","text":"<tag> & ไทย / English"}}
        """;

    [Theory]
    [InlineData("en-TH", "Service finder context", "Physical part available", "Strength and durability", "Outdoor or UV exposure", "3D scanning → 3D design")]
    [InlineData("th-TH", "ข้อมูลจากตัวช่วยเลือกบริการ", "มีชิ้นงานจริง", "ความแข็งแรงและความทนทาน", "กลางแจ้งหรือแสงยูวี", "สแกนสามมิติ → ออกแบบสามมิติ")]
    public async Task FinderSummary_LocalizedDom_EncodesNoteAndPreservesEnvelopeOnSave(
        string culture, string title, string files, string performance, string environment, string path)
    {
        await using var context = await ContextAsync(culture);
        var page = await context.NewPageAsync();
        var original = Envelope.Replace("original staff note", UnsafeNote, StringComparison.Ordinal);
        var boundary = new Boundary(original);
        await boundary.InstallAsync(page);
        await OpenAsync(page);

        var summary = page.Locator("[data-service-finder-context]");
        foreach (var label in new[] { title, files, performance, environment, path })
            await Expect(summary).ToContainTextAsync(label);
        Assert.DoesNotContain("future_context", await summary.InnerTextAsync(), StringComparison.Ordinal);
        Assert.DoesNotContain("future_answer", await summary.InnerTextAsync(), StringComparison.Ordinal);
        Assert.Equal(0, await summary.Locator("img,script").CountAsync());
        Assert.Equal(0, await page.Locator("#finder-injected").CountAsync());
        Assert.False(await page.EvaluateAsync<bool>("() => window.finderInjected === true"));
        var note = page.Locator("#request-internal");
        await Expect(note).ToHaveValueAsync(UnsafeNote);
        Assert.DoesNotContain("service_finder", await note.InputValueAsync(), StringComparison.Ordinal);
        await CaptureAsync(page, $"{culture}-finder-summary");

        await note.FillAsync("  Reviewed / ตรวจสอบแล้ว  ");
        await SaveAsync(page, culture);
        await Expect(page.GetByText(Saved(culture), new() { Exact = true })).ToBeVisibleAsync();
        var capture = Assert.Single(boundary.Updates);
        using var body = AssertWire(capture);
        AssertEnvelopePreserved(original, body.RootElement.GetProperty("internalComment").GetString()!, "Reviewed / ตรวจสอบแล้ว");
        await Expect(page.Locator("#request-internal")).ToHaveValueAsync("Reviewed / ตรวจสอบแล้ว");
        Assert.Equal(2, boundary.DetailReads);
    }

    [Fact]
    public async Task FinderNote_ClearedInDom_RemovesOnlyOperatorComment()
    {
        await using var context = await ContextAsync("en-TH");
        var page = await context.NewPageAsync();
        var boundary = new Boundary(Envelope);
        await boundary.InstallAsync(page);
        await OpenAsync(page);
        await page.Locator("#request-internal").FillAsync(string.Empty);
        await SaveAsync(page, "en-TH");
        await Expect(page.GetByText(Saved("en-TH"), new() { Exact = true })).ToBeVisibleAsync();
        using var body = AssertWire(Assert.Single(boundary.Updates));
        AssertEnvelopePreserved(Envelope, body.RootElement.GetProperty("internalComment").GetString()!, null);
        await Expect(page.Locator("#request-internal")).ToHaveValueAsync(string.Empty);
        await Expect(page.Locator("[data-service-finder-context]")).ToBeVisibleAsync();
    }

    [Theory]
    [InlineData("en-TH", "This request changed after it was loaded. Reload and try again.")]
    [InlineData("th-TH", "คำขอนี้ถูกแก้ไขหลังจากที่คุณเปิดหน้า โปรดโหลดข้อมูลล่าสุดแล้วลองอีกครั้ง")]
    public async Task FinderNote_Stale409_KeepsVisibleLocalNoteWithoutReloadOrFalseSuccess(string culture, string conflict)
    {
        await using var context = await ContextAsync(culture);
        var page = await context.NewPageAsync();
        var boundary = new Boundary(Envelope, conflict: true);
        await boundary.InstallAsync(page);
        await OpenAsync(page);
        const string localNote = "Unsaved review / หมายเหตุที่ยังไม่บันทึก";
        await page.Locator("#request-internal").FillAsync(localNote);
        await SaveAsync(page, culture);
        await Expect(page.GetByText(conflict, new() { Exact = true })).ToBeVisibleAsync();

        using var body = AssertWire(Assert.Single(boundary.Updates));
        AssertEnvelopePreserved(Envelope, body.RootElement.GetProperty("internalComment").GetString()!, localNote);
        Assert.Equal(Envelope, boundary.CurrentComment);
        Assert.Equal(1, boundary.DetailReads);
        Assert.Equal(0, await page.GetByText(Saved(culture), new() { Exact = true }).CountAsync());
        await CaptureAsync(page, $"{culture}-finder-conflict");
        var note = page.Locator("#request-internal");
        Assert.True(await note.CountAsync() == 1, "The conflict response removed the local-note editor from the DOM.");
        await Expect(note).ToBeVisibleAsync();
        await Expect(note).ToHaveValueAsync(localNote);
    }

    [Theory]
    [InlineData(403, "You do not have permission to manage this quotation request.")]
    [InlineData(429, "Quotation service is busy. Try again shortly.")]
    [InlineData(502, "Quotation service returned an invalid response.")]
    [InlineData(0, "Quotation service is temporarily unavailable.")]
    public async Task FinderNote_SaveFailure_KeepsEditorAndContextWithoutReload(int status, string error)
    {
        await using var context = await ContextAsync("en-TH");
        var page = await context.NewPageAsync();
        var boundary = new Boundary(Envelope, failureStatus: status);
        await boundary.InstallAsync(page);
        await OpenAsync(page);
        const string localNote = "Unsaved after unavailable service / ไทย";
        await page.Locator("#request-internal").FillAsync(localNote);
        await SaveAsync(page, "en-TH");
        await Expect(page.GetByText(error, new() { Exact = true })).ToBeVisibleAsync();
        using var body = AssertWire(Assert.Single(boundary.Updates));
        AssertEnvelopePreserved(Envelope, body.RootElement.GetProperty("internalComment").GetString()!, localNote);
        Assert.Equal(Envelope, boundary.CurrentComment);
        Assert.Equal(1, boundary.DetailReads);
        Assert.Equal(0, await page.GetByText(Saved("en-TH"), new() { Exact = true }).CountAsync());
        Assert.True(await page.Locator("#request-internal").CountAsync() == 1, "Save failure removed the local-note editor from the DOM.");
        await Expect(page.Locator("#request-internal")).ToBeVisibleAsync();
        await Expect(page.Locator("#request-internal")).ToHaveValueAsync(localNote);
        await Expect(page.Locator("[data-service-finder-context]")).ToBeVisibleAsync();
    }

    [Fact]
    public async Task FinderNote_PreviousSuccessThenFailure_ClearsStaleSuccessAndKeepsNewNote()
    {
        await using var context = await ContextAsync("en-TH");
        var page = await context.NewPageAsync();
        var boundary = new Boundary(Envelope, failureStatus: 403, failAfterSuccess: true);
        await boundary.InstallAsync(page);
        await OpenAsync(page);
        await page.Locator("#request-internal").FillAsync("Saved first note");
        await SaveAsync(page, "en-TH");
        await Expect(page.GetByText(Saved("en-TH"), new() { Exact = true })).ToBeVisibleAsync();
        await Expect(page.Locator("#request-internal")).ToHaveValueAsync("Saved first note");
        var persisted = boundary.CurrentComment;
        await page.Locator("#request-internal").FillAsync("Unsaved second note");
        await SaveAsync(page, "en-TH");
        await Expect(page.GetByText("You do not have permission to manage this quotation request.", new() { Exact = true })).ToBeVisibleAsync();
        Assert.Equal(2, boundary.Updates.Count);
        using var body = AssertWire(boundary.Updates.Last(), expectedReads: 2);
        AssertEnvelopePreserved(Envelope, body.RootElement.GetProperty("internalComment").GetString()!, "Unsaved second note");
        Assert.Equal(persisted, boundary.CurrentComment);
        Assert.Equal(2, boundary.DetailReads);
        Assert.Equal(0, await page.GetByText(Saved("en-TH"), new() { Exact = true }).CountAsync());
        Assert.True(await page.Locator("#request-internal").CountAsync() == 1, "Second failed save removed the editor.");
        await Expect(page.Locator("#request-internal")).ToHaveValueAsync("Unsaved second note");
    }

    [Theory]
    [MemberData(nameof(OrdinaryOrMalformedNotes))]
    public async Task OrdinaryOrMalformedComment_DomFallback_EditsPlainTextWithoutFinderSummary(string original)
    {
        await using var context = await ContextAsync("en-TH");
        var page = await context.NewPageAsync();
        var boundary = new Boundary(original);
        await boundary.InstallAsync(page);
        await OpenAsync(page);
        await Expect(page.Locator("#request-internal")).ToHaveValueAsync(original);
        Assert.Equal(0, await page.Locator("[data-service-finder-context]").CountAsync());
        await page.Locator("#request-internal").FillAsync("  Plain reviewed / ตรวจสอบแล้ว  ");
        await SaveAsync(page, "en-TH");
        await Expect(page.GetByText(Saved("en-TH"), new() { Exact = true })).ToBeVisibleAsync();
        using var body = AssertWire(Assert.Single(boundary.Updates));
        Assert.Equal("Plain reviewed / ตรวจสอบแล้ว", body.RootElement.GetProperty("internalComment").GetString());
        await Expect(page.Locator("#request-internal")).ToHaveValueAsync("Plain reviewed / ตรวจสอบแล้ว");
        Assert.Equal(0, await page.Locator("[data-service-finder-context]").CountAsync());
    }

    public static IEnumerable<object[]> OrdinaryOrMalformedNotes()
    {
        yield return ["Ordinary note / หมายเหตุทั่วไป"];
        yield return ["{"];
        yield return [Envelope.Replace("\"version\":1", "\"version\":2", StringComparison.Ordinal)];
        yield return [Envelope.Replace("\"source\":", "\"source\":\"service_finder\",\"source\":", StringComparison.Ordinal)];
        yield return [Envelope.Replace("\"files\":", "\"files\":\"files-real-part\",\"files\":", StringComparison.Ordinal)];
        yield return [Envelope.Replace("files-real-part", "unknown-files", StringComparison.Ordinal)];
        yield return [Envelope.Replace("\"operator_comment\":\"original staff note\"", "\"operator_comment\":{}", StringComparison.Ordinal)];
        yield return [Envelope.Replace("\"operator_comment\":\"original staff note\"", "\"operator_comment\":null", StringComparison.Ordinal)];
    }

    private async Task<IBrowserContext> ContextAsync(string culture)
    {
        var context = await playwright.Browser.NewContextAsync(new()
        {
            ViewportSize = new() { Width = 1280, Height = 900 },
            ReducedMotion = ReducedMotion.Reduce,
        });
        await context.AddInitScriptAsync($"localStorage.setItem('maliev_culture','{culture}')");
        return context;
    }

    private async Task OpenAsync(IPage page)
    {
        await page.GotoAsync(new Uri(server.BaseUri, "QuotationRequests/View?id=9").AbsoluteUri);
        await page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        await page.Locator("#request-internal").WaitForAsync();
    }

    private static Task SaveAsync(IPage page, string culture) => page.GetByRole(AriaRole.Button,
        new() { Name = culture == "th-TH" ? "บันทึกคำขอ" : "Save request", Exact = true }).ClickAsync();

    private static string Saved(string culture) => culture == "th-TH" ? "บันทึกคำขอใบเสนอราคาเรียบร้อยแล้ว" : "Quotation request saved.";

    private static JsonDocument AssertWire(CapturedUpdate capture, int expectedReads = 1)
    {
        Assert.Equal("fresh-browser-csrf", capture.Csrf);
        Assert.Equal(expectedReads, capture.DetailReadsBeforeWrite);
        var body = JsonDocument.Parse(capture.Body);
        Assert.Equal(Timestamp, body.RootElement.GetProperty("modifiedDate").GetString());
        Assert.Equal(new[] { "companyName", "country", "done", "email", "firstName", "internalComment", "lastName", "message", "modifiedDate", "taxIdentification", "telephoneNumber" },
            body.RootElement.EnumerateObject().Select(x => x.Name).Order(StringComparer.Ordinal).ToArray());
        return body;
    }

    private static void AssertEnvelopePreserved(string original, string submitted, string? note)
    {
        using var before = JsonDocument.Parse(original);
        using var after = JsonDocument.Parse(submitted);
        foreach (var property in before.RootElement.EnumerateObject().Where(x => x.Name != "operator_comment"))
            Assert.True(JsonElement.DeepEquals(property.Value, after.RootElement.GetProperty(property.Name)), $"Finder property {property.Name} was changed or discarded.");
        if (note is null) Assert.False(after.RootElement.TryGetProperty("operator_comment", out _));
        else Assert.Equal(note, after.RootElement.GetProperty("operator_comment").GetString());
    }

    private static async Task CaptureAsync(IPage page, string name)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Legacy.Maliev.Intranet.slnx"))) directory = directory.Parent;
        var root = directory?.FullName ?? throw new DirectoryNotFoundException("Intranet browser artifact root is unavailable.");
        var output = Path.Combine(root, "TestResults", "finder-browser-proof", "output", "playwright");
        Directory.CreateDirectory(output);
        await page.ScreenshotAsync(new() { Path = Path.Combine(output, name + ".png"), FullPage = true });
    }

    private sealed record CapturedUpdate(string Body, string? Csrf, int DetailReadsBeforeWrite);

    private sealed class Boundary(string original, bool conflict = false, int? failureStatus = null, bool failAfterSuccess = false)
    {
        private int detailReads;
        public int DetailReads => Volatile.Read(ref detailReads);
        public string CurrentComment { get; private set; } = original;
        public ConcurrentQueue<CapturedUpdate> Updates { get; } = new();

        public async Task InstallAsync(IPage page)
        {
            // Exactly the existing QuotationDecisionBrowserTests synthetic session projection; no new grant.
            await page.RouteAsync("**/bff/session", route => JsonAsync(route, new
            {
                isAuthenticated = true,
                employeeId = "employee-7",
                displayName = "Employee Seven",
                roles = new[] { "Employee" },
                csrfToken = "fresh-browser-csrf",
                legacyDatabaseId = 7,
                permissions = new[] { "legacy.quotations.update" },
            }));
            await page.RouteAsync("**/bff/quotation-requests/9/qualification-receipt", route => route.FulfillAsync(new() { Status = 404 }));
            await page.RouteAsync("**/bff/quotation-requests/9", async route =>
            {
                if (route.Request.Method == "GET")
                {
                    Interlocked.Increment(ref detailReads);
                    await JsonAsync(route, new
                    {
                        request = new
                        {
                            id = 9,
                            firstName = "Buyer",
                            lastName = "Fixture",
                            email = "buyer@example.test",
                            telephoneNumber = (string?)null,
                            country = "TH",
                            companyName = (string?)null,
                            taxIdentification = (string?)null,
                            message = "Part review",
                            internalComment = CurrentComment,
                            done = false,
                            createdDate = "2030-07-18T00:00:00",
                            modifiedDate = Timestamp,
                        },
                        files = Array.Empty<object>(),
                    });
                    return;
                }
                Assert.Equal("PUT", route.Request.Method);
                var body = Assert.IsType<string>(route.Request.PostData);
                Updates.Enqueue(new(body, (await route.Request.AllHeadersAsync()).GetValueOrDefault("x-csrf-token"), DetailReads));
                var status = conflict ? 409 : failureStatus is not null && (!failAfterSuccess || Updates.Count > 1) ? failureStatus.Value : 204;
                if (status == 204)
                {
                    using var json = JsonDocument.Parse(body);
                    CurrentComment = json.RootElement.GetProperty("internalComment").GetString()!;
                }
                if (status == 0) await route.AbortAsync("failed");
                else await route.FulfillAsync(new() { Status = status });
            });
        }

        private static Task JsonAsync(IRoute route, object value) => route.FulfillAsync(new()
        {
            Status = 200,
            ContentType = "application/json",
            Body = JsonSerializer.Serialize(value),
        });
    }
}
