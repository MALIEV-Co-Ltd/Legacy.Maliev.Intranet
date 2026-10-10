using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace Legacy.Maliev.Intranet.Contracts;

/// <summary>Only the selected customer-owned company or address.</summary>
public sealed record CustomerRelationDetail(int CustomerId, int? RelationId, CustomerCompanyDetail? Company, CustomerAddressDetail? Address);

/// <summary>Company metadata without assignable identifiers or timestamps.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class CustomerCompanyEdit
{
    /// <summary>Literal company name.</summary>
    [Required, StringLength(256)] public string Name { get; set; } = string.Empty;
    /// <summary>Optional literal tax number.</summary>
    [StringLength(256)] public string? TaxNumber { get; set; }
    /// <summary>Optional literal registrar.</summary>
    [StringLength(256)] public string? Registrar { get; set; }
}

/// <summary>Address metadata without assignable relation identifiers or timestamps.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class CustomerAddressEdit
{
    /// <summary>Optional building.</summary>
    [StringLength(256)] public string? Building { get; set; }
    /// <summary>Required literal first address line.</summary>
    [Required, StringLength(256)] public string AddressLine1 { get; set; } = string.Empty;
    /// <summary>Optional second address line.</summary>
    [StringLength(256)] public string? AddressLine2 { get; set; }
    /// <summary>Optional city.</summary>
    [StringLength(256)] public string? City { get; set; }
    /// <summary>Optional state.</summary>
    [StringLength(256)] public string? State { get; set; }
    /// <summary>Optional postal code.</summary>
    [StringLength(256)] public string? PostalCode { get; set; }
    /// <summary>Selected existing country reference.</summary>
    [Range(1, int.MaxValue)] public int CountryId { get; set; }
}
