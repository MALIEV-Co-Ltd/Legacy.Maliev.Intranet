using Legacy.Maliev.Intranet.Auth;
using Legacy.Maliev.Intranet.Materials;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Legacy.Maliev.Intranet.Pages.Materials;

/// <summary>Lists materials through the authenticated CatalogService boundary.</summary>
public sealed class IndexModel(ILegacyCatalogClient catalog, EmployeeSessionService sessions) : PageModel
{
    /// <summary>The current material page.</summary>
    public PaginatedMaterialResponse Results { get; private set; } = new([], 1, 0, 0, false, false);
    /// <summary>The optional search text.</summary>
    [BindProperty(SupportsGet = true)] public string? Search { get; set; }
    /// <summary>The selected material sort.</summary>
    [BindProperty(SupportsGet = true)] public MaterialSortType Sort { get; set; } = MaterialSortType.MaterialId_Descending;
    /// <summary>The one-based page index.</summary>
    [BindProperty(SupportsGet = true)] public int Index { get; set; } = 1;
    /// <summary>The bounded page size.</summary>
    [BindProperty(SupportsGet = true)] public int Size { get; set; } = 100;

    /// <summary>Loads the material page.</summary>
    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
    {
        Index = Math.Max(1, Index); Size = Math.Clamp(Size, 1, 250);
        var token = await sessions.GetAccessTokenAsync(HttpContext, cancellationToken);
        if (string.IsNullOrWhiteSpace(token)) return RedirectToPage("/Login");
        Results = await catalog.GetMaterialsAsync(Sort, Search, Index, Size, token, cancellationToken)
            ?? new([], Index, 0, 0, false, Index > 1);
        return Page();
    }
}
