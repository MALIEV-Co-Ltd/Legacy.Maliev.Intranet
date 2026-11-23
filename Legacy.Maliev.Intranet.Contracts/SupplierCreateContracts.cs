using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace Legacy.Maliev.Intranet.Contracts;

/// <summary>Browser-safe supplier and owned-address creation contract.</summary>
public sealed class SupplierCreateRequest
{
    [Required, StringLength(256)] public string Name { get; set; } = string.Empty;
    [Url] public string? Website { get; set; }
    public string? TaxNumber { get; set; }
    [EmailAddress] public string? Email { get; set; }
    public string? Note { get; set; }
    public string? Telephone { get; set; }
    public string? Mobile { get; set; }
    public string? Fax { get; set; }
    public string? Building { get; set; }
    [SupplierAddressRequired] public string Address1 { get; set; } = string.Empty;
    public string? Address2 { get; set; }
    public string? City { get; set; }
    public string? State { get; set; }
    public string? PostalCode { get; set; }
    [SupplierCountryRequired] public int CountryId { get; set; }
    /// <summary>Enables the original create-only optional-address behavior; never accepted from the wire.</summary>
    [JsonIgnore] public bool AllowMissingAddress { get; set; }

    /// <summary>Identifies any entered address data, ignoring whitespace-only fields.</summary>
    public bool HasAnyAddress() => CountryId != 0 ||
        !string.IsNullOrWhiteSpace(Building) || !string.IsNullOrWhiteSpace(Address1) ||
        !string.IsNullOrWhiteSpace(Address2) || !string.IsNullOrWhiteSpace(City) ||
        !string.IsNullOrWhiteSpace(State) || !string.IsNullOrWhiteSpace(PostalCode);

    /// <summary>Requires a nonblank first address line and a positive country identifier.</summary>
    public bool HasCreateAddress() => !string.IsNullOrWhiteSpace(Address1) && CountryId > 0;
}

/// <summary>Preserves required address validation for supplier edits.</summary>
public sealed class SupplierAddressRequiredAttribute : RequiredAttribute
{
    /// <inheritdoc />
    protected override ValidationResult? IsValid(object? value, ValidationContext context) =>
        context.ObjectInstance is SupplierCreateRequest { AllowMissingAddress: true } request && !request.HasAnyAddress() ? ValidationResult.Success : base.IsValid(value, context);
}

/// <summary>Preserves required country validation for supplier edits.</summary>
public sealed class SupplierCountryRequiredAttribute : RangeAttribute
{
    /// <summary>Creates the positive country identifier constraint.</summary>
    public SupplierCountryRequiredAttribute() : base(1, int.MaxValue) { }

    /// <inheritdoc />
    protected override ValidationResult? IsValid(object? value, ValidationContext context) =>
        context.ObjectInstance is SupplierCreateRequest { AllowMissingAddress: true } request && !request.HasAnyAddress() ? ValidationResult.Success : base.IsValid(value, context);
}

/// <summary>Safe result returned after supplier creation.</summary>
public sealed record CreatedSupplier(int Id);

/// <summary>Browser-safe editable supplier and owned address.</summary>
public sealed record SupplierDetail(
    int Id, string Name, string? Website, string? TaxNumber, string? Email, string? Note,
    string? Telephone, string? Mobile, string? Fax, string? Building, string Address1,
    string? Address2, string? City, string? State, string? PostalCode, int CountryId);
