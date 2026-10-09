#pragma warning disable CS1591
namespace Legacy.Maliev.Intranet.Contracts;

public sealed record CustomerDocumentSummary(Guid DocumentId, int CustomerId, string Kind, string Title, string Visibility, long Revision);
public sealed record CustomerDocumentAssociation(string Kind, int ResourceId);
public sealed record CustomerDocumentReceipt(Guid DocumentId, Guid VersionId, int CustomerId, string Kind,
    string ContentSha256, int? QuotationId, IReadOnlyList<int> OrderIds, string VerificationStatus,
    string? VerifiedBySubject, DateTimeOffset? VerifiedAtUtc, long Revision);
public sealed record CustomerDocumentVersionReceipt(Guid DocumentId, Guid VersionId, int CustomerId, int VersionNumber, string ContentSha256, long Revision);
public sealed record CustomerDocumentVersionSummary(Guid DocumentId, Guid VersionId, int VersionNumber, string Kind, string ContentSha256, DateTimeOffset CreatedAtUtc, string VerificationStatus, string? VerifiedBySubject, DateTimeOffset? VerifiedAtUtc, long Revision);
public sealed record CustomerDocumentArchiveRequest(long ExpectedRevision, string Reason);
public sealed record CustomerDocumentVerificationRequest(long ExpectedVerificationRevision, string Status, string Reason);
public sealed record CustomerNdaCoverage(string Kind, int ResourceId);
public sealed record CustomerNdaVerificationRequest(Guid VersionId, long ExpectedRevision, string PartyOne, string PartyTwo,
    DateTimeOffset EffectiveAtUtc, DateTimeOffset? ExpiresAtUtc, DateTimeOffset? RenewalAtUtc, string SurvivalKind,
    DateTimeOffset? SurvivalEndsAtUtc, string ResponsibleEmployeeSubject, IReadOnlyList<CustomerNdaCoverage> Coverage, string Reason);
public sealed record CustomerNdaVerificationReceipt(Guid NdaId, Guid DocumentId, Guid VersionId, long VerificationRevision, string AgreementStatus, string ObligationStatus);
public sealed record CustomerNdaAgreementSummary(Guid NdaId, Guid DocumentId, Guid VersionId, long VerificationRevision,
    DateTimeOffset EffectiveAtUtc, DateTimeOffset? ExpiresAtUtc, DateTimeOffset? RenewalAtUtc, string SurvivalKind,
    DateTimeOffset? SurvivalEndsAtUtc, string ResponsibleEmployeeSubject, string AgreementStatus, string ObligationStatus);
public sealed record CustomerNdaReminder(Guid Id, Guid NdaId, Guid VersionId, long RenewalRevision, int LeadDays, string ResponsibleEmployeeSubject, DateTimeOffset DueAtUtc, string State);
public sealed record CustomerDocumentResult<T>(int StatusCode, T? Value);
public sealed record CustomerProtectionRequest(string Kind, int ResourceId, string Action, bool SocialConsent = false, Guid? VersionId = null);
public sealed record CustomerProtectionDecision(bool Allowed, bool Protected, string ObligationStatus, string Reason);
