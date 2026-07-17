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

        var path = $"uploads/{customerId}/{DateTime.UtcNow:yyyy-MM-dd}";
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"/Uploads?bucket=maliev.com&path={Uri.EscapeDataString(path)}")
        {
            Content = content,
        };
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
