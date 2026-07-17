namespace Legacy.Maliev.Intranet.Contracts;

/// <summary>Browser-safe company projection embedded in a customer detail response.</summary>
public sealed record CustomerCompanyDetail(
    int Id,
    string Name,
    string? TaxNumber,
    string? Registrar,
    DateTime? CreatedDate,
    DateTime? ModifiedDate);

/// <summary>Browser-safe address projection embedded in a customer detail response.</summary>
public sealed record CustomerAddressDetail(
    int Id,
    string? Building,
    string AddressLine1,
    string? AddressLine2,
    string? City,
    string? State,
    string? PostalCode,
    int CountryId,
    DateTime? CreatedDate,
    DateTime? ModifiedDate);

/// <summary>Complete browser-safe legacy customer profile projection.</summary>
public sealed record CustomerDetail(
    int Id,
    string FirstName,
    string LastName,
    string FullName,
    string? Telephone,
    string? Mobile,
    string? Fax,
    string Email,
    DateTime? DateOfBirth,
    int? CompanyId,
    int? BillingAddressId,
    int? ShippingAddressId,
    DateTime? CreatedDate,
    DateTime? ModifiedDate,
    CustomerAddressDetail? BillingAddress,
    CustomerCompanyDetail? Company,
    CustomerAddressDetail? ShippingAddress);
