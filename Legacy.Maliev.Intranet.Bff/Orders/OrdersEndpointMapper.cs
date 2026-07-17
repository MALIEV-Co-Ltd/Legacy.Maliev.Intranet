using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using Legacy.Maliev.Intranet.Contracts;

namespace Legacy.Maliev.Intranet.Bff.Orders;

internal static class OrdersEndpointMapper
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
                return Results.Ok(new OrderListPage([], emptyPageIndex, 0, 0, false, emptyPageIndex > 1));
            }

            var failure = MapFailure(response, context);
            if (failure is not null)
            {
                return failure;
            }

            try
            {
                var page = await response.Content.ReadFromJsonAsync<OrderListPage>(cancellationToken);
                var invalid = page?.Items is null ||
                    page.PageIndex < 1 ||
                    page.TotalPages < 0 ||
                    page.TotalRecords < 0 ||
                    page.Items.Any(order => order.Id < 1 || order.ProcessId < 1 || order.Quantity < 0 || order.Manufactured < 0);
                return invalid ? InvalidResponse() : Results.Ok(page);
            }
            catch (System.Text.Json.JsonException)
            {
                return InvalidResponse();
            }
        }
    }

    public static async Task<IResult> MapProcessesAsync(
        Func<CancellationToken, Task<HttpResponseMessage>> send,
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
                return Results.Ok(Array.Empty<OrderProcessItem>());
            }

            var failure = MapFailure(response, context);
            if (failure is not null)
            {
                return failure;
            }

            try
            {
                var processes = await response.Content.ReadFromJsonAsync<List<OrderProcessItem>>(cancellationToken);
                var invalid = processes is null || processes.Any(process =>
                    process.Id < 1 || string.IsNullOrWhiteSpace(process.Name));
                return invalid ? InvalidResponse() : Results.Ok(processes);
            }
            catch (System.Text.Json.JsonException)
            {
                return InvalidResponse();
            }
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
            if (retryAfter.HasValue && retryAfter.Value > TimeSpan.Zero && retryAfter.Value <= TimeSpan.FromHours(1))
            {
                context.Response.Headers.RetryAfter = ((int)Math.Ceiling(retryAfter.Value.TotalSeconds))
                    .ToString(CultureInfo.InvariantCulture);
            }

            return Results.StatusCode(StatusCodes.Status429TooManyRequests);
        }

        return response.IsSuccessStatusCode ? null : Unavailable();
    }

    private static IResult InvalidResponse() =>
        Results.Problem(statusCode: StatusCodes.Status502BadGateway, title: "Invalid OrderService response");

    private static IResult Unavailable() =>
        Results.Problem(statusCode: StatusCodes.Status503ServiceUnavailable, title: "OrderService unavailable");
}
