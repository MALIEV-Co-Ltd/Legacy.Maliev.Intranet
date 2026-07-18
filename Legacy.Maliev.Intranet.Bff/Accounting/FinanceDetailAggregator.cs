using System.Net;
using System.Net.Http.Json;
using Legacy.Maliev.Intranet.Bff.Orders;
using Legacy.Maliev.Intranet.Contracts;

namespace Legacy.Maliev.Intranet.Bff.Accounting;

/// <summary>Aggregates the browser-safe Finance editor from existing service boundaries.</summary>
public sealed class FinanceDetailAggregator(
    FinancesProxy finances,
    OrderEmployeeReferenceProxy employees,
    OrderCatalogReferenceProxy catalog,
    FinanceFileProxy files)
{
    /// <summary>Gets one complete Finance editor projection.</summary>
    public async Task<FinanceDetailPage?> GetAsync(int id, CancellationToken cancellationToken)
    {
        var paymentTask = finances.GetAsync(id, cancellationToken);
        var directionsTask = finances.GetLookupAsync("directions", cancellationToken);
        var typesTask = finances.GetLookupAsync("types", cancellationToken);
        var methodsTask = finances.GetLookupAsync("methods", cancellationToken);
        var metadataTask = finances.GetFilesAsync(id, cancellationToken);
        var employeeTask = employees.GetEmployeesAsync(cancellationToken);
        var currencyTask = catalog.GetCurrenciesAsync(cancellationToken);
        await Task.WhenAll(paymentTask, directionsTask, typesTask, methodsTask, metadataTask, employeeTask, currencyTask);

        using var paymentResponse = await paymentTask;
        if (paymentResponse.StatusCode == HttpStatusCode.NotFound) return null;
        var payment = await ReadAsync<FinancePaymentItem>(paymentResponse, cancellationToken)
            ?? throw new InvalidDataException("AccountingService returned an empty payment.");

        using var directionsResponse = await directionsTask;
        using var typesResponse = await typesTask;
        using var methodsResponse = await methodsTask;
        using var metadataResponse = await metadataTask;
        var directions = await ReadAsync<List<FinanceLookupItem>>(directionsResponse, cancellationToken) ?? [];
        var types = await ReadAsync<List<FinanceLookupItem>>(typesResponse, cancellationToken) ?? [];
        var methods = await ReadAsync<List<FinanceLookupItem>>(methodsResponse, cancellationToken) ?? [];
        var metadata = metadataResponse.StatusCode == HttpStatusCode.NotFound
            ? []
            : await ReadAsync<List<FinanceFileMetadata>>(metadataResponse, cancellationToken) ?? [];

        using var limiter = new SemaphoreSlim(4);
        var resolved = await Task.WhenAll(metadata.Select(item => ResolveAsync(item, limiter, cancellationToken)));
        return new(
            payment,
            (await employeeTask).Select(item => new FinanceLookupItem(item.Id, item.Name)).ToArray(),
            directions,
            types,
            methods,
            (await currencyTask).Select(item => new CatalogCurrency(item.Id, item.ShortName)).ToArray(),
            resolved);
    }

    /// <summary>Gets owned file metadata without resolving signed URLs.</summary>
    public async Task<IReadOnlyList<FinanceFileItem>> GetFilesAsync(int id, CancellationToken cancellationToken)
    {
        using var response = await finances.GetFilesAsync(id, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound) return [];
        var metadata = await ReadAsync<List<FinanceFileMetadata>>(response, cancellationToken) ?? [];
        return metadata.Select(item => new FinanceFileItem(item.Id, item.PaymentId, item.Bucket, item.ObjectName, item.CreatedDate, null)).ToArray();
    }

    private async Task<FinanceFileItem> ResolveAsync(FinanceFileMetadata item, SemaphoreSlim limiter, CancellationToken cancellationToken)
    {
        await limiter.WaitAsync(cancellationToken);
        try
        {
            using var response = await files.GetSignedUrlAsync(item.Bucket, item.ObjectName, cancellationToken);
            var uri = response.StatusCode == HttpStatusCode.NotFound ? null : await ReadAsync<Uri>(response, cancellationToken);
            return new(item.Id, item.PaymentId, item.Bucket, item.ObjectName, item.CreatedDate, uri);
        }
        finally
        {
            limiter.Release();
        }
    }

    private static async Task<T?> ReadAsync<T>(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<T>(cancellationToken);
    }

    private sealed record FinanceFileMetadata(int Id, int PaymentId, string Bucket, string ObjectName, DateTime? CreatedDate);
}
