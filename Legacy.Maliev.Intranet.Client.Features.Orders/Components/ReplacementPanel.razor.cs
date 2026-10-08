using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Legacy.Maliev.Intranet.Contracts;
using Maliev.ShadcnBlazor;
using Maliev.ShadcnBlazor.Components.Forms;
using Maliev.ShadcnBlazor.Components.Selection;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Routing;

namespace Legacy.Maliev.Intranet.Client.Features.Orders.Components;

public partial class ReplacementPanel
{
    [Parameter] public int OrderId { get; set; }
    [Parameter] public EmployeeSessionSummary? Session { get; set; }
    [Parameter] public ReplacementPendingState PendingState { get; set; } = new();
    [Parameter] public EventCallback PendingChanged { get; set; }
    private ReplacementStoredCaseView[] cases = [];
    private readonly HashSet<int> openAuditCases = [];
    private void SetAuditOpen(int caseId, bool open)
    {
        if (open) openAuditCases.Add(caseId);
        else openAuditCases.Remove(caseId);
    }
    private ReplacementDocumentView[] documents = [];
    private ReplacementDocumentVersionView[] versions = [];
    private CancellationTokenSource lifetime = new();
    private bool loading, busy, unavailable;
    private int loadedOrder, selectedCaseId, documentOffset;
    private Guid? selectedDocument, selectedVersion;
    private string action = "Report";
    private string? message, evidenceMessage;
    private string? loadedSubject;
    private ReplacementPendingOperation? pending
    {
        get => PendingState.Operation;
        set { PendingState.Operation = value; _ = PendingChanged.InvokeAsync(); }
    }
    private int generation, documentGeneration;
    private bool ForeignPending => pending is not null && (pending.OrderId != OrderId || pending.EmployeeSubject != Session?.EmployeeId);
    private readonly record struct Lease(int Generation, CancellationToken Token);
    private Lease Capture() => new(generation, lifetime.Token);
    private bool Current(Lease lease) => lease.Generation == generation && !lease.Token.IsCancellationRequested;
    private Form form = new();
    private static readonly JsonSerializerOptions Wire = new(JsonSerializerDefaults.General);
    private string Base => $"/bff/orders/{OrderId}/replacements";
    private bool Has(string permission) => Session?.IsAuthenticated == true && Session.Permissions?.Contains(permission, StringComparer.Ordinal) == true;
    private bool CanRead => Has(ReplacementPermissions.Read);
    private bool CanWrite => Has(ReplacementPermissions.Write);
    private bool CanApprove => Has(ReplacementPermissions.Approve);
    private ReplacementStoredCaseView? Selected => cases.SingleOrDefault(x => x.Id == selectedCaseId);
    private bool NeedsEvidence => action is "Report" or "CompleteQa" or "RecordReturn" or "ConfirmDelivery" or "RecordRecoveryFact";
    private string EvidenceKind => action == "ConfirmDelivery" ? "Acceptance" : "Evidence";
    private bool CanSubmit => !PendingState.OriginalWriteInProgress && !busy && pending is null && !loading && !unavailable && ActionOptions.Any(x => x.Value == action)
        && (!NeedsEvidence || selectedDocument is not null && selectedVersion is not null)
        && (action != "RecordRecoveryFact" || form.FactKind is not ("Approved" or "Received") || CanApprove);
    private IReadOnlyList<ShadcnSelectOption<int>> CaseOptions => (CanWrite ? new[] { new ShadcnSelectOption<int>(0, Text["NewCase"].Value) } : [])
        .Concat(cases.Select(x => new ShadcnSelectOption<int>(x.Id, Text["Case", x.Id].Value))).ToArray();
    private IReadOnlyList<ShadcnSelectOption<string>> ActionOptions
    {
        get
        {
            var names = new List<string>(); var state = Selected?.Value.State;
            if (selectedCaseId == 0 && CanWrite) names.Add("Report");
            if (state == "Reported" && CanApprove) names.AddRange(["Approve", "Reject"]);
            if (state == "Approved")
            {
                if (CanWrite) names.AddRange(["StartAttempt", "CompleteQa", "RecordReturn", "Ship", "ConfirmDelivery", "Close"]);
                if (CanWrite || CanApprove) names.Add("RecordRecoveryFact");
                if (CanApprove) names.AddRange(["AuthorizeRetry", "WaiveReturn", "WaiveQuantity"]);
            }
            if (state == "Closed" && (CanWrite || CanApprove)) names.Add("RecordRecoveryFact");
            return Options(names);
        }
    }
    private IReadOnlyList<ShadcnSelectOption<string>> ReasonOptions => Options(["CarrierDamage", "ManufacturingNonconformance"]);
    private IReadOnlyList<ShadcnSelectOption<string>> ReturnOptions => Options(["Required", "Waived"]);
    private IReadOnlyList<ShadcnSelectOption<string>> FactOptions => Options((CanWrite ? new[] { "Cost", "Claimed", "Rejected" } : []).Concat(CanApprove ? new[] { "Approved", "Received" } : []));
    private IReadOnlyList<ShadcnSelectOption<int>> OriginalOptions => (Selected?.Value.Originals ?? []).Select(x => new ShadcnSelectOption<int>(x.OrderId, Text["Order", x.OrderId].Value)).ToArray();
    private IReadOnlyList<ShadcnSelectOption<int>> AttemptOptions => (Selected?.Value.Attempts ?? []).Where(x => action == "CompleteQa" ? x.ProducedQuantity is null : x.AcceptedQuantity > (Selected?.Value.Shipments ?? []).Where(s => s.AttemptId == x.Id).Sum(s => s.Quantity)).Select(x => new ShadcnSelectOption<int>(x.Id, Text["AttemptOption", x.Id, x.OrderId].Value)).ToArray();
    private IReadOnlyList<ShadcnSelectOption<int>> ShipmentOptions => (Selected?.Value.Shipments ?? []).Where(x => action == "ConfirmDelivery" ? x.Outcome == "InTransit" : x.Outcome == "Failed" && !x.RetryAuthorized).Select(x => new ShadcnSelectOption<int>(x.Id, $"{Text["Shipment"]} {x.Id} · {x.TrackingNumber}")).ToArray();
    private IReadOnlyList<ShadcnSelectOption<int?>> CorrectionOptions => (Selected?.Value.RecoveryFacts ?? []).Where(x => x.Kind == form.FactKind && x.OrderId == form.OrderId && !(Selected?.Value.RecoveryFacts ?? []).Any(y => y.CorrectsFactId == x.Id)).Select(x => new ShadcnSelectOption<int?>(x.Id, $"{Text["Fact", x.Id]} · {x.Amount} {x.Currency} · {x.Description}")).ToArray();
    private IReadOnlyList<ShadcnSelectOption<Guid?>> DocumentOptions => documents.Where(x => x.Kind == EvidenceKind).Select(x => new ShadcnSelectOption<Guid?>(x.DocumentId, x.Title)).ToArray();
    private IReadOnlyList<ShadcnSelectOption<Guid?>> VersionOptions => versions.Where(x => x.Kind == EvidenceKind && x.VerificationStatus == "Verified").Select(x => new ShadcnSelectOption<Guid?>(x.VersionId, Text["VersionOption", x.VersionNumber, x.CreatedAtUtc.ToLocalTime()].Value)).ToArray();
    private IReadOnlyList<ShadcnSelectOption<string>> Options(IEnumerable<string> names) => names.Select(x => new ShadcnSelectOption<string>(x, Label(x))).ToArray();
    private string Label(string? name) => name is null ? "—" : Text[name].Value;
    protected override async Task OnParametersSetAsync()
    {
        if (!CanRead) { generation++; lifetime.Cancel(); cases = []; documents = []; versions = []; loadedOrder = 0; return; }
        if (loadedOrder == OrderId && loadedSubject == Session?.EmployeeId) return;
        generation++; lifetime.Cancel(); lifetime.Dispose(); lifetime = new(); loadedOrder = OrderId; loadedSubject = Session?.EmployeeId;
        selectedCaseId = 0; action = "Report"; form = new() { OrderId = OrderId }; var lease = Capture(); await ReloadAsync();
        if (Current(lease) && !unavailable && NeedsEvidence && !ForeignPending) await ReloadDocumentsAsync();
    }
    private async Task ReloadAsync()
    {
        if (!CanRead || OrderId <= 0) return; var lease = Capture(); var orderId = OrderId; var path = Base; loading = true; unavailable = false; message = null;
        try
        {
            using var response = await Http.GetAsync(path, lease.Token); if (!Current(lease)) return;
            if (!response.IsSuccessStatusCode) { unavailable = true; message = Failure(response.StatusCode); return; }
            var result = await response.Content.ReadFromJsonAsync<ReplacementStoredCaseView[]>(lease.Token); if (!Current(lease)) return;
            if (result is null || result.Any(x => x.Value.Originals.All(o => o.OrderId != orderId))) { unavailable = true; message = Text["InvalidResponse"]; return; }
            cases = result; if (!CanWrite && selectedCaseId == 0 && cases.Length > 0) await CaseChangedAsync(cases[0].Id);
        }
        catch (Exception e) when (e is HttpRequestException or JsonException or OperationCanceledException) { if (Current(lease)) { unavailable = true; message = Text["Unavailable"]; } }
        finally { if (Current(lease)) loading = false; }
    }
    private async Task CaseChangedAsync(int id)
    {
        if (pending is not null) return; selectedCaseId = id; form = new() { OrderId = Selected?.Value.Originals.FirstOrDefault()?.OrderId ?? OrderId };
        action = ActionOptions.FirstOrDefault()?.Value ?? ""; await ActionChangedAsync(action);
    }
    private async Task ActionChangedAsync(string value)
    {
        if (pending is not null) return; action = value;
        form.FactKind = FactOptions.FirstOrDefault()?.Value ?? ""; selectedDocument = null; selectedVersion = null; versions = []; documentOffset = 0;
        form.AttemptId = AttemptOptions.FirstOrDefault()?.Value ?? 0; form.ShipmentId = ShipmentOptions.FirstOrDefault()?.Value ?? 0;
        if (NeedsEvidence) await ReloadDocumentsAsync();
    }
    private async Task ReloadDocumentsAsync()
    {
        var lease = Capture(); var selection = ++documentGeneration; var path = $"{Base}/evidence?offset={documentOffset}";
        documents = []; versions = []; selectedDocument = null; selectedVersion = null; evidenceMessage = null;
        if (!Has("legacy-file.documents.read")) { evidenceMessage = Text["EvidenceUnavailable"]; return; }
        try
        {
            using var response = await Http.GetAsync(path, lease.Token); if (!Current(lease) || selection != documentGeneration) return;
            if (!response.IsSuccessStatusCode) { evidenceMessage = Text["EvidenceUnavailable"]; return; }
            var result = await response.Content.ReadFromJsonAsync<ReplacementDocumentView[]>(lease.Token); if (!Current(lease) || selection != documentGeneration) return;
            documents = result ?? []; if (!DocumentOptions.Any()) evidenceMessage = Text["NoEvidence"];
        }
        catch (Exception e) when (e is HttpRequestException or JsonException or OperationCanceledException) { if (Current(lease) && selection == documentGeneration) evidenceMessage = Text["EvidenceUnavailable"]; }
    }
    private Task PreviousDocumentsAsync() { documentOffset = Math.Max(0, documentOffset - 20); return ReloadDocumentsAsync(); }
    private Task NextDocumentsAsync() { documentOffset = Math.Min(10000, documentOffset + 20); return ReloadDocumentsAsync(); }
    private async Task DocumentChangedAsync(Guid? id)
    {
        var lease = Capture(); var selection = ++documentGeneration; selectedDocument = id; selectedVersion = null; versions = []; evidenceMessage = null;
        if (id is null) return; var path = $"{Base}/evidence/{id:D}/versions?offset=0";
        try
        {
            using var response = await Http.GetAsync(path, lease.Token); if (!Current(lease) || selection != documentGeneration) return;
            if (!response.IsSuccessStatusCode) { evidenceMessage = Text["EvidenceUnavailable"]; return; }
            var result = await response.Content.ReadFromJsonAsync<ReplacementDocumentVersionView[]>(lease.Token); if (!Current(lease) || selection != documentGeneration) return;
            versions = result ?? []; if (!VersionOptions.Any()) evidenceMessage = Text["NoEvidence"];
        }
        catch (Exception e) when (e is HttpRequestException or JsonException or OperationCanceledException) { if (Current(lease) && selection == documentGeneration) evidenceMessage = Text["EvidenceUnavailable"]; }
    }
    private async Task SubmitAsync()
    {
        if (!CanSubmit) return; message = null;
        try
        {
            var evidence = NeedsEvidence ? new ReplacementEvidenceReference(selectedDocument!.Value, selectedVersion!.Value) : null;
            var date = DateOnly.TryParseExact(form.Date, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed) ? parsed : default;
            object payload; string path;
            if (action == "Report") { payload = new ReplacementReportInput(form.ReportReason, form.Quantity, evidence!); path = Base; }
            else
            {
                object command = action switch
                {
                    "Approve" => new { Kind = action, form.ReturnDecision, form.BlockProduction, form.BlockShipment, Reason = form.Note },
                    "Reject" or "WaiveReturn" => new { Kind = action, Reason = form.Note },
                    "StartAttempt" => new { Kind = action, form.OrderId, form.Quantity },
                    "CompleteQa" => new { Kind = action, form.AttemptId, form.Produced, form.Accepted, form.Rejected, FinishedDate = date, Evidence = evidence },
                    "RecordReturn" => new { Kind = action, form.OrderId, form.Quantity, Evidence = evidence, Disposition = form.Note },
                    "Ship" => new { Kind = action, form.AttemptId, form.Quantity, form.Carrier, TrackingNumber = form.Tracking, DestinationSnapshot = form.Destination, ShippedDate = date },
                    "ConfirmDelivery" => new { Kind = action, form.ShipmentId, form.Delivered, Evidence = evidence, Reason = form.Note },
                    "AuthorizeRetry" => new { Kind = action, ShipmentId = form.ShipmentId, Reason = form.Note },
                    "WaiveQuantity" => new { Kind = action, form.OrderId, form.Quantity, Reason = form.Note },
                    "RecordRecoveryFact" => new { Kind = action, form.OrderId, form.FactKind, form.Amount, form.Currency, ObservedDate = date, Description = form.Note, Evidence = evidence, ClaimReference = form.FactKind == "Cost" ? null : form.Claim, form.CorrectsFactId },
                    "Close" => new { Kind = action },
                    _ => throw new InvalidOperationException()
                };
                var decision = action is "Approve" or "Reject" or "AuthorizeRetry" or "WaiveReturn" or "WaiveQuantity" || action == "RecordRecoveryFact" && form.FactKind is "Approved" or "Received";
                payload = new ReplacementCommandInput(Selected!.Value.Revision, JsonSerializer.SerializeToElement(command, Wire)); path = $"{Base}/{selectedCaseId}/{(decision ? "decisions" : "commands")}";
            }
            // Capture immutable payload, case revision and operation before the first await.
            pending = new(Guid.NewGuid(), path, JsonSerializer.Serialize(payload, Wire), OrderId, selectedCaseId, Session?.EmployeeId);
            await RetryPendingAsync();
        }
        catch (Exception e) when (e is HttpRequestException or JsonException or OperationCanceledException) { message = Text["Pending"]; }
    }
    private async Task RetryPendingAsync()
    {
        if (pending is null || busy || ForeignPending || PendingState.OriginalWriteInProgress) return; var captured = pending; var lease = Capture(); busy = true; message = null;
        try
        {
            using var sessionResponse = await Http.GetAsync("/bff/session", lease.Token); if (!Current(lease)) return;
            var session = sessionResponse.IsSuccessStatusCode ? await sessionResponse.Content.ReadFromJsonAsync<EmployeeSessionSummary>(lease.Token) : null;
            if (!Current(lease)) return;
            if (session?.IsAuthenticated != true || session.EmployeeId != captured.EmployeeSubject || string.IsNullOrWhiteSpace(session.CsrfToken)) { message = Text["SessionExpired"]; return; }
            using var request = new HttpRequestMessage(HttpMethod.Post, captured.Path) { Content = new StringContent(captured.Payload, System.Text.Encoding.UTF8, "application/json") };
            request.Headers.Add("X-CSRF-TOKEN", session.CsrfToken); request.Headers.Add("Idempotency-Key", captured.OperationId.ToString("D"));
            pending = captured with { Dispatched = true };
            using var response = await Http.SendAsync(request, lease.Token); if (!Current(lease)) return;
            if (response.IsSuccessStatusCode)
            {
                var value = await response.Content.ReadFromJsonAsync<ReplacementStoredCaseView>(lease.Token); if (!Current(lease)) return;
                if (!PendingMatches(value)) { message = Text["Pending"]; return; }
                await ConfirmAsync(value!); return;
            }
            message = Failure(response.StatusCode);
            // Only a producer's explicit no-receipt revision rejection can resolve an unknown
            // operation. Other errors on retry may occur after an earlier successful commit.
            if (response.StatusCode == HttpStatusCode.Conflict && response.Headers.TryGetValues("X-Replacement-Rejection", out var rejection) && rejection.SingleOrDefault() == "Revision") { pending = null; await ReloadAsync(); message = Text["Conflict"]; }
            else if (!captured.Dispatched && response.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden or HttpStatusCode.NotFound or HttpStatusCode.TooManyRequests) pending = null;
        }
        catch (Exception e) when (e is HttpRequestException or JsonException or OperationCanceledException) { if (Current(lease)) message = Text["Pending"]; }
        finally { if (Current(lease)) busy = false; }
    }
    private void CancelUnsent() { if (pending is { Dispatched: false }) { pending = null; message = null; } }
    private bool PendingMatches(ReplacementStoredCaseView? value) => pending is not null && value is { Id: > 0, Value.Revision: > 0 }
        && value.Value.Originals.Any(x => x.OrderId == pending.OrderId) && (pending.CaseId == 0 || value.Id == pending.CaseId);
    private async Task ConfirmAsync(ReplacementStoredCaseView value)
    {
        pending = null; selectedCaseId = value.Id; message = Text["Recorded"]; await ReloadAsync(); await CaseChangedAsync(value.Id); message = Text["Recorded"];
    }
    private async Task ReconcileAsync()
    {
        if (pending is null || busy || ForeignPending || PendingState.OriginalWriteInProgress) return; var lease = Capture(); var captured = pending; busy = true;
        try
        {
            using var response = await Http.GetAsync($"/bff/orders/{captured.OrderId}/replacements/operations/{captured.OperationId:D}", lease.Token); if (!Current(lease)) return;
            var value = response.IsSuccessStatusCode ? await response.Content.ReadFromJsonAsync<ReplacementStoredCaseView>(lease.Token) : null; if (!Current(lease)) return;
            if (PendingMatches(value)) await ConfirmAsync(value!); else message = Text["Pending"];
        }
        catch (Exception e) when (e is HttpRequestException or JsonException or OperationCanceledException) { if (Current(lease)) message = Text["Pending"]; }
        finally { if (Current(lease)) busy = false; }
    }
    private void PreventPendingNavigation(LocationChangingContext context) { if (pending is not null) context.PreventNavigation(); }
    private string Failure(HttpStatusCode status) => Text[status switch { HttpStatusCode.Unauthorized => "SessionExpired", HttpStatusCode.Forbidden => "Forbidden", HttpStatusCode.BadRequest => "InvalidInput", HttpStatusCode.NotFound => "NotFound", HttpStatusCode.Conflict => "Conflict", HttpStatusCode.TooManyRequests => "RateLimited", _ => "Unavailable" }];
    public void Dispose() { generation++; lifetime.Cancel(); lifetime.Dispose(); }
    private sealed class Form
    {
        public string ReportReason { get; set; } = "CarrierDamage";
        public string ReturnDecision { get; set; } = "Waived";
        public bool BlockProduction { get; set; }
        public bool BlockShipment { get; set; }
        public int OrderId { get; set; }
        public int Quantity { get; set; } = 1;
        public int AttemptId { get; set; }
        public int ShipmentId { get; set; }
        public int Produced { get; set; } = 1;
        public int Accepted { get; set; } = 1;
        public int Rejected { get; set; }
        public string Date { get; set; } = "";
        public string Carrier { get; set; } = "";
        public string Tracking { get; set; } = "";
        public string Destination { get; set; } = "";
        public bool Delivered { get; set; } = true;
        public string Note { get; set; } = "";
        public string FactKind { get; set; } = "Cost";
        public decimal Amount { get; set; }
        public string Currency { get; set; } = "";
        public string Claim { get; set; } = "";
        public int? CorrectsFactId { get; set; }
    }
}
