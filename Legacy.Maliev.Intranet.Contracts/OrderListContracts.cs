namespace Legacy.Maliev.Intranet.Contracts;

/// <summary>Legacy-compatible order list sort values exposed to the browser.</summary>
public enum OrderListSort
{
    /// <summary>Identifier ascending.</summary>
    OrderId_Ascending,
    /// <summary>Identifier descending.</summary>
    OrderId_Descending,
    /// <summary>Creation date ascending.</summary>
    OrderCreatedDate_Ascending,
    /// <summary>Creation date descending.</summary>
    OrderCreatedDate_Descending,
    /// <summary>Modification date ascending.</summary>
    OrderModifiedDate_Ascending,
    /// <summary>Modification date descending.</summary>
    OrderModifiedDate_Descending,
}

/// <summary>Browser-safe order row containing only fields displayed by the legacy index.</summary>
public sealed record OrderListItem(
    int Id,
    int? CustomerId,
    int? EmployeeId,
    string? Name,
    int ProcessId,
    int Quantity,
    int Manufactured,
    int? Remaining,
    decimal? Subtotal,
    DateTime? PromisedDate,
    bool AllowSocialMedia);

/// <summary>Browser-safe legacy-compatible page of orders.</summary>
public sealed record OrderListPage(
    IReadOnlyList<OrderListItem> Items,
    int PageIndex,
    int TotalPages,
    int TotalRecords,
    bool HasNextPage,
    bool HasPreviousPage);

/// <summary>Browser-safe process label returned by OrderService.</summary>
public sealed record OrderProcessItem(
    int Id,
    int CategoryId,
    string Name,
    DateTime? CreatedDate,
    DateTime? ModifiedDate);
