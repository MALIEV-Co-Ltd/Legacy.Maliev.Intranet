using System.ComponentModel.DataAnnotations;

namespace Legacy.Maliev.Intranet.Orders;

/// <summary>Order projection owned by OrderService.</summary>
public sealed record OrderResponse(
    int Id, int? CustomerId, int? EmployeeId, string? Name, string? Description, int ProcessId,
    int? MaterialId, int? SurfaceFinishId, int? ColorId, int Quantity, int Manufactured,
    int? Remaining, decimal? UnitPrice, decimal? DiscountPercent, decimal? Subtotal,
    int? CurrencyId, int? LeadTime, DateTime? PromisedDate, DateTime? FinishedDate,
    int? Turnaround, string? Comment, bool AllowSocialMedia, bool AllowCancellation,
    bool AllowPayment, string? TrackingNumber, DateTime? CreatedDate, DateTime? ModifiedDate);

/// <summary>Complete OrderService write payload.</summary>
public sealed record UpsertOrderRequest(
    int? CustomerId, int? EmployeeId, string? Name, string? Description, int ProcessId,
    int? MaterialId, int? SurfaceFinishId, int? ColorId, int Quantity, int Manufactured,
    decimal? UnitPrice, decimal? DiscountPercent, int? CurrencyId, int? LeadTime,
    DateTime? PromisedDate, DateTime? FinishedDate, string? Comment, bool AllowSocialMedia,
    bool AllowCancellation, bool AllowPayment, string? TrackingNumber);

/// <summary>Order-owned clean cloud-object metadata.</summary>
public sealed record OrderFileResponse(int Id, int OrderId, string Bucket, string ObjectName, DateTime? CreatedDate = null, DateTime? ModifiedDate = null);

/// <summary>Order status projection.</summary>
public sealed record OrderStatusResponse(int Id, string? Name, string? Description, DateTime? CreatedDate, DateTime? ModifiedDate);

/// <summary>Order status history projection.</summary>
public sealed record OrderStatusHistoryResponse(int Id, int OrderId, int OrderStatusId, string? Name, string? Description, DateTime? CreatedDate, DateTime? ModifiedDate);

/// <summary>Validated browser input mapped explicitly to the OrderService payload.</summary>
public sealed class OrderInput
{
    /// <summary>Customer identifier.</summary>
    [Range(1, int.MaxValue)] public int CustomerId { get; set; }
    /// <summary>Assigned employee identifier.</summary>
    [Range(1, int.MaxValue)] public int? EmployeeId { get; set; }
    /// <summary>Order name.</summary>
    [Required, StringLength(256)] public string Name { get; set; } = string.Empty;
    /// <summary>Manufacturing requirements.</summary>
    [StringLength(500)] public string? Description { get; set; }
    /// <summary>Process identifier.</summary>
    [Range(1, int.MaxValue)] public int ProcessId { get; set; }
    /// <summary>Material identifier.</summary>
    [Range(1, int.MaxValue)] public int? MaterialId { get; set; }
    /// <summary>Surface finish identifier.</summary>
    [Range(1, int.MaxValue)] public int? SurfaceFinishId { get; set; }
    /// <summary>Color identifier.</summary>
    [Range(1, int.MaxValue)] public int? ColorId { get; set; }
    /// <summary>Requested quantity.</summary>
    [Range(1, int.MaxValue)] public int Quantity { get; set; } = 1;
    /// <summary>Manufactured quantity.</summary>
    [Range(0, int.MaxValue)] public int Manufactured { get; set; }
    /// <summary>Unit price.</summary>
    [Range(0, double.MaxValue)] public decimal? UnitPrice { get; set; }
    /// <summary>Discount percentage.</summary>
    [Range(0, 100)] public decimal? DiscountPercent { get; set; }
    /// <summary>Currency identifier.</summary>
    [Range(1, int.MaxValue)] public int? CurrencyId { get; set; }
    /// <summary>Lead time in days.</summary>
    [Range(0, int.MaxValue)] public int? LeadTime { get; set; }
    /// <summary>Promised completion date.</summary>
    [DataType(DataType.Date)] public DateTime? PromisedDate { get; set; }
    /// <summary>Actual completion date.</summary>
    [DataType(DataType.Date)] public DateTime? FinishedDate { get; set; }
    /// <summary>Internal comment.</summary>
    [StringLength(4_000)] public string? Comment { get; set; }
    /// <summary>Whether approved content may be shared.</summary>
    public bool AllowSocialMedia { get; set; } = true;
    /// <summary>Whether the customer may cancel.</summary>
    public bool AllowCancellation { get; set; } = true;
    /// <summary>Whether online payment is enabled.</summary>
    public bool AllowPayment { get; set; }
    /// <summary>Shipment tracking number.</summary>
    [StringLength(256)] public string? TrackingNumber { get; set; }
    /// <summary>Whether to send the optional order-created message.</summary>
    public bool SendConfirmationEmail { get; set; }
    /// <summary>Concurrency token returned by OrderService.</summary>
    public DateTime? ModifiedDate { get; set; }

