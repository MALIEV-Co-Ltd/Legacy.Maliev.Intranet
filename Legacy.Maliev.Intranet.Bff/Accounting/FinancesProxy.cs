using Legacy.Maliev.Intranet.Contracts;

namespace Legacy.Maliev.Intranet.Bff.Accounting;

/// <summary>Forwards read-only Finance requests to AccountingService with server-held credentials.</summary>
public sealed class FinancesProxy(HttpClient httpClient)
{
    /// <summary>Gets a bounded payment page.</summary>
    public Task<HttpResponseMessage> GetPageAsync(
        FinancePaymentSort sort,
        string? search,
        int index,
        int size,
        CancellationToken cancellationToken) =>
        SendAsync($"/payments?sort={sort}&search={Uri.EscapeDataString(search ?? string.Empty)}&index={index}&size={size}", cancellationToken);

    /// <summary>Gets one allowlisted summary projection.</summary>
    public Task<HttpResponseMessage> GetSummaryAsync(string path, CancellationToken cancellationToken) =>
        SendAsync(path, cancellationToken);

    private async Task<HttpResponseMessage> SendAsync(string path, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        return await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
    }
}
