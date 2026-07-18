namespace Legacy.Maliev.Intranet.Tests;

public sealed class QuotationEstimateWasmMigrationContractTests
{
    [Fact]
    public void EstimatePage_IsLazyLocalizedAuthorizedAccessibleAndPreservesRollback()
    {
        var root = FindRoot();
        var pagePath = Path.Combine(root, "Legacy.Maliev.Intranet.Client.Features.Quotations", "Pages", "Quotations", "Estimate.razor");
        var resourcePath = Path.ChangeExtension(pagePath, ".resx");

        Assert.True(File.Exists(pagePath));
        Assert.True(File.Exists(resourcePath));
        Assert.True(File.Exists(Path.ChangeExtension(pagePath, ".th.resx")));
        var page = File.ReadAllText(pagePath);
        Assert.Contains("@page \"/Quotations/Estimate\"", page, StringComparison.Ordinal);
        Assert.Contains("@attribute [Authorize]", page, StringComparison.Ordinal);
        Assert.Contains("CncEstimateCalculator.Calculate", page, StringComparison.Ordinal);
        Assert.Contains("SetMachineCostPerHour", page, StringComparison.Ordinal);
        Assert.Contains("SetMaterialCostPerPart", page, StringComparison.Ordinal);
        Assert.Contains("ApplyCalculatedValues", page, StringComparison.Ordinal);
        Assert.Contains("MudTabs", page, StringComparison.Ordinal);
        Assert.Contains("aria-live=\"polite\"", page, StringComparison.Ordinal);
        Assert.DoesNotContain("jquery", page, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("IJSRuntime", page, StringComparison.Ordinal);
        Assert.DoesNotContain("/bff/", page, StringComparison.OrdinalIgnoreCase);

        var rollbackRoutes = File.ReadAllText(Path.Combine(root, "Legacy.Maliev.Intranet", "LegacyRoutes.cs"));
        Assert.Contains("/Quotations/Estimate", rollbackRoutes, StringComparison.Ordinal);
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
