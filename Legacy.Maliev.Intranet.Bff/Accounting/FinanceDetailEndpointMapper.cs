using System.Net;
using System.Net.Http.Json;
using Legacy.Maliev.Intranet.Contracts;
using Legacy.Maliev.Intranet.Server.Accounting;

namespace Legacy.Maliev.Intranet.Bff.Accounting;

internal static class FinanceDetailEndpointMapper
{
    public static async Task<IResult> GetAsync(int id, FinanceDetailAggregator aggregator, CancellationToken cancellationToken)
    {
        if (id <= 0) return Results.BadRequest();
        try
        {
            var page = await aggregator.GetAsync(id, cancellationToken);
            return page is null ? Results.NotFound() : Results.Ok(page);
        }
        catch (Exception exception) when (IsBoundedFailure(exception, cancellationToken))
        {
            return Unavailable();
        }
    }

    public static async Task<IResult> UpdateAsync(int id, FinancePaymentUpdateRequest input, FinancesProxy finances, HttpContext context, CancellationToken cancellationToken)
    {
        if (id <= 0 || input.PaymentDirectionId <= 0 || input.PaymentTypeId <= 0 || input.PaymentMethodId <= 0 || input.Amount < 0 || input.ModifiedDate is null)
            return Results.BadRequest();
        try
        {
            using var response = await finances.UpdateAsync(id, input, cancellationToken);
            return MapWrite(response, context);
        }
        catch (Exception exception) when (IsBoundedFailure(exception, cancellationToken))
        {
            return Unavailable();
        }
    }

    public static async Task<IResult> UploadAsync(
        int id,
        HttpRequest request,
        FinancesProxy finances,
        FinanceFileProxy files,
        FinanceFileWorkflow workflow,
        ILogger<FinanceFileWorkflow> logger,
        CancellationToken cancellationToken)
    {
        if (id <= 0 || !request.HasFormContentType || !Guid.TryParse(request.Headers["Idempotency-Key"].FirstOrDefault(), out var attempt)) return Results.BadRequest();
        try
        {
            var form = await request.ReadFormAsync(cancellationToken);
            var uploads = form.Files.Where(file => file.Length > 0).ToArray();
            if (uploads.Length == 0 || uploads.Sum(file => file.Length) > 200L * 1024 * 1024) return Results.BadRequest();
            using var payment = await finances.GetAsync(id, cancellationToken);
            if (payment.StatusCode == HttpStatusCode.NotFound) return Results.NotFound();
            payment.EnsureSuccessStatusCode();

            var linked = await workflow.UploadAsync(
                id,
                uploads,
                async (selected, token) =>
                {
                    using var response = await files.UploadAsync(id, selected, Operation(attempt, "upload"), token);
                    response.EnsureSuccessStatusCode();
                    var result = await response.Content.ReadFromJsonAsync<FinanceUploadResult>(token)
                        ?? throw new InvalidDataException("FileService returned an empty upload result.");
                    return result.Object.Select(item => new FinanceStoredFile(item.Bucket, item.ObjectName)).ToArray();
                },
                async (paymentId, stored, token) =>
                {
                    using var response = await finances.CreateFileAsync(paymentId, stored.Bucket, stored.ObjectName, Operation(attempt, stored.ObjectName), token);
                    response.EnsureSuccessStatusCode();
                    var item = await response.Content.ReadFromJsonAsync<FinanceFileItem>(token)
                        ?? throw new InvalidDataException("AccountingService returned an empty file record.");
                    return item;
                },
                async (item, token) => { using var response = await finances.DeleteFileAsync(item.Id, token); if (response.StatusCode != HttpStatusCode.NotFound) response.EnsureSuccessStatusCode(); },
                async (stored, token) => { using var response = await files.DeleteAsync(stored.Bucket, stored.ObjectName, token); if (response.StatusCode != HttpStatusCode.NotFound) response.EnsureSuccessStatusCode(); },
                cancellationToken);
            return Results.Ok(linked);
        }
        catch (Exception exception) when (IsBoundedFailure(exception, cancellationToken))
        {
            logger.LogWarning(exception, "Finance file upload failed for payment {PaymentId}.", id);
            return Unavailable();
        }
    }

