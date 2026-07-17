namespace Legacy.Maliev.Intranet.Contracts;

/// <summary>Legacy-compatible supplier list sort values exposed to the browser.</summary>
public enum SupplierListSort
{
    /// <summary>Identifier ascending.</summary>
    SupplierId_Ascending,
    /// <summary>Identifier descending.</summary>
    SupplierId_Descending,
    /// <summary>Name ascending.</summary>
    SupplierName_Ascending,
    /// <summary>Name descending.</summary>
    SupplierName_Descending,
    /// <summary>Creation date ascending.</summary>
    SupplierCreatedDate_Ascending,
    /// <summary>Creation date descending.</summary>
    SupplierCreatedDate_Descending,
    /// <summary>Modification date ascending.</summary>
    SupplierModifiedDate_Ascending,
    /// <summary>Modification date descending.</summary>
    SupplierModifiedDate_Descending,
}

/// <summary>Browser-safe supplier row containing only fields displayed by the legacy index.</summary>
public sealed record SupplierListItem(
    int Id,
    string? Name,
    string? Email,
    string? Telephone);

/// <summary>Browser-safe legacy-compatible page of suppliers.</summary>
public sealed record SupplierListPage(
    IReadOnlyList<SupplierListItem> Items,
    int PageIndex,
    int TotalPages,
    int TotalRecords,
    bool HasNextPage,
    bool HasPreviousPage);
