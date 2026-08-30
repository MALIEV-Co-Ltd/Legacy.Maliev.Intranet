using System.Xml.Linq;

namespace Legacy.Maliev.Intranet.Tests;

public sealed class IntranetParityContractTests
{
    [Fact]
    public void Shell_LogosShareCanonicalVisibleGeometry()
    {
        var root = FindRoot();
        var black = XDocument.Load(Path.Combine(root, "Legacy.Maliev.Intranet.Client", "wwwroot", "images", "MALIEV_BLACK.svg"));
        var white = XDocument.Load(Path.Combine(root, "Legacy.Maliev.Intranet.Client", "wwwroot", "images", "MALIEV_WHITE.svg"));
        XNamespace svg = "http://www.w3.org/2000/svg";
        var blackTransform = black.Descendants(svg + "g").Select(group => group.Attribute("transform")?.Value).FirstOrDefault(transform => transform is not null);
        var whiteTransform = white.Descendants(svg + "g").Select(group => group.Attribute("transform")?.Value).FirstOrDefault(transform => transform is not null);
        var blackPath = black.Descendants(svg + "path").Single();
        var whitePath = white.Descendants(svg + "path").Single();

        Assert.Equal(black.Root!.Attribute("viewBox")!.Value, white.Root!.Attribute("viewBox")!.Value);
        Assert.NotNull(blackTransform);
        Assert.NotNull(whiteTransform);
        Assert.Equal(blackTransform, whiteTransform);
        Assert.Equal(blackPath.Attribute("d")!.Value, whitePath.Attribute("d")!.Value);
        Assert.Equal("#ffffff", whitePath.Attribute("fill")!.Value);
    }

    [Fact]
    public void Shell_UsesTheWorkspaceNavigationAndResponsiveMobileSurface()
    {
        var root = FindRoot();
        var layout = File.ReadAllText(Path.Combine(root, "Legacy.Maliev.Intranet.Client", "Layout", "MainLayout.razor"));
        var topbar = File.ReadAllText(Path.Combine(root, "Legacy.Maliev.Intranet.Client", "Layout", "LegacyTopBar.razor"));
        var css = File.ReadAllText(Path.Combine(root, "Legacy.Maliev.Intranet.Client", "Layout", "LegacyTopBar.razor.css"));
        var rail = File.ReadAllText(Path.Combine(root, "Legacy.Maliev.Intranet.Client", "Components", "Shell", "LegacyNavigationRail.razor"));
        var railCss = File.ReadAllText(Path.Combine(root, "Legacy.Maliev.Intranet.Client", "Components", "Shell", "LegacyNavigationRail.razor.css"));
        var navigation = File.ReadAllText(Path.Combine(root, "Legacy.Maliev.Intranet.Client", "Layout", "LegacyAppNavigation.cs"));
        var blackLogo = Path.Combine(root, "Legacy.Maliev.Intranet.Client", "wwwroot", "images", "MALIEV_BLACK.svg");
        var whiteLogo = Path.Combine(root, "Legacy.Maliev.Intranet.Client", "wwwroot", "images", "MALIEV_WHITE.svg");

        Assert.Contains("<LegacyTopBar", layout, StringComparison.Ordinal);
        Assert.Contains("<LegacyNavigationRail Session=\"session\" />", layout, StringComparison.Ordinal);
        Assert.Contains("IsDrawer=\"true\"", layout, StringComparison.Ordinal);
        Assert.Contains("OnSignOut=\"SignOutAsync\"", layout, StringComparison.Ordinal);
        Assert.True(File.Exists(blackLogo));
        Assert.True(File.Exists(whiteLogo));
        Assert.Contains("legacy-logo-image--light", topbar, StringComparison.Ordinal);
        Assert.Contains("legacy-logo-image--dark", topbar, StringComparison.Ordinal);
        Assert.Contains("legacy-logo-image--light", rail, StringComparison.Ordinal);
        Assert.Contains("legacy-logo-image--dark", rail, StringComparison.Ordinal);
        Assert.DoesNotContain("legacy-workspace-label", topbar, StringComparison.Ordinal);
        Assert.Contains("aria-label", topbar, StringComparison.Ordinal);
        Assert.Contains("aria-controls=\"legacy-navigation-rail-drawer\"", topbar, StringComparison.Ordinal);
        Assert.Contains("Active=\"@IsItemPageCurrent", rail, StringComparison.Ordinal);
        Assert.Contains("Navigation.LocationChanged", layout, StringComparison.Ordinal);
        Assert.Contains("Escape", topbar, StringComparison.Ordinal);
        Assert.Contains("@media (max-width: 1180px)", railCss, StringComparison.Ordinal);
        Assert.Contains("@media (max-width: 720px)", css, StringComparison.Ordinal);
        Assert.Contains("@media (max-width: 420px)", css, StringComparison.Ordinal);
        Assert.Contains("/customers", navigation, StringComparison.Ordinal);
        Assert.Contains("/sales/orders", navigation, StringComparison.Ordinal);
        Assert.Contains("/purchasing", navigation, StringComparison.Ordinal);
        Assert.Contains("/mfg/materials", navigation, StringComparison.Ordinal);
        Assert.Contains("/hr/profile", File.ReadAllText(Path.Combine(root, "Legacy.Maliev.Intranet", "LegacyRoutes.cs")), StringComparison.Ordinal);
    }

