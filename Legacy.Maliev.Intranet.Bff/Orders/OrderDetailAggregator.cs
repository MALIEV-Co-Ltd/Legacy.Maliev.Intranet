using System.Net;
using System.Net.Http.Json;
using Legacy.Maliev.Intranet.Contracts;
using Legacy.Maliev.Intranet.Server.Orders;

namespace Legacy.Maliev.Intranet.Bff.Orders;

/// <summary>Aggregates browser-safe order editor reads across existing services without owning business rules.</summary>
public sealed class OrderDetailAggregator(
    OrderDetailProxy orders,
    OrdersProxy orderLists,
    OrderCatalogReferenceProxy catalog,
    OrderEmployeeReferenceProxy employees,
    OrderFileProxy files)
{
    /// <summary>Gets the complete editor projection, or null when the order does not exist.</summary>
    public async Task<OrderDetailPage?> GetAsync(int id, CancellationToken cancellationToken)
    {
        var orderResponseTask = orders.GetAsync(id, cancellationToken);
        var processesResponseTask = orderLists.GetProcessesAsync(cancellationToken);
        var materialsTask = catalog.GetMaterialsAsync(cancellationToken);
        var colorsTask = catalog.GetColorsAsync(cancellationToken);
        var finishesTask = catalog.GetSurfaceFinishesAsync(cancellationToken);
        var currenciesTask = catalog.GetCurrenciesAsync(cancellationToken);
        var employeesTask = employees.GetEmployeesAsync(cancellationToken);
        var latestResponseTask = orders.GetLatestStatusAsync(id, cancellationToken);
        var historyResponseTask = orders.GetStatusHistoryAsync(id, cancellationToken);
        var fileResponseTask = orders.GetFilesAsync(id, cancellationToken);
        await AwaitAllAndDisposeResponsesOnFailureAsync(
            [orderResponseTask, processesResponseTask, materialsTask, colorsTask, finishesTask, currenciesTask, employeesTask, latestResponseTask, historyResponseTask, fileResponseTask],
            [orderResponseTask, processesResponseTask, latestResponseTask, historyResponseTask, fileResponseTask]);

        using var orderResponse = await orderResponseTask;
        if (orderResponse.StatusCode == HttpStatusCode.NotFound) return null;
        orderResponse.EnsureSuccessStatusCode();
        var order = await orderResponse.Content.ReadFromJsonAsync<OrderDetailItem>(cancellationToken)
            ?? throw new InvalidDataException("OrderService returned an empty order.");

        using var processesResponse = await processesResponseTask;
        processesResponse.EnsureSuccessStatusCode();
        var processes = await processesResponse.Content.ReadFromJsonAsync<List<OrderProcessItem>>(cancellationToken) ?? [];

        using var latestResponse = await latestResponseTask;
        var latest = latestResponse.StatusCode == HttpStatusCode.NotFound
            ? null
            : await ReadSuccessfulAsync<OrderStatusItem>(latestResponse, cancellationToken);
        IReadOnlyList<OrderStatusItem> available = [];
        if (latest is not null)
        {
            using var availableResponse = await orders.GetAvailableStatusesAsync(latest.Id, cancellationToken);
            if (availableResponse.StatusCode != HttpStatusCode.NotFound)
            {
                available = await ReadSuccessfulAsync<List<OrderStatusItem>>(availableResponse, cancellationToken) ?? [];
            }
        }

        using var historyResponse = await historyResponseTask;
        var history = historyResponse.StatusCode == HttpStatusCode.NotFound
            ? []
            : await ReadSuccessfulAsync<List<OrderStatusHistoryItem>>(historyResponse, cancellationToken) ?? [];

        using var fileResponse = await fileResponseTask;
        var stored = fileResponse.StatusCode == HttpStatusCode.NotFound
            ? []
            : await ReadSuccessfulAsync<List<StoredOrderFile>>(fileResponse, cancellationToken) ?? [];
        var resolvedFiles = await Task.WhenAll(stored.Select(file => ResolveFileAsync(file, cancellationToken)));

        return new(
            order,
            processes.Select(item => new OrderLookupItem(item.Id, item.Name)).ToArray(),
            await materialsTask,
            await colorsTask,
            await finishesTask,
            await currenciesTask,
            await employeesTask,
            latest,
            latest is null ? available : available.Where(item => item.Id != latest.Id).ToArray(),
            history,
            resolvedFiles.Where(item => item.Uri is not null).ToArray());
    }

    /// <summary>Gets only the server-owned values required to compose an order label.</summary>
    public async Task<OrderLabelData?> GetLabelAsync(int id, CancellationToken cancellationToken)
    {
        var orderResponseTask = orders.GetAsync(id, cancellationToken);
        var processesResponseTask = orderLists.GetProcessesAsync(cancellationToken);
        var materialsTask = catalog.GetMaterialsAsync(cancellationToken);
        var colorsTask = catalog.GetColorsAsync(cancellationToken);
        var finishesTask = catalog.GetSurfaceFinishesAsync(cancellationToken);
        await AwaitAllAndDisposeResponsesOnFailureAsync(
            [orderResponseTask, processesResponseTask, materialsTask, colorsTask, finishesTask],
            [orderResponseTask, processesResponseTask]);

        using var orderResponse = await orderResponseTask;
        if (orderResponse.StatusCode == HttpStatusCode.NotFound) return null;
        orderResponse.EnsureSuccessStatusCode();
        var order = await orderResponse.Content.ReadFromJsonAsync<OrderDetailItem>(cancellationToken)
            ?? throw new InvalidDataException("OrderService returned an empty order.");

        using var processesResponse = await processesResponseTask;
        processesResponse.EnsureSuccessStatusCode();
        var processes = await processesResponse.Content.ReadFromJsonAsync<List<OrderProcessItem>>(cancellationToken) ?? [];
        var labelPage = new OrderDetailPage(
            order,
            processes.Select(item => new OrderLookupItem(item.Id, item.Name)).ToArray(),
            await materialsTask,
            await colorsTask,
            await finishesTask,
            [],
            [],
            null,
            [],
            [],
            []);
        return OrderLabelComposer.Compose(labelPage);
    }

    private async Task<OrderFileItem> ResolveFileAsync(StoredOrderFile file, CancellationToken cancellationToken)
    {
        using var response = await files.GetSignedUrlAsync(file.Bucket, file.ObjectName, cancellationToken);
        var uri = response.StatusCode == HttpStatusCode.NotFound
            ? null
            : await ReadSuccessfulAsync<Uri>(response, cancellationToken);
        return new(file.Id, file.OrderId, file.ObjectName, uri);
    }

    private static async Task<T?> ReadSuccessfulAsync<T>(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<T>(cancellationToken);
    }

    private static async Task AwaitAllAndDisposeResponsesOnFailureAsync(
        IReadOnlyList<Task> tasks,
        IReadOnlyList<Task<HttpResponseMessage>> responseTasks)
    {
        try
        {
            await Task.WhenAll(tasks);
        }
        catch
        {
            foreach (var responseTask in responseTasks.Where(task => task.IsCompletedSuccessfully))
            {
                responseTask.Result.Dispose();
            }

            throw;
        }
    }
}
