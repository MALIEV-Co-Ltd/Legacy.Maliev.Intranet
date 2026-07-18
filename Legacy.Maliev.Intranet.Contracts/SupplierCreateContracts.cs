using System.ComponentModel.DataAnnotations;

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
    [Required] public string Address1 { get; set; } = string.Empty;
    public string? Address2 { get; set; }
    public string? City { get; set; }
    public string? State { get; set; }
    public string? PostalCode { get; set; }
    [Range(1, int.MaxValue)] public int CountryId { get; set; }
}

/// <summary>Safe result returned after supplier creation.</summary>
public sealed record CreatedSupplier(int Id);

/// <summary>Browser-safe editable supplier and owned address.</summary>
public sealed record SupplierDetail(
    int Id, string Name, string? Website, string? TaxNumber, string? Email, string? Note,
    string? Telephone, string? Mobile, string? Fax, string? Building, string Address1,
    string? Address2, string? City, string? State, string? PostalCode, int CountryId);
