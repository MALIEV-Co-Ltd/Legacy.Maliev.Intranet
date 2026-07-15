using Legacy.Maliev.Intranet.Auth;
using Legacy.Maliev.Intranet.Employees;
using Legacy.Maliev.Intranet.Materials;
using Legacy.Maliev.Intranet.PurchaseOrders;
using Legacy.Maliev.Intranet.Suppliers;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Legacy.Maliev.Intranet.Pages.PurchaseOrders;

/// <summary>Creates a purchase order, QuestPDF document, scanned GCS object, and metadata link.</summary>
public sealed class CreateModel(ILegacyProcurementClient procurement, ILegacyEmployeeClient employees, ILegacyCatalogClient catalog, IPurchaseOrderDocumentClient documents, ILegacyFileClient files, EmployeeSessionService sessions, ILogger<CreateModel> logger) : PageModel
{
    /// <summary>Purchase-order editor.</summary>
    [BindProperty] public PurchaseOrderInput Input { get; set; } = new();
    /// <summary>Selectable suppliers.</summary>
    public IReadOnlyList<SupplierResponse> Suppliers { get; private set; } = [];
    /// <summary>Selectable employees.</summary>
    public IReadOnlyList<EmployeeResponse> Employees { get; private set; } = [];
    /// <summary>Selectable purchasing addresses.</summary>
    public IReadOnlyList<PurchaseOrderAddressResponse> Addresses { get; private set; } = [];

    /// <summary>Loads form options.</summary>
    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
    {
        var token = await sessions.GetAccessTokenAsync(HttpContext, cancellationToken); if (string.IsNullOrWhiteSpace(token)) return RedirectToPage("/Login");
        await LoadOptionsAsync(token, cancellationToken); return Page();
    }

