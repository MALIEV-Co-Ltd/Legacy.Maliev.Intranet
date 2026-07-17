using Legacy.Maliev.Intranet.Contracts;
using System.Text.Json;

namespace Legacy.Maliev.Intranet.Tests;

public sealed class MaterialsViewWasmMigrationContractTests
{
    [Fact]
    public void DetailDto_PreservesTheSevenReadOnlyMaterialValuesAndWireNames()
    {
        var detailType = typeof(CatalogMaterialPage).Assembly.GetType(
            "Legacy.Maliev.Intranet.Contracts.CatalogMaterialDetail");

        Assert.NotNull(detailType);
        Assert.Equal(
            [
                "DensityKilogramPerCubicMeter",
                "Id",
                "Machinable",
                "MaterialGroup",
                "MaterialGroupId",
                "MaterialNumber",
                "Name",
                "Printable",
            ],
            detailType.GetProperties().Select(property => property.Name).Order(StringComparer.Ordinal).ToArray());

        var detail = Activator.CreateInstance(
            detailType,
            42,
            7,
            true,
            false,
            "4140",
            "AISI 4140",
            7850m,
            new CatalogMaterialGroup(7, "Steel"));
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

        Assert.Contains("@page \"/Materials/View\"", page, StringComparison.Ordinal);
        Assert.Contains("@attribute [Authorize]", page, StringComparison.Ordinal);
        Assert.Contains("[SupplyParameterFromQuery(Name = \"id\")]", page, StringComparison.Ordinal);
        Assert.Contains("/bff/catalog/materials/", page, StringComparison.Ordinal);
        Assert.Contains("MudProgress", page, StringComparison.Ordinal);
        Assert.Contains("MudAlert", page, StringComparison.Ordinal);
        Assert.Contains("MudGrid", page, StringComparison.Ordinal);
        Assert.Contains("Text[\"Group\"]", page, StringComparison.Ordinal);
        Assert.Contains("Text[\"Name\"]", page, StringComparison.Ordinal);
        Assert.Contains("Text[\"Number\"]", page, StringComparison.Ordinal);
        Assert.Contains("Text[\"Density\"]", page, StringComparison.Ordinal);
        Assert.Contains("Text[\"Machinable\"]", page, StringComparison.Ordinal);
        Assert.Contains("Text[\"Printable\"]", page, StringComparison.Ordinal);
        Assert.DoesNotContain("HttpMethod.Post", page, StringComparison.Ordinal);
        Assert.DoesNotContain("MudForm", page, StringComparison.Ordinal);
        Assert.Contains("MaterialNotFound", resource, StringComparison.Ordinal);
        Assert.Contains("BackToMaterials", resource, StringComparison.Ordinal);

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
