namespace Legacy.Maliev.Intranet.Orders;

/// <summary>Order projection owned by OrderService.</summary>
public sealed record OrderResponse(
    int Id, int? CustomerId, int? EmployeeId, string? Name, string? Description, int ProcessId,
    int? MaterialId, int? SurfaceFinishId, int? ColorId, int Quantity, int Manufactured,
    int? Remaining, decimal? UnitPrice, decimal? DiscountPercent, decimal? Subtotal,
    int? CurrencyId, int? LeadTime, DateTime? PromisedDate, DateTime? FinishedDate,
    int? Turnaround, string? Comment, bool AllowSocialMedia, bool AllowCancellation,
    bool AllowPayment, string? TrackingNumber, DateTime? CreatedDate, DateTime? ModifiedDate);

/// <summary>Order process projection owned by OrderService.</summary>
public sealed record ProcessResponse(int Id, int CategoryId, string Name, DateTime? CreatedDate, DateTime? ModifiedDate);

/// <summary>Order table projection enriched with cross-service display labels.</summary>
public sealed record OrderTableModel(
    string Caption,
    IReadOnlyList<OrderResponse> Orders,
    IReadOnlyDictionary<int, string> EmployeeNames,
    IReadOnlyDictionary<int, string> ProcessNames);

/// <summary>Paginated OrderService response.</summary>
public sealed record PaginatedResponse<T>(IReadOnlyList<T> Items, int PageIndex, int TotalPages, int TotalRecords)
{
    /// <summary>Whether another page follows.</summary>
    public bool HasNextPage => PageIndex < TotalPages;
    /// <summary>Whether a prior page exists.</summary>
    public bool HasPreviousPage => PageIndex > 1;
}

/// <summary>Sort names accepted by the migrated OrderService.</summary>
public enum OrderSortType
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

/// <summary>Authenticated OrderService read boundary used by the Intranet BFF.</summary>
public interface ILegacyOrderClient
{
    /// <summary>Gets a filtered order page.</summary>
    Task<PaginatedResponse<OrderResponse>?> GetOrdersAsync(OrderSortType sort, string? search, int index, int size, string token, CancellationToken cancellationToken);
    /// <summary>Gets the bounded working set of pending orders.</summary>
    Task<PaginatedResponse<OrderResponse>?> GetPendingOrdersAsync(int size, string token, CancellationToken cancellationToken);
    /// <summary>Gets process labels used by order projections.</summary>
    Task<IReadOnlyList<ProcessResponse>> GetProcessesAsync(string token, CancellationToken cancellationToken);
}
