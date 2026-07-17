namespace Legacy.Maliev.Intranet.Contracts;

/// <summary>Legacy-compatible customer list sort values exposed to the browser.</summary>
public enum CustomerListSort
{
    CustomerId_Ascending,
    CustomerId_Descending,
    CustomerCompany_Ascending,
    CustomerCompany_Descending,
    CustomerEmail_Ascending,
    CustomerEmail_Descending,
    CustomerCreatedDate_Ascending,
    CustomerCreatedDate_Descending,
    CustomerModifiedDate_Ascending,
    CustomerModifiedDate_Descending,
}

/// <summary>Browser-safe company projection embedded in a customer row.</summary>
public sealed record CustomerCompanyListItem(int Id, string Name);

/// <summary>Browser-safe customer row matching the legacy table contract.</summary>
public sealed record CustomerListItem(
    int Id,
    string FirstName,
    string LastName,
    string FullName,
    string Email,
    CustomerCompanyListItem? Company);

/// <summary>Browser-safe legacy-compatible page of customer profiles.</summary>
public sealed record CustomerListPage(
    IReadOnlyList<CustomerListItem> Items,
    int PageIndex,
    int TotalPages,
    int TotalRecords,
    bool HasNextPage,
    bool HasPreviousPage);
