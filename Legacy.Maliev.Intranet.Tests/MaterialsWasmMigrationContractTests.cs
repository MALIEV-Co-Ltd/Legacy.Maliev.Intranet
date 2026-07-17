namespace Legacy.Maliev.Intranet.Tests;

public sealed class MaterialsWasmMigrationContractTests
{
    [Fact]
    public void MaterialsSlice_IsLazyAuthorizedAndPreservesTheLegacyBoundary()
    {
        var root = FindRoot();
        var featureProject = Path.Combine(root, "Legacy.Maliev.Intranet.Client.Features.Catalog", "Legacy.Maliev.Intranet.Client.Features.Catalog.csproj");
        var featurePage = Path.Combine(root, "Legacy.Maliev.Intranet.Client.Features.Catalog", "Pages", "Materials.razor");
        var bffProgram = File.ReadAllText(Path.Combine(root, "Legacy.Maliev.Intranet.Bff", "Program.cs"));
        var app = File.ReadAllText(Path.Combine(root, "Legacy.Maliev.Intranet.Client", "App.razor"));
        var solution = File.ReadAllText(Path.Combine(root, "Legacy.Maliev.Intranet.slnx"));

        Assert.True(File.Exists(featureProject), "The lazy Catalog feature assembly is missing.");
        Assert.True(File.Exists(featurePage), "The WASM Materials route is missing.");
        var page = File.ReadAllText(featurePage);
        Assert.Contains("@page \"/Materials/Index\"", page, StringComparison.Ordinal);
        Assert.Contains("[Authorize", page, StringComparison.Ordinal);
        Assert.Contains("MaterialId_Descending", page, StringComparison.Ordinal);
        Assert.Contains("/bff/catalog/materials", page, StringComparison.Ordinal);
        Assert.Contains("MudTable", page, StringComparison.Ordinal);
        Assert.Contains("MudProgress", page, StringComparison.Ordinal);
        Assert.Contains("MudAlert", page, StringComparison.Ordinal);

        Assert.Contains("legacy-catalog.materials.read", bffProgram, StringComparison.Ordinal);
        Assert.Contains("MapGet(\"/bff/catalog/materials\"", bffProgram, StringComparison.Ordinal);
        Assert.Contains("GetAccessTokenAsync", bffProgram, StringComparison.Ordinal);
        Assert.Contains("Services:Catalog", bffProgram, StringComparison.Ordinal);
        Assert.DoesNotContain("DbContext", bffProgram, StringComparison.Ordinal);

        Assert.Contains("AdditionalAssemblies", app, StringComparison.Ordinal);
        Assert.Contains("Legacy.Maliev.Intranet.Client.Features.Catalog", app, StringComparison.Ordinal);
        Assert.Contains("Legacy.Maliev.Intranet.Client.Features.Catalog", solution, StringComparison.Ordinal);

        Assert.True(File.Exists(Path.Combine(root, "Legacy.Maliev.Intranet", "Pages", "Materials", "Index.cshtml")),
            "The compatibility Razor fallback must remain in this slice.");
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
