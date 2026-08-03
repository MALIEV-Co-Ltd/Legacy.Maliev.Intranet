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

    [Fact]
    public void Navigation_PreservesLegacyCatalogButUsesPermissionAwareActivation()
    {
        var root = FindRoot();
        var navigation = File.ReadAllText(Path.Combine(
            root,
            "Legacy.Maliev.Intranet.Client",
            "Layout",
            "LegacyAppNavigation.cs"));
        var rail = File.ReadAllText(Path.Combine(
            root,
            "Legacy.Maliev.Intranet.Client",
            "Components",
            "Shell",
            "LegacyNavigationRail.razor"));
        var css = File.ReadAllText(Path.Combine(
            root,
            "Legacy.Maliev.Intranet.Client",
            "Layout",
            "LegacyTopBar.razor.css"));

        Assert.Contains("RequiredPermission", navigation, StringComparison.Ordinal);
        Assert.Contains("legacy.orders.read", navigation, StringComparison.Ordinal);
        Assert.Contains("LegacyNavigationAuthorization.IsEnabled", rail, StringComparison.Ordinal);
        Assert.Contains("@if (IsItemEnabled(item))", rail, StringComparison.Ordinal);
        Assert.DoesNotContain("aria-disabled=\"true\"", rail, StringComparison.Ordinal);
        Assert.DoesNotContain(".legacy-mobile-link.disabled", css, StringComparison.Ordinal);
    }

    [Fact]
    public void OwnerWildcardSemantics_MatchQuotationAndPurchaseOrderReadPolicies()
    {
        var root = FindRoot();
        var bff = File.ReadAllText(Path.Combine(root, "Legacy.Maliev.Intranet.Bff", "Program.cs"));

        Assert.Contains("LegacyEmployeePermissions.QuotationRequestsRead, policy => policy", bff, StringComparison.Ordinal);
        Assert.Contains("LegacyEmployeePermissions.PurchaseOrdersRead, policy => policy", bff, StringComparison.Ordinal);
        Assert.True(
            bff.Split("LegacyNavigationAuthorization.IsEnabled", StringSplitOptions.None).Length - 1 >= 3,
            "Navigation and protected read policies must share owner/wildcard semantics.");
    }

    [Fact]
    public void DesktopNavigation_UsesPersistentGroupedRailForLegacyWorkflows()
    {
        var root = FindRoot();
        var navigation = File.ReadAllText(Path.Combine(
            root,
            "Legacy.Maliev.Intranet.Client",
            "Layout",
            "LegacyAppNavigation.cs"));
        var rail = File.ReadAllText(Path.Combine(
            root,
            "Legacy.Maliev.Intranet.Client",
            "Components",
            "Shell",
            "LegacyNavigationRail.razor"));

        Assert.Contains("Description", navigation, StringComparison.Ordinal);
        Assert.Contains("LegacyAppNavigation.Groups", rail, StringComparison.Ordinal);
        Assert.Contains("legacy-rail-group", rail, StringComparison.Ordinal);
        Assert.Contains("aria-current", rail, StringComparison.Ordinal);
        Assert.DoesNotContain("legacy-nav-more-trigger", rail, StringComparison.Ordinal);

        // The primary desktop groups mirror current workspace ordering while
        // retaining only contracts that are actually owned by the migration.
        Assert.Contains("new(\"Sales\"", navigation, StringComparison.Ordinal);
        Assert.Contains("new(\"Finance\"", navigation, StringComparison.Ordinal);
        Assert.Contains("new(\"Manufacturing\"", navigation, StringComparison.Ordinal);
        Assert.Contains("new(\"Purchasing\"", navigation, StringComparison.Ordinal);
        Assert.DoesNotContain("/sales/projects", navigation, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("/commerce/", navigation, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("/finance/delivery-notes", navigation, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("/iam", navigation, StringComparison.OrdinalIgnoreCase);
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
