using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using Legacy.Maliev.Intranet.Contracts;

namespace Legacy.Maliev.Intranet.Tests;

public sealed class MaterialsCreateWasmMigrationContractTests
{
    [Fact]
    public void CreateDto_PreservesLegacyValidationAndCompleteWireShape()
    {
        var type = typeof(CatalogMaterialPage).Assembly.GetType(
            "Legacy.Maliev.Intranet.Contracts.CatalogMaterialUpsertRequest");

        Assert.NotNull(type);
        Assert.Equal(
            [
                "Afnor", "Aisi", "Ams", "Astm", "Bts", "Comment", "CurrencyId",
                "DensityKilogramPerCubicMeter", "Din", "En", "HardnessBrinell",
                "HardnessKnoop", "HardnessRockwellA", "HardnessRockwellB",
                "HardnessRockwellC", "HardnessVickers", "Jis", "MachinabilityPercent",
                "Machinable", "ManufacturerReference", "MaterialGroupId", "MaterialNumber",
                "Name", "PricePerKilogram", "Printable", "Sae", "ShearModulusGigaPascal",
                "Sis", "TensileStrengthUltimateGigaPascal",
                "TensileStrengthYieldMegaPascal", "ThermalConductivityWattPerMeterKelvin",
                "Uni", "Uns", "Url",
            ],
            type.GetProperties().Select(property => property.Name).Order(StringComparer.Ordinal).ToArray());

        var invalid = Activator.CreateInstance(type)!;
        var validation = new List<ValidationResult>();
        Assert.False(Validator.TryValidateObject(invalid, new ValidationContext(invalid), validation, true));
        Assert.Contains(validation, result => result.MemberNames.Contains("Name", StringComparer.Ordinal));
        Assert.Contains(validation, result => result.MemberNames.Contains("MaterialGroupId", StringComparer.Ordinal));

        var json = JsonSerializer.SerializeToElement(invalid, type, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.True(json.TryGetProperty("materialGroupId", out _));
        Assert.True(json.TryGetProperty("thermalConductivityWattPerMeterKelvin", out _));
        Assert.True(json.TryGetProperty("currencyId", out _));
    }

    [Fact]
    public void MaterialsCreate_IsLazyAuthorizedLocalizedCsrfProtectedAndKeepsRazorFallback()
    {
        var root = FindRoot();
        var pagePath = Path.Combine(
            root,
            "Legacy.Maliev.Intranet.Client.Features.Catalog",
            "Pages",
            "MaterialCreate.razor");
        var resourcePath = Path.Combine(
            root,
            "Legacy.Maliev.Intranet.Client.Features.Catalog",
            "Pages",
            "MaterialCreate.resx");

        Assert.True(File.Exists(pagePath), "The lazy WASM Materials/Create route is missing.");
        Assert.True(File.Exists(resourcePath), "The Materials/Create localization resource is missing.");
        var page = File.ReadAllText(pagePath);
        var resource = File.ReadAllText(resourcePath);

        Assert.Contains("@page \"/Materials/Create\"", page, StringComparison.Ordinal);
        Assert.Contains("@attribute [Authorize]", page, StringComparison.Ordinal);
        Assert.Contains("MudForm", page, StringComparison.Ordinal);
        Assert.Contains("/bff/catalog/material-groups", page, StringComparison.Ordinal);
        Assert.Contains("/bff/catalog/currencies", page, StringComparison.Ordinal);
        Assert.Contains("HttpMethod.Post", page, StringComparison.Ordinal);
        Assert.Contains("/bff/catalog/materials", page, StringComparison.Ordinal);
        Assert.Contains("X-CSRF-TOKEN", page, StringComparison.Ordinal);
        Assert.Contains("submitting", page, StringComparison.Ordinal);
        Assert.Contains("Disabled=\"@submitting\"", page, StringComparison.Ordinal);
        Assert.Contains("MudAlert", page, StringComparison.Ordinal);
        Assert.Contains("Text[\"CreateMaterial\"]", page, StringComparison.Ordinal);
        Assert.Contains("CreateMaterial", resource, StringComparison.Ordinal);
        Assert.Contains("CreateFailed", resource, StringComparison.Ordinal);

        Assert.True(
            File.Exists(Path.Combine(root, "Legacy.Maliev.Intranet", "Pages", "Materials", "Create.cshtml")),
            "The compatibility Razor create fallback must remain in this slice.");
        Assert.True(
            File.Exists(Path.Combine(root, "Legacy.Maliev.Intranet", "Pages", "Materials", "Create.cshtml.cs")),
            "The compatibility Razor create PageModel must remain in this slice.");
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
