using System.Net.Http.Headers;

namespace Legacy.Maliev.Intranet.Bff.Accounting;

/// <summary>Streams Finance attachments through FileService scanning and resolves clean objects.</summary>
public sealed class FinanceFileProxy(HttpClient httpClient)
{
    /// <summary>Uploads attachments under a server-owned payment path.</summary>
    public async Task<HttpResponseMessage> UploadAsync(
        int paymentId,
        IReadOnlyList<IFormFile> uploads,
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        using var content = new MultipartFormDataContent();
        foreach (var upload in uploads.Where(file => file.Length > 0))
        {
            var stream = new StreamContent(upload.OpenReadStream());
            stream.Headers.ContentType = MediaTypeHeaderValue.TryParse(upload.ContentType, out var mediaType)
                ? mediaType
                : new MediaTypeHeaderValue("application/octet-stream");
            content.Add(stream, "files", Path.GetFileName(upload.FileName));
        }

        var path = $"accounting/payments/{paymentId}";
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"/Uploads?bucket=maliev.com&path={Uri.EscapeDataString(path)}")
        {
            Content = content,
        };
        request.Headers.Add("Idempotency-Key", idempotencyKey);
        return await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
    }

    /// <summary>Gets a short-lived link for a known clean attachment.</summary>
    public Task<HttpResponseMessage> GetSignedUrlAsync(string bucket, string objectName, CancellationToken cancellationToken) =>
        httpClient.SendAsync(
            new HttpRequestMessage(HttpMethod.Get, $"/uploads/SignedUrl?bucket={Uri.EscapeDataString(bucket)}&objectName={Uri.EscapeDataString(objectName)}"),
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);

    /// <summary>Deletes the exact server-resolved object.</summary>
    public Task<HttpResponseMessage> DeleteAsync(string bucket, string objectName, CancellationToken cancellationToken) =>
        httpClient.SendAsync(
            new HttpRequestMessage(HttpMethod.Delete, $"/Uploads?bucket={Uri.EscapeDataString(bucket)}&objectName={Uri.EscapeDataString(objectName)}"),
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
}

internal sealed record FinanceUploadResult(IReadOnlyList<FinanceUploadedObject> Object);
internal sealed record FinanceUploadedObject(string Bucket, string ObjectName, Uri Uri);