    /// <summary>Creates the complete purchase-order workflow with compensating rollback.</summary>
    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        var token = await sessions.GetAccessTokenAsync(HttpContext, cancellationToken); if (string.IsNullOrWhiteSpace(token)) return RedirectToPage("/Login");
        if (!ModelState.IsValid) { await LoadOptionsAsync(token, cancellationToken); return Page(); }
        PurchaseOrderResponse? order = null; UploadObjectResponse? upload = null; PurchaseOrderFileResponse? fileRecord = null; var itemIds = new List<int>();
        try
        {
            order = await procurement.CreatePurchaseOrderAsync(Input.ToRequest(), token, cancellationToken);
            foreach (var item in Input.Items)
            {
                var created = await procurement.CreateOrderItemAsync(new(order.Id, item.PartNumber, item.Description, item.Quantity, item.UnitPrice), token, cancellationToken); itemIds.Add(created.Id);
            }
            var document = await BuildDocumentAsync(order, token, cancellationToken);
            var pdf = await documents.RenderAsync(document, token, cancellationToken);
            upload = await files.UploadPdfAsync(order.Id, pdf, token, cancellationToken);
            fileRecord = await procurement.CreatePurchaseOrderFileAsync(order.Id, upload.Bucket, upload.ObjectName, token, cancellationToken);
            return RedirectToPage("/PurchaseOrders/View", new { id = order.Id });
        }
        catch (Exception exception) when (exception is HttpRequestException or InvalidOperationException or OperationCanceledException)
        {
            using var rollbackTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            await RollbackAsync(order, itemIds, upload, fileRecord, token, rollbackTimeout.Token);
            logger.LogWarning(exception, "Purchase order creation failed at a migrated service boundary");
            ModelState.AddModelError(string.Empty, "The purchase order could not be completed. No partial order was retained.");
            await LoadOptionsAsync(token, cancellationToken); return Page();
        }
    }

    private async Task LoadOptionsAsync(string token, CancellationToken cancellationToken)
    {
        var suppliersTask = procurement.GetSuppliersAsync(SupplierSortType.SupplierName_Ascending, null, 1, 250, token, cancellationToken);
        var employeesTask = employees.GetEmployeesAsync(EmployeeSortType.EmployeeId_Ascending, null, 1, 250, token, cancellationToken);
        var addressesTask = procurement.GetPurchaseOrderAddressesAsync(token, cancellationToken); await Task.WhenAll(suppliersTask, employeesTask, addressesTask);
        Suppliers = (await suppliersTask)?.Items ?? []; Employees = (await employeesTask)?.Items ?? []; Addresses = await addressesTask;
    }

    private async Task<PurchaseOrderDocument> BuildDocumentAsync(PurchaseOrderResponse order, string token, CancellationToken cancellationToken)
    {
        var supplierTask = procurement.GetSupplierAsync(Input.SupplierId, token, cancellationToken);
        var supplierAddressTask = procurement.GetSupplierAddressAsync(Input.SupplierId, token, cancellationToken);
        var shippingTask = procurement.GetPurchaseOrderAddressAsync(Input.ShippingAddressId, token, cancellationToken);
        var billingTask = procurement.GetPurchaseOrderAddressAsync(Input.BillingAddressId, token, cancellationToken);
        var employeeTask = employees.GetEmployeeAsync(Input.EmployeeId, token, cancellationToken);
        var countriesTask = catalog.GetCountriesAsync(token, cancellationToken);
        await Task.WhenAll(supplierTask, supplierAddressTask, shippingTask, billingTask, employeeTask, countriesTask);
        var supplier = await supplierTask ?? throw new InvalidOperationException("Supplier disappeared while creating the document.");
        var employee = await employeeTask ?? throw new InvalidOperationException("Employee disappeared while creating the document.");
        var countries = (await countriesTask).ToDictionary(value => value.Id, value => value.Name);
        var supplierAddress = await supplierAddressTask;
        var shipping = await shippingTask ?? throw new InvalidOperationException("Shipping address disappeared while creating the document.");
        var billing = await billingTask ?? throw new InvalidOperationException("Billing address disappeared while creating the document.");
        return new(
            Company(Input.BillingCompanyName, Input.BillingContactPerson, Input.BillingTelephone, Input.BillingMobile, Input.BillingFax, billing, countries),
            order.CreatedDate ?? DateTime.UtcNow, Input.Fob, Input.Notes, employee.FullName,
            Input.Items.Select(value => new PurchaseOrderDocumentItem("THB", value.Description, value.PartNumber, value.Quantity, value.Quantity * value.UnitPrice, value.UnitPrice)).ToArray(),
            order.Id, Input.ShippingMethod,
            Company(Input.ShippingCompanyName, Input.ShippingContactPerson, Input.ShippingTelephone, Input.ShippingMobile, Input.ShippingFax, shipping, countries),
            new CompanyInformation(Address(supplierAddress, countries), supplier.Name ?? string.Empty, Input.SupplierContactPerson, supplier.Fax, supplier.Mobile, supplier.Telephone), Input.Terms);
    }

    private static CompanyInformation Company(string name, string? contact, string? telephone, string? mobile, string? fax, PurchaseOrderAddressResponse address, IReadOnlyDictionary<int, string> countries) => new(Address(address, countries), name, contact, fax, mobile, telephone);
    private static PurchaseOrderDocumentAddress Address(PurchaseOrderAddressResponse value, IReadOnlyDictionary<int, string> countries) => new(value.AddressLine1, value.AddressLine2, value.Building, value.City, countries.GetValueOrDefault(value.CountryId, string.Empty), value.PostalCode, value.State);
    private static PurchaseOrderDocumentAddress Address(SupplierAddressResponse? value, IReadOnlyDictionary<int, string> countries) => value is null ? new(null, null, null, null, null, null, null) : new(value.Address1, value.Address2, value.Building, value.City, countries.GetValueOrDefault(value.CountryId, string.Empty), value.PostalCode, value.State);

    private async Task RollbackAsync(PurchaseOrderResponse? order, IEnumerable<int> itemIds, UploadObjectResponse? upload, PurchaseOrderFileResponse? fileRecord, string token, CancellationToken cancellationToken)
    {
        if (fileRecord is not null) await TryRollbackAsync(() => procurement.DeletePurchaseOrderFileAsync(fileRecord.Id, token, cancellationToken), "file metadata", order?.Id);
        if (upload is not null) await TryRollbackAsync(() => files.DeleteAsync(upload.Bucket, upload.ObjectName, token, cancellationToken), "cloud object", order?.Id);
        foreach (var id in itemIds) await TryRollbackAsync(() => procurement.DeleteOrderItemAsync(id, token, cancellationToken), "line item", order?.Id);
        if (order is not null) await TryRollbackAsync(() => procurement.DeletePurchaseOrderAsync(order.Id, token, cancellationToken), "purchase order", order.Id);
    }
    private async Task TryRollbackAsync(Func<Task> action, string resource, int? id)
    {
        try { await action(); } catch (Exception exception) when (exception is HttpRequestException or OperationCanceledException) { logger.LogError(exception, "Failed to roll back {Resource} for purchase order {PurchaseOrderId}", resource, id); }
    }
}