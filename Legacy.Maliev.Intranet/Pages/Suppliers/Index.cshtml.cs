using Legacy.Maliev.Intranet.Auth;
using Legacy.Maliev.Intranet.Suppliers;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
namespace Legacy.Maliev.Intranet.Pages.Suppliers;
/// <summary>Lists suppliers through ProcurementService.</summary>
public sealed class IndexModel(ILegacyProcurementClient procurement, EmployeeSessionService sessions) : PageModel
{
    /// <summary>Current supplier page.</summary>
    public PaginatedResponse<SupplierResponse> Results { get; private set; } = new([], 1, 0, 0);
    /// <summary>Search text.</summary>
    [BindProperty(SupportsGet = true)] public string? Search { get; set; }
    /// <summary>Sort order.</summary>
    [BindProperty(SupportsGet = true)] public SupplierSortType Sort { get; set; } = SupplierSortType.SupplierId_Descending;
    /// <summary>Page index.</summary>
    [BindProperty(SupportsGet = true)] public int Index { get; set; } = 1;
    /// <summary>Page size.</summary>
    [BindProperty(SupportsGet = true)] public int Size { get; set; } = 100;
    /// <summary>Loads suppliers.</summary>
    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
    { Index = Math.Max(1, Index); Size = Math.Clamp(Size, 1, 250); var token = await sessions.GetAccessTokenAsync(HttpContext, cancellationToken); if (string.IsNullOrWhiteSpace(token)) return RedirectToPage("/Login"); Results = await procurement.GetSuppliersAsync(Sort, Search, Index, Size, token, cancellationToken) ?? new([], Index, 0, 0); return Page(); }
}