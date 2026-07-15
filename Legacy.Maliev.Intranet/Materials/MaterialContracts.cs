using System.ComponentModel.DataAnnotations;

namespace Legacy.Maliev.Intranet.Materials;

/// <summary>Legacy-compatible material sort values.</summary>
public enum MaterialSortType
{
    /// <summary>Identifier ascending.</summary>
    MaterialId_Ascending,
    /// <summary>Identifier descending.</summary>
    MaterialId_Descending,
    /// <summary>Machinability ascending.</summary>
    MaterialMachinability_Ascending,
    /// <summary>Machinability descending.</summary>
    MaterialMachinability_Descending,
    /// <summary>Creation time ascending.</summary>
    MaterialCreatedDate_Ascending,
    /// <summary>Creation time descending.</summary>
    MaterialCreatedDate_Descending,
    /// <summary>Modification time descending.</summary>
    MaterialModifiedDate_Descending,
    /// <summary>Modification time ascending.</summary>
    MaterialModifiedDate_Ascending,
    /// <summary>Name ascending.</summary>
    MaterialName_Ascending,
    /// <summary>Name descending.</summary>
    MaterialName_Descending,
    /// <summary>Group ascending.</summary>
    MaterialGroup_Ascending,
    /// <summary>Group descending.</summary>
    MaterialGroup_Descending,
    /// <summary>Density ascending.</summary>
    MaterialDensity_Ascending,
    /// <summary>Density descending.</summary>
    MaterialDensity_Descending,
    /// <summary>Thermal conductivity ascending.</summary>
    MaterialThermalConductivity_Ascending,
    /// <summary>Thermal conductivity descending.</summary>
    MaterialThermalConductivity_Descending,
    /// <summary>Material number ascending.</summary>
    MaterialNumber_Ascending,
    /// <summary>Material number descending.</summary>
    MaterialNumber_Descending,
}

/// <summary>Material group projection.</summary>
public sealed record MaterialGroupResponse(int Id, string Name, string? Description, DateTime? CreatedDate, DateTime? ModifiedDate);
/// <summary>Currency projection.</summary>
public sealed record CurrencyResponse(int Id, string ShortName, string LongName, DateTime? CreatedDate, DateTime? ModifiedDate);
/// <summary>Color projection.</summary>
public sealed record ColorResponse(int Id, string Name, DateTime? CreatedDate, DateTime? ModifiedDate);
/// <summary>Surface-finish projection.</summary>
public sealed record SurfaceFinishResponse(int Id, string Name, DateTime? CreatedDate, DateTime? ModifiedDate);

/// <summary>Complete legacy material projection.</summary>
public sealed record MaterialResponse(
    int Id, int MaterialGroupId, bool Machinable, bool Printable, string Name,
    string? Aisi, string? Din, string? Bts, string? Jis, string? Uns, string? En,
    string? Afnor, string? Uni, string? Sis, string? Sae, string? Astm, string? Ams,
    string? MaterialNumber, string? ManufacturerReference,
    decimal? HardnessBrinell, decimal? HardnessKnoop, decimal? HardnessRockwellA,
    decimal? HardnessRockwellB, decimal? HardnessRockwellC, decimal? HardnessVickers,
    decimal? DensityKilogramPerCubicMeter, decimal? TensileStrengthUltimateGigaPascal,
    decimal? TensileStrengthYieldMegaPascal, decimal? MachinabilityPercent,
    decimal? ShearModulusGigaPascal, decimal? ThermalConductivityWattPerMeterKelvin,
    string? Url, decimal? PricePerKilogram, int? CurrencyId, string? Comment,
    DateTime? CreatedDate, DateTime? ModifiedDate, MaterialGroupResponse? MaterialGroup);

/// <summary>Complete legacy material write payload.</summary>
public sealed record UpsertMaterialRequest(
    [property: Range(1, int.MaxValue)] int MaterialGroupId, bool Machinable, bool Printable,
    [property: Required, StringLength(50)] string Name,
    string? Aisi, string? Din, string? Bts, string? Jis, string? Uns, string? En,
    string? Afnor, string? Uni, string? Sis, string? Sae, string? Astm, string? Ams,
    string? MaterialNumber, string? ManufacturerReference,
    decimal? HardnessBrinell, decimal? HardnessKnoop, decimal? HardnessRockwellA,
    decimal? HardnessRockwellB, decimal? HardnessRockwellC, decimal? HardnessVickers,
    decimal? DensityKilogramPerCubicMeter, decimal? TensileStrengthUltimateGigaPascal,
    decimal? TensileStrengthYieldMegaPascal, decimal? MachinabilityPercent,
    decimal? ShearModulusGigaPascal, decimal? ThermalConductivityWattPerMeterKelvin,
    string? Url, decimal? PricePerKilogram, int? CurrencyId, string? Comment);

