using System.Net;
using Legacy.Maliev.Intranet.Contracts;

namespace Legacy.Maliev.Intranet.Bff.Accounting;

internal static class InvoiceDetailEndpointMapper
{
    public static async Task<IResult> GetAsync(int id, InvoiceDetailAggregator aggregator, CancellationToken cancellationToken)
    {
        if (id <= 0) return Results.BadRequest();
        try
        {
            var detail = await aggregator.GetAsync(id, cancellationToken);
            return detail is null ? Results.NotFound() : Results.Ok(detail);
        }
        catch (Exception exception) when (IsBoundedFailure(exception, cancellationToken)) { return Unavailable(); }
    }

    public static async Task<IResult> UpdateAsync(int id, InvoiceUpdateRequest input, InvoiceDetailAggregator aggregator, InvoiceDetailProxy invoices, HttpContext context, CancellationToken cancellationToken)
    {
        if (id <= 0 || input.ModifiedDate == default || input.InternalComment?.Length > 1000) return Results.BadRequest();
        try
        {
            var invoice = await aggregator.GetInvoiceAsync(id, cancellationToken);
            if (invoice is null) return Results.NotFound();
            using var response = await invoices.UpdateAsync(id, invoice, input, cancellationToken);
            return MapWrite(response, context);
        }
        catch (Exception exception) when (IsBoundedFailure(exception, cancellationToken)) { return Unavailable(); }
    }

    public static async Task<IResult> DeleteAsync(int id, InvoiceDetailAggregator aggregator, InvoiceDetailProxy invoices, InvoiceFileProxy files, CancellationToken cancellationToken)
    {
        if (id <= 0) return Results.BadRequest();
        try
        {
            var owned = await aggregator.GetOwnedAsync(id, cancellationToken);
            if (owned is null) return Results.NotFound();

            // Retry-safe ordering: remove the clean object, then its owned metadata, then dependants and parent.
            foreach (var file in owned.Value.Files)
            {
                using var stored = await files.DeleteAsync(file.Bucket, file.ObjectName, cancellationToken);
                if (stored.StatusCode != HttpStatusCode.NotFound) stored.EnsureSuccessStatusCode();
                using var metadata = await invoices.DeleteInvoiceFileAsync(file.Id, cancellationToken);
                if (metadata.StatusCode != HttpStatusCode.NotFound) metadata.EnsureSuccessStatusCode();
            }
            foreach (var item in owned.Value.Items)
            {
                using var response = await invoices.DeleteOrderItemAsync(item.Id, cancellationToken);
                if (response.StatusCode != HttpStatusCode.NotFound) response.EnsureSuccessStatusCode();
            }
            using var invoice = await invoices.DeleteInvoiceAsync(id, cancellationToken);
            return invoice.StatusCode == HttpStatusCode.NotFound ? Results.NotFound() : invoice.IsSuccessStatusCode ? Results.NoContent() : Unavailable();
        }
        catch (Exception exception) when (IsBoundedFailure(exception, cancellationToken)) { return Unavailable(); }
    }

    private static IResult MapWrite(HttpResponseMessage response, HttpContext context)
    {
        if (response.StatusCode == HttpStatusCode.NotFound) return Results.NotFound();
        if (response.StatusCode is HttpStatusCode.Conflict or HttpStatusCode.PreconditionFailed) return Results.Conflict();
        if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden) return Results.StatusCode((int)response.StatusCode);
        if (response.StatusCode == HttpStatusCode.TooManyRequests)
        {
            if (response.Headers.RetryAfter?.Delta is { } retry) context.Response.Headers.RetryAfter = ((int)Math.Ceiling(retry.TotalSeconds)).ToString(System.Globalization.CultureInfo.InvariantCulture);
            return Results.StatusCode(StatusCodes.Status429TooManyRequests);
        }
        return response.IsSuccessStatusCode ? Results.NoContent() : Unavailable();
    }

    private static bool IsBoundedFailure(Exception exception, CancellationToken cancellationToken) =>
        exception is HttpRequestException or InvalidDataException or System.Text.Json.JsonException or Polly.Timeout.TimeoutRejectedException ||
        exception is OperationCanceledException && !cancellationToken.IsCancellationRequested;
    private static IResult Unavailable() => Results.Problem(statusCode: StatusCodes.Status503ServiceUnavailable, title: "Invoice workflow unavailable");
}
