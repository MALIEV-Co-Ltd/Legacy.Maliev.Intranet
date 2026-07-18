using Legacy.Maliev.Intranet.Contracts;
using Microsoft.Extensions.Logging;

namespace Legacy.Maliev.Intranet.PurchaseOrders;

/// <summary>Normalized purchase-order detail and deletion outcomes.</summary>
public enum PurchaseOrderDetailStatus
{
    /// <summary>The operation completed successfully.</summary>
    Success,
    /// <summary>The purchase order does not exist.</summary>
    NotFound,
    /// <summary>The requested identifier was invalid.</summary>
    BadRequest,
    /// <summary>The server-owned service identity was rejected.</summary>
    Unauthorized,
    /// <summary>The server-owned service identity lacks permission.</summary>
    Forbidden,
    /// <summary>A downstream service throttled the request.</summary>
    RateLimited,
    /// <summary>A downstream response did not satisfy its contract.</summary>
    BadGateway,
    /// <summary>The downstream service is unavailable.</summary>
    Unavailable,
    /// <summary>Deletion stopped after at least one dependent resource was removed and is safe to retry.</summary>
    PartialFailure,
}

/// <summary>Safe purchase-order detail result.</summary>
public sealed record PurchaseOrderDetailResult(PurchaseOrderDetailStatus Status, PurchaseOrderDetail? Detail = null, TimeSpan? RetryAfter = null);

/// <summary>Safe purchase-order deletion result.</summary>
public sealed record PurchaseOrderDeleteResult(PurchaseOrderDetailStatus Status, TimeSpan? RetryAfter = null);

/// <summary>Normalized transport failure from the detail gateway.</summary>
public sealed class PurchaseOrderDetailGatewayException(PurchaseOrderDetailStatus status, TimeSpan? retryAfter = null, Exception? innerException = null)
    : Exception("A purchase-order detail service boundary failed.", innerException)
{
    /// <summary>Gets the normalized status.</summary>
    public PurchaseOrderDetailStatus Status { get; } = status;
    /// <summary>Gets the bounded downstream retry delay.</summary>
    public TimeSpan? RetryAfter { get; } = retryAfter;
}

/// <summary>Internal root order projection.</summary>
public sealed record PurchaseOrderDetailData(int Id, int? SupplierId, string? SupplierContactPerson, int? EmployeeId, string? ShippingMethod, string? Fob, string? Terms, string? Notes, DateTime? CreatedDate);
/// <summary>Internal line-item projection.</summary>
public sealed record PurchaseOrderDetailItemData(int Id, int? PurchaseOrderId, string? PartNumber, string? Description, int? Quantity, decimal? UnitPrice, decimal? Subtotal);
/// <summary>Internal linked clean-file projection.</summary>
public sealed record PurchaseOrderDetailFileData(int Id, int PurchaseOrderId, string Bucket, string ObjectName);

/// <summary>Transport-only boundary used by the purchase-order detail workflow.</summary>
public interface IPurchaseOrderDetailGateway
{
    /// <summary>Loads the root purchase order.</summary>
    Task<PurchaseOrderDetailData?> GetOrderAsync(int id, CancellationToken cancellationToken);
    /// <summary>Loads a supplier display name.</summary>
    Task<string?> GetSupplierNameAsync(int id, CancellationToken cancellationToken);
    /// <summary>Loads an employee display name.</summary>
    Task<string?> GetEmployeeNameAsync(int id, CancellationToken cancellationToken);
    /// <summary>Loads order-owned line items.</summary>
    Task<IReadOnlyList<PurchaseOrderDetailItemData>> GetItemsAsync(int orderId, CancellationToken cancellationToken);
    /// <summary>Loads order-owned clean-file metadata.</summary>
    Task<IReadOnlyList<PurchaseOrderDetailFileData>> GetFilesAsync(int orderId, CancellationToken cancellationToken);
    /// <summary>Resolves a short-lived clean download URL.</summary>
    Task<Uri?> GetSignedUrlAsync(string bucket, string objectName, CancellationToken cancellationToken);
    /// <summary>Deletes one clean object from FileService.</summary>
    Task DeleteStoredFileAsync(string bucket, string objectName, CancellationToken cancellationToken);
    /// <summary>Deletes one ProcurementService file link.</summary>
    Task DeleteFileLinkAsync(int id, CancellationToken cancellationToken);
    /// <summary>Deletes one ProcurementService line item.</summary>
    Task DeleteItemAsync(int id, CancellationToken cancellationToken);
    /// <summary>Deletes the root purchase order.</summary>
    Task DeleteOrderAsync(int id, CancellationToken cancellationToken);
}

