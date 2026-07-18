using Legacy.Maliev.Intranet.Contracts;

namespace Legacy.Maliev.Intranet.Bff.Quotations;

/// <summary>Forwards read-only quotation queries with server-held credentials.</summary>
public sealed class QuotationsProxy(HttpClient httpClient)
{
    /// <summary>Gets one quotation.</summary>
    public Task<HttpResponseMessage> GetAsync(int id, CancellationToken cancellationToken) =>
        SendAsync($"/quotations/{id}", cancellationToken);

    /// <summary>Gets order links owned by one quotation.</summary>
    public Task<HttpResponseMessage> GetOrdersAsync(int id, CancellationToken cancellationToken) =>
        SendAsync($"/quotations/{id}/orders", cancellationToken);

    /// <summary>Gets file metadata owned by one quotation.</summary>
    public Task<HttpResponseMessage> GetFilesAsync(int id, CancellationToken cancellationToken) =>
        SendAsync($"/quotations/{id}/files", cancellationToken);

    /// <summary>Gets a bounded quotation page.</summary>
    public Task<HttpResponseMessage> GetPageAsync(
        QuotationListSort sort,
        string? search,
        int index,
        int size,
        CancellationToken cancellationToken) =>
        SendAsync($"/quotations?sort={sort}&search={Uri.EscapeDataString(search ?? string.Empty)}&index={index}&size={size}", cancellationToken);

    /// <summary>Gets the service-owned quotation decision statistics.</summary>
    public Task<HttpResponseMessage> GetStatsAsync(CancellationToken cancellationToken) =>
        SendAsync("/quotations/stats", cancellationToken);

    private async Task<HttpResponseMessage> SendAsync(string path, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        return await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
    }
}

/// <summary>Resolves clean quotation files through FileService.</summary>
public sealed class QuotationFileProxy(HttpClient httpClient)
{
    /// <summary>Gets a short-lived URL for a known clean object.</summary>
    public Task<HttpResponseMessage> GetSignedUrlAsync(string bucket, string objectName, CancellationToken cancellationToken) =>
        httpClient.SendAsync(
            new HttpRequestMessage(HttpMethod.Get, $"/uploads/SignedUrl?bucket={Uri.EscapeDataString(bucket)}&objectName={Uri.EscapeDataString(objectName)}"),
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
}
