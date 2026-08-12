using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using Legacy.Maliev.Intranet.Auth;
using Legacy.Maliev.Intranet.Bff.Accounting;
using Legacy.Maliev.Intranet.Bff.Orders;
using Legacy.Maliev.Intranet.Bff.Quotations;
using Legacy.Maliev.Intranet.Contracts;

namespace Legacy.Maliev.Intranet.Bff.Customers;

/// <summary>Composes a bounded customer activity feed from authorized legacy sources.</summary>
public sealed class CustomerActivityAggregator(
    OrdersProxy orders,
    QuotationsProxy quotations,
    InvoicesProxy invoices)
{
    private static readonly CustomerHistorySourceSummary Forbidden =
        new(CustomerHistorySourceState.Forbidden, null);

    /// <summary>Gets customer activity without calling record families the employee cannot read.</summary>
    public async Task<CustomerActivityPage> GetAsync(
        int customerId,
        int size,
        ClaimsPrincipal user,
        CancellationToken cancellationToken)
    {
        var pageSize = Math.Clamp(size, 1, 50);
        var permissions = user.FindAll("permissions")
            .Select(static claim => claim.Value)
            .ToHashSet(StringComparer.Ordinal);

        var orderTask = permissions.Contains(LegacyEmployeePermissions.OrdersRead)
            ? LoadOrdersAsync(customerId, pageSize, cancellationToken)
            : Task.FromResult(SourceResult.Forbidden());
        var quotationTask = permissions.Contains(LegacyEmployeePermissions.QuotationsRead)
            ? LoadQuotationsAsync(customerId, pageSize, cancellationToken)
            : Task.FromResult(SourceResult.Forbidden());
        var invoiceTask = permissions.Contains(LegacyEmployeePermissions.AccountingRead)
            ? LoadInvoicesAsync(customerId, pageSize, cancellationToken)
            : Task.FromResult(SourceResult.Forbidden());

        await Task.WhenAll(orderTask, quotationTask, invoiceTask);
        var orderResult = await orderTask;
        var quotationResult = await quotationTask;
        var invoiceResult = await invoiceTask;
        var items = orderResult.Items
            .Concat(quotationResult.Items)
            .Concat(invoiceResult.Items)
            .OrderByDescending(static item => item.Timestamp)
            .ThenBy(static item => item.Kind)
            .ThenByDescending(static item => item.Id)
            .Take(pageSize)
            .ToArray();

        return new CustomerActivityPage(
            items,
            orderResult.Summary,
            quotationResult.Summary,
            invoiceResult.Summary);
    }

    private async Task<SourceResult> LoadOrdersAsync(
        int customerId,
        int size,
        CancellationToken cancellationToken)
    {
        var createdTask = LoadQueryAsync(
            token => orders.GetCustomerAsync(
                customerId,
                OrderListSort.OrderCreatedDate_Descending,
                null,
                1,
                size,
                token),
            response => response.Content.ReadFromJsonAsync<OrderListPage>(cancellationToken),
            page => IsValid(page, customerId),
            page => page.Items,
            page => page.TotalRecords,
            cancellationToken);
        var modifiedTask = LoadQueryAsync(
            token => orders.GetCustomerAsync(
                customerId,
                OrderListSort.OrderModifiedDate_Descending,
                null,
                1,
                size,
                token),
            response => response.Content.ReadFromJsonAsync<OrderListPage>(cancellationToken),
            page => IsValid(page, customerId),
            page => page.Items,
            page => page.TotalRecords,
            cancellationToken);

        await Task.WhenAll(createdTask, modifiedTask);
        return Combine(
            await createdTask,
            await modifiedTask,
            static item => item.Id,
            static item => item.ModifiedDate ?? item.CreatedDate,
            MapOrder,
            size);
    }

    private async Task<SourceResult> LoadQuotationsAsync(
        int customerId,
        int size,
        CancellationToken cancellationToken)
    {
        var createdTask = LoadQueryAsync(
            token => quotations.GetCustomerPageAsync(
                customerId,
                QuotationListSort.QuotationCreatedDate_Descending,
                null,
                1,
                size,
                token),
            response => response.Content.ReadFromJsonAsync<QuotationListPage>(cancellationToken),
            page => IsValid(page, customerId),
            page => page.Items,
            page => page.TotalRecords,
            cancellationToken);
        var modifiedTask = LoadQueryAsync(
            token => quotations.GetCustomerPageAsync(
                customerId,
                QuotationListSort.QuotationModifiedDate_Descending,
                null,
                1,
                size,
                token),
            response => response.Content.ReadFromJsonAsync<QuotationListPage>(cancellationToken),
            page => IsValid(page, customerId),
            page => page.Items,
            page => page.TotalRecords,
            cancellationToken);

        await Task.WhenAll(createdTask, modifiedTask);
        return Combine(
            await createdTask,
            await modifiedTask,
            static item => item.Id,
            static item => item.ModifiedDate ?? item.CreatedDate,
            MapQuotation,
            size);
    }

    private async Task<SourceResult> LoadInvoicesAsync(
        int customerId,
        int size,
        CancellationToken cancellationToken)
    {
        var createdTask = LoadQueryAsync(
            token => invoices.GetCustomerPageAsync(
                customerId,
                InvoiceListSort.InvoiceCreatedDate_Descending,
                null,
                1,
                size,
                token),
            response => response.Content.ReadFromJsonAsync<InvoiceListPage>(cancellationToken),
            page => IsValid(page, customerId),
            page => page.Items,
            page => page.TotalRecords,
            cancellationToken);
        var paidTask = LoadQueryAsync(
            token => invoices.GetCustomerPageAsync(
                customerId,
                InvoiceListSort.InvoicePaymentDate_Descending,
                null,
                1,
                size,
                token),
            response => response.Content.ReadFromJsonAsync<InvoiceListPage>(cancellationToken),
            page => IsValid(page, customerId),
            page => page.Items,
            page => page.TotalRecords,
            cancellationToken);

        await Task.WhenAll(createdTask, paidTask);
        return Combine(
            await createdTask,
            await paidTask,
            static item => item.Id,
            static item => item.PaymentDate ?? item.CreatedDate,
            MapInvoice,
            size);
    }

    private static async Task<QueryResult<TItem>> LoadQueryAsync<TPage, TItem>(
        Func<CancellationToken, Task<HttpResponseMessage>> send,
        Func<HttpResponseMessage, Task<TPage?>> read,
        Func<TPage, bool> isValid,
        Func<TPage, IReadOnlyList<TItem>> items,
        Func<TPage, int> totalRecords,
        CancellationToken cancellationToken)
        where TPage : class
        where TItem : class
    {
        HttpResponseMessage response;
        try
        {
            response = await send(cancellationToken);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return QueryResult<TItem>.Failed(CustomerHistorySourceState.Unavailable);
        }
        catch (HttpRequestException)
        {
            return QueryResult<TItem>.Failed(CustomerHistorySourceState.Unavailable);
        }
        catch (Polly.Timeout.TimeoutRejectedException)
        {
            return QueryResult<TItem>.Failed(CustomerHistorySourceState.Unavailable);
        }

        using (response)
        {
            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                return QueryResult<TItem>.Available([], 0);
            }

            if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            {
                return QueryResult<TItem>.Failed(CustomerHistorySourceState.Forbidden);
            }

            if (response.StatusCode == HttpStatusCode.TooManyRequests)
            {
                return QueryResult<TItem>.Failed(CustomerHistorySourceState.RateLimited);
            }

            if (!response.IsSuccessStatusCode)
            {
                return QueryResult<TItem>.Failed(CustomerHistorySourceState.Unavailable);
            }

            try
            {
                var page = await read(response);
                return page is not null && isValid(page)
                    ? QueryResult<TItem>.Available(items(page), totalRecords(page))
                    : QueryResult<TItem>.Failed(CustomerHistorySourceState.InvalidResponse);
            }
            catch (System.Text.Json.JsonException)
            {
                return QueryResult<TItem>.Failed(CustomerHistorySourceState.InvalidResponse);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                return QueryResult<TItem>.Failed(CustomerHistorySourceState.Unavailable);
            }
            catch (HttpRequestException)
            {
                return QueryResult<TItem>.Failed(CustomerHistorySourceState.Unavailable);
            }
            catch (Polly.Timeout.TimeoutRejectedException)
            {
                return QueryResult<TItem>.Failed(CustomerHistorySourceState.Unavailable);
            }
        }
    }

    private static SourceResult Combine<TItem>(
        QueryResult<TItem> first,
        QueryResult<TItem> second,
        Func<TItem, int> id,
        Func<TItem, DateTime?> timestamp,
        Func<TItem, CustomerActivityItem?> project,
        int size)
        where TItem : class
    {
        var combinedState = MoreSevere(first.State, second.State);
        if (combinedState == CustomerHistorySourceState.Forbidden)
        {
            return SourceResult.Failed([], combinedState);
        }

        var items = new[] { first, second }
            .Where(static result => result.State == CustomerHistorySourceState.Available)
            .SelectMany(static result => result.Items)
            .GroupBy(id)
            .Select(group => group
                .OrderByDescending(timestamp)
                .First())
            .Select(project)
            .OfType<CustomerActivityItem>()
            .OrderByDescending(static item => item.Timestamp)
            .ThenByDescending(static item => item.Id)
            .Take(size)
            .ToArray();

        if (first.State == CustomerHistorySourceState.Available &&
            second.State == CustomerHistorySourceState.Available)
        {
            return SourceResult.Available(items, Math.Max(first.TotalRecords!.Value, second.TotalRecords!.Value));
        }

        return SourceResult.Failed(items, combinedState);
    }

    private static CustomerHistorySourceState MoreSevere(
        CustomerHistorySourceState first,
        CustomerHistorySourceState second) =>
        Severity(first) >= Severity(second) ? first : second;

    private static int Severity(CustomerHistorySourceState state) => state switch
    {
        CustomerHistorySourceState.Forbidden => 4,
        CustomerHistorySourceState.RateLimited => 3,
        CustomerHistorySourceState.InvalidResponse => 2,
        CustomerHistorySourceState.Unavailable => 1,
        CustomerHistorySourceState.Available => 0,
        _ => throw new ArgumentOutOfRangeException(nameof(state)),
    };

    private static bool IsValid(OrderListPage page, int customerId) =>
        IsValidPage(page.Items, page.PageIndex, page.TotalPages, page.TotalRecords) &&
        page.Items.All(item => item is not null &&
            item.Id >= 1 &&
            item.CustomerId == customerId &&
            item.ProcessId >= 1 &&
            item.Quantity >= 0 &&
            item.Manufactured >= 0);

    private static bool IsValid(QuotationListPage page, int customerId) =>
        IsValidPage(page.Items, page.PageIndex, page.TotalPages, page.TotalRecords) &&
        page.Items.All(item => item is not null &&
            item.Id >= 1 &&
            item.CustomerId == customerId &&
            item.Period >= 0 &&
            item.CurrencyId >= 1);

    private static bool IsValid(InvoiceListPage page, int customerId) =>
        IsValidPage(page.Items, page.PageIndex, page.TotalPages, page.TotalRecords) &&
        page.Items.All(item => item is not null &&
            item.Id >= 1 &&
            item.CustomerId == customerId &&
            !string.IsNullOrWhiteSpace(item.Number) &&
            !string.IsNullOrWhiteSpace(item.Currency));

    private static bool IsValidPage<T>(
        IReadOnlyList<T>? items,
        int pageIndex,
        int totalPages,
        int totalRecords) =>
        items is not null && pageIndex >= 1 && totalPages >= 0 && totalRecords >= 0;

    private static CustomerActivityItem? MapOrder(OrderListItem item)
    {
        var timestamp = item.ModifiedDate ?? item.CreatedDate;
        return timestamp is null
            ? null
            : new CustomerActivityItem(
                CustomerHistoryKind.Order,
                item.Id,
                item.Name,
                item.Manufactured >= item.Quantity
                    ? CustomerActivityStatus.Complete
                    : CustomerActivityStatus.InProgress,
                item.Manufactured,
                item.Quantity,
                null,
                null,
                timestamp.Value);
    }

    private static CustomerActivityItem? MapQuotation(QuotationListItem item)
    {
        var timestamp = item.ModifiedDate ?? item.CreatedDate;
        return timestamp is null
            ? null
            : new CustomerActivityItem(
                CustomerHistoryKind.Quotation,
                item.Id,
                null,
                item.Accepted switch
                {
                    true => CustomerActivityStatus.Accepted,
                    false => CustomerActivityStatus.Declined,
                    null => CustomerActivityStatus.Open,
                },
                null,
                null,
                null,
                null,
                timestamp.Value);
    }

    private static CustomerActivityItem? MapInvoice(InvoiceListItem item)
    {
        var timestamp = item.PaymentDate ?? item.CreatedDate;
        return timestamp is null
            ? null
            : new CustomerActivityItem(
                CustomerHistoryKind.Invoice,
                item.Id,
                item.Number,
                item.IsPaid ? CustomerActivityStatus.Paid : CustomerActivityStatus.Outstanding,
                null,
                null,
                item.Total,
                item.Currency,
                timestamp.Value);
    }

    private sealed record QueryResult<TItem>(
        IReadOnlyList<TItem> Items,
        CustomerHistorySourceState State,
        int? TotalRecords)
        where TItem : class
    {
        public static QueryResult<TItem> Available(IReadOnlyList<TItem> items, int totalRecords) =>
            new(items, CustomerHistorySourceState.Available, totalRecords);

        public static QueryResult<TItem> Failed(CustomerHistorySourceState state) =>
            new([], state, null);
    }

    private sealed record SourceResult(
        IReadOnlyList<CustomerActivityItem> Items,
        CustomerHistorySourceSummary Summary)
    {
        public static SourceResult Available(IReadOnlyList<CustomerActivityItem> items, int totalRecords) =>
            new(items, new CustomerHistorySourceSummary(CustomerHistorySourceState.Available, totalRecords));

        public static SourceResult Forbidden() => new([], CustomerActivityAggregator.Forbidden);

        public static SourceResult Failed(
            IReadOnlyList<CustomerActivityItem> items,
            CustomerHistorySourceState state) =>
            new(items, new CustomerHistorySourceSummary(state, null));
    }
}
