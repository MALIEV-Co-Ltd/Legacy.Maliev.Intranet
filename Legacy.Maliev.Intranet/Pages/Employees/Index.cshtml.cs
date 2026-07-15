using Legacy.Maliev.Intranet.Auth;
using Legacy.Maliev.Intranet.Employees;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Legacy.Maliev.Intranet.Pages.Employees;

/// <summary>Lists employee profiles through the authenticated BFF boundary.</summary>
public sealed class IndexModel(ILegacyEmployeeClient employees, EmployeeSessionService sessions) : PageModel
{
    /// <summary>The current page of employee profiles.</summary>
    public PaginatedResponse<EmployeeResponse> Results { get; private set; } = new([], 1, 0, 0);
    /// <summary>The optional profile search text.</summary>
    [BindProperty(SupportsGet = true)]
    public string? Search { get; set; }
    /// <summary>The selected legacy sort order.</summary>
    [BindProperty(SupportsGet = true)]
    public EmployeeSortType Sort { get; set; } = EmployeeSortType.EmployeeId_Descending;
    /// <summary>The requested one-based page index.</summary>
    [BindProperty(SupportsGet = true)]
    public int Index { get; set; } = 1;
    /// <summary>The requested bounded page size.</summary>
    [BindProperty(SupportsGet = true)]
    public int Size { get; set; } = 25;

    /// <summary>Loads the employee page with a fresh server-side employee token.</summary>
    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
    {
        Index = Math.Max(1, Index);
        Size = Math.Clamp(Size, 1, 250);
        var token = await sessions.GetAccessTokenAsync(HttpContext, cancellationToken);
        if (string.IsNullOrWhiteSpace(token)) return RedirectToPage("/Login");

        Results = await employees.GetEmployeesAsync(Sort, Search, Index, Size, token, cancellationToken)
            ?? new([], Index, 0, 0);
        return Page();
    }
}
