using Legacy.Maliev.Intranet.Contracts;

namespace Legacy.Maliev.Intranet.Bff.Quotations;

/// <summary>Forwards read-only quotation queries with server-held credentials.</summary>
public sealed class QuotationsProxy(HttpClient httpClient)
{
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
