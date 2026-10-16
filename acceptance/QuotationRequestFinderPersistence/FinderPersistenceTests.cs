using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using Legacy.Maliev.Intranet.Contracts;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Localization;
using RequestView = Legacy.Maliev.Intranet.Client.Features.Quotations.Pages.QuotationRequests.View;

namespace QuotationRequestFinderPersistence.Acceptance;

public sealed class FinderPersistenceTests(OwnedContainers containers) : IClassFixture<OwnedContainers>
{
    [Theory]
    [InlineData("en-TH", false, false)]
    [InlineData("en-TH", false, true)]
    [InlineData("en-TH", true, false)]
    [InlineData("en-TH", true, true)]
    [InlineData("th-TH", false, false)]
    [InlineData("th-TH", false, true)]
    [InlineData("th-TH", true, false)]
    [InlineData("th-TH", true, true)]
    public async Task Editor_EditOrClear_PersistsOnlyNoteAndReloadsRealApi(string culture, bool optional, bool clear)
    {
        using var cultureScope = new CultureScope(culture);
        await using var fixture = new JoinedFixture(containers);
        await fixture.InitializeAsync(Envelope(optional));
        using var client = await fixture.SignInAsync();
        var view = await LoadAsync(client, fixture.Id);
        Assert.Equal("original staff note", Note(view));
        var before = await fixture.SnapshotAsync();
        var reads = fixture.DetailReads;
        SetNote(view, clear ? string.Empty : "  Reviewed / ตรวจสอบแล้ว  ");
        await InvokeAsync(view, "SaveAsync").WaitAsync(TimeSpan.FromSeconds(20));
        Assert.Null(Field<string?>(view, "saveError"));
        Assert.Null(Field<string?>(view, "error"));
        Assert.Equal(Text(culture, "Saved"), Field<string?>(view, "notice"));
        Assert.Equal(clear ? string.Empty : "Reviewed / ตรวจสอบแล้ว", Note(view));
        Assert.Equal(reads + 1, fixture.DetailReads);
        Assert.Equal(1, fixture.NoteWrites);
        var after = await fixture.SnapshotAsync();
        Assert.NotEqual(before.Xmin, after.Xmin);
        Assert.NotEqual(before.ModifiedDate, after.ModifiedDate);
        Protected(before, after);
        EnvelopeOnlyNoteChanged(Envelope(optional), after.Comment!, clear ? null : "Reviewed / ตรวจสอบแล้ว");
        var detail = Assert.IsType<QuotationRequestDetail>(Field<object>(view, "detail"));
        Assert.Equal(after.Comment, detail.Request.InternalComment);
        using var api = await fixture.ReadApiAsync();
        Assert.Equal(after.Comment, api.RootElement.GetProperty("InternalComment").GetString());
        Assert.Equal(after.ModifiedDate, api.RootElement.GetProperty("ModifiedDate").GetDateTime());
        Assert.Contains($"GET /quotationrequests/{fixture.Id}/files", fixture.Requests);
        Assert.Contains($"GET /quotationrequests/{fixture.Id}/qualification-receipt", fixture.Requests);
        Assert.True(fixture.IamCalls > 0);
        Assert.Empty(fixture.UnexpectedOutbound);
    }

