namespace Legacy.Maliev.Intranet.Contracts;

/// <summary>Legacy quotation-request sort values preserved by the employee routes.</summary>
public enum QuotationRequestSort
{
    RequestId_Ascending, RequestId_Descending,
    RequestCreatedDate_Ascending, RequestCreatedDate_Descending,
    RequestModifiedDate_Ascending, RequestModifiedDate_Descending,
}

/// <summary>Browser-safe quotation request without service navigation properties.</summary>
public sealed record QuotationRequestItem(
    int Id, string? FirstName, string? LastName, string? Email, string? TelephoneNumber,
    string? Country, string? CompanyName, string? TaxIdentification, string? Message,
    string? InternalComment, bool? Done, DateTime? CreatedDate, DateTime? ModifiedDate);

/// <summary>Bounded quotation-request page.</summary>
public sealed record QuotationRequestPage(
    IReadOnlyList<QuotationRequestItem> Items, int PageIndex, int TotalPages, int TotalRecords,
    bool HasNextPage, bool HasPreviousPage);

/// <summary>Validated employee update contract.</summary>
public sealed record QuotationRequestUpdate(
    string? FirstName, string? LastName, string? Email, string? TelephoneNumber,
    string? Country, string? CompanyName, string? TaxIdentification, string? Message,
    string? InternalComment, bool? Done, DateTime? ModifiedDate);

/// <summary>One request-owned file resolved to an optional short-lived clean-object URL.</summary>
public sealed record QuotationRequestFileItem(
    int Id, int? RequestId, string ObjectName, DateTime? CreatedDate, Uri? Uri);

/// <summary>Complete browser-safe quotation-request editor projection.</summary>
public sealed record QuotationRequestDetail(
    QuotationRequestItem Request, IReadOnlyList<QuotationRequestFileItem> Files);
