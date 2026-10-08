using System.Text.Json;

namespace Legacy.Maliev.Intranet.Contracts;

public static class ReplacementPermissions
{
    public const string Read = "legacy.replacements.read";
    public const string Write = "legacy.replacements.write";
    public const string Approve = "legacy.replacements.approve";
}
public sealed record ReplacementEvidenceReference(Guid DocumentId, Guid VersionId);
public sealed record ReplacementReportInput(string Reason, int Quantity, ReplacementEvidenceReference Evidence);
public sealed record ReplacementCommandInput(int ExpectedRevision, JsonElement Command);
public sealed record ReplacementOriginalView(int OrderId, int CustomerId, int Quantity, int Manufactured, int AffectedQuantity, string? TrackingNumber, DateOnly? FinishedDate, int? QuotationId, int? InvoiceId);
public sealed record ReplacementAttemptView(int Id, int OrderId, int Quantity, DateTimeOffset StartedAt, int? ProducedQuantity, int AcceptedQuantity, int RejectedQuantity, DateOnly? FinishedDate, ReplacementEvidenceReference? QaEvidence);
public sealed record ReplacementShipmentView(int Id, int AttemptId, int OrderId, int Quantity, string Carrier, string TrackingNumber, string DestinationSnapshot, DateOnly ShippedDate, string Outcome, ReplacementEvidenceReference? DeliveryEvidence, bool RetryAuthorized);
public sealed record ReplacementReturnView(int OrderId, int Quantity, ReplacementEvidenceReference Evidence, string Disposition);
public sealed record ReplacementWaiverView(int OrderId, int Quantity, string Reason);
public sealed record ReplacementAuditView(int Revision, string Action, int EmployeeId, DateTimeOffset OccurredAt, string Reason);
public sealed record ReplacementRecoveryFactView(int Id, int OrderId, string Kind, decimal Amount, string Currency, DateOnly ObservedDate, string Description, ReplacementEvidenceReference Evidence, string? ClaimReference, int? CorrectsFactId, int? QuotationId, int? InvoiceId);
public sealed record ReplacementCaseView(int CustomerId, string Reason, string State, int Revision, ReplacementEvidenceReference ReportEvidence,
    IReadOnlyList<ReplacementOriginalView> Originals, IReadOnlyList<ReplacementAttemptView> Attempts, IReadOnlyList<ReplacementShipmentView> Shipments,
    IReadOnlyList<ReplacementReturnView> Returns, IReadOnlyList<ReplacementWaiverView> Waivers, IReadOnlyList<ReplacementAuditView> Audit,
    string? ReturnDecision, bool BlockProductionUntilReturn, bool BlockShipmentUntilReturn, IReadOnlyList<ReplacementRecoveryFactView> RecoveryFacts);
public sealed record ReplacementStoredCaseView(int Id, ReplacementCaseView Value);

public sealed record ReplacementDocumentView(Guid DocumentId, int CustomerId, string Kind, string Title, string Visibility, long Revision);
public sealed record ReplacementDocumentVersionView(Guid DocumentId, Guid VersionId, int VersionNumber, string Kind, string ContentSha256,
    DateTimeOffset CreatedAtUtc, string VerificationStatus, string? VerifiedBySubject, DateTimeOffset? VerifiedAtUtc, long Revision);
