using Legacy.Maliev.Intranet.Contracts;
using System.Text.Json;

namespace Legacy.Maliev.Intranet.Tests;

public sealed class MaterialsViewWasmMigrationContractTests
{
    [Fact]
    public void DetailDto_PreservesTheCompleteLegacyMaterialValuesAndWireNames()
    {
        var detailType = typeof(CatalogMaterialPage).Assembly.GetType(
            "Legacy.Maliev.Intranet.Contracts.CatalogMaterialDetail");

        Assert.NotNull(detailType);
        Assert.Equal(
            [
                "Afnor", "Aisi", "Ams", "Astm", "Bts", "Comment", "CreatedDate", "CurrencyId",
                "DensityKilogramPerCubicMeter", "Din", "En", "HardnessBrinell", "HardnessKnoop",
                "HardnessRockwellA", "HardnessRockwellB", "HardnessRockwellC", "HardnessVickers", "Id",
                "Jis", "MachinabilityPercent", "Machinable", "ManufacturerReference", "MaterialGroup",
                "MaterialGroupId", "MaterialNumber", "ModifiedDate", "Name", "PricePerKilogram", "Printable",
                "Sae", "ShearModulusGigaPascal", "Sis", "TensileStrengthUltimateGigaPascal",
                "TensileStrengthYieldMegaPascal", "ThermalConductivityWattPerMeterKelvin", "Uni", "Uns", "Url",
            ],
            detailType.GetProperties().Select(property => property.Name).Order(StringComparer.Ordinal).ToArray());

        const string completeJson = """{"id":42,"materialGroupId":7,"machinable":true,"printable":false,"name":"4140","aisi":"AISI 4140","din":null,"bts":null,"jis":null,"uns":null,"en":null,"afnor":null,"uni":null,"sis":null,"sae":null,"astm":null,"ams":null,"materialNumber":"AISI 4140","manufacturerReference":"ACME","hardnessBrinell":200,"hardnessKnoop":null,"hardnessRockwellA":null,"hardnessRockwellB":null,"hardnessRockwellC":32,"hardnessVickers":null,"densityKilogramPerCubicMeter":7850,"tensileStrengthUltimateGigaPascal":1.2,"tensileStrengthYieldMegaPascal":650,"machinabilityPercent":70,"shearModulusGigaPascal":80,"thermalConductivityWattPerMeterKelvin":42,"url":"https://example.test/4140","pricePerKilogram":100,"currencyId":1,"comment":"Stock","createdDate":"2026-01-01T00:00:00Z","modifiedDate":"2026-01-02T00:00:00Z","materialGroup":{"id":7,"name":"Steel"}}""";
        var detail = JsonSerializer.Deserialize(completeJson, detailType, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.NotNull(detail);
        var json = JsonSerializer.SerializeToElement(
            detail,
            detailType,
            new JsonSerializerOptions(JsonSerializerDefaults.Web));

        Assert.Equal(42, json.GetProperty("id").GetInt32());
        Assert.Equal("Steel", json.GetProperty("materialGroup").GetProperty("name").GetString());
        Assert.Equal("4140", json.GetProperty("name").GetString());
        Assert.Equal("AISI 4140", json.GetProperty("materialNumber").GetString());
        Assert.Equal(7850m, json.GetProperty("densityKilogramPerCubicMeter").GetDecimal());
        Assert.True(json.GetProperty("machinable").GetBoolean());
        Assert.False(json.GetProperty("printable").GetBoolean());
        Assert.Equal("ACME", json.GetProperty("manufacturerReference").GetString());
        Assert.Equal(42m, json.GetProperty("thermalConductivityWattPerMeterKelvin").GetDecimal());
        Assert.Equal(1, json.GetProperty("currencyId").GetInt32());
    }

    [Fact]
    public void MaterialsView_IsLazyAuthorizedLocalizedAndKeepsTheRazorFallback()
    {
        var root = FindRoot();
        var pagePath = Path.Combine(
            root,
            "Legacy.Maliev.Intranet.Client.Features.Catalog",
            "Pages",
            "MaterialDetail.razor");
        var resourcePath = Path.Combine(
            root,
            "Legacy.Maliev.Intranet.Client.Features.Catalog",
            "Pages",
            "MaterialDetail.resx");

        Assert.True(File.Exists(pagePath), "The lazy WASM Materials/View route is missing.");
        Assert.True(File.Exists(resourcePath), "The Materials/View localization resource is missing.");
        var page = File.ReadAllText(pagePath);
        var resource = File.ReadAllText(resourcePath);
        var bffProgram = File.ReadAllText(Path.Combine(root, "Legacy.Maliev.Intranet.Bff", "Program.cs"));

        Assert.Contains("@page \"/Materials/View\"", page, StringComparison.Ordinal);
        Assert.Contains("@attribute [Authorize]", page, StringComparison.Ordinal);
        Assert.Contains("[SupplyParameterFromQuery(Name = \"id\")]", page, StringComparison.Ordinal);
        Assert.Contains("/bff/catalog/materials/", page, StringComparison.Ordinal);
        Assert.Contains("MudProgress", page, StringComparison.Ordinal);
        Assert.Contains("MudAlert", page, StringComparison.Ordinal);
        Assert.Contains("else if (!string.IsNullOrWhiteSpace(error))", page, StringComparison.Ordinal);
        Assert.Contains("MudGrid", page, StringComparison.Ordinal);
        Assert.Contains("Text[\"Group\"]", page, StringComparison.Ordinal);
        Assert.Contains("Text[\"Name\"]", page, StringComparison.Ordinal);
        Assert.Contains("Text[\"Number\"]", page, StringComparison.Ordinal);
        Assert.Contains("Text[\"Density\"]", page, StringComparison.Ordinal);
        Assert.Contains("Text[\"Machinable\"]", page, StringComparison.Ordinal);
        Assert.Contains("Text[\"Printable\"]", page, StringComparison.Ordinal);
        Assert.Contains("MudForm", page, StringComparison.Ordinal);
        Assert.Contains("/bff/catalog/material-groups", page, StringComparison.Ordinal);
        Assert.Contains("/bff/catalog/currencies", page, StringComparison.Ordinal);
        Assert.Contains("HttpMethod.Put", page, StringComparison.Ordinal);
        Assert.Contains("X-CSRF-TOKEN", page, StringComparison.Ordinal);
        Assert.Contains("submitting", page, StringComparison.Ordinal);
        Assert.Contains("Disabled=\"@submitting\"", page, StringComparison.Ordinal);
        Assert.Contains("legacy-catalog.materials.update", bffProgram, StringComparison.Ordinal);
        Assert.Contains("RequireAuthorization(\"legacy-catalog.materials.update\")", bffProgram, StringComparison.Ordinal);
        Assert.Contains("MaterialNotFound", resource, StringComparison.Ordinal);
        Assert.Contains("BackToMaterials", resource, StringComparison.Ordinal);
        Assert.Contains("MaterialSaved", resource, StringComparison.Ordinal);
        Assert.Contains("Conflict", resource, StringComparison.Ordinal);
        Assert.Contains("HttpStatusCode.Conflict", page, StringComparison.Ordinal);

        Assert.True(
            File.Exists(Path.Combine(root, "Legacy.Maliev.Intranet", "Pages", "Materials", "View.cshtml")),
            "The compatibility Razor detail/editor fallback must remain in this slice.");
        Assert.True(
            File.Exists(Path.Combine(root, "Legacy.Maliev.Intranet", "Pages", "Materials", "View.cshtml.cs")),
            "The compatibility Razor detail/editor PageModel must remain in this slice.");
    }

    private static string FindRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Legacy.Maliev.Intranet.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new DirectoryNotFoundException("Could not find repository root.");
    }
}
