using System.Text.Json;

namespace Legacy.Maliev.Intranet.Tests;

public sealed class OriginalIntranetRouteClusterContractTests
{
    [Fact]
    public void CustomerAndQuotationRoutes_HaveExactLocalizedBlazorOwners_WithoutClaimingBrowserAcceptance()
    {
        var root = FindRoot();
        var manifestPath = Path.Combine(root, "docs", "original-intranet-customer-quotation-routes.json");
        using var document = JsonDocument.Parse(File.ReadAllText(manifestPath));
        var manifest = document.RootElement;

        Assert.Equal("4198baa6b0e7903f2b9b6e3d5d68f9d2c2b5b0db", manifest.GetProperty("sourceCommit").GetString());
        Assert.Equal("0bb08d38a7064ba28701fa6c7bbd518f689ae19f", manifest.GetProperty("targetCommit").GetString());

        var routes = manifest.GetProperty("routes").EnumerateArray().ToArray();
        Assert.Equal(9, routes.Length);
        Assert.Equal(9, routes.Select(route => route.GetProperty("source").GetString()).Distinct(StringComparer.OrdinalIgnoreCase).Count());

        foreach (var route in routes)
        {
            var source = Assert.IsType<string>(route.GetProperty("source").GetString());
            var target = Assert.IsType<string>(route.GetProperty("target").GetString());
            var owner = Assert.IsType<string>(route.GetProperty("owner").GetString());
            Assert.Equal(source, target);
            Assert.Equal("route-mapped-browser-unverified", route.GetProperty("status").GetString());
            Assert.True(
                source.StartsWith("/Customers/", StringComparison.OrdinalIgnoreCase)
                || source.StartsWith("/Quotation", StringComparison.OrdinalIgnoreCase),
                $"Unexpected route outside the scoped cluster: {source}.");

            var ownerPath = Path.Combine(root, owner.Replace('/', Path.DirectorySeparatorChar));
            var content = File.ReadAllText(ownerPath);
            Assert.Contains($"@page \"{target}\"", content, StringComparison.Ordinal);
            Assert.Contains("@attribute [Authorize]", content, StringComparison.Ordinal);
            Assert.True(File.Exists(Path.ChangeExtension(ownerPath, ".resx")), $"Missing English resources for {target}.");
            Assert.True(File.Exists(Path.ChangeExtension(ownerPath, ".th.resx")), $"Missing Thai resources for {target}.");
        }
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
