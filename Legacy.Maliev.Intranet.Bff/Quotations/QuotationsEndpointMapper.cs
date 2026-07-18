using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using Legacy.Maliev.Intranet.Contracts;

namespace Legacy.Maliev.Intranet.Bff.Quotations;

internal static class QuotationsEndpointMapper
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
                return Results.Ok(new QuotationListPage([], pageIndex, 0, 0, false, pageIndex > 1));
            }

            var failure = MapFailure(response, context);
            if (failure is not null)
            {
                return failure;
            }

            try
            {
                var page = await response.Content.ReadFromJsonAsync<QuotationListPage>(cancellationToken);
                var invalid = page?.Items is null || page.PageIndex < 1 || page.TotalPages < 0 || page.TotalRecords < 0 ||
                    page.Items.Any(item => item.Id < 1 || item.Period < 0 || item.CurrencyId < 1);
                return invalid ? InvalidResponse() : Results.Ok(page);
            }
            catch (System.Text.Json.JsonException)
            {
                return InvalidResponse();
            }
        }
    }

    public static async Task<IResult> MapStatsAsync(
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
            var failure = MapFailure(response, context);
            if (failure is not null)
            {
                return failure;
            }

            try
            {
                var stats = await response.Content.ReadFromJsonAsync<QuotationStats>(cancellationToken);
                return stats is null || stats.Accepted < 0 || stats.Declined < 0 || stats.Open < 0
                    ? InvalidResponse()
                    : Results.Ok(stats);
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
        Results.Problem(statusCode: StatusCodes.Status502BadGateway, title: "Invalid QuotationService response");

    private static IResult Unavailable() =>
        Results.Problem(statusCode: StatusCodes.Status503ServiceUnavailable, title: "QuotationService unavailable");
}
