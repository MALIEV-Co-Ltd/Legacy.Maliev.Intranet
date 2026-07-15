using Legacy.Maliev.Intranet.Auth;
using Legacy.Maliev.Intranet.Suppliers;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
namespace Legacy.Maliev.Intranet.Pages.Suppliers;
/// <summary>Displays and edits a supplier and owned address.</summary>
public sealed class ViewModel(ILegacyProcurementClient procurement, EmployeeSessionService sessions) : PageModel
{
    /// <summary>Supplier identifier.</summary>
    public int SupplierId { get; private set; }
    /// <summary>Editable supplier input.</summary>
    [BindProperty] public SupplierInput Input { get; set; } = new();
    /// <summary>Loads supplier and address.</summary>
    public async Task<IActionResult> OnGetAsync(int id, CancellationToken cancellationToken) { var token = await sessions.GetAccessTokenAsync(HttpContext, cancellationToken); if (string.IsNullOrWhiteSpace(token)) return RedirectToPage("/Login"); var supplierTask = procurement.GetSupplierAsync(id, token, cancellationToken); var addressTask = procurement.GetSupplierAddressAsync(id, token, cancellationToken); await Task.WhenAll(supplierTask, addressTask); var supplier = await supplierTask; if (supplier is null) return NotFound(); SupplierId = id; Input = SupplierInput.From(supplier, await addressTask); return Page(); }
    /// <summary>Updates supplier and address.</summary>
    public async Task<IActionResult> OnPostAsync(int id, CancellationToken cancellationToken) { SupplierId = id; if (!ModelState.IsValid) return Page(); var token = await sessions.GetAccessTokenAsync(HttpContext, cancellationToken); if (string.IsNullOrWhiteSpace(token)) return RedirectToPage("/Login"); var existing = await procurement.GetSupplierAddressAsync(id, token, cancellationToken); await procurement.UpdateSupplierAsync(id, Input.Supplier(), token, cancellationToken); if (existing is null) await procurement.CreateSupplierAddressAsync(id, Input.Address(), token, cancellationToken); else await procurement.UpdateSupplierAddressAsync(existing.Id, Input.Address(), token, cancellationToken); return RedirectToPage(new { id }); }
    /// <summary>Deletes a supplier.</summary>
    public async Task<IActionResult> OnPostDeleteAsync(int id, CancellationToken cancellationToken) { var token = await sessions.GetAccessTokenAsync(HttpContext, cancellationToken); if (string.IsNullOrWhiteSpace(token)) return RedirectToPage("/Login"); await procurement.DeleteSupplierAsync(id, token, cancellationToken); return RedirectToPage("/Suppliers/Index"); }
}