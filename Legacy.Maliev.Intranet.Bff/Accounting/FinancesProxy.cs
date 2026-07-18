using Legacy.Maliev.Intranet.Contracts;

namespace Legacy.Maliev.Intranet.Bff.Accounting;

/// <summary>Forwards read-only Finance requests to AccountingService with server-held credentials.</summary>
public sealed class FinancesProxy(HttpClient httpClient)
{
    /// <summary>Gets one payment.</summary>
    public Task<HttpResponseMessage> GetAsync(int id, CancellationToken cancellationToken) =>
        SendAsync(new HttpRequestMessage(HttpMethod.Get, $"/payments/{id}"), cancellationToken);

    /// <summary>Gets payment lookup values from an allowlisted Accounting route.</summary>
    public Task<HttpResponseMessage> GetLookupAsync(string resource, CancellationToken cancellationToken) =>
        SendAsync(new HttpRequestMessage(HttpMethod.Get, $"/payments/{resource}"), cancellationToken);

    /// <summary>Gets payment-file metadata owned by one payment.</summary>
    public Task<HttpResponseMessage> GetFilesAsync(int id, CancellationToken cancellationToken) =>
        SendAsync(new HttpRequestMessage(HttpMethod.Get, $"/payments/{id}/files"), cancellationToken);

    /// <summary>Updates a payment using the legacy optimistic-concurrency timestamp.</summary>
    public Task<HttpResponseMessage> UpdateAsync(int id, FinancePaymentUpdateRequest input, CancellationToken cancellationToken)
    {
        var request = new HttpRequestMessage(HttpMethod.Put, $"/payments/{id}") { Content = JsonContent.Create(input) };
        if (input.ModifiedDate is DateTime modified)
        {
            request.Headers.TryAddWithoutValidation("If-Unmodified-Since", modified.ToUniversalTime().ToString("O", System.Globalization.CultureInfo.InvariantCulture));
        }

        return SendAsync(request, cancellationToken);
    }

    /// <summary>Deletes one payment.</summary>
    public Task<HttpResponseMessage> DeleteAsync(int id, CancellationToken cancellationToken) =>
        SendAsync(new HttpRequestMessage(HttpMethod.Delete, $"/payments/{id}"), cancellationToken);

    /// <summary>Links one scanned object to a payment with a stable workflow identity.</summary>
    public Task<HttpResponseMessage> CreateFileAsync(int paymentId, string bucket, string objectName, string idempotencyKey, CancellationToken cancellationToken)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/payments/files")
        {
            Content = JsonContent.Create(new { paymentId, bucket, objectName }),
        };
        request.Headers.Add("Idempotency-Key", idempotencyKey);
        return SendAsync(request, cancellationToken);
    }

    /// <summary>Deletes one payment-file metadata record.</summary>
    public Task<HttpResponseMessage> DeleteFileAsync(int fileId, CancellationToken cancellationToken) =>
        SendAsync(new HttpRequestMessage(HttpMethod.Delete, $"/payments/files/{fileId}"), cancellationToken);

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
        return await SendAsync(new HttpRequestMessage(HttpMethod.Get, path), cancellationToken);
    }

    private async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        using (request)
        {
            return await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        }
    }
}
