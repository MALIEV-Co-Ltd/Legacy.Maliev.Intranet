using Legacy.Maliev.Intranet.Contracts;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Legacy.Maliev.Intranet.Pages;

/// <summary>Staff-only, redacted compatibility error page.</summary>
public sealed class ErrorModel : PageModel
{
    /// <summary>Gets the validated HTTP failure code, if supplied.</summary>
    public int? DisplayCode { get; private set; }

    /// <summary>Gets the reference for this page request, not the triggering failure.</summary>
    public string PageReference => HttpContext.TraceIdentifier;

    /// <summary>Handles direct and status-redirected visits without reflecting URL data.</summary>
    public void OnGet(string? code = null)
    {
        Response.Headers.CacheControl = "no-store";
        DisplayCode = ErrorPageStatus.DisplayCode(code);
    }
}
