namespace Legacy.Maliev.Intranet.Tests;

public sealed class ShadcnStyleSystemContractTests
{
    [Fact]
    public void ClientShell_LoadsTheSemanticLayerAfterGeneratedFeatureStyles()
    {
        var root = FindRoot();
        var index = Read(root, "Legacy.Maliev.Intranet.Client", "wwwroot", "index.html");

        var generatedStyles = index.IndexOf("Legacy.Maliev.Intranet.Client.styles.css", StringComparison.Ordinal);
        var shadcnStyles = index.IndexOf("css/shadcn.css", StringComparison.Ordinal);

        Assert.True(generatedStyles >= 0, "Generated component styles must be loaded.");
        Assert.True(shadcnStyles > generatedStyles, "The semantic layer must be last so it can normalize every feature component.");
    }

    [Fact]
    public void DesignTokens_ExposeTheOfficialNeutralShadcnSemanticScale()
    {
        var root = FindRoot();
        var tokens = Read(root, "Legacy.Maliev.Intranet.Client", "wwwroot", "css", "design-tokens.css");
        var required = new[]
        {
            "--shadcn-background",
            "--shadcn-foreground",
            "--shadcn-card",
            "--shadcn-popover",
            "--shadcn-primary",
            "--shadcn-primary-foreground",
            "--shadcn-secondary",
            "--shadcn-muted",
            "--shadcn-accent",
            "--shadcn-destructive",
            "--shadcn-border",
            "--shadcn-input",
            "--shadcn-ring",
            "--shadcn-radius",
            "--shadcn-sidebar",
            "--shadcn-sidebar-primary",
            "--shadcn-chart-1",
        };

        Assert.All(required, token => Assert.Contains(token, tokens, StringComparison.Ordinal));
        Assert.Contains("--shadcn-primary: oklch(0.205 0 0)", tokens, StringComparison.Ordinal);
        Assert.Contains("--shadcn-primary-foreground: oklch(0.985 0 0)", tokens, StringComparison.Ordinal);
        Assert.Contains("--shadcn-radius: 0.625rem", tokens, StringComparison.Ordinal);
        Assert.Contains("--shadcn-radius-md: calc(var(--shadcn-radius) * 0.8)", tokens, StringComparison.Ordinal);
        Assert.Contains(":root[data-maliev-theme=\"dark\"]", tokens, StringComparison.Ordinal);
        Assert.Contains("--shadcn-background: oklch(0.145 0 0)", tokens, StringComparison.Ordinal);
    }

    [Fact]
    public void LegacyCompatibilityAliasesResolveToTheSameSemanticPalette()
    {
        var root = FindRoot();
        var tokens = Read(root, "Legacy.Maliev.Intranet.Client", "wwwroot", "css", "design-tokens.css");

        Assert.Contains("--legacy-primary: var(--shadcn-primary)", tokens, StringComparison.Ordinal);
        Assert.Contains("--legacy-primary-soft: var(--shadcn-accent)", tokens, StringComparison.Ordinal);
        Assert.Contains("--maliev-action-primary:       var(--shadcn-primary)", tokens, StringComparison.Ordinal);
        Assert.Contains("--maliev-action-primary-text:  var(--shadcn-primary-foreground)", tokens, StringComparison.Ordinal);
        Assert.Contains("--maliev-focus-color:      var(--shadcn-ring)", tokens, StringComparison.Ordinal);
        Assert.Contains("--maliev-focus-ring:       0 0 0 2px var(--shadcn-background), 0 0 0 4px var(--shadcn-ring)", tokens, StringComparison.Ordinal);
        Assert.Contains("--legacy-focus-ring: 0 0 0 2px var(--shadcn-background), 0 0 0 4px var(--shadcn-ring)", tokens, StringComparison.Ordinal);
    }

    [Fact]
    public void SemanticLayer_NormalizesCoreMudBlazorVariantsAndWorkspacePrimitives()
    {
        var root = FindRoot();
        var css = Read(root, "Legacy.Maliev.Intranet.Client", "wwwroot", "css", "shadcn.css");
        var requiredSelectors = new[]
        {
            ".mud-button-root.mud-button-outlined",
            ".mud-button-root.mud-button-text",
            ".mud-input.mud-input-outlined",
            ".mud-input.mud-input-underline",
            ".legacy-page-container .mud-checkbox > :where(.mud-icon-button-root, .mud-button-root.mud-icon-button)",
            ".mud-popover",
            ".mud-dialog",
            ".mud-table-root",
            ".mud-card",
            ".mud-alert",
            ".mud-chip",
            ".mud-tabs-toolbar",
            ".legacy-navigation-rail .legacy-rail-link.active",
            ".legacy-workspace-shell .legacy-profile",
            ".legacy-workspace-shell .legacy-global-search input.mud-input-slot",
            ".legacy-workspace-shell .legacy-global-search .mud-input-control",
            ".mlv-table",
            "@media (max-width: 600px)",
            "@media (prefers-reduced-motion: reduce)",
            "@media (forced-colors: active)",
        };

        Assert.All(requiredSelectors, selector => Assert.Contains(selector, css, StringComparison.Ordinal));
        Assert.Contains("box-shadow: none !important", css, StringComparison.Ordinal);
        Assert.Contains("font-family: var(--maliev-font-sans) !important", css, StringComparison.Ordinal);
        Assert.Contains("min-height: 2.25rem", css, StringComparison.Ordinal);
        Assert.Contains("var(--shadcn-sidebar-primary)", css, StringComparison.Ordinal);
        Assert.Contains("font-weight: var(--maliev-font-weight-body)", css, StringComparison.Ordinal);
        Assert.Contains("font-weight: var(--maliev-font-weight-heading)", css, StringComparison.Ordinal);

        var quickActions = Read(root, "Legacy.Maliev.Intranet.Client", "Components", "Shell", "LegacyQuickActions.razor.css");
        var navigation = Read(root, "Legacy.Maliev.Intranet.Client", "Components", "Shell", "LegacyNavigationRail.razor.css");
        Assert.Contains("background: var(--shadcn-primary)", quickActions, StringComparison.Ordinal);
        Assert.Contains("color: var(--shadcn-primary-foreground)", quickActions, StringComparison.Ordinal);
        Assert.Contains("background: var(--shadcn-sidebar-primary)", navigation, StringComparison.Ordinal);
        Assert.Contains("color: var(--shadcn-sidebar-primary-foreground)", navigation, StringComparison.Ordinal);
    }

    [Fact]
    public void MainLayout_MapsMudBlazorPaletteToTheSameNeutralSemanticTheme()
    {
        var root = FindRoot();
        var layout = Read(root, "Legacy.Maliev.Intranet.Client", "Layout", "MainLayout.razor");

        Assert.Contains("Primary = \"#171717\"", layout, StringComparison.Ordinal);
        Assert.Contains("Background = \"#ffffff\"", layout, StringComparison.Ordinal);
        Assert.Contains("Primary = \"#e4e4e7\"", layout, StringComparison.Ordinal);
        Assert.Contains("Background = \"#252525\"", layout, StringComparison.Ordinal);
    }

    private static string Read(string root, params string[] segments) =>
        File.ReadAllText(Path.Combine([root, .. segments]));

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
