using System.ComponentModel.DataAnnotations;

namespace Legacy.Maliev.Intranet.Contracts;

/// <summary>Request-only employee credentials sent from the browser to the same-origin BFF.</summary>
public sealed record EmployeeSignInRequest(
    [property: Required, EmailAddress, StringLength(320)] string Email,
    [property: Required, StringLength(1024, MinimumLength = 1)] string Password,
    string? ReturnUrl);

/// <summary>Browser-safe result of a successful employee sign-in.</summary>
public sealed record EmployeeSignInResponse(string RedirectUrl);
