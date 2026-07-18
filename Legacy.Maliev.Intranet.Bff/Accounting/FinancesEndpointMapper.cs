using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using Legacy.Maliev.Intranet.Contracts;

namespace Legacy.Maliev.Intranet.Bff.Accounting;

internal static class FinancesEndpointMapper
{
    public static async Task<IResult> MapPageAsync(
        Func<CancellationToken, Task<HttpResponseMessage>> send,
        int pageIndex,
        HttpContext context,
        CancellationToken cancellationToken)
    {
        var response = await SendAsync(send, cancellationToken);
        if (response is null)
        {
            return Unavailable();
        }

        using (response)
        {
            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                return Results.Ok(new FinancePaymentPage([], pageIndex, 0, 0, false, pageIndex > 1));
            }

            var failure = MapFailure(response, context);
            if (failure is not null)
            {
                return failure;
            }

            try
            {
                var page = await response.Content.ReadFromJsonAsync<FinancePaymentPage>(cancellationToken);
                var invalid = page?.Items is null || page.PageIndex < 1 || page.TotalPages < 0 || page.TotalRecords < 0 ||
                    page.Items.Any(item => item.Id < 1 || item.PaymentDirectionId < 1 || item.PaymentTypeId < 1 || item.PaymentMethodId < 1);
                return invalid ? InvalidResponse() : Results.Ok(page);
            }
            catch (System.Text.Json.JsonException)
            {
                return InvalidResponse();
            }
        }
    }

    public static async Task<IResult> MapSummaryAsync(
        Func<CancellationToken, Task<HttpResponseMessage>> send,
        HttpContext context,
        CancellationToken cancellationToken)
    {
        var response = await SendAsync(send, cancellationToken);
        if (response is null)
        {
            return Unavailable();
        }

        using (response)
        {
            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                return Results.Ok(new FinanceSummary([]));
            }

            var failure = MapFailure(response, context);
            if (failure is not null)
            {
                return failure;
            }

            try
            {
                var summary = await response.Content.ReadFromJsonAsync<FinanceSummary>(cancellationToken);
                var invalid = summary?.Details is null || summary.Details.Any(detail => string.IsNullOrWhiteSpace(detail.CurrencyId));
                return invalid ? InvalidResponse() : Results.Ok(summary);
            }
            catch (System.Text.Json.JsonException)
            {
                return InvalidResponse();
            }
        }
    }

    public static async Task<IResult> MapTrendAsync(
        Func<CancellationToken, Task<HttpResponseMessage>> send,
        HttpContext context,
        CancellationToken cancellationToken)
    {
        var response = await SendAsync(send, cancellationToken);
        if (response is null)
        {
            return Unavailable();
        }

        using (response)
        {
            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                return Results.Ok(new Dictionary<DateTime, decimal>());
            }

            var failure = MapFailure(response, context);
            if (failure is not null)
            {
                return failure;
            }

            try
            {
                var trend = await response.Content.ReadFromJsonAsync<Dictionary<DateTime, decimal>>(cancellationToken);
                return trend is null ? InvalidResponse() : Results.Ok(trend);
            }
            catch (System.Text.Json.JsonException)
            {
                return InvalidResponse();
            }
        }
    }

    private static async Task<HttpResponseMessage?> SendAsync(
        Func<CancellationToken, Task<HttpResponseMessage>> send,
        CancellationToken cancellationToken)
    {
        try
        {
            return await send(cancellationToken);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return null;
        }
        catch (HttpRequestException)
        {
            return null;
        }
        catch (Polly.Timeout.TimeoutRejectedException)
        {
            return null;
        }
    }

    private static IResult? MapFailure(HttpResponseMessage response, HttpContext context)
    {
        if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
        {
            return Results.StatusCode((int)response.StatusCode);
        }

        if (response.StatusCode == HttpStatusCode.TooManyRequests)
        {
            var retryAfter = response.Headers.RetryAfter?.Delta;
            if (retryAfter is { } delay && delay > TimeSpan.Zero && delay <= TimeSpan.FromHours(1))
            {
                context.Response.Headers.RetryAfter = ((int)Math.Ceiling(delay.TotalSeconds)).ToString(CultureInfo.InvariantCulture);
            }

            return Results.StatusCode(StatusCodes.Status429TooManyRequests);
        }

        return response.IsSuccessStatusCode ? null : Unavailable();
    }

    private static IResult InvalidResponse() =>
        Results.Problem(statusCode: StatusCodes.Status502BadGateway, title: "Invalid AccountingService response");

    private static IResult Unavailable() =>
        Results.Problem(statusCode: StatusCodes.Status503ServiceUnavailable, title: "AccountingService unavailable");
}
