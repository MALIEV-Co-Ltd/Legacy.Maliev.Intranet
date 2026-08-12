using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using Legacy.Maliev.Intranet.Bff.Accounting;
using Legacy.Maliev.Intranet.Bff.Orders;
using Legacy.Maliev.Intranet.Bff.Quotations;
using Legacy.Maliev.Intranet.Contracts;

namespace Legacy.Maliev.Intranet.Bff.Customers;

internal static class CustomerHistoryEndpointMapper
{
    public static Task<IResult> OrdersAsync(
        int customerId,
        OrderListSort? sort,
        string? search,
        int? index,
        int? size,
        HttpContext context,
        OrdersProxy orders,
        CancellationToken cancellationToken)
    {
        if (customerId <= 0) return Task.FromResult(Results.BadRequest());

        var pageIndex = Math.Max(index ?? 1, 1);
        var pageSize = Math.Clamp(size ?? 100, 1, 100);
        return MapPageAsync(
            token => orders.GetCustomerAsync(
                customerId,
                sort ?? OrderListSort.OrderCreatedDate_Descending,
                search,
                pageIndex,
                pageSize,
                token),
            pageIndex,
            context,
            response => response.Content.ReadFromJsonAsync<OrderListPage>(cancellationToken),
            page => page.Items is not null &&
                page.PageIndex >= 1 &&
                page.TotalPages >= 0 &&
                page.TotalRecords >= 0 &&
                page.Items.All(item =>
                    item is not null &&
                    item.Id >= 1 &&
                    item.CustomerId == customerId &&
                    item.ProcessId >= 1 &&
                    item.Quantity >= 0 &&
                    item.Manufactured >= 0),
            emptyIndex => new OrderListPage([], emptyIndex, 0, 0, false, emptyIndex > 1),
            "OrderService",
            cancellationToken);
    }

    public static Task<IResult> QuotationsAsync(
        int customerId,
        QuotationListSort? sort,
        string? search,
        int? index,
        int? size,
        HttpContext context,
        QuotationsProxy quotations,
        CancellationToken cancellationToken)
    {
        if (customerId <= 0) return Task.FromResult(Results.BadRequest());

        var pageIndex = Math.Max(index ?? 1, 1);
        var pageSize = Math.Clamp(size ?? 100, 1, 100);
        return MapPageAsync(
            token => quotations.GetCustomerPageAsync(
                customerId,
                sort ?? QuotationListSort.QuotationCreatedDate_Descending,
                search,
                pageIndex,
                pageSize,
                token),
            pageIndex,
            context,
            response => response.Content.ReadFromJsonAsync<QuotationListPage>(cancellationToken),
            page => page.Items is not null &&
                page.PageIndex >= 1 &&
                page.TotalPages >= 0 &&
                page.TotalRecords >= 0 &&
                page.Items.All(item =>
                    item is not null &&
                    item.Id >= 1 &&
                    item.CustomerId == customerId &&
                    item.Period >= 0 &&
                    item.CurrencyId >= 1),
            emptyIndex => new QuotationListPage([], emptyIndex, 0, 0, false, emptyIndex > 1),
            "QuotationService",
            cancellationToken);
    }

    public static Task<IResult> InvoicesAsync(
        int customerId,
        InvoiceListSort? sort,
        string? search,
        int? index,
        int? size,
        HttpContext context,
        InvoicesProxy invoices,
        CancellationToken cancellationToken)
    {
        if (customerId <= 0) return Task.FromResult(Results.BadRequest());

        var pageIndex = Math.Max(index ?? 1, 1);
        var pageSize = Math.Clamp(size ?? 100, 1, 100);
        return MapPageAsync(
            token => invoices.GetCustomerPageAsync(
                customerId,
                sort ?? InvoiceListSort.InvoiceCreatedDate_Descending,
                search,
                pageIndex,
                pageSize,
                token),
            pageIndex,
            context,
            response => response.Content.ReadFromJsonAsync<InvoiceListPage>(cancellationToken),
            page => page.Items is not null &&
                page.PageIndex >= 1 &&
                page.TotalPages >= 0 &&
                page.TotalRecords >= 0 &&
                page.Items.All(item =>
                    item is not null &&
                    item.Id >= 1 &&
                    item.CustomerId == customerId &&
                    !string.IsNullOrWhiteSpace(item.Number)),
            emptyIndex => new InvoiceListPage([], emptyIndex, 0, 0, false, emptyIndex > 1),
            "AccountingService",
            cancellationToken);
    }

    private static async Task<IResult> MapPageAsync<TPage>(
        Func<CancellationToken, Task<HttpResponseMessage>> send,
        int pageIndex,
        HttpContext context,
        Func<HttpResponseMessage, Task<TPage?>> read,
        Func<TPage, bool> isValid,
        Func<int, TPage> emptyPage,
        string serviceName,
        CancellationToken cancellationToken)
        where TPage : class
    {
        HttpResponseMessage response;
        try
        {
            response = await send(cancellationToken);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return Unavailable(serviceName);
        }
        catch (HttpRequestException)
        {
            return Unavailable(serviceName);
        }
        catch (Polly.Timeout.TimeoutRejectedException)
        {
            return Unavailable(serviceName);
        }

        using (response)
        {
            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                return Results.Ok(emptyPage(pageIndex));
            }

            if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            {
                return Results.StatusCode((int)response.StatusCode);
            }

            if (response.StatusCode == HttpStatusCode.TooManyRequests)
            {
                SetBoundedRetryAfter(context, response);
                return Results.StatusCode(StatusCodes.Status429TooManyRequests);
            }

            if (!response.IsSuccessStatusCode)
            {
                return Unavailable(serviceName);
            }

            try
            {
                var page = await read(response);
                return page is not null && isValid(page)
                    ? Results.Ok(page)
                    : InvalidResponse(serviceName);
            }
            catch (System.Text.Json.JsonException)
            {
                return InvalidResponse(serviceName);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                return Unavailable(serviceName);
            }
            catch (HttpRequestException)
            {
                return Unavailable(serviceName);
            }
            catch (Polly.Timeout.TimeoutRejectedException)
            {
                return Unavailable(serviceName);
            }
        }
    }

    private static void SetBoundedRetryAfter(HttpContext context, HttpResponseMessage response)
    {
        var retryAfter = response.Headers.RetryAfter?.Delta;
        if (retryAfter is { } delay && delay > TimeSpan.Zero && delay <= TimeSpan.FromHours(1))
        {
            context.Response.Headers.RetryAfter = ((int)Math.Ceiling(delay.TotalSeconds))
                .ToString(CultureInfo.InvariantCulture);
        }
    }

    private static IResult InvalidResponse(string serviceName) =>
        Results.Problem(
            statusCode: StatusCodes.Status502BadGateway,
            title: $"Invalid {serviceName} response");

    private static IResult Unavailable(string serviceName) =>
        Results.Problem(
            statusCode: StatusCodes.Status503ServiceUnavailable,
            title: $"{serviceName} unavailable");
}
