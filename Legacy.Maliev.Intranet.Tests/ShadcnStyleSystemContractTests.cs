namespace Legacy.Maliev.Intranet.Tests;

public sealed class ShadcnStyleSystemContractTests
{
    [Fact]
    public void ClientShell_LoadsPackageAssetsBeforeProductStyles()
    {
        var index = Read("Legacy.Maliev.Intranet.Client", "wwwroot", "index.html");
        var packageBase = index.IndexOf("_content/Maliev.ShadcnBlazor/css/shadcn-base.css", StringComparison.Ordinal);
        var packageAdapter = index.IndexOf("_content/Maliev.ShadcnBlazor/css/shadcn-mudblazor.css", StringComparison.Ordinal);
        var productStyles = index.IndexOf("css/shadcn.css", StringComparison.Ordinal);
        var generatedStyles = index.IndexOf("Legacy.Maliev.Intranet.Client.styles.css", StringComparison.Ordinal);

        Assert.True(packageBase >= 0);
        Assert.True(packageAdapter > packageBase);
        Assert.True(productStyles > packageAdapter);
        Assert.True(generatedStyles > productStyles);
    }

    [Fact]
    public void PackageOwnsCanonicalTokensAndReusableMudAppearance()
    {
        var tokens = Read("Legacy.Maliev.Intranet.Client", "wwwroot", "css", "design-tokens.css");
        var packageBase = Read("Maliev.ShadcnBlazor", "wwwroot", "css", "shadcn-base.css");
        var adapter = Read("Maliev.ShadcnBlazor", "wwwroot", "css", "shadcn-mudblazor.css");

        Assert.Contains("--shadcn-background:", packageBase, StringComparison.Ordinal);
        Assert.Contains("--shadcn-control-height: 2.25rem", packageBase, StringComparison.Ordinal);
        Assert.Contains(".mud-button-root", adapter, StringComparison.Ordinal);
        Assert.DoesNotContain("--shadcn-background:", tokens, StringComparison.Ordinal);
        Assert.Contains("--legacy-primary: var(--shadcn-primary)", tokens, StringComparison.Ordinal);
        Assert.Contains("--maliev-surface-card: var(--shadcn-card)", tokens, StringComparison.Ordinal);
    }

    [Fact]
    public void ProductStylesKeepNamedShellAndOperationsGeometry()
    {
        var semantic = Read("Legacy.Maliev.Intranet.Client", "wwwroot", "css", "shadcn.css");
        var toolbar = Read("Legacy.Maliev.Intranet.Client.Shared", "Components", "ListToolbar.razor.css");

        Assert.Contains(".legacy-page-container .operations-page-header", semantic, StringComparison.Ordinal);
        Assert.Contains(".legacy-page-container .list-toolbar__grid > .mud-input-control", semantic, StringComparison.Ordinal);
        Assert.Contains("grid-template-columns: minmax(15rem, 1.7fr) minmax(12rem, 1.1fr) minmax(8rem, 0.6fr) auto", toolbar, StringComparison.Ordinal);
        Assert.DoesNotContain("height: 2.75rem", toolbar, StringComparison.Ordinal);
    }

    [Fact]
    public void ShellLogosUseOfficialLightAndDarkSvgAssetsWithoutFilters()
    {
        var topBar = Read("Legacy.Maliev.Intranet.Client", "Layout", "LegacyTopBar.razor");
        var rail = Read("Legacy.Maliev.Intranet.Client", "Components", "Shell", "LegacyNavigationRail.razor");
        var appCss = Read("Legacy.Maliev.Intranet.Client", "wwwroot", "css", "app.css");

        Assert.Contains("images/MALIEV_BLACK.svg", topBar, StringComparison.Ordinal);
        Assert.Contains("images/MALIEV_WHITE.svg", topBar, StringComparison.Ordinal);
        Assert.Contains("images/MALIEV_BLACK.svg", rail, StringComparison.Ordinal);
        Assert.Contains("images/MALIEV_WHITE.svg", rail, StringComparison.Ordinal);
        Assert.Contains(":root[data-maliev-theme=\"dark\"] .legacy-logo-image--dark", appCss, StringComparison.Ordinal);
        Assert.DoesNotContain("filter: invert", appCss, StringComparison.OrdinalIgnoreCase);
    }

    private static string Read(params string[] segments) => File.ReadAllText(Path.Combine([FindRoot(), .. segments]));

    private static string FindRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Legacy.Maliev.Intranet.slnx"))) directory = directory.Parent;
        return directory?.FullName ?? throw new DirectoryNotFoundException("Could not find repository root.");
    }
}
