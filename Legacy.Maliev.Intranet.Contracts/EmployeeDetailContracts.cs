namespace Legacy.Maliev.Intranet.Contracts;

/// <summary>Browser-safe address projection embedded in an employee profile.</summary>
public sealed record EmployeeAddressDetail(
    int Id,
    string? Building,
    string? AddressLine1,
    string? AddressLine2,
    string? City,
    string? State,
    string? PostalCode,
    int CountryId,
    DateTime? CreatedDate,
    DateTime? ModifiedDate);

/// <summary>Browser-safe role projection embedded in an employee profile.</summary>
public sealed record EmployeeRoleDetail(
    int Id,
    string? Name,
    string? Description,
    DateTime? CreatedDate,
    DateTime? ModifiedDate);

/// <summary>Complete browser-safe employee profile matching the legacy detail contract.</summary>
public sealed record EmployeeDetail(
    int Id,
    int? RoleId,
    string FirstName,
    string LastName,
    string FullName,
    string? PhoneNumber,
    string Email,
    DateTime? DateOfBirth,
    int? HomeAddressId,
    DateTime? CreatedDate,
    DateTime? ModifiedDate,
    EmployeeAddressDetail? HomeAddress,
    EmployeeRoleDetail? Role);
