namespace Legacy.Maliev.Intranet.Contracts;

/// <summary>Browser-safe purchase-order detail projection.</summary>
public sealed record PurchaseOrderDetail(
    int Id,
    string? SupplierName,
    string? SupplierContactPerson,
    string? OrderedBy,
    string? ShippingMethod,
    string? Fob,
    string? Terms,
    string? Notes,
    DateTime? CreatedDate,
    IReadOnlyList<PurchaseOrderDetailLine> Items,
    IReadOnlyList<PurchaseOrderDownloadLink> Downloads);

/// <summary>Browser-safe purchase-order line projection.</summary>
public sealed record PurchaseOrderDetailLine(string? PartNumber, string? Description, int Quantity, decimal UnitPrice, decimal Subtotal);

/// <summary>Short-lived clean download URL without bucket or service credentials.</summary>
public sealed record PurchaseOrderDownloadLink(string Name, Uri Url);
