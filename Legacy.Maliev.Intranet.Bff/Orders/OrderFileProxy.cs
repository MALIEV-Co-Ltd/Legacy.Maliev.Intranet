using System.Net.Http.Headers;
using System.Net.Http.Json;

namespace Legacy.Maliev.Intranet.Bff.Orders;

/// <summary>Streams order files through FileService quarantine and resolves clean-object URLs.</summary>
public sealed class OrderFileProxy(HttpClient httpClient)
{
    /// <summary>Uploads files under a server-owned bucket and customer path.</summary>
    public async Task<HttpResponseMessage> UploadAsync(
        int customerId,
        IReadOnlyList<IFormFile> uploads,
        CancellationToken cancellationToken)
        => await UploadAsync(customerId, uploads, null, cancellationToken);

    /// <summary>Uploads files with a durable workflow identity for replay-safe FileService implementations.</summary>
    public async Task<HttpResponseMessage> UploadAsync(
        int customerId,
        IReadOnlyList<IFormFile> uploads,
        string? idempotencyKey,
        CancellationToken cancellationToken)
        => await UploadAsync(
            customerId,
            uploads,
            $"uploads/{customerId}/{DateTime.UtcNow:yyyy-MM-dd}",
            idempotencyKey,
            cancellationToken);

    /// <summary>Uploads files with the exact path and workflow identity persisted by a durable saga.</summary>
    public async Task<HttpResponseMessage> UploadAsync(
        int customerId,
        IReadOnlyList<IFormFile> uploads,
        string uploadPath,
        string? idempotencyKey,
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

        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"/Uploads?bucket=maliev.com&path={Uri.EscapeDataString(uploadPath)}")
        {
            Content = content,
        };
        if (!string.IsNullOrWhiteSpace(idempotencyKey)) request.Headers.Add("Idempotency-Key", idempotencyKey);
        return await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
    }

    /// <summary>Gets a short-lived URL for a known clean object.</summary>
    public Task<HttpResponseMessage> GetSignedUrlAsync(
        string bucket,
        string objectName,
        CancellationToken cancellationToken) =>
        httpClient.SendAsync(
            new HttpRequestMessage(
                HttpMethod.Get,
                $"/uploads/SignedUrl?bucket={Uri.EscapeDataString(bucket)}&objectName={Uri.EscapeDataString(objectName)}"),
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);

    /// <summary>Deletes the exact server-resolved object.</summary>
    public Task<HttpResponseMessage> DeleteAsync(
        string bucket,
        string objectName,
        CancellationToken cancellationToken) =>
        httpClient.SendAsync(
            new HttpRequestMessage(
                HttpMethod.Delete,
                $"/Uploads?bucket={Uri.EscapeDataString(bucket)}&objectName={Uri.EscapeDataString(objectName)}"),
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
}

/// <summary>FileService clean-object response.</summary>
public sealed record OrderUploadResult(IReadOnlyList<OrderUploadedObject> Object);

/// <summary>One uploaded and scanned object.</summary>
public sealed record OrderUploadedObject(string Bucket, string ObjectName, Uri Uri);
