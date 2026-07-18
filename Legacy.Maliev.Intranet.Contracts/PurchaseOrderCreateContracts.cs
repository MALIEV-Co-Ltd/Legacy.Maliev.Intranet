using System.ComponentModel.DataAnnotations;

namespace Legacy.Maliev.Intranet.Contracts;

/// <summary>Browser-safe purchase-order creation request.</summary>
public sealed class PurchaseOrderCreateRequest
{
    [Range(1, int.MaxValue)] public int SupplierId { get; set; }
    [StringLength(256)] public string? SupplierContactPerson { get; set; }
    [Range(1, int.MaxValue)] public int ShippingAddressId { get; set; }
    [StringLength(256)] public string? ShippingContactPerson { get; set; }
    [StringLength(64)] public string? ShippingTelephone { get; set; }
    [StringLength(64)] public string? ShippingMobile { get; set; }
    [StringLength(64)] public string? ShippingFax { get; set; }
    [Required, StringLength(256)] public string ShippingCompanyName { get; set; } = "MALIEV Co., Ltd.";
    [Range(1, int.MaxValue)] public int BillingAddressId { get; set; }
    [StringLength(256)] public string? BillingContactPerson { get; set; }
    [StringLength(64)] public string? BillingTelephone { get; set; }
    [StringLength(64)] public string? BillingMobile { get; set; }
    [StringLength(64)] public string? BillingFax { get; set; }
    [Required, StringLength(256)] public string BillingCompanyName { get; set; } = "MALIEV Co., Ltd.";
    [StringLength(128)] public string? Fob { get; set; }
    [StringLength(256)] public string? Terms { get; set; }
    [StringLength(256)] public string? ShippingMethod { get; set; }
    [Range(1, int.MaxValue)] public int EmployeeId { get; set; }
    [StringLength(1000)] public string? Notes { get; set; }
    [MinLength(1)] public List<PurchaseOrderCreateItem> Items { get; set; } = [new()];
}

/// <summary>Browser-safe purchase-order line input.</summary>
public sealed class PurchaseOrderCreateItem
{
    [StringLength(128)] public string? PartNumber { get; set; }
    [Required, StringLength(1000)] public string Description { get; set; } = string.Empty;
    [Range(1, int.MaxValue)] public int Quantity { get; set; } = 1;
    [Range(typeof(decimal), "0", "999999999999")] public decimal UnitPrice { get; set; }
}

/// <summary>Selectable supplier used by the purchase-order editor.</summary>
public sealed record PurchaseOrderSupplierOption(int Id, string Name);

/// <summary>Selectable employee used by the purchase-order editor.</summary>
public sealed record PurchaseOrderEmployeeOption(int Id, string FullName);

/// <summary>Selectable reusable company address.</summary>
public sealed record PurchaseOrderAddressOption(int Id, string AddressLine1, string? City);

/// <summary>All reference data needed to render the creation form.</summary>
public sealed record PurchaseOrderCreateOptions(
    IReadOnlyList<PurchaseOrderSupplierOption> Suppliers,
    IReadOnlyList<PurchaseOrderEmployeeOption> Employees,
    IReadOnlyList<PurchaseOrderAddressOption> Addresses);

/// <summary>Safe result returned after the complete purchase-order workflow succeeds.</summary>
public sealed record CreatedPurchaseOrder(int Id);
