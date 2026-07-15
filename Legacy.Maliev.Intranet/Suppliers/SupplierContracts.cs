using System.ComponentModel.DataAnnotations;

namespace Legacy.Maliev.Intranet.Suppliers;

/// <summary>Legacy supplier projection.</summary>
public sealed record SupplierResponse(int Id, string? Name, string? Website, string? TaxNumber, string? Email, string? Note, int? AddressId, string? Telephone, string? Mobile, string? Fax, DateTime? CreatedDate, DateTime? ModifiedDate);
/// <summary>Legacy supplier-owned address projection.</summary>
public sealed record SupplierAddressResponse(int Id, string? Building, string? Address1, string? Address2, string? City, string? State, string? PostalCode, int CountryId, DateTime? ModifiedDate, DateTime? CreatedDate);
/// <summary>Supplier write payload.</summary>
public sealed record UpsertSupplierRequest(string? Name, string? Website, string? TaxNumber, string? Email, string? Note, string? Telephone, string? Mobile, string? Fax);
/// <summary>Supplier address write payload.</summary>
public sealed record UpsertSupplierAddressRequest(string? Building, string? Address1, string? Address2, string? City, string? State, string? PostalCode, int CountryId);
/// <summary>Legacy paginated response.</summary>
public sealed record PaginatedResponse<T>(IReadOnlyList<T> Items, int PageIndex, int TotalPages, int TotalRecords)
{
    /// <summary>Whether another page follows.</summary>
    public bool HasNextPage => PageIndex < TotalPages;
    /// <summary>Whether a prior page exists.</summary>
    public bool HasPreviousPage => PageIndex > 1;
}
/// <summary>Legacy supplier sort names.</summary>
public enum SupplierSortType
{
    /// <summary>Identifier ascending.</summary>
    SupplierId_Ascending,
    /// <summary>Identifier descending.</summary>
    SupplierId_Descending,
    /// <summary>Name ascending.</summary>
    SupplierName_Ascending,
    /// <summary>Name descending.</summary>
    SupplierName_Descending,
    /// <summary>Creation ascending.</summary>
    SupplierCreatedDate_Ascending,
    /// <summary>Creation descending.</summary>
    SupplierCreatedDate_Descending,
    /// <summary>Modification ascending.</summary>
    SupplierModifiedDate_Ascending,
    /// <summary>Modification descending.</summary>
    SupplierModifiedDate_Descending,
}

/// <summary>Mutable supplier editor input.</summary>
public sealed class SupplierInput
{
    /// <summary>Supplier name.</summary>
    [Required, StringLength(256)] public string Name { get; set; } = string.Empty;
    /// <summary>Website.</summary>
    [Url] public string? Website { get; set; }
    /// <summary>Tax number.</summary>
    public string? TaxNumber { get; set; }
    /// <summary>Email.</summary>
    [EmailAddress] public string? Email { get; set; }
    /// <summary>Note.</summary>
    public string? Note { get; set; }
    /// <summary>Telephone.</summary>
    public string? Telephone { get; set; }
    /// <summary>Mobile.</summary>
    public string? Mobile { get; set; }
    /// <summary>Fax.</summary>
    public string? Fax { get; set; }
    /// <summary>Building.</summary>
    public string? Building { get; set; }
    /// <summary>Primary address line.</summary>
    [Required] public string Address1 { get; set; } = string.Empty;
    /// <summary>Secondary address line.</summary>
    public string? Address2 { get; set; }
    /// <summary>City.</summary>
    public string? City { get; set; }
    /// <summary>State.</summary>
    public string? State { get; set; }
    /// <summary>Postal code.</summary>
    public string? PostalCode { get; set; }
    /// <summary>Country identifier.</summary>
    [Range(1, int.MaxValue)] public int CountryId { get; set; }

    internal UpsertSupplierRequest Supplier() => new(Name, Website, TaxNumber, Email, Note, Telephone, Mobile, Fax);
    internal UpsertSupplierAddressRequest Address() => new(Building, Address1, Address2, City, State, PostalCode, CountryId);
    internal static SupplierInput From(SupplierResponse supplier, SupplierAddressResponse? address) => new()
    {
        Name = supplier.Name ?? string.Empty,
        Website = supplier.Website,
        TaxNumber = supplier.TaxNumber,
        Email = supplier.Email,
        Note = supplier.Note,
        Telephone = supplier.Telephone,
        Mobile = supplier.Mobile,
        Fax = supplier.Fax,
        Building = address?.Building,
        Address1 = address?.Address1 ?? string.Empty,
        Address2 = address?.Address2,
        City = address?.City,
        State = address?.State,
        PostalCode = address?.PostalCode,
        CountryId = address?.CountryId ?? 0,
    };
}

