using System.Globalization;
using System.Net;
using System.Text.Encodings.Web;
using System.Text.Json;
using Legacy.Maliev.Intranet.Auth;
using Legacy.Maliev.Intranet.Contracts;
using Microsoft.AspNetCore.Routing;

namespace Legacy.Maliev.Intranet.Bff.Operations;

/// <summary>Maps employee sessions to privacy-safe outcome receipts.</summary>
internal static class OutcomeReadbackEndpointMapper
{
    internal const string BrowserReceiptPath = "/Operations/Check";
    internal const int MaximumPayloadBytes = 1024 * 1024;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private static readonly JsonSerializerOptions BrowserJsonOptions = new(JsonOptions) { WriteIndented = true };

    /// <summary>Identifies only the explicitly mapped browser receipt, not a query-selected format.</summary>
    internal static bool IsBrowserReceipt(HttpContext context) =>
        context.GetEndpoint() is RouteEndpoint endpoint &&
        string.Equals(endpoint.RoutePattern.RawText, BrowserReceiptPath, StringComparison.Ordinal);

    /// <summary>Returns one validated aggregate receipt, never a raw producer response.</summary>
    internal static async Task<IResult> GetAsync(
        string? source,
        string? fromUtc,
        string? toUtc,
        HttpContext context,
        EmployeeSessionService sessions,
        AggregateOutcomeProxy outcomes,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        if (source is not ("quotation" or "invoice" or "qualification") ||
            !AggregateOutcomePayload.TryUtc(fromUtc, out var from) ||
            !AggregateOutcomePayload.TryUtc(toUtc, out var to) ||
            from >= to || to - from > TimeSpan.FromDays(31) ||
            to > timeProvider.GetUtcNow().UtcDateTime)
        {
            return Results.BadRequest(new { error = "invalid_aggregate_window_or_source" });
        }

        var requiredPermission = source switch
        {
            "quotation" => LegacyEmployeePermissions.QuotationsRead,
            "qualification" => LegacyEmployeePermissions.QuotationRequestsRead,
            _ => LegacyEmployeePermissions.AccountingRead,
        };
        if (!LegacyNavigationAuthorization.IsEnabled(context.User, requiredPermission))
        {
            return Results.Forbid();
        }

        context.Response.Headers.CacheControl = "no-store";
        context.Response.Headers.Pragma = "no-cache";
        var accessToken = await sessions.GetAccessTokenAsync(context, cancellationToken);
        if (string.IsNullOrWhiteSpace(accessToken))
        {
            return Receipt(source, StatusCodes.Status401Unauthorized, null, timeProvider, context);
        }

        try
        {
            using var response = await outcomes.GetAsync(source, from, to, accessToken, cancellationToken);
            if (response.StatusCode != HttpStatusCode.OK)
            {
                return Receipt(source, (int)response.StatusCode, null, timeProvider, context);
            }

            if (response.Content.Headers.ContentLength is > MaximumPayloadBytes)
            {
                return Receipt(source, StatusCodes.Status502BadGateway, null, timeProvider, context);
            }

            var body = new byte[MaximumPayloadBytes + 1];
            using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            var length = 0;
            while (length < body.Length)
            {
                var read = await stream.ReadAsync(body.AsMemory(length), cancellationToken);
                if (read == 0)
                {
                    break;
                }

                length += read;
            }

            if (length > MaximumPayloadBytes)
            {
                return Receipt(source, StatusCodes.Status502BadGateway, null, timeProvider, context);
            }

            var payload = AggregateOutcomePayload.TryValidate(body.AsMemory(0, length), source, from, to, out var validated)
                ? validated
                : (JsonElement?)null;
            if (payload.HasValue && source == "qualification" && IsBrowserReceipt(context))
            {
                payload = AggregateOutcomePayload.SummarizeQualification(payload.Value);
            }

            return Receipt(
                source,
                payload.HasValue ? StatusCodes.Status200OK : StatusCodes.Status502BadGateway,
                payload,
                timeProvider,
                context);
        }
        catch (Exception exception) when (exception is HttpRequestException or IOException or
            OperationCanceledException && !cancellationToken.IsCancellationRequested)
        {
            var status = exception is OperationCanceledException
                ? StatusCodes.Status504GatewayTimeout
                : StatusCodes.Status502BadGateway;
            return Receipt(source, status, null, timeProvider, context);
        }
    }

    private static IResult Receipt(string source, int status, JsonElement? payload, TimeProvider timeProvider, HttpContext context)
    {
        var receipt = new AggregateOutcomeReceipt(
            source,
            timeProvider.GetUtcNow().UtcDateTime.ToString("O", CultureInfo.InvariantCulture),
            status,
            payload);
        if (IsBrowserReceipt(context))
        {
            context.Response.Headers["X-Content-Type-Options"] = "nosniff";
            var encoded = HtmlEncoder.Default.Encode(JsonSerializer.Serialize(receipt, BrowserJsonOptions));
            return Results.Text(
                "<!doctype html><html lang=\"en\"><head><meta charset=\"utf-8\"><meta name=\"viewport\" content=\"width=device-width, initial-scale=1\"><title>Aggregate outcome receipt</title></head><body><h1>Aggregate outcome receipt</h1><pre>" + encoded + "</pre></body></html>",
                "text/html; charset=utf-8",
                statusCode: StatusCodes.Status200OK);
        }

        return Results.Text(
            JsonSerializer.Serialize(receipt, JsonOptions),
            "application/json",
            statusCode: StatusCodes.Status200OK);
    }

    private sealed record AggregateOutcomeReceipt(
        string Source,
        string CapturedAtUtc,
        int HttpStatus,
        JsonElement? Payload);
}
