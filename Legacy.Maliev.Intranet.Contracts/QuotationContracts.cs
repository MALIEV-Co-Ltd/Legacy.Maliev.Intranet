namespace Legacy.Maliev.Intranet.Contracts;

/// <summary>Legacy quotation sort values exposed to the browser.</summary>
public enum QuotationListSort
{
    QuotationId_Ascending,
    QuotationId_Descending,
    QuotationCreatedDate_Ascending,
    QuotationCreatedDate_Descending,
    QuotationModifiedDate_Ascending,
    QuotationModifiedDate_Descending,
}

/// <summary>Browser-safe quotation list row with deterministic financial values.</summary>
public sealed record QuotationListItem(
    int Id,
    int? CustomerId,
    int? EmployeeId,
    int? InvoiceId,
    int Period,
    DateTime ExpirationDate,
    decimal Subtotal,
    decimal Vat,
    decimal Total,
    decimal? WithholdingTax,
    decimal? QuotedAmount,
    int CurrencyId,
    string? Comment,
    string? Fob,
    string? ShippedVia,
    string? Terms,
    bool? Accepted,
    DateTime? CreatedDate,
    DateTime? ModifiedDate);

/// <summary>Browser-safe page of legacy quotations.</summary>
public sealed record QuotationListPage(
    IReadOnlyList<QuotationListItem> Items,
    int PageIndex,
    int TotalPages,
    int TotalRecords,
    bool HasNextPage,
    bool HasPreviousPage);

/// <summary>Accepted, declined, and open quotation counts.</summary>
public sealed record QuotationStats(int Accepted, int Declined, int Open);
