using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using Legacy.Maliev.Intranet.Contracts;

namespace Legacy.Maliev.Intranet.Bff.Quotations;

internal static class QuotationDecisionEndpointMapper
{
    public static async Task<IResult> DecideAsync(
        int id,
        QuotationDecisionInput input,
        HttpContext context,
        QuotationDecisionProxy proxy,
        CancellationToken cancellationToken)
    {
        if (id < 1)
        {
            return Results.BadRequest();
        }

        HttpResponseMessage response;
        try
        {
            response = await proxy.DecideAsync(
                id,
                input.Accepted,
                input.ExpectedModifiedDate,
                cancellationToken);
        }
        catch (Exception exception) when (
            exception is HttpRequestException or Polly.Timeout.TimeoutRejectedException
            || exception is OperationCanceledException && !cancellationToken.IsCancellationRequested)
        {
            return Unavailable();
        }

        using (response)
        {
            if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden or HttpStatusCode.NotFound)
            {
                return Results.StatusCode((int)response.StatusCode);
            }

            if (response.StatusCode == HttpStatusCode.TooManyRequests)
            {
                CopyBoundedRetryAfter(response, context);
                return Results.StatusCode(StatusCodes.Status429TooManyRequests);
            }

            if (response.StatusCode == HttpStatusCode.Conflict)
            {
                var partial = await ReadSafeResultAsync(response, cancellationToken);
                return partial?.Status == "DependencyConflict"
                    ? Results.Conflict(partial)
                    : Results.Conflict();
            }

            if (!response.IsSuccessStatusCode)
            {
                return Unavailable();
            }

            var result = await ReadSafeResultAsync(response, cancellationToken);
            return result?.Status == "Completed"
                ? Results.Ok(result)
                : InvalidResponse();
        }
    }

    private static async Task<QuotationDecisionResult?> ReadSafeResultAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        try
        {
            var wire = await response.Content.ReadFromJsonAsync<QuotationDecisionServiceResult>(cancellationToken);
            var status = wire?.Status switch
            {
                QuotationDecisionServiceStatus.Completed => "Completed",
                QuotationDecisionServiceStatus.DependencyConflict => "DependencyConflict",
                _ => null,
            };
            return wire is not null
                && status is not null
                && wire.CompletedOrders >= 0
                && wire.TotalOrders >= 0
                && wire.CompletedOrders <= wire.TotalOrders
                    ? new QuotationDecisionResult(status, wire.CompletedOrders, wire.TotalOrders, wire.ModifiedDate)
                    : null;
        }
        catch (System.Text.Json.JsonException)
        {
            return null;
        }
    }

    private static void CopyBoundedRetryAfter(HttpResponseMessage response, HttpContext context)
    {
        var retryAfter = response.Headers.RetryAfter?.Delta;
        if (retryAfter is { } delay && delay > TimeSpan.Zero && delay <= TimeSpan.FromHours(1))
        {
            context.Response.Headers.RetryAfter = ((int)Math.Ceiling(delay.TotalSeconds)).ToString(CultureInfo.InvariantCulture);
        }
    }

    private static IResult InvalidResponse() =>
        Results.Problem(statusCode: StatusCodes.Status502BadGateway, title: "Invalid QuotationService response");

    private static IResult Unavailable() =>
        Results.Problem(statusCode: StatusCodes.Status503ServiceUnavailable, title: "QuotationService unavailable");

    private sealed record QuotationDecisionServiceResult(
        QuotationDecisionServiceStatus Status,
        int CompletedOrders,
        int TotalOrders,
        DateTime? ModifiedDate);

    private enum QuotationDecisionServiceStatus
    {
        Completed,
        NotFound,
        Conflict,
        DependencyConflict,
        DependencyUnavailable,
    }
}
