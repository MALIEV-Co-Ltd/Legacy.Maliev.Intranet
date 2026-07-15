using Legacy.Maliev.Intranet.Auth;
using Legacy.Maliev.Intranet.Customers;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Legacy.Maliev.Intranet.Pages.Customers;

/// <summary>Lists legacy customer profiles through the authenticated BFF boundary.</summary>
public sealed class IndexModel(ILegacyCustomerClient customers, EmployeeSessionService sessions) : PageModel
{
    /// <summary>The current page of customer profiles.</summary>
    public PaginatedResponse<CustomerResponse> Results { get; private set; } = new([], 1, 0, 0);

    /// <summary>The optional profile search text.</summary>
    [BindProperty(SupportsGet = true)]
    public string? Search { get; set; }

    /// <summary>The selected legacy sort order.</summary>
    [BindProperty(SupportsGet = true)]
    public CustomerSortType Sort { get; set; } = CustomerSortType.CustomerCreatedDate_Descending;

    /// <summary>The requested one-based page index.</summary>
    [BindProperty(SupportsGet = true)]
    public int Index { get; set; } = 1;

    /// <summary>The requested bounded page size.</summary>
    [BindProperty(SupportsGet = true)]
    public int Size { get; set; } = 25;

    /// <summary>Loads the customer page with a fresh server-side employee token.</summary>
    /// <param name="cancellationToken">Token to cancel the asynchronous operation.</param>
    /// <returns>The rendered page or a redirect to login when the session expired.</returns>
    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
    {
        Index = Math.Max(1, Index);
        Size = Math.Clamp(Size, 1, 250);
        var token = await sessions.GetAccessTokenAsync(HttpContext, cancellationToken);
        if (string.IsNullOrWhiteSpace(token))
        {
            return RedirectToPage("/Login");
        }

        Results = await customers.GetCustomersAsync(Sort, Search, Index, Size, token, cancellationToken)
            ?? new([], Index, 0, 0);
        return Page();
    }
}