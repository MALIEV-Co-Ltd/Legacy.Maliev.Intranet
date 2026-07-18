using System.ComponentModel.DataAnnotations;

namespace Legacy.Maliev.Intranet.Contracts;

/// <summary>Requests an enumeration-safe employee password recovery message.</summary>
public sealed record EmployeeRecoveryEmailRequest(
    [property: Required, EmailAddress, StringLength(320)] string Email);

/// <summary>Completes an employee email confirmation with an opaque action token.</summary>
public sealed record EmployeeEmailConfirmationRequest(
    [property: Required, EmailAddress, StringLength(320)] string Email,
    [property: Required, StringLength(256, MinimumLength = 32)] string Token);

/// <summary>Completes an employee password reset with server-validated confirmation.</summary>
public sealed record EmployeePasswordResetRequest(
    [property: Required, EmailAddress, StringLength(320)] string Email,
    [property: Required, StringLength(256, MinimumLength = 32)] string Token,
    [property: Required, StringLength(1024, MinimumLength = 6), DataType(DataType.Password)] string Password,
    [property: Required, Compare("Password"), DataType(DataType.Password)] string ConfirmPassword);
