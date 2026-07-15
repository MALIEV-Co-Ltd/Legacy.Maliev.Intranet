using Legacy.Maliev.Intranet.Auth;
using Legacy.Maliev.Intranet.Employees;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Legacy.Maliev.Intranet.Pages.Employees;

/// <summary>Displays an employee profile without coupling to its identity storage.</summary>
public sealed class ViewModel(ILegacyEmployeeClient employees, EmployeeSessionService sessions) : PageModel
{
    /// <summary>The employee profile displayed by the page.</summary>
    public EmployeeResponse Employee { get; private set; } = null!;

    /// <summary>Loads one employee through the bearer-authenticated service boundary.</summary>
    public async Task<IActionResult> OnGetAsync(int id, CancellationToken cancellationToken)
    {
        var token = await sessions.GetAccessTokenAsync(HttpContext, cancellationToken);
        if (string.IsNullOrWhiteSpace(token)) return RedirectToPage("/Login");

        var employee = await employees.GetEmployeeAsync(id, token, cancellationToken);
        if (employee is null) return NotFound();

        Employee = employee;
        return Page();
    }
}
