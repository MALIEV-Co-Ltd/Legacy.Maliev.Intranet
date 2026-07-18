using System.Net;
using System.Net.Http.Json;
using Legacy.Maliev.Intranet.Contracts;

namespace Legacy.Maliev.Intranet.Bff.Accounting;

/// <summary>Builds the browser-safe Invoice View from AccountingService and FileService.</summary>
public sealed class InvoiceDetailAggregator(InvoiceDetailProxy invoices, InvoiceFileProxy files)
{
    /// <summary>Gets one complete detail response and resolves clean file links.</summary>
    public async Task<InvoiceDetailPage?> GetAsync(int id, CancellationToken cancellationToken)
    {
        using var invoiceResponse = await invoices.GetAsync(id, cancellationToken);
        if (invoiceResponse.StatusCode == HttpStatusCode.NotFound) return null;
        var invoice = await ReadAsync<InvoiceDetail>(invoiceResponse, cancellationToken)
            ?? throw new InvalidDataException("AccountingService returned an empty invoice.");
        if (invoice.Id != id) throw new InvalidDataException("AccountingService returned the wrong invoice.");

        var itemsTask = ReadItemsAsync(id, cancellationToken);
        var invoiceFilesTask = ReadFilesAsync(id, false, cancellationToken);
        var receiptFilesTask = invoice.ReceiptId is int receiptId
            ? ReadFilesAsync(receiptId, true, cancellationToken)
            : Task.FromResult<IReadOnlyList<OwnedFile>>([]);
        await Task.WhenAll(itemsTask, invoiceFilesTask, receiptFilesTask);

        return new(
            invoice,
            await itemsTask,
            await ResolveAsync(await invoiceFilesTask, cancellationToken),
            await ResolveAsync(await receiptFilesTask, cancellationToken));
    }

    /// <summary>Gets server-only owned resources for safe update and deletion workflows.</summary>
    public async Task<(InvoiceDetail Invoice, IReadOnlyList<InvoiceOrderItem> Items, IReadOnlyList<OwnedFile> Files)?> GetOwnedAsync(int id, CancellationToken cancellationToken)
    {
        using var response = await invoices.GetAsync(id, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound) return null;
        var invoice = await ReadAsync<InvoiceDetail>(response, cancellationToken)
            ?? throw new InvalidDataException("AccountingService returned an empty invoice.");
        if (invoice.Id != id) throw new InvalidDataException("AccountingService returned the wrong invoice.");
        var itemsTask = ReadItemsAsync(id, cancellationToken);
        var filesTask = ReadFilesAsync(id, false, cancellationToken);
        await Task.WhenAll(itemsTask, filesTask);
        return (invoice, await itemsTask, await filesTask);
    }

    private async Task<IReadOnlyList<InvoiceOrderItem>> ReadItemsAsync(int invoiceId, CancellationToken cancellationToken)
    {
        using var response = await invoices.GetOrderItemsAsync(invoiceId, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound) return [];
        var items = await ReadAsync<List<InvoiceOrderItem>>(response, cancellationToken) ?? [];
        if (items.Any(item => item.Id <= 0 || item.InvoiceId != invoiceId)) throw new InvalidDataException("AccountingService returned unowned invoice items.");
        return items;
    }

    private async Task<IReadOnlyList<OwnedFile>> ReadFilesAsync(int ownerId, bool receipt, CancellationToken cancellationToken)
    {
        using var response = receipt
            ? await invoices.GetReceiptFilesAsync(ownerId, cancellationToken)
            : await invoices.GetInvoiceFilesAsync(ownerId, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound) return [];
        var metadata = await ReadAsync<List<FileMetadata>>(response, cancellationToken) ?? [];
        var files = metadata.Select(item => new OwnedFile(item.Id, item.InvoiceId ?? item.ReceiptId ?? 0, item.Bucket, item.ObjectName, item.CreatedDate)).ToArray();
        if (files.Any(item => item.Id <= 0 || item.OwnerId != ownerId || string.IsNullOrWhiteSpace(item.Bucket) || string.IsNullOrWhiteSpace(item.ObjectName)))
            throw new InvalidDataException("AccountingService returned unowned invoice files.");
        return files;
    }

    private async Task<IReadOnlyList<InvoiceDownload>> ResolveAsync(IReadOnlyList<OwnedFile> metadata, CancellationToken cancellationToken)
    {
        using var limiter = new SemaphoreSlim(4);
        return await Task.WhenAll(metadata.Select(async item =>
        {
            await limiter.WaitAsync(cancellationToken);
            try
            {
                using var response = await files.GetSignedUrlAsync(item.Bucket, item.ObjectName, cancellationToken);
                var uri = response.StatusCode == HttpStatusCode.NotFound ? null : await ReadAsync<Uri>(response, cancellationToken);
                return new InvoiceDownload(item.Id, item.OwnerId, item.ObjectName, item.CreatedDate, uri);
            }
            finally { limiter.Release(); }
        }));
    }

    private static async Task<T?> ReadAsync<T>(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<T>(cancellationToken);
    }

    private sealed record FileMetadata(int Id, int? InvoiceId, int? ReceiptId, string Bucket, string ObjectName, DateTime? CreatedDate);
}

/// <summary>Server-only object identity proven to belong to an invoice or receipt.</summary>
public sealed record OwnedFile(int Id, int OwnerId, string Bucket, string ObjectName, DateTime? CreatedDate);