/// <summary>Mutable, validated browser input mapped explicitly to the immutable Catalog API payload.</summary>
public sealed class MaterialInput
{
    /// <summary>Material group identifier.</summary>
    [Range(1, int.MaxValue)] public int MaterialGroupId { get; set; }
    /// <summary>Whether CNC machining is supported.</summary>
    public bool Machinable { get; set; }
    /// <summary>Whether 3D printing is supported.</summary>
    public bool Printable { get; set; }
    /// <summary>Material name.</summary>
    [Required, StringLength(50)] public string Name { get; set; } = string.Empty;
    /// <summary>AISI designation.</summary>
    public string? Aisi { get; set; }
    /// <summary>DIN designation.</summary>
    public string? Din { get; set; }
    /// <summary>BTS designation.</summary>
    public string? Bts { get; set; }
    /// <summary>JIS designation.</summary>
    public string? Jis { get; set; }
    /// <summary>UNS designation.</summary>
    public string? Uns { get; set; }
    /// <summary>EN designation.</summary>
    public string? En { get; set; }
    /// <summary>AFNOR designation.</summary>
    public string? Afnor { get; set; }
    /// <summary>UNI designation.</summary>
    public string? Uni { get; set; }
    /// <summary>SIS designation.</summary>
    public string? Sis { get; set; }
    /// <summary>SAE designation.</summary>
    public string? Sae { get; set; }
    /// <summary>ASTM designation.</summary>
    public string? Astm { get; set; }
    /// <summary>AMS designation.</summary>
    public string? Ams { get; set; }
    /// <summary>Internal or industry material number.</summary>
    public string? MaterialNumber { get; set; }
    /// <summary>Manufacturer reference.</summary>
    public string? ManufacturerReference { get; set; }
    /// <summary>Brinell hardness.</summary>
    public decimal? HardnessBrinell { get; set; }
    /// <summary>Knoop hardness.</summary>
    public decimal? HardnessKnoop { get; set; }
    /// <summary>Rockwell A hardness.</summary>
    public decimal? HardnessRockwellA { get; set; }
    /// <summary>Rockwell B hardness.</summary>
    public decimal? HardnessRockwellB { get; set; }
    /// <summary>Rockwell C hardness.</summary>
    public decimal? HardnessRockwellC { get; set; }
    /// <summary>Vickers hardness.</summary>
    public decimal? HardnessVickers { get; set; }
    /// <summary>Density in kilograms per cubic metre.</summary>
    public decimal? DensityKilogramPerCubicMeter { get; set; }
    /// <summary>Ultimate tensile strength in gigapascals.</summary>
    public decimal? TensileStrengthUltimateGigaPascal { get; set; }
    /// <summary>Yield tensile strength in megapascals.</summary>
    public decimal? TensileStrengthYieldMegaPascal { get; set; }
    /// <summary>Machinability percentage.</summary>
    public decimal? MachinabilityPercent { get; set; }
    /// <summary>Shear modulus in gigapascals.</summary>
    public decimal? ShearModulusGigaPascal { get; set; }
    /// <summary>Thermal conductivity in watts per metre-kelvin.</summary>
    public decimal? ThermalConductivityWattPerMeterKelvin { get; set; }
    /// <summary>Datasheet URL.</summary>
    [Url] public string? Url { get; set; }
    /// <summary>Price per kilogram.</summary>
    public decimal? PricePerKilogram { get; set; }
    /// <summary>Currency identifier for the price.</summary>
    [Range(1, int.MaxValue)] public int? CurrencyId { get; set; }
    /// <summary>Free-form compatibility comment.</summary>
    public string? Comment { get; set; }

    internal UpsertMaterialRequest ToRequest() => new(
        MaterialGroupId, Machinable, Printable, Name, Aisi, Din, Bts, Jis, Uns, En, Afnor, Uni, Sis,
        Sae, Astm, Ams, MaterialNumber, ManufacturerReference, HardnessBrinell, HardnessKnoop,
        HardnessRockwellA, HardnessRockwellB, HardnessRockwellC, HardnessVickers,
        DensityKilogramPerCubicMeter, TensileStrengthUltimateGigaPascal, TensileStrengthYieldMegaPascal,
        MachinabilityPercent, ShearModulusGigaPascal, ThermalConductivityWattPerMeterKelvin,
        Url, PricePerKilogram, CurrencyId, Comment);

