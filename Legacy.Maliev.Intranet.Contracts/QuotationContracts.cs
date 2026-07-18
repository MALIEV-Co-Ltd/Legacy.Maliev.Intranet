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

/// <summary>Customer contact fields displayed by the read-only quotation detail.</summary>
public sealed record QuotationCustomer(int Id, string FullName, string Email, string? Telephone, string? Mobile, string? Fax);

/// <summary>Employee attribution displayed by the read-only quotation detail.</summary>
public sealed record QuotationEmployee(int Id, string FullName, string Email);

/// <summary>Currency label owned by CatalogService.</summary>
public sealed record QuotationCurrency(int Id, string ShortName, string LongName);

/// <summary>Invoice label owned by AccountingService.</summary>
public sealed record QuotationInvoice(int Id, string Number);

/// <summary>Read-only order relationship owned by QuotationService.</summary>
public sealed record QuotationOrderLink(int Id, int QuotationId, int OrderId, DateTime? CreatedDate);

/// <summary>Read-only clean quotation document link.</summary>
public sealed record QuotationFile(int Id, int QuotationId, string ObjectName, DateTime? CreatedDate, Uri? Uri);

/// <summary>Complete browser-safe read-only quotation detail projection.</summary>
public sealed record QuotationDetailPage(
    QuotationListItem Quotation,
    QuotationCustomer? Customer,
    QuotationEmployee? Employee,
    QuotationCurrency Currency,
    QuotationInvoice? Invoice,
    IReadOnlyList<QuotationOrderLink> Orders,
    IReadOnlyList<QuotationFile> Files);

/// <summary>One browser-edited quotation line; authoritative totals are calculated server-side.</summary>
public sealed record QuotationCreateLine(
    [property: System.ComponentModel.DataAnnotations.Range(1, int.MaxValue)] int? OrderId,
    [property: System.ComponentModel.DataAnnotations.Required, System.ComponentModel.DataAnnotations.StringLength(2000)] string Description,
    [property: System.ComponentModel.DataAnnotations.Range(1, int.MaxValue)] int Quantity,
    [property: System.ComponentModel.DataAnnotations.Range(typeof(decimal), "0", "9999999999999999")] decimal UnitPrice,
    [property: System.ComponentModel.DataAnnotations.Range(typeof(decimal), "0", "100")] decimal DiscountPercent);

/// <summary>Validated quotation input accepted from the same-origin browser.</summary>
public sealed record QuotationCreateRequest(
    [property: System.ComponentModel.DataAnnotations.Range(1, int.MaxValue)] int CustomerId,
    [property: System.ComponentModel.DataAnnotations.Range(1, int.MaxValue)] int EmployeeId,
    [property: System.ComponentModel.DataAnnotations.Range(1, int.MaxValue)] int CurrencyId,
    [property: System.ComponentModel.DataAnnotations.Range(1, 365)] int Period,
    [property: System.ComponentModel.DataAnnotations.StringLength(256)] string? ShippedVia,
    [property: System.ComponentModel.DataAnnotations.StringLength(256)] string? Fob,
    [property: System.ComponentModel.DataAnnotations.StringLength(1000)] string? Terms,
    [property: System.ComponentModel.DataAnnotations.StringLength(4000)] string? Comment,
    bool WithholdingTaxEnabled,
    [property: System.ComponentModel.DataAnnotations.Required, System.ComponentModel.DataAnnotations.MinLength(1)] IReadOnlyList<QuotationCreateLine> Lines);

/// <summary>Safe result returned after the durable server workflow commits a quotation.</summary>
public sealed record QuotationCreatedResult(int Id, string? Warning);

/// <summary>Reference data required by the browser create form.</summary>
public sealed record QuotationCreatePage(
    IReadOnlyList<QuotationEmployee> Employees,
    IReadOnlyList<QuotationCurrency> Currencies,
    QuotationCustomer? Customer,
    IReadOnlyList<OrderListItem> Orders,
    int? CurrentEmployeeId);