    [Theory]
    [InlineData("en-TH")]
    [InlineData("th-TH")]
    public async Task Editor_StaleVersion_LeavesWinningGraphAndLocalNoteWithoutReload(string culture)
    {
        using var cultureScope = new CultureScope(culture);
        await using var fixture = new JoinedFixture(containers);
        await fixture.InitializeAsync(Envelope(true));
        using var staleClient = await fixture.SignInAsync();
        using var winnerClient = await fixture.SignInAsync();
        var stale = await LoadAsync(staleClient, fixture.Id);
        var winner = await LoadAsync(winnerClient, fixture.Id);
        SetNote(winner, "Winning / ชนะ");
        await InvokeAsync(winner, "SaveAsync").WaitAsync(TimeSpan.FromSeconds(20));
        Assert.Null(Field<string?>(winner, "saveError"));
        var winning = await fixture.SnapshotAsync();
        var reads = fixture.DetailReads;
        SetNote(stale, "Local stale / ยังไม่ได้บันทึก");
        await InvokeAsync(stale, "SaveAsync").WaitAsync(TimeSpan.FromSeconds(20));
        Assert.Equal(HttpStatusCode.Conflict, fixture.LastNoteStatus);
        Assert.Equal(Text(culture, "Conflict"), Field<string?>(stale, "saveError"));
        Assert.Null(Field<string?>(stale, "notice"));
        Assert.False(Field<bool>(stale, "submitting"));
        Assert.Equal("Local stale / ยังไม่ได้บันทึก", Note(stale));
        Assert.Equal(reads, fixture.DetailReads);
        Assert.Equal(winning, await fixture.SnapshotAsync());
        Assert.Empty(fixture.UnexpectedOutbound);
    }

    [Theory]
    [InlineData("anonymous", 401)]
    [InlineData("permission", 403)]
    [InlineData("missing-csrf", 400)]
    [InlineData("invalid-csrf", 400)]
    public async Task Bff_RejectedAuthority_NeverReachesWriter(string rejected, int status)
    {
        await using var fixture = new JoinedFixture(containers);
        await fixture.InitializeAsync(Envelope(true));
        using var client = rejected == "anonymous" ? fixture.AnonymousClient() : await fixture.SignInAsync(rejected != "permission");
        var before = await fixture.SnapshotAsync();
        using var sessionResponse = await client.GetAsync("/bff/session");
        var session = await sessionResponse.Content.ReadFromJsonAsync<EmployeeSessionSummary>();
        using var request = new HttpRequestMessage(HttpMethod.Put, $"/bff/quotation-requests/{fixture.Id}")
        {
            Content = JsonContent.Create(new { FirstName = "Synthetic", InternalComment = "forbidden overwrite", Done = false, ModifiedDate = before.ModifiedDate }),
        };
        if (rejected != "missing-csrf") request.Headers.Add("X-CSRF-TOKEN", rejected == "invalid-csrf" ? "invalid" : session!.CsrfToken);
        using var result = await client.SendAsync(request);
        Assert.Equal(status, (int)result.StatusCode);
        Assert.Equal(0, fixture.NoteWrites);
        Assert.Equal(before, await fixture.SnapshotAsync());
        Assert.Empty(fixture.UnexpectedOutbound);
    }

    // Literal fixture values; neither expected context nor expected note is produced by MergeOperatorComment.
    internal static string Envelope(bool optional) => optional ?
        """{"source":"service_finder","version":1,"answers":{"files":"files-real-part","service":"service-3d","material":"material-plastic","quantity":"quantity-1-10","end-use":"use-prototype","performance":"performance-strength","environment":"environment-outdoor","future_answer":{"keep":[1,true,null,"ไทย / English"]}},"recommended_service_ids":["scanning","design"],"finder_path":["scanning","design"],"operator_comment":"original staff note","future_context":{"keep":"unchanged","nested":{"text":"<tag> & ไทย / English"}}}""" :
        """{"source":"service_finder","version":1,"answers":{"files":"files-3d","service":"service-3d","material":"material-plastic","quantity":"quantity-1-10","end-use":"use-prototype","future_answer":{"keep":[1,true,null,"ไทย / English"]}},"recommended_service_ids":["scanning","design"],"finder_path":["scanning","design"],"operator_comment":"original staff note","future_context":{"keep":"unchanged","nested":{"text":"<tag> & ไทย / English"}}}""";