/// <summary>Typed ProcurementService supplier boundary.</summary>
public interface ILegacyProcurementClient
{
    /// <summary>Gets a supplier page.</summary>
    Task<PaginatedResponse<SupplierResponse>?> GetSuppliersAsync(SupplierSortType sort, string? search, int index, int size, string token, CancellationToken cancellationToken);
    /// <summary>Gets one supplier.</summary>
    Task<SupplierResponse?> GetSupplierAsync(int id, string token, CancellationToken cancellationToken);
    /// <summary>Gets a supplier address.</summary>
    Task<SupplierAddressResponse?> GetSupplierAddressAsync(int id, string token, CancellationToken cancellationToken);
    /// <summary>Creates an idempotent supplier.</summary>
    Task<SupplierResponse> CreateSupplierAsync(UpsertSupplierRequest request, string token, CancellationToken cancellationToken);
    /// <summary>Creates the supplier-owned address.</summary>
    Task<SupplierAddressResponse> CreateSupplierAddressAsync(int id, UpsertSupplierAddressRequest request, string token, CancellationToken cancellationToken);
    /// <summary>Updates a supplier.</summary>
    Task UpdateSupplierAsync(int id, UpsertSupplierRequest request, string token, CancellationToken cancellationToken);
    /// <summary>Updates a supplier address.</summary>
    Task UpdateSupplierAddressAsync(int addressId, UpsertSupplierAddressRequest request, string token, CancellationToken cancellationToken);
    /// <summary>Deletes a supplier.</summary>
    Task DeleteSupplierAsync(int id, string token, CancellationToken cancellationToken);

    /// <summary>Gets a purchase-order page.</summary>
    Task<PaginatedResponse<PurchaseOrderResponse>?> GetPurchaseOrdersAsync(PurchaseOrderSortType sort, string? search, int index, int size, string token, CancellationToken cancellationToken);
    /// <summary>Gets one purchase order.</summary>
    Task<PurchaseOrderResponse?> GetPurchaseOrderAsync(int id, string token, CancellationToken cancellationToken);
    /// <summary>Creates an idempotent purchase order.</summary>
    Task<PurchaseOrderResponse> CreatePurchaseOrderAsync(UpsertPurchaseOrderRequest request, string token, CancellationToken cancellationToken);
    /// <summary>Deletes a purchase order.</summary>
    Task DeletePurchaseOrderAsync(int id, string token, CancellationToken cancellationToken);
    /// <summary>Gets the reusable purchase-order addresses.</summary>
    Task<IReadOnlyList<PurchaseOrderAddressResponse>> GetPurchaseOrderAddressesAsync(string token, CancellationToken cancellationToken);
    /// <summary>Gets one purchase-order address.</summary>
    Task<PurchaseOrderAddressResponse?> GetPurchaseOrderAddressAsync(int id, string token, CancellationToken cancellationToken);
    /// <summary>Gets a purchase order's line items.</summary>
    Task<IReadOnlyList<OrderItemResponse>> GetOrderItemsAsync(int purchaseOrderId, string token, CancellationToken cancellationToken);
    /// <summary>Creates a line item.</summary>
    Task<OrderItemResponse> CreateOrderItemAsync(UpsertOrderItemRequest request, string token, CancellationToken cancellationToken);
    /// <summary>Deletes a line item.</summary>
    Task DeleteOrderItemAsync(int id, string token, CancellationToken cancellationToken);
    /// <summary>Gets a purchase order's linked files.</summary>
    Task<IReadOnlyList<PurchaseOrderFileResponse>> GetPurchaseOrderFilesAsync(int purchaseOrderId, string token, CancellationToken cancellationToken);
    /// <summary>Links a clean cloud object to a purchase order.</summary>
    Task<PurchaseOrderFileResponse> CreatePurchaseOrderFileAsync(int purchaseOrderId, string bucket, string objectName, string token, CancellationToken cancellationToken);
    /// <summary>Deletes a purchase-order file record.</summary>
    Task DeletePurchaseOrderFileAsync(int id, string token, CancellationToken cancellationToken);
}