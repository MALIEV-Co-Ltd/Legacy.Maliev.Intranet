using System.Net.Http.Json;
using Legacy.Maliev.Intranet.Contracts;

namespace Legacy.Maliev.Intranet.Bff.Quotations;

/// <summary>Forwards quotation-request operations with server-held service credentials.</summary>
public sealed class QuotationRequestsProxy(HttpClient httpClient)
{
    /// <summary>Gets a bounded request page.</summary>
    public Task<HttpResponseMessage> GetPageAsync(QuotationRequestSort sort, string? search, int index, int size, CancellationToken token) =>
        SendAsync(new(HttpMethod.Get, $"/quotationrequests?sort={sort}&search={Uri.EscapeDataString(search ?? string.Empty)}&index={index}&size={size}"), token);
    /// <summary>Gets one request.</summary>
    public Task<HttpResponseMessage> GetAsync(int id, CancellationToken token) => SendAsync(new(HttpMethod.Get, $"/quotationrequests/{id}"), token);
    /// <summary>Gets request-owned file metadata.</summary>
    public Task<HttpResponseMessage> GetFilesAsync(int id, CancellationToken token) => SendAsync(new(HttpMethod.Get, $"/quotationrequests/{id}/files"), token);
    /// <summary>Updates one request with its expected modified timestamp.</summary>
    public Task<HttpResponseMessage> UpdateAsync(int id, QuotationRequestUpdate input, CancellationToken token)
    {
        var payload = new QuotationRequestUpdatePayload(
            input.FirstName,
            input.LastName,
            input.Email,
            input.TelephoneNumber,
            input.Country,
            input.CompanyName,
            input.TaxIdentification,
            input.Message,
            input.InternalComment,
            input.Done);
        var request = new HttpRequestMessage(HttpMethod.Put, $"/quotationrequests/{id}") { Content = JsonContent.Create(payload) };
        if (input.ModifiedDate is DateTime modified) request.Headers.TryAddWithoutValidation("X-Expected-Modified-Date", modified.ToUniversalTime().ToString("O", System.Globalization.CultureInfo.InvariantCulture));
        return SendAsync(request, token);
    }
    private async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
    {
        using (request) return await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token);
    }

    private sealed record QuotationRequestUpdatePayload(
        string? FirstName,
        string? LastName,
        string? Email,
        string? TelephoneNumber,
        string? Country,
        string? CompanyName,
        string? TaxIdentification,
        string? Message,
        string? InternalComment,
        bool? Done);
}

/// <summary>Resolves clean request files through FileService.</summary>
public sealed class QuotationRequestFilesProxy(HttpClient httpClient)
{
    /// <summary>Gets a short-lived URL for a known clean object.</summary>
    public Task<HttpResponseMessage> GetSignedUrlAsync(string bucket, string objectName, CancellationToken token) =>
        httpClient.SendAsync(new HttpRequestMessage(HttpMethod.Get, $"/uploads/SignedUrl?bucket={Uri.EscapeDataString(bucket)}&objectName={Uri.EscapeDataString(objectName)}"), HttpCompletionOption.ResponseHeadersRead, token);
}
