using System.ComponentModel.DataAnnotations;

namespace Legacy.Maliev.Intranet.Contracts;

/// <summary>Allowlisted profile fields that employees may update for a legacy customer.</summary>
public sealed class CustomerUpdateRequest
{
    /// <summary>Gets or sets the customer's given name.</summary>
    [Required, StringLength(256)]
    public string FirstName { get; set; } = string.Empty;

    /// <summary>Gets or sets the customer's family name.</summary>
    [Required, StringLength(256)]
    public string LastName { get; set; } = string.Empty;

    /// <summary>Gets or sets the customer's correspondence email.</summary>
    [Required, EmailAddress, StringLength(320)]
    public string Email { get; set; } = string.Empty;

    /// <summary>Gets or sets the optional primary telephone number.</summary>
    [Phone, StringLength(64)]
    public string? Telephone { get; set; }

    /// <summary>Gets or sets the optional mobile telephone number.</summary>
    [StringLength(64)]
    public string? Mobile { get; set; }

    /// <summary>Gets or sets the optional fax number.</summary>
    [StringLength(64)]
    public string? Fax { get; set; }

    /// <summary>Gets or sets the optional date of birth.</summary>
    [DataType(DataType.Date)]
    public DateTime? DateOfBirth { get; set; }
}