    private static void EnvelopeOnlyNoteChanged(string original, string actual, string? expectedNote)
    {
        using var before = JsonDocument.Parse(original);
        using var after = JsonDocument.Parse(actual);
        foreach (var property in before.RootElement.EnumerateObject().Where(x => x.Name != "operator_comment"))
            Assert.True(JsonElement.DeepEquals(property.Value, after.RootElement.GetProperty(property.Name)), property.Name);
        Assert.Equal(before.RootElement.EnumerateObject().Count() - (expectedNote is null ? 1 : 0), after.RootElement.EnumerateObject().Count());
        if (expectedNote is null) Assert.False(after.RootElement.TryGetProperty("operator_comment", out _));
        else Assert.Equal(expectedNote, after.RootElement.GetProperty("operator_comment").GetString());
    }

    private static void Protected(Snapshot before, Snapshot after)
    {
        var oldRow = JsonNode.Parse(before.Row)!.AsObject();
        var newRow = JsonNode.Parse(after.Row)!.AsObject();
        foreach (var name in new[] { "InternalComment", "ModifiedDate" }) { oldRow.Remove(name); newRow.Remove(name); }
        Assert.True(JsonNode.DeepEquals(oldRow, newRow));
        Assert.Equal(before.History, after.History);
        Assert.Equal(before.Files, after.Files);
    }

    private static async Task<RequestView> LoadAsync(HttpClient client, int id)
    {
        var view = new RequestView { Id = id };
        Set(view, "Http", client);
        Set(view, "Navigation", new FixtureNavigation());
        Set(view, "Text", new StringLocalizer<RequestView>(new ResourceManagerStringLocalizerFactory(
            Microsoft.Extensions.Options.Options.Create(new LocalizationOptions()),
            Microsoft.Extensions.Logging.Abstractions.NullLoggerFactory.Instance)));
        await InvokeAsync(view, "LoadAsync").WaitAsync(TimeSpan.FromSeconds(20));
        Assert.Null(Field<string?>(view, "error"));
        Assert.Null(Field<string?>(view, "qualificationError"));
        Assert.Equal(id, Assert.IsType<QuotationRequestDetail>(Field<object>(view, "detail")).Request.Id);
        return view;
    }

    private static string Text(string culture, string key) => key switch
    {
        "Saved" => culture.StartsWith("th", StringComparison.Ordinal) ? "บันทึกคำขอใบเสนอราคาเรียบร้อยแล้ว" : "Quotation request saved.",
        "Conflict" => culture.StartsWith("th", StringComparison.Ordinal) ? "คำขอนี้ถูกแก้ไขหลังจากที่คุณเปิดหน้า โปรดโหลดข้อมูลล่าสุดแล้วลองอีกครั้ง" : "This request changed after it was loaded. Reload and try again.",
        _ => throw new ArgumentOutOfRangeException(nameof(key)),
    };

    private static T Field<T>(object target, string name) => (T)target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(target)!;
    private static void Set(object target, string name, object value) => target.GetType().GetProperty(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(target, value);
    private static string? Note(RequestView view) => (string?)Field<object>(view, "edit").GetType().GetProperty("InternalComment")!.GetValue(Field<object>(view, "edit"));
    private static void SetNote(RequestView view, string value) => Set(Field<object>(view, "edit"), "InternalComment", value);
    private static async Task InvokeAsync(object view, string method) => await Assert.IsAssignableFrom<Task>(view.GetType().GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(view, null));

    private sealed class FixtureNavigation : NavigationManager
    {
        public FixtureNavigation() => Initialize("https://localhost/", "https://localhost/QuotationRequests/View");
        protected override void NavigateToCore(string uri, NavigationOptions options) => Uri = ToAbsoluteUri(uri).ToString();
    }

    private sealed class CultureScope : IDisposable
    {
        private readonly CultureInfo previous = CultureInfo.CurrentCulture;
        private readonly CultureInfo previousUi = CultureInfo.CurrentUICulture;
        public CultureScope(string culture) { CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture); CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(culture); }
        public void Dispose() { CultureInfo.CurrentCulture = previous; CultureInfo.CurrentUICulture = previousUi; }
    }
}