/// <summary>Owns safe aggregation and dependency-first deletion for purchase-order details.</summary>
public sealed class PurchaseOrderDetailService(IPurchaseOrderDetailGateway gateway, ILogger<PurchaseOrderDetailService> logger)
{
    /// <summary>Loads one complete browser-safe purchase order.</summary>
    public async Task<PurchaseOrderDetailResult> GetAsync(int id, CancellationToken cancellationToken)
    {
        if (id <= 0) return new(PurchaseOrderDetailStatus.BadRequest);
        try
        {
            var order = await gateway.GetOrderAsync(id, cancellationToken);
            if (order is null) return new(PurchaseOrderDetailStatus.NotFound);
            if (order.Id != id) return new(PurchaseOrderDetailStatus.BadGateway);
            var supplierTask = order.SupplierId is { } supplierId ? gateway.GetSupplierNameAsync(supplierId, cancellationToken) : Task.FromResult<string?>(null);
            var employeeTask = order.EmployeeId is { } employeeId ? gateway.GetEmployeeNameAsync(employeeId, cancellationToken) : Task.FromResult<string?>(null);
            var itemsTask = gateway.GetItemsAsync(id, cancellationToken);
            var filesTask = gateway.GetFilesAsync(id, cancellationToken);
            await Task.WhenAll(supplierTask, employeeTask, itemsTask, filesTask);
            var items = itemsTask.Result;
            var files = filesTask.Result;
            if (items.Any(value => value.Id <= 0 || value.PurchaseOrderId != id) || files.Any(value => value.Id <= 0 || value.PurchaseOrderId != id))
                return new(PurchaseOrderDetailStatus.BadGateway);
            var downloads = new List<PurchaseOrderDownloadLink>();
            foreach (var file in files)
            {
                var uri = await gateway.GetSignedUrlAsync(file.Bucket, file.ObjectName, cancellationToken);
                if (uri is not null) downloads.Add(new(Path.GetFileName(file.ObjectName), uri));
            }
            return new(PurchaseOrderDetailStatus.Success, new PurchaseOrderDetail(
                id, await supplierTask, order.SupplierContactPerson, await employeeTask, order.ShippingMethod,
                order.Fob, order.Terms, order.Notes, order.CreatedDate,
                items.Select(value => new PurchaseOrderDetailLine(value.PartNumber, value.Description, value.Quantity ?? 0,
                    value.UnitPrice ?? 0, value.Subtotal ?? (value.Quantity ?? 0) * (value.UnitPrice ?? 0))).ToArray(), downloads));
        }
        catch (Exception exception) when (IsRecoverable(exception, cancellationToken))
        {
            return Failure(exception);
        }
    }

    /// <summary>Deletes clean objects, metadata, line items, then the root order so retries remain safe.</summary>
    public async Task<PurchaseOrderDeleteResult> DeleteAsync(int id, CancellationToken cancellationToken)
    {
        if (id <= 0) return new(PurchaseOrderDetailStatus.BadRequest);
        var changed = false;
        try
        {
            var order = await gateway.GetOrderAsync(id, cancellationToken);
            if (order is null) return new(PurchaseOrderDetailStatus.NotFound);
            var files = await gateway.GetFilesAsync(id, cancellationToken);
            var items = await gateway.GetItemsAsync(id, cancellationToken);
            if (files.Any(value => value.Id <= 0 || value.PurchaseOrderId != id) || items.Any(value => value.Id <= 0 || value.PurchaseOrderId != id))
                return new(PurchaseOrderDetailStatus.BadGateway);
            foreach (var file in files)
            {
                await gateway.DeleteStoredFileAsync(file.Bucket, file.ObjectName, cancellationToken);
                changed = true;
                await gateway.DeleteFileLinkAsync(file.Id, cancellationToken);
            }
            foreach (var item in items)
            {
                await gateway.DeleteItemAsync(item.Id, cancellationToken);
                changed = true;
            }
            await gateway.DeleteOrderAsync(id, cancellationToken);
            return new(PurchaseOrderDetailStatus.Success);
        }
        catch (Exception exception) when (IsRecoverable(exception, cancellationToken))
        {
            if (changed)
            {
                logger.LogError(exception, "Purchase-order {PurchaseOrderId} deletion stopped after removing a dependent resource; retry is required.", id);
                return new(PurchaseOrderDetailStatus.PartialFailure);
            }
            var failure = Failure(exception);
            return new(failure.Status, failure.RetryAfter);
        }
    }

    private static PurchaseOrderDetailResult Failure(Exception exception) => exception is PurchaseOrderDetailGatewayException boundary
        ? new(boundary.Status, RetryAfter: Bounded(boundary.RetryAfter))
        : new(PurchaseOrderDetailStatus.Unavailable);
    private static bool IsRecoverable(Exception exception, CancellationToken caller) =>
        exception is PurchaseOrderDetailGatewayException or HttpRequestException ||
        exception is OperationCanceledException && !caller.IsCancellationRequested ||
        string.Equals(exception.GetType().FullName, "Polly.Timeout.TimeoutRejectedException", StringComparison.Ordinal);
    private static TimeSpan? Bounded(TimeSpan? value) => value is { } delay && delay > TimeSpan.Zero && delay <= TimeSpan.FromHours(1) ? delay : null;
}
