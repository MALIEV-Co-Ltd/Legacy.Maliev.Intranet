using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using Legacy.Maliev.Intranet.Contracts;

namespace Legacy.Maliev.Intranet.Bff.Procurement;

internal static class SuppliersEndpointMapper
{
    public static async Task<IResult> MapPageAsync(
        Func<CancellationToken, Task<HttpResponseMessage>> send,
        int emptyPageIndex,
        HttpContext context,
        CancellationToken cancellationToken)
    {
        HttpResponseMessage response;
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
                return Results.Ok(new SupplierListPage([], emptyPageIndex, 0, 0, false, emptyPageIndex > 1));
            }

            if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            {
                return Results.StatusCode((int)response.StatusCode);
            }

            if (response.StatusCode == HttpStatusCode.TooManyRequests)
            {
                var retryAfter = response.Headers.RetryAfter?.Delta;
                if (retryAfter.HasValue && retryAfter.Value > TimeSpan.Zero && retryAfter.Value <= TimeSpan.FromHours(1))
                {
                    context.Response.Headers.RetryAfter = ((int)Math.Ceiling(retryAfter.Value.TotalSeconds))
                        .ToString(CultureInfo.InvariantCulture);
                }

                return Results.StatusCode(StatusCodes.Status429TooManyRequests);
            }

            if (!response.IsSuccessStatusCode)
            {
                return Unavailable();
            }

            try
            {
                var page = await response.Content.ReadFromJsonAsync<SupplierListPage>(cancellationToken);
                var invalid = page?.Items is null ||
                    page.PageIndex < 1 ||
                    page.TotalPages < 0 ||
                    page.TotalRecords < 0 ||
                    page.Items.Any(supplier => supplier.Id < 1);
                return invalid ? InvalidResponse() : Results.Ok(page);
            }
            catch (System.Text.Json.JsonException)
            {
                return InvalidResponse();
            }
        }
    }

    private static IResult InvalidResponse() =>
        Results.Problem(statusCode: StatusCodes.Status502BadGateway, title: "Invalid ProcurementService response");

    private static IResult Unavailable() =>
        Results.Problem(statusCode: StatusCodes.Status503ServiceUnavailable, title: "ProcurementService unavailable");
}
