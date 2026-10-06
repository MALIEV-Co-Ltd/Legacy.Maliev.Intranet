using System.Text.Json.Serialization;

namespace Legacy.Maliev.Intranet.Contracts;

/// <summary>Catalog v1 lookup projections. Codes remain strings, including leading zeroes.</summary>
public sealed record LookupArea(string Code, string NameTh, string? NameEn,
    string? ProvinceCode = null, string? DistrictCode = null)
{
    [JsonIgnore] public string? ParentCode => DistrictCode ?? ProvinceCode;
}
public sealed record LookupPage<T>(string DatasetVersion, IReadOnlyList<T> Items, bool HasMore, string? NextCursor);
public sealed record LookupAddressCombination(LookupArea Province, LookupArea District,
    LookupArea Subdistrict, string Postcode);
public sealed record LookupAddressPage(string DatasetVersion, IReadOnlyList<LookupAddressCombination> Items,
    bool HasMore, string? NextCursor);
public sealed record LookupAddressConstraints(string? ProvinceCode = null, string? DistrictCode = null,
    string? SubdistrictCode = null, string? Postcode = null);
public sealed record LookupResolveRequest(string Text, LookupAddressConstraints? Constraints = null);
public sealed record LookupUniqueFields(LookupArea? Province, LookupArea? District,
    LookupArea? Subdistrict, string? Postcode);
public sealed record LookupExtractedSpan(int Start, int Length, string Kind, string Text);
public sealed record LookupResolveResponse(string DatasetVersion, string OriginalText, string NormalizedText,
    string Outcome, IReadOnlyList<LookupAddressCombination> Candidates, bool HasMore,
    LookupUniqueFields UniqueFields, string DetailText, IReadOnlyList<LookupExtractedSpan> ExtractedSpans,
    IReadOnlyList<string> Conflicts);
public sealed record LookupCompany(string? NameTh, string? NameEn, string? TaxId, string? Status,
    string? CompanyType, string? Objectives, string? RegisteredAddress, string? SourceUrl, DateTimeOffset? RetrievedAt);
public sealed record LookupCompanyPage(string Outcome, string Provider, string Capability,
    IReadOnlyList<LookupCompany> Items, bool HasMore);
