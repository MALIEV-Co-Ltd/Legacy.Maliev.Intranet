namespace Legacy.Maliev.Intranet.Contracts;

/// <summary>Rejects malformed successful lookup payloads instead of treating missing data as no-match.</summary>
public static class LookupResponseValidation
{
    public static bool IsValid(object? value) => value switch
    {
        LookupPage<LookupArea> page => ValidPage(page.DatasetVersion, page.Items, page.HasMore, page.NextCursor) && page.Items.All(ValidArea),
        LookupPage<string> page => ValidPage(page.DatasetVersion, page.Items, page.HasMore, page.NextCursor) && page.Items.All(x => Digits(x, 5)),
        LookupAddressPage page => ValidPage(page.DatasetVersion, page.Items, page.HasMore, page.NextCursor) && page.Items.All(ValidCombination),
        LookupResolveResponse result => !string.IsNullOrWhiteSpace(result.DatasetVersion) && result.OriginalText is not null &&
            result.NormalizedText is not null && result.DetailText is not null && result.UniqueFields is not null &&
            result.Outcome is "exact" or "ambiguous" or "not-found" or "conflict" &&
            result.Candidates is not null && result.Candidates.Count <= 50 && result.Candidates.All(ValidCombination) &&
            result.Conflicts is not null && result.ExtractedSpans is not null &&
            result.ExtractedSpans.All(span => span is not null && span.Start >= 0 && span.Length >= 0 &&
                span.Start <= result.OriginalText.Length && span.Length <= result.OriginalText.Length - span.Start),
        LookupCompanyPage page => page.Outcome is "matches" or "no-match" or "unavailable" or "unsupported" &&
            page.Provider == "creden" && page.Capability is "suggestion" or "detail" && page.Items is not null &&
            page.Items.Count <= 50 && page.Items.All(item => item is not null &&
                (!string.IsNullOrWhiteSpace(item.NameTh) || !string.IsNullOrWhiteSpace(item.NameEn) || !string.IsNullOrWhiteSpace(item.TaxId))),
        EmployeeSessionSummary => true,
        _ => false
    };

    private static bool ValidPage<T>(string version, IReadOnlyList<T>? items, bool more, string? cursor) =>
        !string.IsNullOrWhiteSpace(version) && items is not null && items.Count <= 50 &&
        (!more || !string.IsNullOrWhiteSpace(cursor)) && cursor?.Length is not > 512;
    private static bool ValidArea(LookupArea? area) => area is not null && !string.IsNullOrWhiteSpace(area.Code) &&
        !string.IsNullOrWhiteSpace(area.NameTh);
    private static bool ValidCombination(LookupAddressCombination? value) => value is not null &&
        ValidArea(value.Province) && ValidArea(value.District) && ValidArea(value.Subdistrict) &&
        value.District.ParentCode == value.Province.Code && value.Subdistrict.ParentCode == value.District.Code && Digits(value.Postcode, 5);
    private static bool Digits(string? value, int length) => value is not null && value.Length == length && value.All(char.IsAsciiDigit);
}
