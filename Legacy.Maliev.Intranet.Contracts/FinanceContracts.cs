namespace Legacy.Maliev.Intranet.Contracts;

/// <summary>Legacy payment sort values preserved by the Finance index route.</summary>
public enum FinancePaymentSort
{
    /// <summary>Newest created payment first.</summary>
    PaymentCreatedDate_Descending,
    /// <summary>Oldest created payment first.</summary>
    PaymentCreatedDate_Ascending,
    /// <summary>Payment identifier ascending.</summary>
    PaymentId_Ascending,
    /// <summary>Payment identifier descending.</summary>
    PaymentId_Descending,
    /// <summary>Payment date ascending.</summary>
    PaymentDate_Ascending,
    /// <summary>Payment date descending.</summary>
    PaymentDate_Descending,
    /// <summary>Direction ascending.</summary>
    PaymentDirection_Ascending,
    /// <summary>Direction descending.</summary>
    PaymentDirection_Descending,
    /// <summary>Type ascending.</summary>
    PaymentType_Ascending,
    /// <summary>Type descending.</summary>
    PaymentType_Descending,
    /// <summary>Method ascending.</summary>
    PaymentMethod_Ascending,
    /// <summary>Method descending.</summary>
    PaymentMethod_Descending,
    /// <summary>Recipient ascending.</summary>
    Recipient_Ascending,
    /// <summary>Recipient descending.</summary>
    Recipient_Descending,
}

/// <summary>Browser-safe payment row with no service navigation properties.</summary>
public sealed record FinancePaymentItem(
    int Id,
    int? EmployeeId,
    int PaymentDirectionId,
    int PaymentTypeId,
    string? Description,
    int PaymentMethodId,
    decimal Amount,
    int? CurrencyId,
    string? Recipient,
    string? TransactionNumber,
    DateTime? PaymentDate,
    DateTime? CreatedDate,
    DateTime? ModifiedDate);

/// <summary>Browser-safe page of legacy payments.</summary>
public sealed record FinancePaymentPage(
    IReadOnlyList<FinancePaymentItem> Items,
    int PageIndex,
    int TotalPages,
    int TotalRecords,
    bool HasNextPage,
    bool HasPreviousPage);

/// <summary>One currency-specific amount and period delta.</summary>
public sealed record FinanceSummaryDetail(
    string CurrencyId,
    decimal CurrentAmount,
    decimal PreviousAmount,
    decimal DeltaAmount,
    decimal DeltaPercent);

/// <summary>Finance summary projected from AccountingService.</summary>
public sealed record FinanceSummary(IReadOnlyList<FinanceSummaryDetail> Details);

/// <summary>Browser-safe Finance lookup value.</summary>
public sealed record FinanceLookupItem(int Id, string Name);

/// <summary>Browser-safe payment-file projection with an optional short-lived clean-object URL.</summary>
public sealed record FinanceFileItem(
    int Id,
    int PaymentId,
    string Bucket,
    string ObjectName,
    DateTime? CreatedDate,
    Uri? Uri);

/// <summary>Complete browser-safe projection for the legacy Finance editor.</summary>
public sealed record FinanceDetailPage(
    FinancePaymentItem Payment,
    IReadOnlyList<FinanceLookupItem> Employees,
    IReadOnlyList<FinanceLookupItem> Directions,
    IReadOnlyList<FinanceLookupItem> Types,
    IReadOnlyList<FinanceLookupItem> Methods,
    IReadOnlyList<CatalogCurrency> Currencies,
    IReadOnlyList<FinanceFileItem> Files);

/// <summary>Validated Finance editor write contract.</summary>
public sealed record FinancePaymentUpdateRequest(
    int? EmployeeId,
    int PaymentDirectionId,
    int PaymentTypeId,
    string? Description,
    int PaymentMethodId,
    decimal Amount,
    int? CurrencyId,
    string? Recipient,
    string? TransactionNumber,
    DateTime? PaymentDate,
    DateTime? ModifiedDate);

/// <summary>Lookup projection for creating a Finance payment.</summary>
public sealed record FinanceCreatePage(
    IReadOnlyList<FinanceLookupItem> Employees,
    IReadOnlyList<FinanceLookupItem> Directions,
    IReadOnlyList<FinanceLookupItem> Types,
    IReadOnlyList<FinanceLookupItem> Methods,
    IReadOnlyList<CatalogCurrency> Currencies);

/// <summary>Validated create-payment payload.</summary>
public sealed record FinancePaymentCreateRequest(
    int EmployeeId,
    int PaymentDirectionId,
    int PaymentTypeId,
    string Description,
    int PaymentMethodId,
    decimal Amount,
    int CurrencyId,
    string? Recipient,
    string? TransactionNumber,
    DateTime PaymentDate);

/// <summary>Result of a completed Finance creation workflow.</summary>
public sealed record FinancePaymentCreatedResult(int Id);