    /// <summary>Maps the validated form to the complete API payload.</summary>
    public UpsertOrderRequest ToRequest() => new(
        CustomerId, EmployeeId, Name, Description, ProcessId, MaterialId, SurfaceFinishId,
        ColorId, Quantity, Manufactured, UnitPrice, DiscountPercent, CurrencyId, LeadTime,
        PromisedDate, FinishedDate, Comment, AllowSocialMedia, AllowCancellation, AllowPayment,
        TrackingNumber);

    /// <summary>Creates an editor from an OrderService projection.</summary>
    public static OrderInput From(OrderResponse value) => new()
    {
        CustomerId = value.CustomerId ?? 0,
        EmployeeId = value.EmployeeId,
        Name = value.Name ?? string.Empty,
        Description = value.Description,
        ProcessId = value.ProcessId,
        MaterialId = value.MaterialId,
        SurfaceFinishId = value.SurfaceFinishId,
        ColorId = value.ColorId,
        Quantity = value.Quantity,
        Manufactured = value.Manufactured,
        UnitPrice = value.UnitPrice,
        DiscountPercent = value.DiscountPercent,
        CurrencyId = value.CurrencyId,
        LeadTime = value.LeadTime,
        PromisedDate = value.PromisedDate,
        FinishedDate = value.FinishedDate,
        Comment = value.Comment,
        AllowSocialMedia = value.AllowSocialMedia,
        AllowCancellation = value.AllowCancellation,
        AllowPayment = value.AllowPayment,
        TrackingNumber = value.TrackingNumber,
        ModifiedDate = value.ModifiedDate,
    };
}

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
    /// <summary>Gets one order.</summary>
    Task<OrderResponse?> GetOrderAsync(int id, string token, CancellationToken cancellationToken);
    /// <summary>Creates an idempotent order.</summary>
    Task<OrderResponse> CreateOrderAsync(UpsertOrderRequest request, string token, CancellationToken cancellationToken);
    /// <summary>Updates an order using optimistic concurrency.</summary>
    Task UpdateOrderAsync(int id, UpsertOrderRequest request, DateTimeOffset? expectedModifiedDate, string token, CancellationToken cancellationToken);
    /// <summary>Deletes one order.</summary>
    Task DeleteOrderAsync(int id, string token, CancellationToken cancellationToken);
    /// <summary>Creates the initial New status.</summary>
    Task CreateNewOrderStatusAsync(int orderId, string token, CancellationToken cancellationToken);
    /// <summary>Gets the latest status.</summary>
    Task<OrderStatusResponse?> GetLatestStatusAsync(int orderId, string token, CancellationToken cancellationToken);
    /// <summary>Gets the status history.</summary>
    Task<IReadOnlyList<OrderStatusHistoryResponse>> GetStatusHistoryAsync(int orderId, string token, CancellationToken cancellationToken);
    /// <summary>Gets permitted next statuses.</summary>
    Task<IReadOnlyList<OrderStatusResponse>> GetAvailableStatusesAsync(int currentStatusId, string token, CancellationToken cancellationToken);
    /// <summary>Transitions to a permitted status idempotently.</summary>
    Task TransitionOrderAsync(int orderId, int statusId, string token, CancellationToken cancellationToken);
    /// <summary>Gets order file metadata.</summary>
    Task<IReadOnlyList<OrderFileResponse>> GetOrderFilesAsync(int orderId, string token, CancellationToken cancellationToken);
    /// <summary>Links a clean cloud object to an order.</summary>
    Task<OrderFileResponse> CreateOrderFileAsync(int orderId, string bucket, string objectName, string token, CancellationToken cancellationToken);
    /// <summary>Deletes order file metadata.</summary>
    Task DeleteOrderFileAsync(int fileId, string token, CancellationToken cancellationToken);
}
