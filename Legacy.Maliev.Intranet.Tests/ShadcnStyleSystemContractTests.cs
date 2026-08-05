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
    public void DesignTokens_ExposeComposableShadcnSemanticsWithoutReplacingBrandActions()
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
        };

        Assert.All(required, token => Assert.Contains(token, tokens, StringComparison.Ordinal));
        Assert.Contains("--shadcn-primary: var(--legacy-primary)", tokens, StringComparison.Ordinal);
        Assert.Contains("--shadcn-radius: 0.5rem", tokens, StringComparison.Ordinal);
        Assert.Contains(":root[data-maliev-theme=\"dark\"]", tokens, StringComparison.Ordinal);
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
            ".mlv-table",
            "@media (max-width: 600px)",
            "@media (prefers-reduced-motion: reduce)",
            "@media (forced-colors: active)",
        };

        Assert.All(requiredSelectors, selector => Assert.Contains(selector, css, StringComparison.Ordinal));
        Assert.Contains("box-shadow: none !important", css, StringComparison.Ordinal);
        Assert.Contains("font-weight: var(--maliev-font-weight-body)", css, StringComparison.Ordinal);
        Assert.Contains("font-weight: var(--maliev-font-weight-heading)", css, StringComparison.Ordinal);
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
