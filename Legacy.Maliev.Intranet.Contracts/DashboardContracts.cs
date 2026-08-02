namespace Legacy.Maliev.Intranet.Contracts;

/// <summary>One permission-scoped dashboard card projected by the legacy BFF.</summary>
public sealed record LegacyDashboardCard(
    string Key,
    int Count,
    string NavigateTo);

/// <summary>
/// Browser-safe legacy dashboard projection. Counts are fetched only for the
/// employee permissions carried by the server-side session; downstream errors
/// are isolated in <see cref="DegradedSources"/>.
/// </summary>
public sealed record LegacyDashboardSnapshot(
    DateTimeOffset GeneratedAt,
    IReadOnlyList<LegacyDashboardCard> Cards,
    IReadOnlyList<string> DegradedSources)
{
    /// <summary>Newest orders returned by OrderService for the employee's authorized workspace.</summary>
    public IReadOnlyList<LegacyDashboardOrder> RecentOrders { get; init; } = [];

    /// <summary>Newest quotations returned by QuotationService for the employee's authorized workspace.</summary>
    public IReadOnlyList<LegacyDashboardQuotation> RecentQuotations { get; init; } = [];

    /// <summary>Newest customer profiles returned by CustomerService for authorized employees.</summary>
    public IReadOnlyList<LegacyDashboardCustomer> RecentCustomers { get; init; } = [];

    /// <summary>Newest accounting records returned by AccountingService for authorized employees.</summary>
    public IReadOnlyList<LegacyDashboardPayment> RecentPayments { get; init; } = [];

    /// <summary>Recent legacy-owned business events assembled without exposing service credentials.</summary>
    public IReadOnlyList<LegacyDashboardActivity> RecentActivity { get; init; } = [];

    /// <summary>Service-owned quotation decision totals, when the employee can read quotations.</summary>
    public QuotationStats? QuotationSummary { get; init; }

    /// <summary>AccountingService-owned monthly amounts grouped by currency.</summary>
    public IReadOnlyList<FinanceSummaryDetail> MonthlyFinance { get; init; } = [];
}

/// <summary>Bounded order row used by the operations dashboard.</summary>
public sealed record LegacyDashboardOrder(
    int Id,
    string? Name,
    int Quantity,
    int Manufactured,
    int? Remaining,
    DateTime? PromisedDate,
    string NavigateTo);

/// <summary>Bounded quotation row used by the sales dashboard.</summary>
public sealed record LegacyDashboardQuotation(
    int Id,
    decimal Total,
    decimal? QuotedAmount,
    int CurrencyId,
    DateTime ExpirationDate,
    bool? Accepted,
    DateTime? CreatedDate,
    string NavigateTo);

/// <summary>Bounded customer row retained from the original legacy dashboard.</summary>
public sealed record LegacyDashboardCustomer(
    int Id,
    string FullName,
    string Email,
    string? Company,
    string NavigateTo);

/// <summary>Bounded payment row retained from the original legacy dashboard.</summary>
public sealed record LegacyDashboardPayment(
    int Id,
    decimal Amount,
    int? CurrencyId,
    string? Recipient,
    DateTime? PaymentDate,
    DateTime? CreatedDate,
    string NavigateTo);

/// <summary>One timestamped, legacy-owned activity visible to the current employee.</summary>
public sealed record LegacyDashboardActivity(
    string Kind,
    string? Title,
    string State,
    DateTime? OccurredAt,
    string NavigateTo);
