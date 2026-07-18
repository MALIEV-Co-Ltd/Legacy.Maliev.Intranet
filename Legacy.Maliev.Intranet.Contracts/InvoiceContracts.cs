namespace Legacy.Maliev.Intranet.Contracts;

/// <summary>Legacy invoice sort values exposed to the browser.</summary>
public enum InvoiceListSort
{
    InvoiceId_Ascending,
    InvoiceId_Descending,
    InvoiceCreatedDate_Ascending,
    InvoiceCreatedDate_Descending,
    InvoicePaymentDate_Ascending,
    InvoicePaymentDate_Descending,
}

/// <summary>Browser-safe invoice row with no persistence navigation properties.</summary>
public sealed record InvoiceListItem(
    int Id,
    int CustomerId,
    string Number,
    string Currency,
    string? PurchaseOrderNumber,
    decimal? Subtotal,
    decimal? Vat,
    decimal? Total,
    decimal? WithholdingTax,
    decimal? Outstanding,
    bool IsPaid,
    int? ReceiptId,
    DateTime? PaymentDate,
    DateTime? CreatedDate);

/// <summary>Browser-safe legacy-compatible page of invoices.</summary>
public sealed record InvoiceListPage(
    IReadOnlyList<InvoiceListItem> Items,
    int PageIndex,
    int TotalPages,
    int TotalRecords,
    bool HasNextPage,
    bool HasPreviousPage);
