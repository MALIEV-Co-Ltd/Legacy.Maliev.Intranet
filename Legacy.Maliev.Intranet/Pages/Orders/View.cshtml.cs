using Legacy.Maliev.Intranet.Auth;
using Legacy.Maliev.Intranet.Orders;
using Legacy.Maliev.Intranet.PurchaseOrders;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using System.Net;

namespace Legacy.Maliev.Intranet.Pages.Orders;

/// <summary>Views and edits one order through typed service boundaries.</summary>
public sealed class ViewModel(
    ILegacyOrderClient orders,
    ILegacyFileClient files,
    IOrderDocumentClient documents,
    OrderReferenceDataLoader referenceDataLoader,
    EmployeeSessionService sessions,
    ILogger<ViewModel> logger) : PageModel
{
    /// <summary>Validated editable fields.</summary>
    [BindProperty] public OrderInput Input { get; set; } = new();
    /// <summary>New files streamed to FileService.</summary>
    [BindProperty] public List<IFormFile> Files { get; set; } = [];
    /// <summary>Current OrderService projection.</summary>
    public OrderResponse? Order { get; private set; }
    /// <summary>Reference data for labels and editors.</summary>
    public OrderReferenceData ReferenceData { get; private set; } = OrderReferenceData.Empty;
    /// <summary>Status history.</summary>
    public IReadOnlyList<OrderStatusHistoryResponse> History { get; private set; } = [];
    /// <summary>Current and available statuses.</summary>
    public IReadOnlyList<OrderStatusResponse> Statuses { get; private set; } = [];
    /// <summary>Signed clean-file downloads.</summary>
    public IReadOnlyList<OrderDownload> Downloads { get; private set; } = [];

    /// <summary>Loads one order editor.</summary>
    public async Task<IActionResult> OnGetAsync(int id, CancellationToken cancellationToken)
    {
        if (id <= 0) return BadRequest("Order id is required.");
        var token = await sessions.GetAccessTokenAsync(HttpContext, cancellationToken);
        if (string.IsNullOrWhiteSpace(token)) return RedirectToPage("/Login");
        return await LoadAsync(id, token, false, cancellationToken) ? Page() : NotFound();
    }

    /// <summary>Updates order fields using the returned ModifiedDate concurrency token.</summary>
    public async Task<IActionResult> OnPostUpdateAsync(int id, CancellationToken cancellationToken)
    {
        var token = await sessions.GetAccessTokenAsync(HttpContext, cancellationToken);
        if (string.IsNullOrWhiteSpace(token)) return RedirectToPage("/Login");
        if (!ModelState.IsValid)
        {
            await LoadAsync(id, token, true, cancellationToken);
            return Page();
        }
        try
        {
            await orders.UpdateOrderAsync(id, Input.ToRequest(), AsOffset(Input.ModifiedDate), token, cancellationToken);
            TempData["Notice"] = "Order updated.";
            return RedirectToPage(new { id });
        }
        catch (HttpRequestException exception) when (exception.StatusCode == HttpStatusCode.Conflict)
        {
            logger.LogWarning("Concurrent update rejected for order {OrderId}.", id);
            ModelState.AddModelError(string.Empty, "This order was changed by another request. Reload and review the latest values.");
            await LoadAsync(id, token, true, cancellationToken);
            return Page();
        }
    }

    /// <summary>Transitions order status independently from field edits.</summary>
    public async Task<IActionResult> OnPostStatusAsync(int id, int statusId, CancellationToken cancellationToken)
    {
        if (id <= 0 || statusId <= 0) return BadRequest();
        var token = await sessions.GetAccessTokenAsync(HttpContext, cancellationToken);
        if (string.IsNullOrWhiteSpace(token)) return RedirectToPage("/Login");
        var current = await orders.GetLatestStatusAsync(id, token, cancellationToken);
        var available = current is null ? [] : await orders.GetAvailableStatusesAsync(current.Id, token, cancellationToken);
        var selected = available.SingleOrDefault(value => value.Id == statusId);
        await orders.TransitionOrderAsync(id, statusId, token, cancellationToken);
        if (string.Equals(selected?.Name, "Accepted", StringComparison.OrdinalIgnoreCase))
            await DisableCancellationAsync(id, token, cancellationToken);
        return RedirectToPage(new { id });
    }

    /// <summary>Uploads scanned files and records OrderService metadata.</summary>
    public async Task<IActionResult> OnPostUploadAsync(int id, CancellationToken cancellationToken)
    {
        var token = await sessions.GetAccessTokenAsync(HttpContext, cancellationToken);
        if (string.IsNullOrWhiteSpace(token)) return RedirectToPage("/Login");
        var order = await orders.GetOrderAsync(id, token, cancellationToken);
        if (order?.CustomerId is not int customerId) return NotFound();
        if (Files.Count == 0) return RedirectToPage(new { id });
        var uploads = await files.UploadOrderFilesAsync(customerId, Files, token, cancellationToken);
        var metadata = new List<OrderFileResponse>();
        try
        {
            foreach (var upload in uploads)
                metadata.Add(await orders.CreateOrderFileAsync(id, upload.Bucket, upload.ObjectName, token, cancellationToken));
        }
        catch (HttpRequestException)
        {
            foreach (var record in metadata)
                await TryFileRollbackAsync(() => orders.DeleteOrderFileAsync(record.Id, token, cancellationToken), id);
            foreach (var upload in uploads)
                await TryFileRollbackAsync(() => files.DeleteAsync(upload.Bucket, upload.ObjectName, token, cancellationToken), id);
            throw;
        }
        return RedirectToPage(new { id });
    }

    /// <summary>Deletes a file only after resolving its server-owned metadata.</summary>
    public async Task<IActionResult> OnPostRemoveFileAsync(int id, int fileId, CancellationToken cancellationToken)
    {
        var token = await sessions.GetAccessTokenAsync(HttpContext, cancellationToken);
        if (string.IsNullOrWhiteSpace(token)) return RedirectToPage("/Login");
        var record = (await orders.GetOrderFilesAsync(id, token, cancellationToken)).SingleOrDefault(value => value.Id == fileId);
        if (record is null) return NotFound();
        await files.DeleteAsync(record.Bucket, record.ObjectName, token, cancellationToken);
        await orders.DeleteOrderFileAsync(record.Id, token, cancellationToken);
        return RedirectToPage(new { id });
    }

    /// <summary>Renders the preserved QuestPDF order label.</summary>
    public async Task<IActionResult> OnGetLabelAsync(int id, CancellationToken cancellationToken)
    {
        var token = await sessions.GetAccessTokenAsync(HttpContext, cancellationToken);
        if (string.IsNullOrWhiteSpace(token)) return RedirectToPage("/Login");
        var orderTask = orders.GetOrderAsync(id, token, cancellationToken);
        var referencesTask = referenceDataLoader.LoadAsync(token, cancellationToken);
        await Task.WhenAll(orderTask, referencesTask);
        var order = await orderTask;
        if (order is null) return NotFound();
        var references = await referencesTask;
        var payload = new OrderLabelRequest(
            order.Id.ToString(), order.Name ?? "-", order.Quantity, order.Manufactured, order.Remaining ?? 0,
            references.Processes.FirstOrDefault(value => value.Id == order.ProcessId)?.Name ?? "-",
            references.Materials.FirstOrDefault(value => value.Id == order.MaterialId)?.Name ?? "-",
            references.Colors.FirstOrDefault(value => value.Id == order.ColorId)?.Name ?? "-",
            references.SurfaceFinishes.FirstOrDefault(value => value.Id == order.SurfaceFinishId)?.Name ?? "-",
            order.Description ?? "-");
        return File(await documents.RenderOrderLabelAsync(payload, token, cancellationToken), "application/pdf", $"OrderLabel_{id}.pdf");
    }

    private async Task<bool> LoadAsync(int id, string token, bool preserveInput, CancellationToken cancellationToken)
    {
        var orderTask = orders.GetOrderAsync(id, token, cancellationToken);
        var referencesTask = referenceDataLoader.LoadAsync(token, cancellationToken);
        var filesTask = orders.GetOrderFilesAsync(id, token, cancellationToken);
        var historyTask = orders.GetStatusHistoryAsync(id, token, cancellationToken);
        var latestTask = orders.GetLatestStatusAsync(id, token, cancellationToken);
        await Task.WhenAll(orderTask, referencesTask, filesTask, historyTask, latestTask);
        Order = await orderTask;
        if (Order is null) return false;
        if (!preserveInput) Input = OrderInput.From(Order);
        ReferenceData = await referencesTask;
        History = await historyTask;
        var latest = await latestTask;
        var available = latest is null ? [] : await orders.GetAvailableStatusesAsync(latest.Id, token, cancellationToken);
        Statuses = latest is null ? available : [latest, .. available.Where(value => value.Id != latest.Id)];
        var downloads = await Task.WhenAll((await filesTask).Select(async record =>
            new OrderDownload(record.Id, record.ObjectName, await files.GetSignedUrlAsync(record.Bucket, record.ObjectName, token, cancellationToken))));
        Downloads = downloads.Where(value => value.Uri is not null).ToArray();
        return true;
    }

    private static DateTimeOffset? AsOffset(DateTime? value) => value is null
        ? null
        : new DateTimeOffset(DateTime.SpecifyKind(value.Value, DateTimeKind.Utc));

    private async Task DisableCancellationAsync(int id, string token, CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < 2; attempt++)
        {
            var order = await orders.GetOrderAsync(id, token, cancellationToken);
            if (order is null || !order.AllowCancellation) return;
            var input = OrderInput.From(order);
            input.AllowCancellation = false;
            try
            {
                await orders.UpdateOrderAsync(id, input.ToRequest(), AsOffset(order.ModifiedDate), token, cancellationToken);
                return;
            }
            catch (HttpRequestException exception) when (exception.StatusCode == HttpStatusCode.Conflict && attempt == 0)
            {
                logger.LogWarning("Retrying cancellation lock after a concurrent Accepted transition for order {OrderId}.", id);
            }
        }
    }

    private async Task TryFileRollbackAsync(Func<Task> action, int orderId)
    {
        try { await action(); }
        catch (HttpRequestException exception) { logger.LogError(exception, "Failed to roll back a new file for order {OrderId}.", orderId); }
    }
}

/// <summary>Signed clean-file link.</summary>
public sealed record OrderDownload(int FileId, string ObjectName, Uri? Uri);
