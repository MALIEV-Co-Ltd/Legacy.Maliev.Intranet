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

/// <summary>Complete browser-safe invoice projection used by the legacy detail editor.</summary>
public sealed record InvoiceDetail(
    int Id,
    int CustomerId,
    string Number,
    string? Comment,
    string? InternalComment,
    string? SalesPerson,
    string Currency,
    string? PurchaseOrderNumber,
    string? Requisitioner,
    string? ShippedVia,
    string? Fob,
    string? Terms,
    string? BillingAddressRecipient,
    string? BillingAddressCompany,
    string? BillingAddressBuilding,
    string? BillingAddressLine1,
    string? BillingAddressLine2,
    string? BillingAddressCity,
    string? BillingAddressState,
    string? BillingAddressPostalCode,
    string? BillingAddressCountry,
    string? ShippingAddressRecipient,
    string? ShippingAddressRecipientTelephone,
    string? ShippingAddressCompany,
    string? ShippingAddressBuilding,
    string? ShippingAddressLine1,
    string? ShippingAddressLine2,
    string? ShippingAddressCity,
    string? ShippingAddressState,
    string? ShippingAddressPostalCode,
    string? ShippingAddressCountry,
    string? CommercialRegistration,
    string? TaxIdentification,
    decimal? Subtotal,
    decimal? Vat,
    decimal? Total,
    decimal? WithholdingTax,
    decimal? Outstanding,
    bool IsPaid,
    int? ReceiptId,
    DateTime? PaymentDate,
    DateTime? CreatedDate,
    DateTime? ModifiedDate);

/// <summary>Browser-safe invoice line item.</summary>
public sealed record InvoiceOrderItem(
    int Id,
    int? InvoiceId,
    string? Description,
    int? Quantity,
    decimal? UnitPrice,
    decimal? Subtotal,
    DateTime? CreatedDate,
    DateTime? ModifiedDate);

/// <summary>Short-lived clean-object link without storage credentials or bucket identity.</summary>
public sealed record InvoiceDownload(int Id, int OwnerId, string ObjectName, DateTime? CreatedDate, Uri? Uri);

/// <summary>Complete Invoice View response assembled by the same-origin BFF.</summary>
public sealed record InvoiceDetailPage(
    InvoiceDetail Invoice,
    IReadOnlyList<InvoiceOrderItem> OrderItems,
    IReadOnlyList<InvoiceDownload> InvoiceFiles,
    IReadOnlyList<InvoiceDownload> ReceiptFiles);

/// <summary>Only fields the historical Invoice View allowed an employee to change.</summary>
public sealed record InvoiceUpdateRequest(
    bool IsPaid,
    DateTime? PaymentDate,
    string? InternalComment,
    DateTime ModifiedDate);
