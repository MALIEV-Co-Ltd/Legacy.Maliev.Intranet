using System.ComponentModel.DataAnnotations;

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

/// <summary>Browser-safe currency option for material pricing.</summary>
public sealed record CatalogCurrency(int Id, string ShortName);

/// <summary>Browser-safe result returned after a material is created.</summary>
public sealed record CatalogCreatedMaterial(int Id);

/// <summary>Complete legacy-compatible material write payload validated at the browser and BFF boundaries.</summary>
public sealed class CatalogMaterialUpsertRequest
{
    [Range(1, int.MaxValue)] public int MaterialGroupId { get; set; }
    public bool Machinable { get; set; }
    public bool Printable { get; set; }
    [Required, StringLength(50)] public string Name { get; set; } = string.Empty;
    public string? Aisi { get; set; }
    public string? Din { get; set; }
    public string? Bts { get; set; }
    public string? Jis { get; set; }
    public string? Uns { get; set; }
    public string? En { get; set; }
    public string? Afnor { get; set; }
    public string? Uni { get; set; }
    public string? Sis { get; set; }
    public string? Sae { get; set; }
    public string? Astm { get; set; }
    public string? Ams { get; set; }
    public string? MaterialNumber { get; set; }
    public string? ManufacturerReference { get; set; }
    public decimal? HardnessBrinell { get; set; }
    public decimal? HardnessKnoop { get; set; }
    public decimal? HardnessRockwellA { get; set; }
    public decimal? HardnessRockwellB { get; set; }
    public decimal? HardnessRockwellC { get; set; }
    public decimal? HardnessVickers { get; set; }
    public decimal? DensityKilogramPerCubicMeter { get; set; }
    public decimal? TensileStrengthUltimateGigaPascal { get; set; }
    public decimal? TensileStrengthYieldMegaPascal { get; set; }
    public decimal? MachinabilityPercent { get; set; }
    public decimal? ShearModulusGigaPascal { get; set; }
    public decimal? ThermalConductivityWattPerMeterKelvin { get; set; }
    [Url] public string? Url { get; set; }
    public decimal? PricePerKilogram { get; set; }
    [Range(1, int.MaxValue)] public int? CurrencyId { get; set; }
    public string? Comment { get; set; }
}

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

/// <summary>Browser-safe material detail preserving the complete legacy editor wire contract.</summary>
public sealed record CatalogMaterialDetail(
    int Id,
    int MaterialGroupId,
    bool Machinable,
    bool Printable,
    string Name,
    string? Aisi,
    string? Din,
    string? Bts,
    string? Jis,
    string? Uns,
    string? En,
    string? Afnor,
    string? Uni,
    string? Sis,
    string? Sae,
    string? Astm,
    string? Ams,
    string? MaterialNumber,
    string? ManufacturerReference,
    decimal? HardnessBrinell,
    decimal? HardnessKnoop,
    decimal? HardnessRockwellA,
    decimal? HardnessRockwellB,
    decimal? HardnessRockwellC,
    decimal? HardnessVickers,
    decimal? DensityKilogramPerCubicMeter,
    decimal? TensileStrengthUltimateGigaPascal,
    decimal? TensileStrengthYieldMegaPascal,
    decimal? MachinabilityPercent,
    decimal? ShearModulusGigaPascal,
    decimal? ThermalConductivityWattPerMeterKelvin,
    string? Url,
    decimal? PricePerKilogram,
    int? CurrencyId,
    string? Comment,
    DateTime? CreatedDate,
    DateTime? ModifiedDate,
    CatalogMaterialGroup? MaterialGroup);

/// <summary>Browser-safe legacy-compatible material page.</summary>
public sealed record CatalogMaterialPage(
    IReadOnlyList<CatalogMaterialListItem> Items,
    int PageIndex,
    int TotalPages,
    int TotalRecords,
    bool HasNextPage,
    bool HasPreviousPage);
