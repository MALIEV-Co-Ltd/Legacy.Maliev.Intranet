using Legacy.Maliev.Intranet.Auth;
using Legacy.Maliev.Intranet.Employees;
using Legacy.Maliev.Intranet.PurchaseOrders;
using Legacy.Maliev.Intranet.Suppliers;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Legacy.Maliev.Intranet.Pages.PurchaseOrders;

/// <summary>Displays and deletes a complete purchase order.</summary>
public sealed class ViewModel(ILegacyProcurementClient procurement, ILegacyEmployeeClient employees, ILegacyFileClient files, EmployeeSessionService sessions, ILogger<ViewModel> logger) : PageModel
{
    /// <summary>Purchase order.</summary>
    public PurchaseOrderResponse Order { get; private set; } = null!;
    /// <summary>Supplier.</summary>
    public SupplierResponse? Supplier { get; private set; }
    /// <summary>Ordering employee.</summary>
    public EmployeeResponse? Employee { get; private set; }
    /// <summary>Line items.</summary>
    public IReadOnlyList<OrderItemResponse> Items { get; private set; } = [];
    /// <summary>Downloadable clean objects.</summary>
    public IReadOnlyList<PurchaseOrderDownload> Downloads { get; private set; } = [];

    /// <summary>Loads order details and signed download URLs.</summary>
    public async Task<IActionResult> OnGetAsync(int id, CancellationToken cancellationToken)
    {
        var token = await sessions.GetAccessTokenAsync(HttpContext, cancellationToken); if (string.IsNullOrWhiteSpace(token)) return RedirectToPage("/Login");
        var order = await procurement.GetPurchaseOrderAsync(id, token, cancellationToken); if (order is null) return NotFound(); Order = order;
        var supplierTask = order.SupplierId is int supplierId ? procurement.GetSupplierAsync(supplierId, token, cancellationToken) : Task.FromResult<SupplierResponse?>(null);
        var employeeTask = order.EmployeeId is int employeeId ? employees.GetEmployeeAsync(employeeId, token, cancellationToken) : Task.FromResult<EmployeeResponse?>(null);
        var itemsTask = procurement.GetOrderItemsAsync(id, token, cancellationToken); var recordsTask = procurement.GetPurchaseOrderFilesAsync(id, token, cancellationToken);
        await Task.WhenAll(supplierTask, employeeTask, itemsTask, recordsTask); Supplier = await supplierTask; Employee = await employeeTask; Items = await itemsTask;
        var downloads = new List<PurchaseOrderDownload>(); foreach (var record in await recordsTask) { var uri = await files.GetSignedUrlAsync(record.Bucket, record.ObjectName, token, cancellationToken); if (uri is not null) downloads.Add(new(record.ObjectName, uri)); }
        Downloads = downloads;
        return Page();
    }

    /// <summary>Deletes cloud objects, metadata, line items, then the order.</summary>
    public async Task<IActionResult> OnPostDeleteAsync(int id, CancellationToken cancellationToken)
    {
        var token = await sessions.GetAccessTokenAsync(HttpContext, cancellationToken); if (string.IsNullOrWhiteSpace(token)) return RedirectToPage("/Login");
        var fileRecords = await procurement.GetPurchaseOrderFilesAsync(id, token, cancellationToken);
        try
        {
            foreach (var record in fileRecords) { await files.DeleteAsync(record.Bucket, record.ObjectName, token, cancellationToken); await procurement.DeletePurchaseOrderFileAsync(record.Id, token, cancellationToken); }
            foreach (var item in await procurement.GetOrderItemsAsync(id, token, cancellationToken)) await procurement.DeleteOrderItemAsync(item.Id, token, cancellationToken);
            await procurement.DeletePurchaseOrderAsync(id, token, cancellationToken); return RedirectToPage("/PurchaseOrders/Index");
        }
        catch (HttpRequestException exception)
        {
            logger.LogError(exception, "Purchase order deletion stopped before deleting parent {PurchaseOrderId}", id); TempData["Error"] = "Deletion stopped because a dependent resource could not be removed. Retry safely."; return RedirectToPage(new { id });
        }
    }
}

/// <summary>Signed file download.</summary>
public sealed record PurchaseOrderDownload(string ObjectName, Uri Uri);