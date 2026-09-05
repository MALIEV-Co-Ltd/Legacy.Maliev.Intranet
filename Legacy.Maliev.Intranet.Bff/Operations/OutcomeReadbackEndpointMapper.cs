using System.Globalization;
using System.Net;
using System.Text.Json;
using Legacy.Maliev.Intranet.Auth;
using Legacy.Maliev.Intranet.Contracts;

namespace Legacy.Maliev.Intranet.Bff.Operations;

/// <summary>Maps employee sessions to privacy-safe aggregate outcome receipts.</summary>
internal static class OutcomeReadbackEndpointMapper
{
    internal const int MaximumPayloadBytes = 1024 * 1024;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

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
        if (source is not ("quotation" or "invoice") ||
            !AggregateOutcomePayload.TryUtc(fromUtc, out var from) ||
            !AggregateOutcomePayload.TryUtc(toUtc, out var to) ||
            from >= to || to - from > TimeSpan.FromDays(31) ||
            to > timeProvider.GetUtcNow().UtcDateTime)
        {
            return Results.BadRequest(new { error = "invalid_aggregate_window_or_source" });
        }

        var requiredPermission = source == "quotation"
            ? LegacyEmployeePermissions.QuotationsRead
            : LegacyEmployeePermissions.AccountingRead;
        if (!LegacyNavigationAuthorization.IsEnabled(context.User, requiredPermission))
        {
            return Results.Forbid();
        }

        context.Response.Headers.CacheControl = "no-store";
        context.Response.Headers.Pragma = "no-cache";
        var accessToken = await sessions.GetAccessTokenAsync(context, cancellationToken);
        if (string.IsNullOrWhiteSpace(accessToken))
        {
            return Receipt(source, StatusCodes.Status401Unauthorized, null, timeProvider);
        }

        try
        {
            using var response = await outcomes.GetAsync(source, from, to, accessToken, cancellationToken);
            if (response.StatusCode != HttpStatusCode.OK)
            {
                return Receipt(source, (int)response.StatusCode, null, timeProvider);
            }

            if (response.Content.Headers.ContentLength is > MaximumPayloadBytes)
            {
                return Receipt(source, StatusCodes.Status502BadGateway, null, timeProvider);
            }

            var body = await response.Content.ReadAsByteArrayAsync(cancellationToken);
            var payload = AggregateOutcomePayload.TryValidate(body, source, from, to, out var validated)
                ? validated
                : (JsonElement?)null;
            return Receipt(
                source,
                payload.HasValue ? StatusCodes.Status200OK : StatusCodes.Status502BadGateway,
                payload,
                timeProvider);
        }
        catch (Exception exception) when (exception is HttpRequestException or IOException or
            OperationCanceledException && !cancellationToken.IsCancellationRequested)
        {
            var status = exception is OperationCanceledException
                ? StatusCodes.Status504GatewayTimeout
                : StatusCodes.Status502BadGateway;
            return Receipt(source, status, null, timeProvider);
        }
    }

    private static IResult Receipt(string source, int status, JsonElement? payload, TimeProvider timeProvider)
    {
        var receipt = new AggregateOutcomeReceipt(
            source,
            timeProvider.GetUtcNow().UtcDateTime.ToString("O", CultureInfo.InvariantCulture),
            status,
            payload);
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
