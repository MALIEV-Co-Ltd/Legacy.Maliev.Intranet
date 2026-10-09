using System.Net.Http.Headers;
using System.Text.Json;
using System.Net.Http.Json;
using Legacy.Maliev.Intranet.Contracts;

namespace Legacy.Maliev.Intranet.Bff.CustomerDocuments;

/// <summary>Bounded customer registry transport using acting employee credentials only.</summary>
public sealed class CustomerDocumentProxy(HttpClient httpClient)
{
    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = false };
    /// <summary>Reads the bounded registry summaries.</summary>
    public Task<CustomerDocumentResult<IReadOnlyList<CustomerDocumentSummary>>> ListAsync(int customerId, string credential, CancellationToken token) => ReadAsync<IReadOnlyList<CustomerDocumentSummary>>(HttpMethod.Get, $"customers/{customerId}/documents", null, credential, token);
    /// <summary>Reads sealed immutable version history.</summary>
    public Task<CustomerDocumentResult<IReadOnlyList<CustomerDocumentVersionSummary>>> VersionsAsync(int customerId, Guid documentId, string credential, CancellationToken token) => ReadAsync<IReadOnlyList<CustomerDocumentVersionSummary>>(HttpMethod.Get, $"customers/{customerId}/documents/{documentId:D}/versions", null, credential, token);
    /// <summary>Reads exact immutable evidence.</summary>
    public Task<CustomerDocumentResult<CustomerDocumentReceipt>> ReceiptAsync(int customerId, Guid documentId, Guid versionId, string credential, CancellationToken token) => ReadAsync<CustomerDocumentReceipt>(HttpMethod.Get, $"customers/{customerId}/documents/{documentId:D}/versions/{versionId:D}/receipt", null, credential, token);
    /// <summary>Appends exact non-NDA evidence verification without rewriting version bytes.</summary>
    public Task<CustomerDocumentResult<CustomerDocumentReceipt>> VerifyEvidenceAsync(int customerId, Guid documentId, Guid versionId, CustomerDocumentVerificationRequest input, string credential, CancellationToken token) => ReadAsync<CustomerDocumentReceipt>(HttpMethod.Post, $"customers/{customerId}/documents/{documentId:D}/versions/{versionId:D}/verification", JsonContent.Create(input, options: Json), credential, token);
    /// <summary>Verifies externally signed evidence through current downstream authority.</summary>
    public Task<CustomerDocumentResult<CustomerNdaVerificationReceipt>> VerifyAsync(int customerId, Guid documentId, CustomerNdaVerificationRequest input, string credential, CancellationToken token) => ReadAsync<CustomerNdaVerificationReceipt>(HttpMethod.Post, $"customers/{customerId}/documents/{documentId:D}/nda/verification", JsonContent.Create(input, options: Json), credential, token);
    /// <summary>Reads employee-only persisted calendar and surviving obligation evidence.</summary>
    public Task<CustomerDocumentResult<CustomerNdaAgreementSummary>> NdaAsync(int customerId, Guid documentId, Guid versionId, string credential, CancellationToken token) => ReadAsync<CustomerNdaAgreementSummary>(HttpMethod.Get, $"customers/{customerId}/documents/{documentId:D}/nda?versionId={versionId:D}", null, credential, token);
    /// <summary>Reads only the current responsible employee worklist.</summary>
    public Task<CustomerDocumentResult<IReadOnlyList<CustomerNdaReminder>>> RemindersAsync(string credential, CancellationToken token, DateTimeOffset? dueFromUtc = null, DateTimeOffset? dueThroughUtc = null, string? state = null, int limit = 20)
    {
        var query = new List<string> { $"limit={limit}" };
        if (dueFromUtc is not null) query.Add("dueFromUtc=" + Uri.EscapeDataString(dueFromUtc.Value.ToString("O", System.Globalization.CultureInfo.InvariantCulture)));
        if (dueThroughUtc is not null) query.Add("dueThroughUtc=" + Uri.EscapeDataString(dueThroughUtc.Value.ToString("O", System.Globalization.CultureInfo.InvariantCulture)));
        if (state is not null) query.Add("state=" + Uri.EscapeDataString(state));
        return ReadAsync<IReadOnlyList<CustomerNdaReminder>>(HttpMethod.Get, "staff/nda-reminders?" + string.Join("&", query), null, credential, token);
    }
    /// <summary>Evaluates a server-resolved output resource before disclosure.</summary>
    public Task<CustomerDocumentResult<CustomerProtectionDecision>> EvaluateAsync(int customerId, CustomerProtectionRequest input, string credential, CancellationToken token) => ReadAsync<CustomerProtectionDecision>(HttpMethod.Post, $"customers/{customerId}/protection/evaluate", JsonContent.Create(input, options: Json), credential, token);
    /// <summary>Archives metadata without deleting protected originals.</summary>
    public async Task<int> ArchiveAsync(int customerId, Guid documentId, CustomerDocumentArchiveRequest input, string credential, CancellationToken token)
    {
        try
        {
            using var request = Request(HttpMethod.Post, $"customers/{customerId}/documents/{documentId:D}/archive", credential);
            request.Content = JsonContent.Create(input, options: Json);
            using var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token);
            return response.IsSuccessStatusCode ? 204 : SafeStatus(response);
        }
        catch (Exception exception) when (Bounded(exception, token)) { return 503; }
    }

    /// <summary>Uploads one file with the owner's multipart contract and replay identity.</summary>
    public async Task<CustomerDocumentResult<CustomerDocumentVersionReceipt>> UploadAsync(int customerId, Guid? documentId, IFormCollection form, string idempotencyKey, string credential, CancellationToken token)
    {
        using var multipart = new MultipartFormDataContent();
        foreach (var key in new[] { "Kind", "Title", "Visibility", "Associations", "ExpectedRevision" })
            if (form.TryGetValue(key, out var value)) multipart.Add(new StringContent(value.ToString()), key);
        var file = form.Files[0];
        await using var stream = file.OpenReadStream();
        using var content = new StreamContent(stream);
        content.Headers.ContentType = new MediaTypeHeaderValue(file.ContentType);
        multipart.Add(content, "files", Path.GetFileName(file.FileName));
        return await ReadAsync<CustomerDocumentVersionReceipt>(HttpMethod.Post, documentId is null ? $"customers/{customerId}/documents" : $"customers/{customerId}/documents/{documentId:D}/versions", multipart, credential, token, idempotencyKey);
    }

    /// <summary>Buffers only a bounded attachment; no provider URL or header is forwarded.</summary>
    public async Task<CustomerDocumentResult<byte[]>> DownloadAsync(int customerId, Guid documentId, Guid versionId, string credential, CancellationToken token)
    {
        try
        {
            using var request = Request(HttpMethod.Get, $"customers/{customerId}/documents/{documentId:D}/versions/{versionId:D}/download", credential);
            using var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token);
            if (!response.IsSuccessStatusCode) return new(SafeStatus(response), null);
            if (response.Content.Headers.ContentType?.MediaType is not ("application/pdf" or "image/png" or "image/jpeg")) return new(503, null);
            var bytes = await ReadBoundedAsync(response.Content, 20 * 1024 * 1024, token);
            return bytes.Length == 0 ? new(503, null) : new(200, bytes);
        }
        catch (Exception exception) when (Bounded(exception, token)) { return new(503, null); }
    }

    private async Task<CustomerDocumentResult<T>> ReadAsync<T>(HttpMethod method, string path, HttpContent? content, string credential, CancellationToken token, string? idempotencyKey = null)
    {
        try
        {
            using var request = Request(method, path, credential); request.Content = content;
            if (idempotencyKey is not null) request.Headers.Add("Idempotency-Key", idempotencyKey);
            using var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token);
            if (!response.IsSuccessStatusCode) return new(SafeStatus(response), default);
            var value = JsonSerializer.Deserialize<T>(await ReadBoundedAsync(response.Content, 256 * 1024, token), Json);
            return value is null ? new(503, default) : new(200, value);
        }
        catch (Exception exception) when (Bounded(exception, token)) { return new(503, default); }
    }
    private static HttpRequestMessage Request(HttpMethod method, string path, string credential)
    {
        if (string.IsNullOrWhiteSpace(credential)) throw new InvalidDataException();
        var request = new HttpRequestMessage(method, path); request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", credential); return request;
    }
    private static async Task<byte[]> ReadBoundedAsync(HttpContent content, int limit, CancellationToken token)
    {
        if (content.Headers.ContentLength > limit) throw new InvalidDataException();
        await using var source = await content.ReadAsStreamAsync(token); using var destination = new MemoryStream(); var buffer = new byte[8192]; int read;
        while ((read = await source.ReadAsync(buffer, token)) > 0) { if (destination.Length + read > limit) throw new InvalidDataException(); destination.Write(buffer, 0, read); }
        if (content.Headers.ContentLength is { } expected && expected != destination.Length) throw new InvalidDataException();
        return destination.ToArray();
    }
    private static int SafeStatus(HttpResponseMessage response) => (int)response.StatusCode is 400 or 401 or 403 or 404 or 409 or 413 or 415 or 422 ? (int)response.StatusCode : 503;
    private static bool Bounded(Exception exception, CancellationToken token) => exception is HttpRequestException or InvalidDataException or JsonException || exception is OperationCanceledException && !token.IsCancellationRequested;
}
