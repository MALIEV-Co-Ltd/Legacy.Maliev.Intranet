using Legacy.Maliev.Intranet.Auth;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using System.ComponentModel.DataAnnotations;

namespace Legacy.Maliev.Intranet.Pages;

/// <summary>Employee sign-in boundary backed only by Legacy.Maliev.AuthService.</summary>
public sealed class LoginModel(ILegacyAuthClient authClient, EmployeeSessionService sessionService) : PageModel
{
    /// <summary>Gets or sets employee email.</summary>
    [BindProperty]
    [Required, EmailAddress, StringLength(320)]
    public string Email { get; set; } = string.Empty;

    /// <summary>Gets or sets employee password for this request only.</summary>
    [BindProperty]
    [Required, StringLength(1024, MinimumLength = 1)]
    public string Password { get; set; } = string.Empty;

    /// <summary>Gets or sets a validated local return URL.</summary>
    [BindProperty(SupportsGet = true)]
    public string? ReturnUrl { get; set; }

    /// <summary>Authenticates and creates an opaque server-side employee session.</summary>
    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return Page();
        }

        var login = await authClient.LoginAsync(Email.Trim(), Password, cancellationToken);
        Password = string.Empty;
        if (!login.Succeeded)
        {
            ModelState.AddModelError(string.Empty, "The email or password is invalid.");
            return Page();
        }

        await sessionService.SignInAsync(HttpContext, login);
        return LocalRedirect(Url.IsLocalUrl(ReturnUrl) ? ReturnUrl! : "/Dashboard");
    }
}