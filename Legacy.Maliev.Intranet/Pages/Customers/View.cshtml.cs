using Legacy.Maliev.Intranet.Auth;
using Legacy.Maliev.Intranet.Customers;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Legacy.Maliev.Intranet.Pages.Customers;

/// <summary>Displays a customer profile without coupling to its identity storage.</summary>
public sealed class ViewModel(ILegacyCustomerClient customers, EmployeeSessionService sessions) : PageModel
{
    /// <summary>The customer profile displayed by the page.</summary>
    public CustomerResponse Customer { get; private set; } = null!;

    /// <summary>Loads one customer through the bearer-authenticated service boundary.</summary>
    /// <param name="id">The legacy customer identifier.</param>
    /// <param name="cancellationToken">Token to cancel the asynchronous operation.</param>
    /// <returns>The page, a not-found result, or a login redirect.</returns>
    public async Task<IActionResult> OnGetAsync(int id, CancellationToken cancellationToken)
    {
        var token = await sessions.GetAccessTokenAsync(HttpContext, cancellationToken);
        if (string.IsNullOrWhiteSpace(token))
        {
            return RedirectToPage("/Login");
        }

        var customer = await customers.GetCustomerAsync(id, token, cancellationToken);
        if (customer is null)
        {
            return NotFound();
        }

        Customer = customer;
        return Page();
    }
}
