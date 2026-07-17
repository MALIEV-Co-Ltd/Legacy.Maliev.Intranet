namespace Legacy.Maliev.Intranet.Contracts;

/// <summary>Legacy-compatible material list sort values exposed to the browser.</summary>
public enum CatalogMaterialSort
{
    MaterialId_Ascending,
    MaterialId_Descending,
    MaterialMachinability_Ascending,
    MaterialMachinability_Descending,
    MaterialCreatedDate_Ascending,
    MaterialCreatedDate_Descending,
    MaterialModifiedDate_Descending,
    MaterialModifiedDate_Ascending,
    MaterialName_Ascending,
    MaterialName_Descending,
    MaterialGroup_Ascending,
    MaterialGroup_Descending,
    MaterialDensity_Ascending,
    MaterialDensity_Descending,
    MaterialThermalConductivity_Ascending,
    MaterialThermalConductivity_Descending,
    MaterialNumber_Ascending,
    MaterialNumber_Descending,
}

/// <summary>Browser-safe material group projection.</summary>
public sealed record CatalogMaterialGroup(int Id, string Name);

/// <summary>Browser-safe material row matching the legacy table semantics.</summary>
public sealed record CatalogMaterialListItem(
    int Id,
    int MaterialGroupId,
    bool Machinable,
    bool Printable,
    string Name,
    string? MaterialNumber,
    decimal? DensityKilogramPerCubicMeter,
    CatalogMaterialGroup? MaterialGroup);

/// <summary>Browser-safe read-only material detail matching the legacy display semantics.</summary>
public sealed record CatalogMaterialDetail(
    int Id,
    int MaterialGroupId,
    bool Machinable,
    bool Printable,
    string Name,
    string? MaterialNumber,
    decimal? DensityKilogramPerCubicMeter,
    CatalogMaterialGroup? MaterialGroup);

/// <summary>Browser-safe legacy-compatible material page.</summary>
public sealed record CatalogMaterialPage(
    IReadOnlyList<CatalogMaterialListItem> Items,
    int PageIndex,
    int TotalPages,
    int TotalRecords,
    bool HasNextPage,
    bool HasPreviousPage);
