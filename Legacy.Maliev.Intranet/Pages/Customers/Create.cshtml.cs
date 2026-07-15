using Legacy.Maliev.Intranet.Auth;
using Legacy.Maliev.Intranet.Customers;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Legacy.Maliev.Intranet.Pages.Customers;

/// <summary>Creates a profile and its AuthService-owned identity with compensating rollback.</summary>
public sealed class CreateModel(
    ILegacyCustomerClient customers,
    ILegacyAuthClient auth,
    EmployeeSessionService sessions,
    ILogger<CreateModel> logger) : PageModel
{
    /// <summary>The validated profile and initial-account form values.</summary>
    [BindProperty]
    public CreateCustomerInput Input { get; set; } = new();

    /// <summary>Displays an empty customer creation form.</summary>
    public void OnGet()
    {
    }

    /// <summary>Creates the profile and AuthService identity with profile rollback on failure.</summary>
    /// <param name="cancellationToken">Token to cancel the asynchronous operation.</param>
    /// <returns>A customer view redirect on success or the form with a safe error.</returns>
    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return Page();
        }

        var token = await sessions.GetAccessTokenAsync(HttpContext, cancellationToken);
        if (string.IsNullOrWhiteSpace(token))
        {
            return RedirectToPage("/Login");
        }

        CustomerResponse? profile = null;
        try
        {
            profile = await customers.CreateCustomerAsync(
                new(Input.FirstName, Input.LastName, Input.Telephone, Input.Mobile, Input.Fax, Input.Email, Input.DateOfBirth),
                token,
                cancellationToken);
            var identity = await auth.CreateCustomerIdentityAsync(
                profile.Id,
                new(Input.Email, Input.Email, Input.Password, true, Input.Telephone, Input.Fax, Input.Mobile),
                token,
                cancellationToken);
            if (identity is null)
            {
                throw new InvalidOperationException("A customer identity already exists for this profile.");
            }

            return RedirectToPage("/Customers/View", new { id = profile.Id });
        }
        catch (Exception exception) when (exception is HttpRequestException or InvalidOperationException)
        {
            if (profile is not null)
            {
                try
                {
                    await customers.DeleteCustomerAsync(profile.Id, token, cancellationToken);
                }
                catch (HttpRequestException rollbackException)
                {
                    logger.LogError(rollbackException, "Customer profile rollback failed for profile {CustomerId}", profile.Id);
                }
            }

            logger.LogWarning("Customer creation failed at a downstream service boundary");
            ModelState.AddModelError(string.Empty, "The customer could not be created. No password was stored. Please retry.");
            return Page();
        }
    }
}