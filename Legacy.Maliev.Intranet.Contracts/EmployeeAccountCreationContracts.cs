using System.ComponentModel.DataAnnotations;

namespace Legacy.Maliev.Intranet.Contracts;

/// <summary>Validated employee profile and initial account fields accepted by the employee BFF.</summary>
public sealed class CreateEmployeeAccountRequest
{
    [Required, StringLength(256)] public string FirstName { get; set; } = string.Empty;
    [Required, StringLength(256)] public string LastName { get; set; } = string.Empty;
    [Required, EmailAddress, StringLength(320)] public string Email { get; set; } = string.Empty;
    [Required, StringLength(1024, MinimumLength = 6), DataType(DataType.Password)] public string Password { get; set; } = string.Empty;
    [Required, Compare(nameof(Password)), DataType(DataType.Password)] public string ConfirmPassword { get; set; } = string.Empty;
    [Phone, StringLength(64)] public string? PhoneNumber { get; set; }
    [Range(1, int.MaxValue)] public int? RoleId { get; set; }
    [DataType(DataType.Date)] public DateTime? DateOfBirth { get; set; }
}

/// <summary>Browser-safe identifier returned after both employee records are created.</summary>
public sealed record CreatedEmployeeAccount(int Id);
