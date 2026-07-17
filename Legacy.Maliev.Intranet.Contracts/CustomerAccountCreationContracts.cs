using System.ComponentModel.DataAnnotations;

namespace Legacy.Maliev.Intranet.Contracts;

/// <summary>Validated customer profile and initial account fields accepted by the employee BFF.</summary>
public sealed class CreateCustomerAccountRequest
{
    /// <summary>Gets or sets the customer's given name.</summary>
    [Required, StringLength(256)]
    public string FirstName { get; set; } = string.Empty;

    /// <summary>Gets or sets the customer's family name.</summary>
    [Required, StringLength(256)]
    public string LastName { get; set; } = string.Empty;

    /// <summary>Gets or sets the email used by the profile and identity.</summary>
    [Required, EmailAddress, StringLength(320)]
    public string Email { get; set; } = string.Empty;

    /// <summary>Gets or sets the initial account password carried only in JSON.</summary>
    [Required, StringLength(1024, MinimumLength = 6), DataType(DataType.Password)]
    public string Password { get; set; } = string.Empty;

    /// <summary>Gets or sets the repeated password used only for boundary validation.</summary>
    [Required, Compare(nameof(Password)), DataType(DataType.Password)]
    public string ConfirmPassword { get; set; } = string.Empty;

    /// <summary>Gets or sets the optional telephone number.</summary>
    [Phone, StringLength(64)]
    public string? Telephone { get; set; }

    /// <summary>Gets or sets the optional mobile number.</summary>
    [StringLength(64)]
    public string? Mobile { get; set; }

    /// <summary>Gets or sets the optional fax number retained for compatibility.</summary>
    [StringLength(64)]
    public string? Fax { get; set; }

    /// <summary>Gets or sets the optional date of birth.</summary>
    [DataType(DataType.Date)]
    public DateTime? DateOfBirth { get; set; }
}

/// <summary>Browser-safe identifier returned after both profile and identity are created.</summary>
public sealed record CreatedCustomerAccount(int Id);
