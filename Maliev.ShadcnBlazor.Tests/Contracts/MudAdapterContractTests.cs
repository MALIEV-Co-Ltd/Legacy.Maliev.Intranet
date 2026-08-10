namespace Maliev.ShadcnBlazor.Tests.Contracts;

public sealed class MudAdapterContractTests
{
    internal static readonly string[] ProductionTypes =
    [
        "MudAlert", "MudBreadcrumbs", "MudButton", "MudChart", "MudCheckBox", "MudChip",
        "MudContainer", "MudDatePicker", "MudDialogProvider", "MudDivider", "MudExpansionPanel",
        "MudExpansionPanels", "MudForm", "MudGrid", "MudIcon", "MudIconButton", "MudItem",
        "MudLayout", "MudLink", "MudList", "MudListItem", "MudMainContent", "MudNumericField",
        "MudPaper", "MudPopoverProvider", "MudProgressCircular", "MudProgressLinear", "MudSelect",
        "MudSelectItem", "MudSimpleTable", "MudSkeleton", "MudSnackbarProvider", "MudStack",
        "MudTable", "MudTabPanel", "MudTabs", "MudTd", "MudText", "MudTextField", "MudTh",
        "MudThemeProvider"
    ];

    [Fact]
    public void ProductionInventoryIsFrozenAtFortyOneUniqueTypes()
    {
        Assert.Equal(41, ProductionTypes.Length);
        Assert.Equal(41, ProductionTypes.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void BaseOwnsPrimitivesAndAdapterConsumesThem()
    {
        var foundation = ReadFoundation();
        var css = ReadAdapter();
        Assert.Contains(":where(.shadcn-scope, .shadcn-overlay-scope)", css, StringComparison.Ordinal);
        Assert.Contains("height: var(--shadcn-control-height)", css, StringComparison.Ordinal);
        Assert.DoesNotContain("--shadcn-control-height:", css, StringComparison.Ordinal);
        Assert.Contains("--shadcn-control-height: 2.25rem", foundation, StringComparison.Ordinal);
        Assert.Contains("--shadcn-control-height-sm: 2rem", foundation, StringComparison.Ordinal);
        Assert.Contains("@media (pointer: coarse)", foundation, StringComparison.Ordinal);
        Assert.Contains("min-width: 2.75rem", foundation, StringComparison.Ordinal);
        Assert.Contains("min-height: 2.75rem", foundation, StringComparison.Ordinal);
        Assert.Contains("@media (prefers-reduced-motion: reduce)", foundation, StringComparison.Ordinal);
        Assert.Contains("@media (forced-colors: active)", foundation, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(".mud-button-root", "height: var(--shadcn-control-height)")]
    [InlineData(".mud-button-filled", "background: var(--shadcn-primary)")]
    [InlineData(".mud-button-outlined", "border: 1px solid var(--shadcn-border)")]
    [InlineData(".mud-button-text", "background: transparent")]
    [InlineData(".mud-icon-button-root", "width: var(--shadcn-control-height)")]
    [InlineData(".mud-input-control", "min-height: var(--shadcn-control-height)")]
    [InlineData(".mud-input-error", "var(--shadcn-destructive)")]
    [InlineData(".mud-select-input", "var(--shadcn-foreground)")]
    [InlineData(".mud-list-item-selected", "background: var(--shadcn-accent)")]
    [InlineData(".mud-picker", "background: var(--shadcn-popover)")]
    [InlineData(".mud-checkbox", "var(--shadcn-primary)")]
    public void ActionsTypographyAndFormsExposeCanonicalContracts(string selector, string declaration)
    {
        var css = ReadAdapter();

        Assert.Contains(selector, css, StringComparison.Ordinal);
        Assert.Contains(declaration, css, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(".mud-button-root:hover")]
    [InlineData(".mud-button-root:active")]
    [InlineData(".mud-button-root:focus-visible")]
    [InlineData(".mud-button-root:disabled")]
    [InlineData(".mud-input-control.mud-disabled")]
    [InlineData(".mud-input-control.mud-input-readonly")]
    [InlineData(".mud-input-error:focus-within")]
    [InlineData(".mud-checkbox.mud-checked")]
    [InlineData(".mud-checkbox.mud-indeterminate")]
    [InlineData(".mud-list-item-selected")]
    [InlineData(".mud-popover-open")]
    [InlineData("[data-shadcn-theme=\"dark\"]")]
    public void ActionsTypographyAndFormsExposeRequiredStateSelectors(string selector)
    {
        Assert.Contains(selector, ReadAdapter(), StringComparison.Ordinal);
    }

    [Fact]
    public void CoarsePointerRulesDoNotMakeCheckboxButtonsFullWidth()
    {
        var css = ReadAdapter();

        Assert.DoesNotMatch(
            @"\.mud-checkbox\s+\.mud-button-root\s*\{[^}]*\bwidth\s*:\s*(?:100%|100vw)",
            css);
    }

    [Fact]
    public void DarkThemeStateRulesRemainInsideTheApprovedProviderScope()
    {
        var css = ReadAdapter();
        const string darkScope = ":where(.shadcn-scope, .shadcn-overlay-scope)[data-shadcn-theme=\"dark\"]";

        Assert.Equal(2, css.Split(darkScope, StringSplitOptions.None).Length - 1);
        Assert.DoesNotContain("\n[data-shadcn-theme=\"dark\"] :where(.mud-", css, StringComparison.Ordinal);
    }

    [Fact]
    public void CoarsePointerRulesReassertFortyFourPixelMinimumsForCompactActionControls()
    {
        var css = ReadAdapter();
        const string coarsePointerMarker = "@media (pointer: coarse)";
        Assert.Contains(coarsePointerMarker, css, StringComparison.Ordinal);
        var coarsePointer = css[css.IndexOf(coarsePointerMarker, StringComparison.Ordinal)..];

        Assert.Matches(@"\.mud-input-adornment \.mud-icon-button-root,[\s\S]*?\.mud-input-clear-button\s*\{[\s\S]*?min-width:\s*2\.75rem;[\s\S]*?min-height:\s*2\.75rem;", coarsePointer);
        Assert.Matches(@"\.mud-checkbox \.mud-icon-button-root\s*\{[\s\S]*?width:\s*2\.75rem;[\s\S]*?height:\s*2\.75rem;[\s\S]*?min-width:\s*2\.75rem;[\s\S]*?min-height:\s*2\.75rem;", coarsePointer);
    }

    internal static string ReadAdapter() => File.ReadAllText(Path.Combine(
        FindRoot(), "Maliev.ShadcnBlazor", "wwwroot", "css", "shadcn-mudblazor.css"));

    private static string ReadFoundation() => File.ReadAllText(Path.Combine(
        FindRoot(), "Maliev.ShadcnBlazor", "wwwroot", "css", "shadcn-base.css"));

    private static string FindRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Legacy.Maliev.Intranet.slnx")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new DirectoryNotFoundException("Could not find repository root.");
    }
}
