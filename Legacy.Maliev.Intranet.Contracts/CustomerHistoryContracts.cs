namespace Legacy.Maliev.Intranet.Contracts;

/// <summary>Customer-owned record families represented in the activity feed.</summary>
public enum CustomerHistoryKind
{
    /// <summary>An OrderService order.</summary>
    Order,
    /// <summary>A QuotationService quotation.</summary>
    Quotation,
    /// <summary>An AccountingService invoice.</summary>
    Invoice,
}

/// <summary>Availability of one permission-scoped customer history source.</summary>
public enum CustomerHistorySourceState
{
    /// <summary>The authorized source returned a valid page.</summary>
    Available,
    /// <summary>The employee or downstream session cannot read the source.</summary>
    Forbidden,
    /// <summary>The downstream source rate-limited the request.</summary>
    RateLimited,
    /// <summary>The downstream source could not serve the request.</summary>
    Unavailable,
    /// <summary>The downstream source returned an invalid browser-safe projection.</summary>
    InvalidResponse,
}

/// <summary>Typed, localization-independent state of one customer activity record.</summary>
public enum CustomerActivityStatus
{
    /// <summary>An order has remaining units.</summary>
    InProgress,
    /// <summary>An order has manufactured all requested units.</summary>
    Complete,
    /// <summary>A quotation has not been accepted or declined.</summary>
    Open,
    /// <summary>A quotation was accepted.</summary>
    Accepted,
    /// <summary>A quotation was declined.</summary>
    Declined,
    /// <summary>An invoice was paid.</summary>
    Paid,
    /// <summary>An invoice remains outstanding.</summary>
    Outstanding,
}

/// <summary>Safe availability and count metadata for one activity source.</summary>
public sealed record CustomerHistorySourceSummary(CustomerHistorySourceState State, int? TotalRecords);

/// <summary>One timestamped customer-owned record projected for browser display.</summary>
public sealed record CustomerActivityItem(
    CustomerHistoryKind Kind,
    int Id,
    string? Label,
    CustomerActivityStatus Status,
    int? CompletedUnits,
    int? TotalUnits,
    decimal? Amount,
    string? Currency,
    DateTime Timestamp);

/// <summary>A bounded activity feed plus independent source availability.</summary>
public sealed record CustomerActivityPage(
    IReadOnlyList<CustomerActivityItem> Items,
    CustomerHistorySourceSummary Orders,
    CustomerHistorySourceSummary Quotations,
    CustomerHistorySourceSummary Invoices);
