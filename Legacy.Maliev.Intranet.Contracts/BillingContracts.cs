namespace Legacy.Maliev.Intranet.Contracts;

/// <summary>Stage values of the versioned Accounting read/preview wire.</summary>
public enum BillingStageKind { Full, Deposit, Installment, Remaining }
/// <summary>Authoritative commercial components; independent from legal tax recognition.</summary>
public sealed record BillingMoney(decimal Base, decimal Vat, decimal Gross, string Currency);
/// <summary>Immutable source line allocation.</summary>
public sealed record BillingLine(int SourceLineId, BillingMoney Amount, string TaxCategory);
/// <summary>Financial source values retained by Accounting.</summary>
public sealed record BillingSnapshot(int QuotationId, int CustomerId, string TaxId, string Revision, string Digest,
    BillingMoney Cap, IReadOnlyList<BillingLine> Lines, int CurrencyPrecision);
/// <summary>Routing for the same legal customer and tax identity.</summary>
public sealed record BillingRecipient(int CustomerId, string TaxId, string Recipient, string? Branch, string Address);
/// <summary>Evidence requirement kinds retained in stage history.</summary>
public enum BillingEvidenceKind { Shipment, Release, Acceptance, BillingInstruction }
/// <summary>Stage history of source-owned requirements; not proof of satisfaction.</summary>
public sealed record BillingEvidenceRequirement(BillingEvidenceKind Kind, IReadOnlyList<int> OrderIds);
/// <summary>One issued commercial request with separate cash, withholding and credits.</summary>
public sealed record BillingStageView(Guid Id, BillingStageKind Kind, BillingMoney Amount, decimal Cash, decimal Withholding,
    decimal Credit, DateOnly? DueDate, BillingRecipient Recipient, IReadOnlyList<BillingEvidenceRequirement> Requirements,
    IReadOnlyList<BillingLine> Portions, IReadOnlyList<BillingLine> Credits);
/// <summary>Staff financial timeline supplied by Accounting; never recomputed from tax documents.</summary>
public sealed record BillingAccountView(Guid Id, long Revision, BillingSnapshot Snapshot, IReadOnlyList<BillingStageView> Stages,
    BillingMoney Billed, decimal Cash, decimal Withholding, decimal Outstanding, decimal Unbilled, decimal VatRecognized);
/// <summary>Nonmutating preview request; browser supplies no actor or source/evidence authority.</summary>
public sealed record BillingPreviewRequest(BillingStageKind Kind, decimal? Amount, decimal? Percentage, DateOnly? DueDate,
    BillingRecipient Recipient, long ExpectedRevision);
/// <summary>Financial feasibility only; no evidence or issuance authorization.</summary>
public sealed record BillingStagePreview(Guid AccountId, long Revision, BillingMoney Amount, IReadOnlyList<BillingLine> Portions);
