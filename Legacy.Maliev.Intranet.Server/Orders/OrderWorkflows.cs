using Legacy.Maliev.Intranet.Contracts;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Legacy.Maliev.Intranet.Server.Orders;

/// <summary>Coordinates scanned order-file storage and OrderService metadata outside the BFF transport layer.</summary>
public sealed class OrderFileWorkflow(ILogger<OrderFileWorkflow> logger)
{
    /// <summary>Links every clean upload and compensates both boundaries if linking fails.</summary>
    public async Task<IReadOnlyList<OrderFileItem>> UploadAsync(
        int orderId,
        int customerId,
        IReadOnlyList<IFormFile> files,
        Func<int, IReadOnlyList<IFormFile>, CancellationToken, Task<IReadOnlyList<StoredOrderFile>>> upload,
        Func<int, StoredOrderFile, CancellationToken, Task<OrderFileItem>> link,
        Func<OrderFileItem, CancellationToken, Task> unlink,
        Func<StoredOrderFile, CancellationToken, Task> delete,
        CancellationToken cancellationToken)
    {
        var stored = await upload(customerId, files, cancellationToken);
        var linked = new List<OrderFileItem>();
        try
        {
            foreach (var item in stored)
            {
                linked.Add(await link(orderId, item, cancellationToken));
            }

            return linked;
        }
        catch
        {
            using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            foreach (var item in linked)
            {
                await TryCompensateAsync(
                    "unlink order-file metadata",
                    item.Id,
                    () => unlink(item, cleanup.Token));
            }

            foreach (var item in stored)
            {
                await TryCompensateAsync(
                    "delete stored order file",
                    item.Id,
                    () => delete(item, cleanup.Token));
            }

            throw;
        }
    }

    /// <summary>Removes only metadata resolved as owned by the requested order.</summary>
    public async Task<bool> RemoveAsync(
        int fileId,
        IReadOnlyList<StoredOrderFile> ownedFiles,
        Func<StoredOrderFile, CancellationToken, Task> delete,
        Func<int, CancellationToken, Task> unlink,
        CancellationToken cancellationToken)
    {
        var owned = ownedFiles.SingleOrDefault(file => file.Id == fileId);
        if (owned is null)
        {
            return false;
        }

        await delete(owned, cancellationToken);
        await unlink(owned.Id, cancellationToken);
        return true;
    }

    private async Task TryCompensateAsync(string operation, int fileId, Func<Task> compensate)
    {
        try
        {
            await compensate();
        }
        catch (Exception exception) when (exception is not OutOfMemoryException and not StackOverflowException and not AccessViolationException)
        {
            logger.LogError(exception, "Failed to {CompensationOperation} for file {FileId} after an order-file workflow failure.", operation, fileId);
        }
    }
}

/// <summary>Server-owned storage identity for an order file.</summary>
public sealed record StoredOrderFile(int Id, int OrderId, string Bucket, string ObjectName, Uri? Uri = null);

/// <summary>Composes the preserved DocumentService label from server-owned order and lookup values.</summary>
public static class OrderLabelComposer
{
    /// <summary>Builds the legacy order-label payload without trusting browser-supplied labels.</summary>
    public static OrderLabelData Compose(OrderDetailPage page) => new(
        page.Order.Id.ToString(System.Globalization.CultureInfo.InvariantCulture),
        page.Order.Name ?? "-",
        page.Order.Quantity,
        page.Order.Manufactured,
        page.Order.Remaining ?? 0,
        Lookup(page.Processes, page.Order.ProcessId),
        Lookup(page.Materials, page.Order.MaterialId),
        Lookup(page.Colors, page.Order.ColorId),
        Lookup(page.SurfaceFinishes, page.Order.SurfaceFinishId),
        page.Order.Description ?? "-");

    private static string Lookup(IReadOnlyList<OrderLookupItem> items, int? id) =>
        items.FirstOrDefault(item => item.Id == id)?.Name ?? "-";
}

/// <summary>Server-owned data for the preserved four-by-three order label.</summary>
public sealed record OrderLabelData(
    string Id,
    string Name,
    int OrderQuantity,
    int ManufactureQuantity,
    int RemainingQuantity,
    string Process,
    string Material,
    string Color,
    string SurfaceFinish,
    string Description);
