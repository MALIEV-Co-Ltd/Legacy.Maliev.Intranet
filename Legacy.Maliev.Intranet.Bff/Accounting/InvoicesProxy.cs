using Legacy.Maliev.Intranet.Contracts;

namespace Legacy.Maliev.Intranet.Bff.Accounting;

/// <summary>Forwards read-only invoice queries with server-held service credentials.</summary>
public sealed class InvoicesProxy(HttpClient httpClient)
{
    /// <summary>Gets one invoice for server-side quotation detail composition.</summary>
    public Task<HttpResponseMessage> GetAsync(int id, CancellationToken cancellationToken) =>
        SendAsync($"/invoices/{id}", cancellationToken);

    /// <summary>Gets one paid or unpaid invoice page.</summary>
    public Task<HttpResponseMessage> GetPageAsync(
        InvoiceListSort sort,
        string? search,
        int index,
        int size,
        bool paid,
        CancellationToken cancellationToken)
    {
        var path = $"/invoices?sort={sort}&search={Uri.EscapeDataString(search ?? string.Empty)}&index={index}&size={size}&paid={(paid ? "true" : "false")}";
        return SendAsync(path, cancellationToken);
    }

    private async Task<HttpResponseMessage> SendAsync(string path, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        return await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
    }
}
