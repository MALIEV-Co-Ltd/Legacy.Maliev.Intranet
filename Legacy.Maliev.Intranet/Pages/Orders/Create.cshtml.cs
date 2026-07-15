using Legacy.Maliev.Intranet.Auth;
using Legacy.Maliev.Intranet.Customers;
using Legacy.Maliev.Intranet.Orders;
using Legacy.Maliev.Intranet.PurchaseOrders;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Legacy.Maliev.Intranet.Pages.Orders;

/// <summary>Creates a customer order through independently owned services.</summary>
public sealed class CreateModel(
    ILegacyOrderClient orders,
    ILegacyCustomerClient customers,
    ILegacyFileClient files,
    ILegacyOrderNotificationClient notifications,
    OrderReferenceDataLoader referenceDataLoader,
    EmployeeSessionService sessions,
    ILogger<CreateModel> logger) : PageModel
{
    /// <summary>Validated order fields.</summary>
    [BindProperty] public OrderInput Input { get; set; } = new();
    /// <summary>Files streamed to FileService quarantine.</summary>
    [BindProperty] public List<IFormFile> Files { get; set; } = [];
    /// <summary>Selected customer projection.</summary>
    public CustomerResponse? Customer { get; private set; }
    /// <summary>Order editor reference data.</summary>
    public OrderReferenceData ReferenceData { get; private set; } = OrderReferenceData.Empty;

    /// <summary>Loads the create form and optional preselected customer.</summary>
    public async Task<IActionResult> OnGetAsync(int? customerId, CancellationToken cancellationToken)
    {
        var token = await sessions.GetAccessTokenAsync(HttpContext, cancellationToken);
        if (string.IsNullOrWhiteSpace(token)) return RedirectToPage("/Login");
        if (customerId is > 0)
        {
            Input.CustomerId = customerId.Value;
            Customer = await customers.GetCustomerAsync(customerId.Value, token, cancellationToken);
        }
        ReferenceData = await referenceDataLoader.LoadAsync(token, cancellationToken);
        return Page();
    }

    /// <summary>Creates the order, scanned file links and initial status with compensating rollback.</summary>
    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        var token = await sessions.GetAccessTokenAsync(HttpContext, cancellationToken);
        if (string.IsNullOrWhiteSpace(token)) return RedirectToPage("/Login");
        Customer = Input.CustomerId > 0
            ? await customers.GetCustomerAsync(Input.CustomerId, token, cancellationToken)
            : null;
        if (Customer is null) ModelState.AddModelError("Input.CustomerId", "Select an existing customer.");
        if (!ModelState.IsValid)
        {
            ReferenceData = await referenceDataLoader.LoadAsync(token, cancellationToken);
            return Page();
        }

        OrderResponse? order = null;
        var uploads = new List<UploadObjectResponse>();
        var metadata = new List<OrderFileResponse>();
        try
        {
            order = await orders.CreateOrderAsync(Input.ToRequest(), token, cancellationToken);
            if (Files.Count > 0)
            {
                uploads.AddRange(await files.UploadOrderFilesAsync(Input.CustomerId, Files, token, cancellationToken));
                foreach (var upload in uploads)
                    metadata.Add(await orders.CreateOrderFileAsync(order.Id, upload.Bucket, upload.ObjectName, token, cancellationToken));
            }
            await orders.CreateNewOrderStatusAsync(order.Id, token, cancellationToken);
        }
        catch (Exception exception) when (exception is HttpRequestException or InvalidOperationException)
        {
            await RollbackAsync(order, metadata, uploads, token, cancellationToken);
            logger.LogWarning(exception, "Order creation failed at a downstream service boundary.");
            ModelState.AddModelError(string.Empty, "The order could not be created. No partial order or file upload was retained.");
            ReferenceData = await referenceDataLoader.LoadAsync(token, cancellationToken);
            return Page();
        }

        if (Input.SendConfirmationEmail)
        {
            try
            {
                await notifications.SendCreatedAsync(Customer!.Email, order.Id, token, cancellationToken);
            }
            catch (HttpRequestException exception)
            {
                logger.LogWarning(exception, "Order {OrderId} was created but its optional notification failed.", order.Id);
                TempData["Warning"] = "The order was created, but the confirmation email could not be sent.";
            }
        }
        return RedirectToPage("/Orders/View", new { id = order.Id });
    }

    private async Task RollbackAsync(
        OrderResponse? order,
        IEnumerable<OrderFileResponse> metadata,
        IEnumerable<UploadObjectResponse> uploads,
        string token,
        CancellationToken cancellationToken)
    {
        foreach (var record in metadata.Reverse())
            await TryRollbackAsync(() => orders.DeleteOrderFileAsync(record.Id, token, cancellationToken), "order file metadata", order?.Id);
        foreach (var upload in uploads.Reverse())
            await TryRollbackAsync(() => files.DeleteAsync(upload.Bucket, upload.ObjectName, token, cancellationToken), "cloud object", order?.Id);
        if (order is not null)
            await TryRollbackAsync(() => orders.DeleteOrderAsync(order.Id, token, cancellationToken), "order", order.Id);
    }

    private async Task TryRollbackAsync(Func<Task> action, string resource, int? orderId)
    {
        try { await action(); }
        catch (HttpRequestException exception) { logger.LogError(exception, "Failed to roll back {Resource} for order {OrderId}.", resource, orderId); }
    }
}
