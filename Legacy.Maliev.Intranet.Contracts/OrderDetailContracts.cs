using System.ComponentModel.DataAnnotations;

namespace Legacy.Maliev.Intranet.Contracts;

/// <summary>Complete browser-safe projection for the legacy order editor.</summary>
public sealed record OrderDetailItem(
    int Id,
    int? CustomerId,
    int? EmployeeId,
    string? Name,
    string? Description,
    int ProcessId,
    int? MaterialId,
    int? SurfaceFinishId,
    int? ColorId,
    int Quantity,
    int Manufactured,
    int? Remaining,
    decimal? UnitPrice,
    decimal? DiscountPercent,
    decimal? Subtotal,
    int? CurrencyId,
    int? LeadTime,
    DateTime? PromisedDate,
    DateTime? FinishedDate,
    int? Turnaround,
    string? Comment,
    bool AllowSocialMedia,
    bool AllowCancellation,
    bool AllowPayment,
    string? TrackingNumber,
    DateTime? CreatedDate,
    DateTime? ModifiedDate);

/// <summary>Validated complete OrderService update payload plus its optimistic-concurrency token.</summary>
public sealed record OrderUpdateRequest(
    [property: Range(1, int.MaxValue)] int? EmployeeId,
    [property: Required, StringLength(256)] string Name,
    [property: StringLength(500)] string? Description,
    [property: Range(1, int.MaxValue)] int ProcessId,
    [property: Range(1, int.MaxValue)] int? MaterialId,
    [property: Range(1, int.MaxValue)] int? SurfaceFinishId,
    [property: Range(1, int.MaxValue)] int? ColorId,
    [property: Range(1, int.MaxValue)] int Quantity,
    [property: Range(0, int.MaxValue)] int Manufactured,
    [property: Range(0, double.MaxValue)] decimal? UnitPrice,
    [property: Range(0, 100)] decimal? DiscountPercent,
    [property: Range(1, int.MaxValue)] int? CurrencyId,
    [property: Range(0, int.MaxValue)] int? LeadTime,
    DateTime? PromisedDate,
    DateTime? FinishedDate,
    [property: StringLength(4_000)] string? Comment,
    bool AllowSocialMedia,
    bool AllowCancellation,
    bool AllowPayment,
    [property: StringLength(256)] string? TrackingNumber,
    DateTime? ModifiedDate);

/// <summary>Small identifier and display label used by the order editor.</summary>
public sealed record OrderLookupItem(int Id, string Name);

/// <summary>Currency identifier and short display label.</summary>
public sealed record OrderCurrencyItem(int Id, string ShortName);

/// <summary>Order status option.</summary>
public sealed record OrderStatusItem(int Id, string? Name, string? Description, DateTime? CreatedDate, DateTime? ModifiedDate);

/// <summary>One historical order status entry.</summary>
public sealed record OrderStatusHistoryItem(int Id, int OrderId, int OrderStatusId, string? Name, string? Description, DateTime? CreatedDate, DateTime? ModifiedDate);

/// <summary>Order-owned file metadata plus a short-lived clean-object URL.</summary>
public sealed record OrderFileItem(int Id, int OrderId, string ObjectName, Uri? Uri);

/// <summary>All read-only data needed to render the editor without exposing service credentials.</summary>
public sealed record OrderDetailPage(
    OrderDetailItem Order,
    IReadOnlyList<OrderLookupItem> Processes,
    IReadOnlyList<OrderLookupItem> Materials,
    IReadOnlyList<OrderLookupItem> Colors,
    IReadOnlyList<OrderLookupItem> SurfaceFinishes,
    IReadOnlyList<OrderCurrencyItem> Currencies,
    IReadOnlyList<OrderLookupItem> Employees,
    OrderStatusItem? CurrentStatus,
    IReadOnlyList<OrderStatusItem> AvailableStatuses,
    IReadOnlyList<OrderStatusHistoryItem> History,
    IReadOnlyList<OrderFileItem> Files);
