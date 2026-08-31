namespace Legacy.Maliev.Intranet.Tests;

public sealed class ShadcnStyleSystemContractTests
{
    [Fact]
    public void ClientShell_LoadsPackageAssetsBeforeProductStyles()
    {
        var index = Read("Legacy.Maliev.Intranet.Client", "wwwroot", "index.html");
        var packageBase = index.IndexOf("_content/Maliev.ShadcnBlazor/css/shadcn-base.css", StringComparison.Ordinal);
        var packageForms = index.IndexOf("_content/Maliev.ShadcnBlazor/css/shadcn-forms.css", StringComparison.Ordinal);
        var productStyles = index.IndexOf("css/shadcn.css", StringComparison.Ordinal);
        var generatedStyles = index.IndexOf("Legacy.Maliev.Intranet.Client.styles.css", StringComparison.Ordinal);

        Assert.True(packageBase >= 0);
        Assert.True(packageForms > packageBase);
        Assert.True(productStyles > packageForms);
        Assert.True(generatedStyles > productStyles);
    }

    [Fact]
    public void ProductTokensAliasReleasedPackageTokensWithoutRedefiningThem()
    {
        var tokens = Read("Legacy.Maliev.Intranet.Client", "wwwroot", "css", "design-tokens.css");
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
        Assert.Contains(".legacy-page-container .list-toolbar__grid > *", semantic, StringComparison.Ordinal);
        Assert.Contains("grid-template-columns: minmax(14rem, 1.8fr) minmax(10rem, 1fr) minmax(7rem, 0.5fr) auto", toolbar, StringComparison.Ordinal);
        Assert.Contains("background: var(--shadcn-muted)", toolbar, StringComparison.Ordinal);
        Assert.Contains("border-inline-start: 1px solid var(--shadcn-border)", toolbar, StringComparison.Ordinal);
        Assert.Contains("--shadcn-control-height: 2.75rem", toolbar, StringComparison.Ordinal);
    }

    [Fact]
    public void NamedShellCompositionsUsePackageVariablesForInteractiveAppearance()
    {
        var semantic = Read("Legacy.Maliev.Intranet.Client", "wwwroot", "css", "shadcn.css");
        var search = Read("Legacy.Maliev.Intranet.Client", "Components", "Shell", "LegacyGlobalSearch.razor.css");
        var topBar = Read("Legacy.Maliev.Intranet.Client", "Layout", "LegacyTopBar.razor.css");
        var rail = Read("Legacy.Maliev.Intranet.Client", "Components", "Shell", "LegacyNavigationRail.razor");

        Assert.Contains(".legacy-workspace-frame .legacy-topbar", semantic, StringComparison.Ordinal);
        Assert.DoesNotContain(".legacy-workspace-shell .legacy-topbar", semantic, StringComparison.Ordinal);
        Assert.Contains("<ShadcnSidebarMenuButton", rail, StringComparison.Ordinal);
        Assert.Contains("<ShadcnSidebarMenuSubButton", rail, StringComparison.Ordinal);
        Assert.DoesNotContain(".legacy-navigation-rail .legacy-rail-link.active", semantic, StringComparison.Ordinal);
        Assert.Contains("background: var(--shadcn-popover)", search, StringComparison.Ordinal);
        Assert.Contains("background: var(--shadcn-popover)", topBar, StringComparison.Ordinal);
    }

    [Fact]
    public void ShellUsesTheOfficialBrandAssetWithThemeAwareContrast()
    {
        var topBar = Read("Legacy.Maliev.Intranet.Client", "Layout", "LegacyTopBar.razor");
        var rail = Read("Legacy.Maliev.Intranet.Client", "Components", "Shell", "LegacyNavigationRail.razor");
        var tokens = Read("Legacy.Maliev.Intranet.Client", "wwwroot", "css", "design-tokens.css");

        Assert.DoesNotContain("images/MALIEV_BLACK.svg", topBar, StringComparison.Ordinal);
        Assert.DoesNotContain("images/MALIEV_WHITE.svg", topBar, StringComparison.Ordinal);
        Assert.Contains("src=\"images/MALIEV_BLACK.svg\"", rail, StringComparison.Ordinal);
        Assert.Contains("alt=\"MALIEV\"", rail, StringComparison.Ordinal);
        Assert.DoesNotContain("legacy-rail-brand__mark", rail, StringComparison.Ordinal);
        Assert.Contains("--legacy-brand-logo-filter: invert(1)", tokens, StringComparison.Ordinal);
    }

    private static string Read(params string[] segments) => File.ReadAllText(Path.Combine([FindRoot(), .. segments]));

    private static string FindRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Legacy.Maliev.Intranet.slnx"))) directory = directory.Parent;
        return directory?.FullName ?? throw new DirectoryNotFoundException("Could not find repository root.");
    }
}
