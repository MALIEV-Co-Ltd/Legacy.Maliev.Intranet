using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using Legacy.Maliev.Intranet.Contracts;

namespace Legacy.Maliev.Intranet.Bff.Accounting;

internal static class InvoicesEndpointMapper
{
    public static async Task<IResult> MapPageAsync(
        Func<CancellationToken, Task<HttpResponseMessage>> send,
        int pageIndex,
        HttpContext context,
        CancellationToken cancellationToken)
    {
        HttpResponseMessage? response;
        try
        {
            response = await send(cancellationToken);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return Unavailable();
        }
        catch (HttpRequestException)
        {
            return Unavailable();
        }
        catch (Polly.Timeout.TimeoutRejectedException)
        {
            return Unavailable();
        }

        using (response)
        {
            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                return Results.Ok(new InvoiceListPage([], pageIndex, 0, 0, false, pageIndex > 1));
            }

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

            if (!response.IsSuccessStatusCode)
            {
                return Unavailable();
            }

            try
            {
                var page = await response.Content.ReadFromJsonAsync<InvoiceListPage>(cancellationToken);
                var invalid = page?.Items is null || page.PageIndex < 1 || page.TotalPages < 0 || page.TotalRecords < 0 ||
                    page.Items.Any(invoice => invoice.Id < 1 || invoice.CustomerId < 1 || string.IsNullOrWhiteSpace(invoice.Number));
                return invalid
                    ? Results.Problem(statusCode: StatusCodes.Status502BadGateway, title: "Invalid AccountingService response")
                    : Results.Ok(page);
            }
            catch (System.Text.Json.JsonException)
            {
                return Results.Problem(statusCode: StatusCodes.Status502BadGateway, title: "Invalid AccountingService response");
            }
        }
    }

    private static IResult Unavailable() =>
        Results.Problem(statusCode: StatusCodes.Status503ServiceUnavailable, title: "AccountingService unavailable");
}
