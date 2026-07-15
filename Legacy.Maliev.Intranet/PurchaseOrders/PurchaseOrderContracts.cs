using System.ComponentModel.DataAnnotations;

namespace Legacy.Maliev.Intranet.Suppliers;

/// <summary>Legacy purchase-order projection.</summary>
public sealed record PurchaseOrderResponse(int Id, int? SupplierId, string? SupplierContactPerson, int? ShippingAddressId, string? ShippingContactPerson, string? ShippingTelephone, string? ShippingMobile, string? ShippingFax, int? BillingAddressId, string? BillingContactPerson, string? BillingTelephone, string? BillingMobile, string? BillingFax, string? Fob, string? Terms, string? ShippingMethod, int? EmployeeId, string? Notes, DateTime? CreatedDate, DateTime? ModifiedDate);
/// <summary>Purchase-order write contract.</summary>
public sealed record UpsertPurchaseOrderRequest(int? SupplierId, string? SupplierContactPerson, int? ShippingAddressId, string? ShippingContactPerson, string? ShippingTelephone, string? ShippingMobile, string? ShippingFax, int? BillingAddressId, string? BillingContactPerson, string? BillingTelephone, string? BillingMobile, string? BillingFax, string? Fob, string? Terms, string? ShippingMethod, int? EmployeeId, string? Notes);
/// <summary>Reusable purchasing address.</summary>
public sealed record PurchaseOrderAddressResponse(int Id, string? Building, string AddressLine1, string? AddressLine2, string? City, string? State, string? PostalCode, int CountryId, DateTime? CreatedDate, DateTime? ModifiedDate);
/// <summary>Purchase-order line item.</summary>
public sealed record OrderItemResponse(int Id, int? PurchaseOrderId, string? PartNumber, string? Description, int? Quantity, decimal? UnitPrice, decimal? Subtotal, DateTime? CreatedDate, DateTime? ModifiedDate);
/// <summary>Line-item write contract.</summary>
public sealed record UpsertOrderItemRequest(int? PurchaseOrderId, string? PartNumber, string? Description, int? Quantity, decimal? UnitPrice);
/// <summary>Cloud-object metadata linked to a purchase order.</summary>
public sealed record PurchaseOrderFileResponse(int Id, int PurchaseOrderId, string Bucket, string ObjectName, DateTime? CreatedDate, DateTime? ModifiedDate);

/// <summary>Legacy purchase-order sort names.</summary>
public enum PurchaseOrderSortType
{
    /// <summary>Identifier ascending.</summary>
    PurchaseOrderId_Ascending,
    /// <summary>Identifier descending.</summary>
    PurchaseOrderId_Descending,
    /// <summary>Creation ascending.</summary>
    PurchaseOrderCreatedDate_Ascending,
    /// <summary>Creation descending.</summary>
    PurchaseOrderCreatedDate_Descending,
}

/// <summary>Validated purchase-order editor.</summary>
public sealed class PurchaseOrderInput
{
    /// <summary>Supplier identifier.</summary>
    [Range(1, int.MaxValue)] public int SupplierId { get; set; }
    /// <summary>Supplier contact.</summary>
    [StringLength(256)] public string? SupplierContactPerson { get; set; }
    /// <summary>Shipping address identifier.</summary>
    [Range(1, int.MaxValue)] public int ShippingAddressId { get; set; }
    /// <summary>Shipping contact.</summary>
    [StringLength(256)] public string? ShippingContactPerson { get; set; }
    /// <summary>Shipping telephone.</summary>
    [StringLength(64)] public string? ShippingTelephone { get; set; }
    /// <summary>Shipping mobile.</summary>
    [StringLength(64)] public string? ShippingMobile { get; set; }
    /// <summary>Shipping fax.</summary>
    [StringLength(64)] public string? ShippingFax { get; set; }
    /// <summary>Shipping company name printed on the PDF.</summary>
    [Required, StringLength(256)] public string ShippingCompanyName { get; set; } = "MALIEV Co., Ltd.";
    /// <summary>Billing address identifier.</summary>
    [Range(1, int.MaxValue)] public int BillingAddressId { get; set; }
    /// <summary>Billing contact.</summary>
    [StringLength(256)] public string? BillingContactPerson { get; set; }
    /// <summary>Billing telephone.</summary>
    [StringLength(64)] public string? BillingTelephone { get; set; }
    /// <summary>Billing mobile.</summary>
    [StringLength(64)] public string? BillingMobile { get; set; }
    /// <summary>Billing fax.</summary>
    [StringLength(64)] public string? BillingFax { get; set; }
    /// <summary>Billing company name printed on the PDF.</summary>
    [Required, StringLength(256)] public string BillingCompanyName { get; set; } = "MALIEV Co., Ltd.";
    /// <summary>Free-on-board term.</summary>
    [StringLength(128)] public string? Fob { get; set; }
    /// <summary>Payment terms.</summary>
    [StringLength(256)] public string? Terms { get; set; }
    /// <summary>Shipping method.</summary>
    [StringLength(256)] public string? ShippingMethod { get; set; }
    /// <summary>Ordering employee.</summary>
    [Range(1, int.MaxValue)] public int EmployeeId { get; set; }
    /// <summary>Additional notes.</summary>
    [StringLength(1000)] public string? Notes { get; set; }
    /// <summary>Line items.</summary>
    [MinLength(1)] public List<OrderItemInput> Items { get; set; } = [new()];

    internal UpsertPurchaseOrderRequest ToRequest() => new(SupplierId, SupplierContactPerson, ShippingAddressId, ShippingContactPerson, ShippingTelephone, ShippingMobile, ShippingFax, BillingAddressId, BillingContactPerson, BillingTelephone, BillingMobile, BillingFax, Fob, Terms, ShippingMethod, EmployeeId, Notes);
}

/// <summary>Validated purchase-order line editor.</summary>
public sealed class OrderItemInput
{
    /// <summary>Part number.</summary>
    [StringLength(128)] public string? PartNumber { get; set; }
    /// <summary>Description.</summary>
    [Required, StringLength(1000)] public string Description { get; set; } = string.Empty;
    /// <summary>Quantity.</summary>
    [Range(1, int.MaxValue)] public int Quantity { get; set; } = 1;
    /// <summary>Unit price.</summary>
    [Range(typeof(decimal), "0", "999999999999")] public decimal UnitPrice { get; set; }
}