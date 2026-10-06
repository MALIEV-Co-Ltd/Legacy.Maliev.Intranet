using System.Net;
using System.Net.Http.Json;
using Legacy.Maliev.Intranet.Contracts;

namespace Legacy.Maliev.Intranet.Client.Shared.Infrastructure;

/// <summary>Uses only the existing authenticated same-origin browser HttpClient.</summary>
public sealed class LookupClient(HttpClient http)
{
    public async Task<LookupPage<LookupArea>> SearchAreasAsync(string level, string query, LookupAddressConstraints constraints,
        string? cursor, CancellationToken cancellationToken)
    {
        var page = await GetAsync<LookupPage<LookupArea>>(
            "/bff/lookups/thai-addresses/" + level + AddressQuery(query, constraints, cursor), cancellationToken);
        if (page.Items.Any(area => !ValidScopedArea(area, level, constraints)))
            throw new LookupRequestException(HttpStatusCode.BadGateway);
        return page;
    }

    private static bool ValidScopedArea(LookupArea area, string level, LookupAddressConstraints constraints) => level switch
    {
        "provinces" => Digits(area.Code, 2) && area.ProvinceCode is null && area.DistrictCode is null &&
            (constraints.ProvinceCode is null || area.Code == constraints.ProvinceCode),
        "districts" => Digits(area.Code, 4) && Digits(area.ProvinceCode, 2) && area.DistrictCode is null &&
            (constraints.ProvinceCode is null || area.ProvinceCode == constraints.ProvinceCode) &&
            (constraints.DistrictCode is null || area.Code == constraints.DistrictCode),
        "subdistricts" => Digits(area.Code, 6) && Digits(area.DistrictCode, 4) &&
            (area.ProvinceCode is null || Digits(area.ProvinceCode, 2) &&
                (constraints.ProvinceCode is null || area.ProvinceCode == constraints.ProvinceCode)) &&
            (constraints.DistrictCode is null || area.DistrictCode == constraints.DistrictCode) &&
            (constraints.SubdistrictCode is null || area.Code == constraints.SubdistrictCode),
        _ => false
    };

    private static bool Digits(string? value, int length) => value is not null && value.Length == length && value.All(char.IsAsciiDigit);

    public Task<LookupAddressPage> SearchAddressesAsync(string query, LookupAddressConstraints constraints,
        string? cursor, CancellationToken cancellationToken) => GetAsync<LookupAddressPage>(
        "/bff/lookups/thai-addresses/autocomplete" + Query(("q", query), ("provinceCode", constraints.ProvinceCode),
            ("districtCode", constraints.DistrictCode), ("subdistrictCode", constraints.SubdistrictCode),
            ("postcode", constraints.Postcode), ("cursor", cursor), ("limit", "20")), cancellationToken);

    public Task<LookupCompanyPage> SearchCompaniesAsync(string query, bool taxId, string language,
        CancellationToken cancellationToken) => GetAsync<LookupCompanyPage>("/bff/lookups/companies/search" +
        Query(("q", query), ("queryType", taxId ? "tax-id" : "name"), ("language", language), ("limit", "20")), cancellationToken);

    public async Task<LookupResolveResponse> ResolveAsync(LookupResolveRequest input, CancellationToken cancellationToken)
    {
        using var sessionResponse = await http.GetAsync("/bff/session", cancellationToken);
        var session = await ReadAsync<EmployeeSessionSummary>(sessionResponse, cancellationToken);
        if (!session.IsAuthenticated) throw new LookupRequestException(HttpStatusCode.Unauthorized);
        if (string.IsNullOrWhiteSpace(session.CsrfToken)) throw new LookupRequestException(HttpStatusCode.Forbidden);
        using var request = new HttpRequestMessage(HttpMethod.Post, "/bff/lookups/thai-addresses/resolve")
        { Content = JsonContent.Create(input) };
        request.Headers.Add("X-CSRF-TOKEN", session.CsrfToken);
        using var response = await http.SendAsync(request, cancellationToken);
        var result = await ReadAsync<LookupResolveResponse>(response, cancellationToken);
        if (result.OriginalText != input.Text) throw new LookupRequestException(HttpStatusCode.BadGateway);
        return result;
    }

    private async Task<T> GetAsync<T>(string path, CancellationToken cancellationToken)
    {
        using var response = await http.GetAsync(path, cancellationToken);
        return await ReadAsync<T>(response, cancellationToken);
    }

    private static async Task<T> ReadAsync<T>(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (!response.IsSuccessStatusCode) throw new LookupRequestException(response.StatusCode);
        try
        {
            var value = await response.Content.ReadFromJsonAsync<T>(cancellationToken);
            if (!LookupResponseValidation.IsValid(value)) throw new LookupRequestException(HttpStatusCode.BadGateway);
            return value!;
        }
        catch (System.Text.Json.JsonException)
        {
            throw new LookupRequestException(HttpStatusCode.BadGateway);
        }
    }

    private static string Query(params (string Name, string? Value)[] values) => "?" + string.Join("&",
        values.Where(value => !string.IsNullOrWhiteSpace(value.Value))
            .Select(value => value.Name + "=" + Uri.EscapeDataString(value.Value!)));

    private static string AddressQuery(string query, LookupAddressConstraints constraints, string? cursor) =>
        Query(("q", query), ("provinceCode", constraints.ProvinceCode), ("districtCode", constraints.DistrictCode),
            ("subdistrictCode", constraints.SubdistrictCode), ("postcode", constraints.Postcode), ("cursor", cursor), ("limit", "20"));
}

/// <summary>Contains a status only; provider bodies and customer data are not exception messages.</summary>
public sealed class LookupRequestException(HttpStatusCode statusCode) : Exception("Lookup request failed.")
{
    public HttpStatusCode StatusCode { get; } = statusCode;
}
