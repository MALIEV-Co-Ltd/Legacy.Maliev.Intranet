namespace Legacy.Maliev.Intranet.Bff.Accounting;

/// <summary>Resolves and deletes only server-owned clean invoice objects.</summary>
public sealed class InvoiceFileProxy(HttpClient httpClient)
{
    /// <summary>Gets a short-lived URL for a known clean object.</summary>
    public Task<HttpResponseMessage> GetSignedUrlAsync(string bucket, string objectName, CancellationToken cancellationToken) =>
        httpClient.SendAsync(
            new(HttpMethod.Get, $"/uploads/SignedUrl?bucket={Uri.EscapeDataString(bucket)}&objectName={Uri.EscapeDataString(objectName)}"),
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);

    /// <summary>Deletes an exact server-resolved clean object.</summary>
    public Task<HttpResponseMessage> DeleteAsync(string bucket, string objectName, CancellationToken cancellationToken) =>
        httpClient.SendAsync(
            new(HttpMethod.Delete, $"/Uploads?bucket={Uri.EscapeDataString(bucket)}&objectName={Uri.EscapeDataString(objectName)}"),
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
}
