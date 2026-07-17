using System.ComponentModel.DataAnnotations;

namespace Legacy.Maliev.Intranet.Contracts;

/// <summary>Validated browser-owned fields for creating a legacy order.</summary>
public sealed record OrderCreateRequest(
    [property: Range(1, int.MaxValue)] int CustomerId,
    [property: Required, StringLength(256)] string Name,
    [property: StringLength(500)] string? Description,
    [property: Range(1, int.MaxValue)] int ProcessId,
    [property: Range(1, int.MaxValue)] int? MaterialId,
    [property: Range(1, int.MaxValue)] int? SurfaceFinishId,
    [property: Range(1, int.MaxValue)] int? ColorId,
    [property: Range(1, int.MaxValue)] int Quantity,
    bool SendConfirmationEmail,
    bool AllowSocialMedia);

/// <summary>Safe creation result returned to the same-origin WASM client.</summary>
public sealed record OrderCreatedResult(int Id, string? Warning);

/// <summary>Initial server-owned reference data for the create form.</summary>
public sealed record OrderCreatePage(
    IReadOnlyList<OrderLookupItem> Processes,
    IReadOnlyList<OrderLookupItem> Materials,
    CustomerDetail? Customer);

/// <summary>Material-dependent options resolved by CatalogService.</summary>
public sealed record OrderMaterialOptions(
    IReadOnlyList<OrderLookupItem> Colors,
    IReadOnlyList<OrderLookupItem> SurfaceFinishes);
