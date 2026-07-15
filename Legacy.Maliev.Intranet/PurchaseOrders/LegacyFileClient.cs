using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;

namespace Legacy.Maliev.Intranet.PurchaseOrders;

/// <summary>Uses FileService quarantine, malware scanning, GCS, and signed URLs.</summary>
public sealed class LegacyFileClient(HttpClient httpClient) : ILegacyFileClient
{
    /// <inheritdoc />
    public async Task<UploadObjectResponse> UploadPdfAsync(int purchaseOrderId, byte[] pdf, string token, CancellationToken cancellationToken)
    {
        using var content = new MultipartFormDataContent();
        var file = new ByteArrayContent(pdf); file.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
        content.Add(file, "files", $"PurchaseOrder_{purchaseOrderId}.pdf");
        var path = $"purchaseorders/{DateTime.UtcNow:yyyy/MM/dd}";
        using var request = Create(HttpMethod.Post, $"/Uploads?bucket=maliev.com&path={Uri.EscapeDataString(path)}", token); request.Content = content;
        using var response = await httpClient.SendAsync(request, cancellationToken); response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<UploadResultResponse>(cancellationToken) ?? throw new InvalidOperationException("Empty FileService response.");
        return result.Object.Single();
    }
    /// <inheritdoc />
    public async Task<Uri?> GetSignedUrlAsync(string bucket, string objectName, string token, CancellationToken cancellationToken)
    {
        using var request = Create(HttpMethod.Get, $"/uploads/SignedUrl?bucket={Uri.EscapeDataString(bucket)}&objectName={Uri.EscapeDataString(objectName)}", token);
        using var response = await httpClient.SendAsync(request, cancellationToken); if (response.StatusCode == HttpStatusCode.NotFound) return null; response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<Uri>(cancellationToken);
    }
    /// <inheritdoc />
    public async Task DeleteAsync(string bucket, string objectName, string token, CancellationToken cancellationToken)
    {
        using var request = Create(HttpMethod.Delete, $"/Uploads?bucket={Uri.EscapeDataString(bucket)}&objectName={Uri.EscapeDataString(objectName)}", token);
        using var response = await httpClient.SendAsync(request, cancellationToken); if (response.StatusCode != HttpStatusCode.NotFound) response.EnsureSuccessStatusCode();
    }
    private static HttpRequestMessage Create(HttpMethod method, string uri, string token)
    {
        var request = new HttpRequestMessage(method, uri); request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token); return request;
    }
}

/// <summary>FileService purchase-order boundary.</summary>
public interface ILegacyFileClient
{
    /// <summary>Uploads and scans a generated PDF.</summary>
    Task<UploadObjectResponse> UploadPdfAsync(int purchaseOrderId, byte[] pdf, string token, CancellationToken cancellationToken);
    /// <summary>Gets a signed URL for a known-clean object.</summary>
    Task<Uri?> GetSignedUrlAsync(string bucket, string objectName, string token, CancellationToken cancellationToken);
    /// <summary>Deletes a cloud object.</summary>
    Task DeleteAsync(string bucket, string objectName, string token, CancellationToken cancellationToken);
}
/// <summary>FileService upload response.</summary>
public sealed record UploadResultResponse(IReadOnlyList<UploadObjectResponse> Object);
/// <summary>Uploaded clean object.</summary>
public sealed record UploadObjectResponse(string Bucket, string ObjectName, Uri Uri);