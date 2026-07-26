using System.Text.RegularExpressions;

namespace Legacy.Maliev.Intranet.Tests;

public sealed class LegacyNavigationContractTests
{
    private static readonly string[] ExpectedLegacyOwnedLinks =
    [
        "/Dashboard",
        "/customers",
        "/customers/new",
        "/sales/orders",
        "/Orders/Create",
        "/QuotationRequests/Index",
        "/Quotations/Index",
        "/Quotations/Create",
        "/accounting",
        "/finance/invoices",
        "/accounting/new",
        "/Finances/Index",
        "/Finances/Create",
        "/Finances/NetProfitChart",
        "/Finances/YearlyActivityChart",
        "/purchasing",
        "/purchasing/suppliers",
        "/purchasing/new",
        "/Suppliers/Create",
        "/mfg/materials",
        "/Materials/Create",
        "/Employees/Index",
        "/hr/profile",
        "/Server/ErrorReport",
    ];

    [Fact]
    public void Navigation_ExposesEveryLegacyOwnedWorkspaceWorkflow()
    {
        var root = FindRoot();
        var source = File.ReadAllText(Path.Combine(
            root,
            "Legacy.Maliev.Intranet.Client",
            "Layout",
            "LegacyAppNavigation.cs"));
        var links = ExtractLinks(source);

        Assert.Equal(ExpectedLegacyOwnedLinks.Length, links.Count);
        Assert.Equal(ExpectedLegacyOwnedLinks.Length, links.Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.All(ExpectedLegacyOwnedLinks, expected => Assert.Contains(expected, links, StringComparer.OrdinalIgnoreCase));
    }

    [Fact]
    public void Navigation_DoesNotExposeCurrentOnlyRoutesWithoutLegacyContracts()
    {
        var root = FindRoot();
        var source = File.ReadAllText(Path.Combine(
            root,
            "Legacy.Maliev.Intranet.Client",
            "Layout",
            "LegacyAppNavigation.cs"));
        var links = ExtractLinks(source);

        Assert.DoesNotContain("/sales/projects", links, StringComparer.OrdinalIgnoreCase);
        Assert.DoesNotContain("/commerce/catalog", links, StringComparer.OrdinalIgnoreCase);
        Assert.DoesNotContain("/finance/delivery-notes", links, StringComparer.OrdinalIgnoreCase);
        Assert.DoesNotContain("/mfg/equipment", links, StringComparer.OrdinalIgnoreCase);
        Assert.DoesNotContain("/iam", links, StringComparer.OrdinalIgnoreCase);
    }

    private static IReadOnlyList<string> ExtractLinks(string source)
    {
        var matches = Regex.Matches(
            source,
            "new\\(\\\"[^\\\"]+\\\",\\s*\\\"(?<href>[^\\\"]+)\\\"",
            RegexOptions.CultureInvariant);

        return matches
            .Select(match => match.Groups["href"].Value)
            .ToArray();
    }

    private static string FindRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Legacy.Maliev.Intranet.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new DirectoryNotFoundException();
    }
}
