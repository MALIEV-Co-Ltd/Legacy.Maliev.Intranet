using System.ComponentModel.DataAnnotations;

namespace Legacy.Maliev.Intranet.Employees;

/// <summary>Legacy employee profile returned by EmployeeService.</summary>
public sealed record EmployeeResponse(
    int Id, int? RoleId, string FirstName, string LastName, string FullName,
    string? PhoneNumber, string Email, DateTime? DateOfBirth, int? HomeAddressId,
    DateTime? CreatedDate, DateTime? ModifiedDate, AddressResponse? HomeAddress, RoleResponse? Role);

/// <summary>Employee address projection embedded in a profile.</summary>
public sealed record AddressResponse(
    int Id, string? Building, string? AddressLine1, string? AddressLine2, string? City,
    string? State, string? PostalCode, int CountryId, DateTime? CreatedDate, DateTime? ModifiedDate);

/// <summary>Employee role projection embedded in a profile.</summary>
public sealed record RoleResponse(int Id, string? Name, string? Description, DateTime? CreatedDate, DateTime? ModifiedDate);

/// <summary>Employee profile write contract owned by EmployeeService.</summary>
public sealed record UpsertEmployeeRequest(
    int? RoleId, string FirstName, string LastName, string? PhoneNumber,
    string Email, DateTime? DateOfBirth, int? HomeAddressId);

/// <summary>Legacy paginated employee response shape.</summary>
public sealed record PaginatedResponse<T>(IReadOnlyList<T> Items, int PageIndex, int TotalPages, int TotalRecords)
{
    /// <summary>Whether another page follows this page.</summary>
    public bool HasNextPage => PageIndex < TotalPages;
    /// <summary>Whether a page precedes this page.</summary>
    public bool HasPreviousPage => PageIndex > 1;
}

/// <summary>Legacy employee sort names.</summary>
public enum EmployeeSortType
{
    /// <summary>Orders profiles by identifier from lowest to highest.</summary>
    EmployeeId_Ascending,
    /// <summary>Orders profiles by identifier from highest to lowest.</summary>
    EmployeeId_Descending,
    /// <summary>Orders profiles alphabetically by email.</summary>
    EmployeeEmail_Ascending,
    /// <summary>Orders profiles in reverse alphabetical email order.</summary>
    EmployeeEmail_Descending,
}

/// <summary>Validated employee profile and initial identity fields.</summary>
public sealed class CreateEmployeeInput
{
    /// <summary>The employee's given name.</summary>
    [Required, StringLength(256)]
    public string FirstName { get; set; } = string.Empty;
    /// <summary>The employee's family name.</summary>
    [Required, StringLength(256)]
    public string LastName { get; set; } = string.Empty;
    /// <summary>The email used for profile correspondence and account login.</summary>
    [Required, EmailAddress, StringLength(320)]
    public string Email { get; set; } = string.Empty;
    /// <summary>The initial account password sent only to AuthService.</summary>
    [Required, StringLength(1024, MinimumLength = 6), DataType(DataType.Password)]
    public string Password { get; set; } = string.Empty;
    /// <summary>The repeated password used only for server-side form validation.</summary>
    [Required, Compare(nameof(Password)), DataType(DataType.Password)]
    public string ConfirmPassword { get; set; } = string.Empty;
    /// <summary>The employee's optional phone number.</summary>
    [Phone, StringLength(64)]
    public string? PhoneNumber { get; set; }
    /// <summary>The optional legacy role identifier.</summary>
    [Range(1, int.MaxValue)]
    public int? RoleId { get; set; }
    /// <summary>The employee's optional date of birth.</summary>
    [DataType(DataType.Date)]
    public DateTime? DateOfBirth { get; set; }
}

/// <summary>Calls the profile-only legacy EmployeeService.</summary>
public interface ILegacyEmployeeClient
{
    /// <summary>Retrieves a bearer-authorized page of employee profiles.</summary>
    Task<PaginatedResponse<EmployeeResponse>?> GetEmployeesAsync(EmployeeSortType sort, string? search, int index, int size, string accessToken, CancellationToken cancellationToken);
    /// <summary>Retrieves one employee profile.</summary>
    Task<EmployeeResponse?> GetEmployeeAsync(int id, string accessToken, CancellationToken cancellationToken);
    /// <summary>Creates a profile without taking ownership of its identity.</summary>
    Task<EmployeeResponse> CreateEmployeeAsync(UpsertEmployeeRequest request, string accessToken, CancellationToken cancellationToken);
    /// <summary>Deletes a profile during a failed cross-service creation rollback.</summary>
    Task DeleteEmployeeAsync(int id, string accessToken, CancellationToken cancellationToken);
}