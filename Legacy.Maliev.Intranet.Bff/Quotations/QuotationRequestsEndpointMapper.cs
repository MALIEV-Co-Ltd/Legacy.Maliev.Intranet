using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using Legacy.Maliev.Intranet.Contracts;

namespace Legacy.Maliev.Intranet.Bff.Quotations;

internal static class QuotationRequestsEndpointMapper
{
    public static async Task<IResult> PageAsync(Func<CancellationToken, Task<HttpResponseMessage>> send, int pageIndex, HttpContext context, CancellationToken token)
    {
        var response = await SafeSendAsync(send, token); if (response is null) return Unavailable();
        using (response)
        {
            if (response.StatusCode == HttpStatusCode.NotFound) return Results.Ok(new QuotationRequestPage([], pageIndex, 0, 0, false, pageIndex > 1));
            var failure = Failure(response, context); if (failure is not null) return failure;
            try
            {
                var page = await response.Content.ReadFromJsonAsync<DownstreamPage>(token);
                if (page?.Items is null || page.PageIndex < 1 || page.TotalPages < 0 || page.TotalRecords < 0 || page.Items.Any(x => x.Id <= 0)) return Invalid();
                return Results.Ok(new QuotationRequestPage(
                    page.Items,
                    page.PageIndex,
                    page.TotalPages,
                    page.TotalRecords,
                    page.PageIndex < page.TotalPages,
                    page.PageIndex > 1));
            }
            catch (System.Text.Json.JsonException) { return Invalid(); }
        }
    }

    public static async Task<IResult> DetailAsync(int id, QuotationRequestsProxy requests, QuotationRequestFilesProxy files, CancellationToken token)
    {
        if (id <= 0) return Results.BadRequest();
        try
        {
            using var response = await requests.GetAsync(id, token); if (response.StatusCode == HttpStatusCode.NotFound) return Results.NotFound(); response.EnsureSuccessStatusCode();
            var item = await response.Content.ReadFromJsonAsync<QuotationRequestItem>(token) ?? throw new InvalidDataException();
            if (item.Id != id) throw new InvalidDataException();
            using var metadataResponse = await requests.GetFilesAsync(id, token);
            var metadata = metadataResponse.StatusCode == HttpStatusCode.NotFound ? [] : await ReadAsync<List<RequestFile>>(metadataResponse, token) ?? [];
            if (metadata.Any(file => file.Id <= 0 || file.RequestId != id)) throw new InvalidDataException();
            using var limiter = new SemaphoreSlim(4);
            var resolved = await Task.WhenAll(metadata.Select(x => ResolveAsync(x, files, limiter, token)));
            return Results.Ok(new QuotationRequestDetail(item, resolved));
        }
        catch (Exception exception) when (Bounded(exception, token)) { return Unavailable(); }
    }

    public static async Task<IResult> UpdateAsync(int id, QuotationRequestUpdate input, QuotationRequestsProxy requests, HttpContext context, CancellationToken token)
    {
        if (id <= 0 || input.ModifiedDate is null || input.Done is null) return Results.BadRequest();
        try { using var response = await requests.UpdateAsync(id, input, token); if (response.StatusCode == HttpStatusCode.NotFound) return Results.NotFound(); if (response.StatusCode == HttpStatusCode.Conflict) return Results.Conflict(); return Failure(response, context) ?? Results.NoContent(); }
        catch (Exception exception) when (Bounded(exception, token)) { return Unavailable(); }
    }

    private static async Task<QuotationRequestFileItem> ResolveAsync(RequestFile item, QuotationRequestFilesProxy files, SemaphoreSlim limiter, CancellationToken token)
    {
        if (string.IsNullOrWhiteSpace(item.Bucket) || string.IsNullOrWhiteSpace(item.ObjectName)) return new(item.Id, item.RequestId, item.ObjectName ?? "-", item.CreatedDate, null);
        await limiter.WaitAsync(token); try { using var response = await files.GetSignedUrlAsync(item.Bucket, item.ObjectName, token); var uri = response.StatusCode == HttpStatusCode.NotFound ? null : await ReadAsync<Uri>(response, token); return new(item.Id, item.RequestId, item.ObjectName, item.CreatedDate, uri); } finally { limiter.Release(); }
    }
    private static async Task<T?> ReadAsync<T>(HttpResponseMessage response, CancellationToken token) { response.EnsureSuccessStatusCode(); return await response.Content.ReadFromJsonAsync<T>(token); }
    private static async Task<HttpResponseMessage?> SafeSendAsync(Func<CancellationToken, Task<HttpResponseMessage>> send, CancellationToken token) { try { return await send(token); } catch (Exception ex) when (Bounded(ex, token)) { return null; } }
    private static IResult? Failure(HttpResponseMessage response, HttpContext context) { if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden) return Results.StatusCode((int)response.StatusCode); if (response.StatusCode == HttpStatusCode.TooManyRequests) { if (response.Headers.RetryAfter?.Delta is { } delay && delay > TimeSpan.Zero && delay <= TimeSpan.FromHours(1)) context.Response.Headers.RetryAfter = ((int)Math.Ceiling(delay.TotalSeconds)).ToString(CultureInfo.InvariantCulture); return Results.StatusCode(429); } return response.IsSuccessStatusCode ? null : Unavailable(); }
    private static bool Bounded(Exception ex, CancellationToken token) => ex is HttpRequestException or InvalidDataException or System.Text.Json.JsonException or Polly.Timeout.TimeoutRejectedException || ex is OperationCanceledException && !token.IsCancellationRequested;
    private static IResult Invalid() => Results.Problem(statusCode: 502, title: "Invalid QuotationService response");
    private static IResult Unavailable() => Results.Problem(statusCode: 503, title: "QuotationService unavailable");
    private sealed record DownstreamPage(IReadOnlyList<QuotationRequestItem> Items, int PageIndex, int TotalPages, int TotalRecords);
    private sealed record RequestFile(int Id, int? RequestId, string? Bucket, string? ObjectName, DateTime? CreatedDate, DateTime? ModifiedDate);
}
