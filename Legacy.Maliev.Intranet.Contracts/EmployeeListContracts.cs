namespace Legacy.Maliev.Intranet.Contracts;

/// <summary>Legacy-compatible employee list sort values exposed to the browser.</summary>
public enum EmployeeListSort
{
    EmployeeId_Ascending,
    EmployeeId_Descending,
    EmployeeEmail_Ascending,
    EmployeeEmail_Descending,
}

/// <summary>Browser-safe role projection embedded in an employee row.</summary>
public sealed record EmployeeRoleListItem(int Id, string? Name);

/// <summary>Browser-safe employee row matching the legacy table contract.</summary>
public sealed record EmployeeListItem(
    int Id,
    string FirstName,
    string LastName,
    string FullName,
    string Email,
    EmployeeRoleListItem? Role);

/// <summary>Browser-safe legacy-compatible page of employee profiles.</summary>
public sealed record EmployeeListPage(
    IReadOnlyList<EmployeeListItem> Items,
    int PageIndex,
    int TotalPages,
    int TotalRecords,
    bool HasNextPage,
    bool HasPreviousPage);
