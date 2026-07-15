using Legacy.Maliev.Intranet.Auth;
using Legacy.Maliev.Intranet.Suppliers;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
namespace Legacy.Maliev.Intranet.Pages.Suppliers;
/// <summary>Creates a supplier and its owned address with rollback.</summary>
public sealed class CreateModel(ILegacyProcurementClient procurement, EmployeeSessionService sessions, ILogger<CreateModel> logger) : PageModel
{
    /// <summary>Supplier editor input.</summary>
    [BindProperty] public SupplierInput Input { get; set; } = new();
    /// <summary>Displays the form.</summary>
    public void OnGet() { }
    /// <summary>Creates supplier and address.</summary>
    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid) return Page(); var token = await sessions.GetAccessTokenAsync(HttpContext, cancellationToken); if (string.IsNullOrWhiteSpace(token)) return RedirectToPage("/Login"); SupplierResponse? supplier = null;
        try { supplier = await procurement.CreateSupplierAsync(Input.Supplier(), token, cancellationToken); await procurement.CreateSupplierAddressAsync(supplier.Id, Input.Address(), token, cancellationToken); return RedirectToPage("/Suppliers/View", new { id = supplier.Id }); }
        catch (HttpRequestException exception) { if (supplier is not null) try { await procurement.DeleteSupplierAsync(supplier.Id, token, cancellationToken); } catch (HttpRequestException rollback) { logger.LogError(rollback, "Supplier rollback failed for {SupplierId}", supplier.Id); } logger.LogWarning(exception, "Supplier creation failed at ProcurementService"); ModelState.AddModelError(string.Empty, "The supplier could not be created. Please retry."); return Page(); }
    }
}