    internal static MaterialInput From(MaterialResponse value) => new()
    {
        MaterialGroupId = value.MaterialGroupId,
        Machinable = value.Machinable,
        Printable = value.Printable,
        Name = value.Name,
        Aisi = value.Aisi,
        Din = value.Din,
        Bts = value.Bts,
        Jis = value.Jis,
        Uns = value.Uns,
        En = value.En,
        Afnor = value.Afnor,
        Uni = value.Uni,
        Sis = value.Sis,
        Sae = value.Sae,
        Astm = value.Astm,
        Ams = value.Ams,
        MaterialNumber = value.MaterialNumber,
        ManufacturerReference = value.ManufacturerReference,
        HardnessBrinell = value.HardnessBrinell,
        HardnessKnoop = value.HardnessKnoop,
        HardnessRockwellA = value.HardnessRockwellA,
        HardnessRockwellB = value.HardnessRockwellB,
        HardnessRockwellC = value.HardnessRockwellC,
        HardnessVickers = value.HardnessVickers,
        DensityKilogramPerCubicMeter = value.DensityKilogramPerCubicMeter,
        TensileStrengthUltimateGigaPascal = value.TensileStrengthUltimateGigaPascal,
        TensileStrengthYieldMegaPascal = value.TensileStrengthYieldMegaPascal,
        MachinabilityPercent = value.MachinabilityPercent,
        ShearModulusGigaPascal = value.ShearModulusGigaPascal,
        ThermalConductivityWattPerMeterKelvin = value.ThermalConductivityWattPerMeterKelvin,
        Url = value.Url,
        PricePerKilogram = value.PricePerKilogram,
        CurrencyId = value.CurrencyId,
        Comment = value.Comment,
    };
}

/// <summary>Paginated material response.</summary>
public sealed record PaginatedMaterialResponse(
    IReadOnlyList<MaterialResponse> Items, int PageIndex, int TotalPages, int TotalRecords,
    bool HasNextPage, bool HasPreviousPage);

/// <summary>Typed boundary for the extracted CatalogService.</summary>
public interface ILegacyCatalogClient
{
    /// <summary>Gets a bounded material page.</summary>
    Task<PaginatedMaterialResponse?> GetMaterialsAsync(MaterialSortType sort, string? search, int index, int size, string accessToken, CancellationToken cancellationToken);
    /// <summary>Gets one material.</summary>
    Task<MaterialResponse?> GetMaterialAsync(int id, string accessToken, CancellationToken cancellationToken);
    /// <summary>Gets all material groups.</summary>
    Task<IReadOnlyList<MaterialGroupResponse>> GetMaterialGroupsAsync(string accessToken, CancellationToken cancellationToken);
    /// <summary>Gets all currencies.</summary>
    Task<IReadOnlyList<CurrencyResponse>> GetCurrenciesAsync(string accessToken, CancellationToken cancellationToken);
    /// <summary>Gets all colors.</summary>
    Task<IReadOnlyList<ColorResponse>> GetColorsAsync(string accessToken, CancellationToken cancellationToken);
    /// <summary>Gets all surface finishes.</summary>
    Task<IReadOnlyList<SurfaceFinishResponse>> GetSurfaceFinishesAsync(string accessToken, CancellationToken cancellationToken);
    /// <summary>Gets colors linked to a material.</summary>
    Task<IReadOnlyList<ColorResponse>> GetMaterialColorsAsync(int id, string accessToken, CancellationToken cancellationToken);
    /// <summary>Gets finishes linked to a material.</summary>
    Task<IReadOnlyList<SurfaceFinishResponse>> GetMaterialSurfaceFinishesAsync(int id, string accessToken, CancellationToken cancellationToken);
    /// <summary>Creates a material.</summary>
    Task<MaterialResponse> CreateMaterialAsync(UpsertMaterialRequest request, string accessToken, CancellationToken cancellationToken);
    /// <summary>Updates a material.</summary>
    Task UpdateMaterialAsync(int id, UpsertMaterialRequest request, string accessToken, CancellationToken cancellationToken);
    /// <summary>Synchronizes color links to the selected identifiers.</summary>
    Task SyncMaterialColorsAsync(int id, IReadOnlyCollection<int> selectedIds, string accessToken, CancellationToken cancellationToken);
    /// <summary>Synchronizes surface-finish links to the selected identifiers.</summary>
    Task SyncMaterialSurfaceFinishesAsync(int id, IReadOnlyCollection<int> selectedIds, string accessToken, CancellationToken cancellationToken);
}