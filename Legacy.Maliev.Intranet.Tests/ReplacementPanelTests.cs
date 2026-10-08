using System.Net;
using System.Net.Http.Json;
using Bunit;
using Legacy.Maliev.Intranet.Client.Features.Orders.Components;
using Legacy.Maliev.Intranet.Contracts;
using Microsoft.Extensions.DependencyInjection;
using Maliev.ShadcnBlazor.Components.Forms;
using Maliev.ShadcnBlazor.Components.Selection;
namespace Legacy.Maliev.Intranet.Tests;

public sealed class ReplacementPanelTests
{
    [Fact]
    public void Missing_permission_does_not_read_or_render_mutations()
    {
        using var handler = new Handler(); using var context = Context(handler);
        var cut = context.Render<ReplacementPanel>(p => p.Add(x => x.OrderId, 94826).Add(x => x.Session, new(true, "staff", "Staff", [], "csrf", 7, [])));
        Assert.Empty(handler.Paths); Assert.Empty(cut.FindAll("form"));
    }
    [Fact]
    public void Unavailable_module_is_visible_and_disables_workflow()
    {
        using var handler = new Handler { Unavailable = true }; using var context = Context(handler);
        var cut = context.Render<ReplacementPanel>(p => p.Add(x => x.OrderId, 94826).Add(x => x.Session, Session()));
        cut.WaitForAssertion(() => Assert.Contains("unavailable", cut.Markup, StringComparison.OrdinalIgnoreCase)); Assert.Empty(cut.FindAll("form"));
    }
    [Fact]
    public void Timeline_preserves_original_tracking_and_separate_replacement_counts()
    {
        using var handler = new Handler(); using var context = Context(handler);
        var cut = context.Render<ReplacementPanel>(p => p.Add(x => x.OrderId, 94826).Add(x => x.Session, Session()));
        cut.WaitForAssertion(() => Assert.Contains("LALAMOVE", cut.Markup)); Assert.Contains("NEW-TRACK", cut.Markup); Assert.Contains("94826", cut.Markup); Assert.DoesNotContain("94876", cut.Markup); Assert.Empty(cut.FindAll("input[name='Manufactured']"));
    }
    [Fact]
    public async Task Session_failure_can_retry_the_same_captured_operation_without_new_key()
    {
        using var handler = new Handler { SessionFailures = 1 }; using var context = Context(handler);
        var cut = context.Render<ReplacementPanel>(p => p.Add(x => x.OrderId, 94826).Add(x => x.Session, Session()));
        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll("#replacement-document")));
        var doc = cut.FindComponents<ShadcnSelect<Guid?>>().Single(x => x.FindAll("#replacement-document").Count > 0);
        await cut.InvokeAsync(() => doc.Instance.ValueChanged.InvokeAsync(handler.DocumentId));
        var version = cut.FindComponents<ShadcnSelect<Guid?>>().Single(x => x.FindAll("#replacement-version").Count > 0);
        await cut.InvokeAsync(() => version.Instance.ValueChanged.InvokeAsync(handler.VersionId));
        await cut.Find("form").SubmitAsync(); var operation = cut.Find("code").TextContent;
        Assert.Empty(handler.Posts);
        await cut.Find("#replacement-retry").ClickAsync(new());
        Assert.Equal(operation, Assert.Single(handler.Posts).Key); Assert.Empty(cut.FindAll("code"));
    }
    [Fact]
    public async Task Approver_only_can_record_approved_recovery_without_offering_write_facts()
    {
        using var handler = new Handler(); using var context = Context(handler);
        var session = Session() with { Permissions = [ReplacementPermissions.Read, ReplacementPermissions.Approve, "legacy-file.documents.read"] };
        var cut = context.Render<ReplacementPanel>(p => p.Add(x => x.OrderId, 94826).Add(x => x.Session, session));
        cut.WaitForAssertion(() => Assert.Contains("LALAMOVE", cut.Markup));
        var action = cut.FindComponents<ShadcnSelect<string>>().Single(x => x.FindAll("#replacement-action").Count > 0);
        Assert.Contains(action.Instance.Options, x => x.Value == "RecordRecoveryFact");
        await cut.InvokeAsync(() => action.Instance.ValueChanged.InvokeAsync("RecordRecoveryFact"));
        var facts = cut.FindComponents<ShadcnSelect<string>>().Single(x => x.FindAll("#replacement-fact-kind").Count > 0);
        Assert.All(facts.Instance.Options, x => Assert.Contains(x.Value, new[] { "Approved", "Received" }));
        Assert.Contains(facts.Instance.Value, new[] { "Approved", "Received" });
    }
    [Fact]
    public async Task Lost_ack_survives_child_remount_and_retry_reuses_frozen_payload()
    {
        using var handler = new Handler { PostFailures = 1 }; using var context = Context(handler); var state = new ReplacementPendingState();
        var first = context.Render<ReplacementPanel>(p => p.Add(x => x.OrderId, 94826).Add(x => x.Session, Session()).Add(x => x.PendingState, state));
        await SelectEvidence(first, handler); await first.Find("form").SubmitAsync(); var captured = Assert.IsType<ReplacementPendingOperation>(state.Operation);
        first.Dispose();
        var restored = context.Render<ReplacementPanel>(p => p.Add(x => x.OrderId, 94826).Add(x => x.Session, Session()).Add(x => x.PendingState, state));
        restored.WaitForAssertion(() => Assert.Equal(captured.OperationId.ToString(), restored.Find("code").TextContent));
        await restored.Find("form").SubmitAsync(); Assert.Single(handler.Posts);
        await restored.Find("#replacement-retry").ClickAsync(new()); Assert.Null(state.Operation); Assert.Equal(2, handler.Posts.Count);
        Assert.Equal(handler.Posts[0], handler.Posts[1]);
    }
    [Fact]
    public void Pending_action_never_moves_to_another_employee_session()
    {
        using var handler = new Handler(); using var context = Context(handler); var operation = Guid.NewGuid();
        var state = new ReplacementPendingState { Operation = new(operation, "/bff/orders/94826/replacements", "{}", 94826, 0, "original-staff", true) };
        var cut = context.Render<ReplacementPanel>(p => p.Add(x => x.OrderId, 94826).Add(x => x.Session, Session()).Add(x => x.PendingState, state));
        cut.WaitForAssertion(() => Assert.Contains("original employee", cut.Markup)); Assert.DoesNotContain(operation.ToString(), cut.Markup); Assert.Empty(cut.FindAll("form")); Assert.Empty(handler.Posts);
    }
    [Fact]
    public async Task Cancelled_old_order_read_never_overrides_new_order()
    {
        using var handler = new Handler { HoldFirst = true }; using var context = Context(handler);
        var cut = context.Render<ReplacementPanel>(p => p.Add(x => x.OrderId, 94826).Add(x => x.Session, Session()));
        await handler.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        cut.Render(p => p.Add(x => x.OrderId, 94876).Add(x => x.Session, Session()));
        cut.WaitForAssertion(() => { Assert.Contains("94876", cut.Markup); Assert.DoesNotContain("94826", cut.Markup); });
    }
    [Fact]
    public async Task Explicit_revision_rejection_resolves_pending_and_refreshes_case()
    {
        using var handler = new Handler { RejectRevision = true }; using var context = Context(handler); var state = new ReplacementPendingState();
        var cut = context.Render<ReplacementPanel>(p => p.Add(x => x.OrderId, 94826).Add(x => x.Session, Session()).Add(x => x.PendingState, state));
        cut.WaitForAssertion(() => Assert.Contains("LALAMOVE", cut.Markup));
        await cut.InvokeAsync(() => cut.FindComponents<ShadcnSelect<int>>().Single(x => x.FindAll("#replacement-case").Count > 0).Instance.ValueChanged.InvokeAsync(3));
        await cut.Find("form").SubmitAsync(); Assert.Null(state.Operation); Assert.Single(handler.Posts); Assert.Empty(cut.FindAll("#replacement-retry"));
        Assert.Contains("case or operation changed", cut.Markup, StringComparison.OrdinalIgnoreCase);
    }
    private static async Task SelectEvidence(IRenderedComponent<ReplacementPanel> cut, Handler handler)
    {
        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll("#replacement-document")));
        await cut.InvokeAsync(() => cut.FindComponents<ShadcnSelect<Guid?>>().Single(x => x.FindAll("#replacement-document").Count > 0).Instance.ValueChanged.InvokeAsync(handler.DocumentId));
        await cut.InvokeAsync(() => cut.FindComponents<ShadcnSelect<Guid?>>().Single(x => x.FindAll("#replacement-version").Count > 0).Instance.ValueChanged.InvokeAsync(handler.VersionId));
    }
    private static EmployeeSessionSummary Session() => new(true, "staff", "Staff", [], "csrf", 7, [ReplacementPermissions.Read, ReplacementPermissions.Write, ReplacementPermissions.Approve, "legacy-file.documents.read"]);
    private static BunitContext Context(Handler h) { var c = new BunitContext(); c.Services.AddLocalization(); c.Services.AddSingleton(new HttpClient(h) { BaseAddress = new("https://localhost/") }); c.JSInterop.Mode = JSRuntimeMode.Loose; return c; }
    private sealed class Handler : HttpMessageHandler
    {
        public bool Unavailable; public List<string> Paths = [];
        public int SessionFailures, PostFailures;
        public bool HoldFirst, RejectRevision; public TaskCompletionSource Entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Guid DocumentId = Guid.NewGuid(), VersionId = Guid.NewGuid();
        public List<(string? Key, string? Body)> Posts = [];
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken c)
        {
            Paths.Add(r.RequestUri!.AbsolutePath);
            if (HoldFirst && r.RequestUri.AbsolutePath == "/bff/orders/94826/replacements") { Entered.TrySetResult(); await Task.Delay(Timeout.Infinite, c); }
            if (Unavailable) return new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
            if (r.RequestUri.AbsolutePath == "/bff/session")
            {
                if (SessionFailures-- > 0) throw new HttpRequestException("Interrupted session read");
                return new(HttpStatusCode.OK) { Content = JsonContent.Create(Session()) };
            }
            if (r.Method == HttpMethod.Post)
            {
                Posts.Add((r.Headers.GetValues("Idempotency-Key").Single(), await r.Content!.ReadAsStringAsync(c)));
                if (PostFailures-- > 0) return new(HttpStatusCode.ServiceUnavailable);
                if (RejectRevision) { var rejected = new HttpResponseMessage(HttpStatusCode.Conflict); rejected.Headers.Add("X-Replacement-Rejection", "Revision"); return rejected; }
            }
            var orderId = r.RequestUri.AbsolutePath.Contains("/94876/", StringComparison.Ordinal) ? 94876 : 94826;
            var evidence = new ReplacementEvidenceReference(DocumentId, VersionId);
            var value = new ReplacementStoredCaseView(3, new(69797, "ManufacturingNonconformance", "Approved", 4, evidence, [new(orderId, 69797, 1, 1, 1, "LALAMOVE", new(2026, 9, 30), null, null)], [new(1, orderId, 1, DateTimeOffset.UtcNow, 1, 1, 0, new(2026, 10, 8), evidence)], [new(1, 1, orderId, 1, "Carrier", "NEW-TRACK", "Destination", new(2026, 10, 8), "InTransit", null, false)], [], [], [new(1, "Reported", 7, DateTimeOffset.UtcNow, "Defect")], "Waived", false, false, []));
            return new(HttpStatusCode.OK) { Content = r.Method == HttpMethod.Post ? JsonContent.Create(value) : r.RequestUri.AbsolutePath.EndsWith("/versions", StringComparison.Ordinal) ? JsonContent.Create(new[] { new ReplacementDocumentVersionView(DocumentId, VersionId, 1, "Evidence", new string('a', 64), DateTimeOffset.UtcNow, "Verified", "staff", DateTimeOffset.UtcNow, 1) }) : r.RequestUri.AbsolutePath.EndsWith("/evidence", StringComparison.Ordinal) ? JsonContent.Create(new[] { new ReplacementDocumentView(DocumentId, 69797, "Evidence", "QA report", "Internal", 1) }) : JsonContent.Create(new[] { value }) };
        }
    }
}
