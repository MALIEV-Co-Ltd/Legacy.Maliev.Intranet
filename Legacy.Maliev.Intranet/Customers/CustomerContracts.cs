using System.ComponentModel.DataAnnotations;

namespace Legacy.Maliev.Intranet.Customers;

/// <summary>Legacy customer profile returned by CustomerService.</summary>
public sealed record CustomerResponse(
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
    AddressResponse? BillingAddress,
    CompanyResponse? Company,
    AddressResponse? ShippingAddress);

/// <summary>Legacy company projection embedded in a customer response.</summary>
public sealed record CompanyResponse(int Id, string Name, string? TaxNumber, string? Registrar, DateTime? CreatedDate, DateTime? ModifiedDate);

/// <summary>Legacy address projection embedded in a customer response.</summary>
public sealed record AddressResponse(
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

/// <summary>Customer profile write contract owned by CustomerService.</summary>
public sealed record UpsertCustomerRequest(
    string FirstName,
    string LastName,
    string? Telephone,
    string? Mobile,
    string? Fax,
    string Email,
    DateTime? DateOfBirth,
    int? CompanyId = null,
    int? BillingAddressId = null,
    int? ShippingAddressId = null);

/// <summary>Legacy paginated response shape.</summary>
public sealed record PaginatedResponse<T>(IReadOnlyList<T> Items, int PageIndex, int TotalPages, int TotalRecords)
{
    /// <summary>Whether another page follows this page.</summary>
    public bool HasNextPage => PageIndex < TotalPages;

    /// <summary>Whether a page precedes this page.</summary>
    public bool HasPreviousPage => PageIndex > 1;
}

/// <summary>Legacy customer sort names.</summary>
public enum CustomerSortType
{
    /// <summary>Orders profiles by identifier from lowest to highest.</summary>
    CustomerId_Ascending,
    /// <summary>Orders profiles by identifier from highest to lowest.</summary>
    CustomerId_Descending,
    /// <summary>Orders profiles alphabetically by company name.</summary>
    CustomerCompany_Ascending,
    /// <summary>Orders profiles in reverse alphabetical company order.</summary>
    CustomerCompany_Descending,
    /// <summary>Orders profiles alphabetically by email address.</summary>
    CustomerEmail_Ascending,
    /// <summary>Orders profiles in reverse alphabetical email order.</summary>
    CustomerEmail_Descending,
    /// <summary>Orders profiles from earliest to latest creation time.</summary>
    CustomerCreatedDate_Ascending,
    /// <summary>Orders profiles from latest to earliest creation time.</summary>
    CustomerCreatedDate_Descending,
    /// <summary>Orders profiles from earliest to latest modification time.</summary>
    CustomerModifiedDate_Ascending,
    /// <summary>Orders profiles from latest to earliest modification time.</summary>
    CustomerModifiedDate_Descending,
}

/// <summary>Validated form fields for the profile and identity transaction.</summary>
public sealed class CreateCustomerInput
{
    /// <summary>The customer's given name used by the legacy profile.</summary>
    [Required, StringLength(256)]
    public string FirstName { get; set; } = string.Empty;

    /// <summary>The customer's family name used by the legacy profile.</summary>
    [Required, StringLength(256)]
    public string LastName { get; set; } = string.Empty;

    /// <summary>The email used for both profile correspondence and account login.</summary>
    [Required, EmailAddress, StringLength(320)]
    public string Email { get; set; } = string.Empty;

    /// <summary>The initial account password sent only to AuthService.</summary>
    [Required, StringLength(1024, MinimumLength = 6), DataType(DataType.Password)]
    public string Password { get; set; } = string.Empty;

    /// <summary>The repeated password used only for server-side form validation.</summary>
    [Required, Compare(nameof(Password)), DataType(DataType.Password)]
    public string ConfirmPassword { get; set; } = string.Empty;

    /// <summary>The customer's optional primary telephone number.</summary>
    [Phone, StringLength(64)]
    public string? Telephone { get; set; }

    /// <summary>The customer's optional mobile telephone number.</summary>
    [StringLength(64)]
    public string? Mobile { get; set; }

    /// <summary>The customer's optional fax number retained for compatibility.</summary>
    [StringLength(64)]
    public string? Fax { get; set; }

    /// <summary>The customer's optional date of birth.</summary>
    [DataType(DataType.Date)]
    public DateTime? DateOfBirth { get; set; }
}

/// <summary>Calls the profile-only legacy CustomerService.</summary>
public interface ILegacyCustomerClient
{
    /// <summary>Retrieves a bearer-authorized page of legacy customer profiles.</summary>
    /// <param name="sort">The legacy profile ordering.</param>
    /// <param name="search">The optional customer search text.</param>
    /// <param name="index">The one-based page index.</param>
    /// <param name="size">The bounded page size.</param>
    /// <param name="accessToken">The server-side employee access token.</param>
    /// <param name="cancellationToken">Token to cancel the asynchronous operation.</param>
    /// <returns>The matching page, or <see langword="null"/> when no page exists.</returns>
    Task<PaginatedResponse<CustomerResponse>?> GetCustomersAsync(CustomerSortType sort, string? search, int index, int size, string accessToken, CancellationToken cancellationToken);

    /// <summary>Retrieves one legacy customer profile.</summary>
    /// <param name="id">The legacy customer identifier.</param>
    /// <param name="accessToken">The server-side employee access token.</param>
    /// <param name="cancellationToken">Token to cancel the asynchronous operation.</param>
    /// <returns>The matching customer, or <see langword="null"/> when absent.</returns>
    Task<CustomerResponse?> GetCustomerAsync(int id, string accessToken, CancellationToken cancellationToken);

    /// <summary>Creates a profile without taking ownership of its authentication identity.</summary>
    /// <param name="request">The customer profile fields.</param>
    /// <param name="accessToken">The server-side employee access token.</param>
    /// <param name="cancellationToken">Token to cancel the asynchronous operation.</param>
    /// <returns>The created customer profile.</returns>
    Task<CustomerResponse> CreateCustomerAsync(UpsertCustomerRequest request, string accessToken, CancellationToken cancellationToken);

    /// <summary>Deletes a profile during a failed cross-service creation rollback.</summary>
    /// <param name="id">The legacy customer identifier.</param>
    /// <param name="accessToken">The server-side employee access token.</param>
    /// <param name="cancellationToken">Token to cancel the asynchronous operation.</param>
    /// <returns>A task representing the asynchronous delete.</returns>
    Task DeleteCustomerAsync(int id, string accessToken, CancellationToken cancellationToken);
}