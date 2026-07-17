namespace Legacy.Maliev.Intranet.Contracts;

/// <summary>Legacy-compatible purchase-order list sort values exposed to the browser.</summary>
public enum PurchaseOrderListSort
{
    /// <summary>Identifier ascending.</summary>
    PurchaseOrderId_Ascending,
    /// <summary>Identifier descending.</summary>
    PurchaseOrderId_Descending,
    /// <summary>Creation date ascending.</summary>
    PurchaseOrderCreatedDate_Ascending,
    /// <summary>Creation date descending.</summary>
    PurchaseOrderCreatedDate_Descending,
}

/// <summary>Browser-safe purchase-order row containing only fields displayed by the legacy index.</summary>
public sealed record PurchaseOrderListItem(
    int Id,
    int? EmployeeId,
    string? Fob,
    string? Terms,
    string? ShippingMethod,
    DateTime? CreatedDate);

/// <summary>Browser-safe legacy-compatible page of purchase orders.</summary>
public sealed record PurchaseOrderListPage(
    IReadOnlyList<PurchaseOrderListItem> Items,
    int PageIndex,
    int TotalPages,
    int TotalRecords,
    bool HasNextPage,
    bool HasPreviousPage);