    [Fact]
    public void Login_UsesAnEmptyAuthLayoutAndWorkspaceShellProvidesSkipTarget()
    {
        var root = FindRoot();
        var login = File.ReadAllText(Path.Combine(root, "Legacy.Maliev.Intranet.Client", "Pages", "Login.razor"));
        var emptyLayout = File.ReadAllText(Path.Combine(root, "Legacy.Maliev.Intranet.Client", "Layout", "EmptyLayout.razor"));
        var mainLayout = File.ReadAllText(Path.Combine(root, "Legacy.Maliev.Intranet.Client", "Layout", "MainLayout.razor"));

        Assert.Contains("@layout Layout.EmptyLayout", login, StringComparison.Ordinal);
        Assert.DoesNotContain("<LegacyTopBar", emptyLayout, StringComparison.Ordinal);
        Assert.Contains("id=\"main-content\"", emptyLayout, StringComparison.Ordinal);
        Assert.Contains("legacy-skip-link", mainLayout, StringComparison.Ordinal);
        Assert.Contains("id=\"main-content\"", mainLayout, StringComparison.Ordinal);
    }

    [Fact]
    public void Typography_UsesTheCurrentWorkspaceFontContract()
    {
        var root = FindRoot();
        var index = File.ReadAllText(Path.Combine(root, "Legacy.Maliev.Intranet.Client", "wwwroot", "index.html"));
        var css = File.ReadAllText(Path.Combine(root, "Legacy.Maliev.Intranet.Client", "wwwroot", "css", "app.css"));

        Assert.Contains("css/ibm-plex-sans-thai.css", index, StringComparison.Ordinal);
        Assert.Contains("'IBM Plex Sans Thai', sans-serif", index, StringComparison.Ordinal);
        Assert.Contains("var(--maliev-font-sans)", css, StringComparison.Ordinal);
        Assert.DoesNotContain("fonts.googleapis.com", index, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Dashboard_StaysBehindTheSameOriginBffAndPermissionScopedAggregator()
    {
        var root = FindRoot();
        var program = File.ReadAllText(Path.Combine(root, "Legacy.Maliev.Intranet.Bff", "Program.cs"));
        var aggregator = File.ReadAllText(Path.Combine(root, "Legacy.Maliev.Intranet.Bff", "Dashboard", "LegacyDashboardAggregator.cs"));
        var page = File.ReadAllText(Path.Combine(root, "Legacy.Maliev.Intranet.Client", "Pages", "Dashboard.razor"));

        Assert.Contains("MapGet(\"/bff/dashboard\"", program, StringComparison.Ordinal);
        Assert.Contains(".RequireAuthorization()", program, StringComparison.Ordinal);
        Assert.Contains("FindAll(\"permissions\")", aggregator, StringComparison.Ordinal);
        Assert.Contains("ReadFromJsonAsync", aggregator, StringComparison.Ordinal);
        Assert.DoesNotContain("DbContext", aggregator, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("GetForPresentationAsync(\"/bff/dashboard\"", page, StringComparison.Ordinal);
        Assert.DoesNotContain("AccessToken", page, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("RefreshToken", page, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void DashboardMetricLinks_AreTouchSized()
    {
        var root = FindRoot();
        var styles = File.ReadAllText(Path.Combine(root, "Legacy.Maliev.Intranet.Client", "Components", "Dashboard", "DashboardMetricCard.razor.css"));

        Assert.Contains(".dashboard-metric-link", styles, StringComparison.Ordinal);
        Assert.Contains("min-height: 44px", styles, StringComparison.Ordinal);
    }

    [Fact]
    public void SessionProjection_PreservesValidatedPermissionsWithoutExposingTokens()
    {
        var root = FindRoot();
        var contract = File.ReadAllText(Path.Combine(root, "Legacy.Maliev.Intranet.Contracts", "EmployeeSessionSummary.cs"));
        var program = File.ReadAllText(Path.Combine(root, "Legacy.Maliev.Intranet.Bff", "Program.cs"));
        var provider = File.ReadAllText(Path.Combine(root, "Legacy.Maliev.Intranet.Client", "EmployeeAuthenticationStateProvider.cs"));

        Assert.Contains("IReadOnlyList<string>? Permissions = null", contract, StringComparison.Ordinal);
        Assert.Contains("FindAll(\"permissions\")", program, StringComparison.Ordinal);
        Assert.Contains("Distinct(StringComparer.Ordinal)", program, StringComparison.Ordinal);
        Assert.Contains("new Claim(\"permissions\", permission)", provider, StringComparison.Ordinal);
        Assert.DoesNotContain("AccessToken", contract, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("RefreshToken", contract, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void CurrentWorkspaceAliases_AreDeclaredAndLazyLoadedByTheMatchingFeatureAssembly()
    {
        var root = FindRoot();
        var app = File.ReadAllText(Path.Combine(root, "Legacy.Maliev.Intranet.Client", "App.razor"));
        var routes = File.ReadAllText(Path.Combine(root, "Legacy.Maliev.Intranet", "LegacyRoutes.cs"));

        foreach (var alias in Legacy.Maliev.Intranet.LegacyRoutes.CompatibilityAliases)
        {
            Assert.Contains($"\"{alias}\"", routes, StringComparison.Ordinal);
        }

        Assert.Contains("sales/customers", app, StringComparison.Ordinal);
        Assert.Contains("sales/orders", app, StringComparison.Ordinal);
        Assert.Contains("purchasing/suppliers", app, StringComparison.Ordinal);
        Assert.Contains("finance/invoices", app, StringComparison.Ordinal);
        Assert.Contains("mfg/materials", app, StringComparison.Ordinal);
    }

    [Fact]
    public void NumericDetailAliases_RedirectOnlyToLegacyOwnedQueryContracts()
    {
        var root = FindRoot();
        var redirect = File.ReadAllText(Path.Combine(root, "Legacy.Maliev.Intranet.Client", "Pages", "CompatibilityDetailRedirect.razor"));
        var routes = File.ReadAllText(Path.Combine(root, "Legacy.Maliev.Intranet", "LegacyRoutes.cs"));

        Assert.Contains("@page \"/sales/orders/{Id:int}\"", redirect, StringComparison.Ordinal);
        Assert.Contains("@page \"/purchasing/{Id:int}\"", redirect, StringComparison.Ordinal);
        Assert.Contains("@page \"/mfg/procurement/{Id:int}\"", redirect, StringComparison.Ordinal);
        Assert.Contains("Navigation.NavigateTo($\"{legacyPath}?id={Id}\", replace: true)", redirect, StringComparison.Ordinal);
        Assert.Contains("@attribute [Authorize]", redirect, StringComparison.Ordinal);
        Assert.DoesNotContain("Guid", redirect, StringComparison.Ordinal);

        Assert.Contains("\"/sales/orders/{Id:int}\"", routes, StringComparison.Ordinal);
        Assert.Contains("\"/purchasing/{Id:int}\"", routes, StringComparison.Ordinal);
        Assert.Contains("\"/mfg/procurement/{Id:int}\"", routes, StringComparison.Ordinal);
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