    public static async Task<IResult> RemoveFileAsync(int id, int fileId, FinanceDetailAggregator aggregator, FinancesProxy finances, FinanceFileProxy files, FinanceFileWorkflow workflow, CancellationToken cancellationToken)
    {
        if (id <= 0 || fileId <= 0) return Results.BadRequest();
        try
        {
            var owned = await aggregator.GetFilesAsync(id, cancellationToken);
            var removed = await workflow.RemoveAsync(
                fileId,
                owned,
                async (item, token) => { using var response = await files.DeleteAsync(item.Bucket, item.ObjectName, token); if (response.StatusCode != HttpStatusCode.NotFound) response.EnsureSuccessStatusCode(); },
                async (ownedId, token) => { using var response = await finances.DeleteFileAsync(ownedId, token); if (response.StatusCode != HttpStatusCode.NotFound) response.EnsureSuccessStatusCode(); },
                cancellationToken);
            return removed ? Results.NoContent() : Results.NotFound();
        }
        catch (Exception exception) when (IsBoundedFailure(exception, cancellationToken))
        {
            return Unavailable();
        }
    }

    public static async Task<IResult> DeleteAsync(int id, FinanceDetailAggregator aggregator, FinancesProxy finances, FinanceFileProxy files, CancellationToken cancellationToken)
    {
        if (id <= 0) return Results.BadRequest();
        try
        {
            // Sequential ordering is intentional: each retry tolerates already-missing storage/metadata.
            foreach (var item in await aggregator.GetFilesAsync(id, cancellationToken))
            {
                using var storage = await files.DeleteAsync(item.Bucket, item.ObjectName, cancellationToken);
                if (storage.StatusCode != HttpStatusCode.NotFound) storage.EnsureSuccessStatusCode();
                using var metadata = await finances.DeleteFileAsync(item.Id, cancellationToken);
                if (metadata.StatusCode != HttpStatusCode.NotFound) metadata.EnsureSuccessStatusCode();
            }

            using var response = await finances.DeleteAsync(id, cancellationToken);
            return response.StatusCode == HttpStatusCode.NotFound ? Results.NotFound() : response.IsSuccessStatusCode ? Results.NoContent() : Unavailable();
        }
        catch (Exception exception) when (IsBoundedFailure(exception, cancellationToken))
        {
            return Unavailable();
        }
    }

    private static string Operation(Guid attempt, string step)
    {
        var hash = System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes($"{attempt:D}:{step}"));
        return new Guid(hash.AsSpan(0, 16)).ToString("D");
    }
    private static IResult MapWrite(HttpResponseMessage response, HttpContext context)
    {
        if (response.StatusCode == HttpStatusCode.NotFound) return Results.NotFound();
        if (response.StatusCode == HttpStatusCode.Conflict) return Results.Conflict();
        if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden) return Results.StatusCode((int)response.StatusCode);
        if (response.StatusCode == HttpStatusCode.TooManyRequests)
        {
            if (response.Headers.RetryAfter?.Delta is { } retry) context.Response.Headers.RetryAfter = ((int)Math.Ceiling(retry.TotalSeconds)).ToString(System.Globalization.CultureInfo.InvariantCulture);
            return Results.StatusCode(StatusCodes.Status429TooManyRequests);
        }
        return response.IsSuccessStatusCode ? Results.NoContent() : Unavailable();
    }
    private static bool IsBoundedFailure(Exception exception, CancellationToken cancellationToken) =>
        exception is HttpRequestException or InvalidDataException or System.Text.Json.JsonException or Polly.Timeout.TimeoutRejectedException || exception is OperationCanceledException && !cancellationToken.IsCancellationRequested;
    private static IResult Unavailable() => Results.Problem(statusCode: StatusCodes.Status503ServiceUnavailable, title: "Finance workflow unavailable");
}
