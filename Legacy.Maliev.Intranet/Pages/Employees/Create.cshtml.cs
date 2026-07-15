using Legacy.Maliev.Intranet.Auth;
using Legacy.Maliev.Intranet.Employees;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Legacy.Maliev.Intranet.Pages.Employees;

/// <summary>Creates an employee profile and AuthService-owned identity with compensating rollback.</summary>
public sealed class CreateModel(
    ILegacyEmployeeClient employees,
    ILegacyAuthClient auth,
    EmployeeSessionService sessions,
    ILogger<CreateModel> logger) : PageModel
{
    /// <summary>The validated profile and initial-account form values.</summary>
    [BindProperty]
    public CreateEmployeeInput Input { get; set; } = new();

    /// <summary>Displays an empty employee creation form.</summary>
    public void OnGet()
    {
    }

    /// <summary>Creates the profile and identity with profile rollback on failure.</summary>
    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid) return Page();

        var token = await sessions.GetAccessTokenAsync(HttpContext, cancellationToken);
        if (string.IsNullOrWhiteSpace(token)) return RedirectToPage("/Login");

        EmployeeResponse? profile = null;
        try
        {
            profile = await employees.CreateEmployeeAsync(
                new(Input.RoleId, Input.FirstName, Input.LastName, Input.PhoneNumber, Input.Email, Input.DateOfBirth, null),
                token,
                cancellationToken);
            var identity = await auth.CreateEmployeeIdentityAsync(
                profile.Id,
                new(Input.Email, Input.Email, Input.Password, true, Input.PhoneNumber),
                token,
                cancellationToken);
            if (identity is null) throw new InvalidOperationException("An employee identity already exists for this profile.");

            return RedirectToPage("/Employees/View", new { id = profile.Id });
        }
        catch (Exception exception) when (exception is HttpRequestException or InvalidOperationException)
        {
            if (profile is not null)
            {
                try
                {
                    await employees.DeleteEmployeeAsync(profile.Id, token, cancellationToken);
                }
                catch (HttpRequestException rollbackException)
                {
                    logger.LogError(rollbackException, "Employee profile rollback failed for profile {EmployeeId}", profile.Id);
                }
            }

            logger.LogWarning("Employee creation failed at a downstream service boundary");
            ModelState.AddModelError(string.Empty, "The employee could not be created. No password was stored. Please retry.");
            return Page();
        }
    }
}