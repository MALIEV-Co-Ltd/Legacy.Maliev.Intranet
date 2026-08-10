using System.Text.RegularExpressions;

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
    [MemberData(nameof(SurfaceAndOverlayContracts))]
    public void SurfacesAndOverlaysExposeCanonicalContracts(string selector, string declaration)
    {
        var css = ReadAdapter();

        Assert.Contains(selector, css, StringComparison.Ordinal);
        Assert.Contains(declaration, css, StringComparison.Ordinal);
    }

    public static TheoryData<string, string> SurfaceAndOverlayContracts => new()
    {
        { ".mud-paper", "background: var(--shadcn-card)" },
        { ".mud-divider", "border-color: var(--shadcn-border)" },
        { ".mud-expand-panel", "border: 1px solid var(--shadcn-border)" },
        { ".mud-expand-panel-header", "min-height: var(--shadcn-control-height)" },
        { ".mud-tab.mud-tab-active", "background: var(--shadcn-background)" },
        { ".mud-list-item-selected", "color: var(--shadcn-accent-foreground)" },
        { ".mud-chip", "border-radius: var(--shadcn-radius-md)" },
        { ".mud-popover", "background: var(--shadcn-popover)" },
        { ".mud-dialog", "background: var(--shadcn-background)" },
        { ".mud-snackbar", "background: var(--shadcn-foreground)" }
    };

    [Theory]
    [InlineData(".mud-expand-panel-header:hover")]
    [InlineData(".mud-expand-panel-header:focus-visible")]
    [InlineData(".mud-expand-panel-header.mud-disabled")]
    [InlineData(".mud-expand-panel.mud-panel-expanded")]
    [InlineData(".mud-tab:hover")]
    [InlineData(".mud-tab:focus-visible")]
    [InlineData(".mud-tab.mud-disabled")]
    [InlineData(".mud-tab.mud-tab-active")]
    [InlineData(".mud-tab-slider")]
    [InlineData(".mud-list-item:hover")]
    [InlineData(".mud-list-item.mud-active")]
    [InlineData(".mud-chip-color-success")]
    [InlineData(".mud-popover-open")]
    [InlineData(".mud-dialog")]
    [InlineData(".mud-snackbar")]
    public void SurfacesAndOverlaysExposeRequiredStateSelectors(string selector)
    {
        Assert.Contains(selector, ReadAdapter(), StringComparison.Ordinal);
    }

    [Fact]
    public void PortalSurfacesShareTheTokenizedZIndexLayer()
    {
        var css = ReadAdapter();

        const string scope = ":where(.shadcn-scope, .shadcn-overlay-scope)";
        Assert.DoesNotContain(
            $"{scope} :where(.mud-popover, .mud-menu, .mud-picker, .mud-dialog)",
            css,
            StringComparison.Ordinal);
        Assert.Contains($"{scope} .mud-popover,", css, StringComparison.Ordinal);
        Assert.Contains($"{scope} .mud-menu,", css, StringComparison.Ordinal);
        Assert.Contains($"{scope} .mud-picker,", css, StringComparison.Ordinal);
        Assert.Contains($"{scope} .mud-dialog {{", css, StringComparison.Ordinal);
        Assert.Contains("z-index: 50", css, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(".mud-alert-filled-success")]
    [InlineData(".mud-alert-outlined-success")]
    [InlineData(".mud-alert-text-success")]
    [InlineData(".mud-alert-filled-warning")]
    [InlineData(".mud-alert-outlined-warning")]
    [InlineData(".mud-alert-text-warning")]
    [InlineData(".mud-alert-filled-error")]
    [InlineData(".mud-alert-outlined-error")]
    [InlineData(".mud-alert-text-error")]
    public void SnackbarsTargetMudBlazorNineAlertSeverityClasses(string selector)
    {
        Assert.Contains(selector, ReadAdapter(), StringComparison.Ordinal);
    }

    [Fact]
    public void SnackbarsDoNotTargetNonexistentSeverityClasses()
    {
        var css = ReadAdapter();

        Assert.DoesNotContain(".mud-snackbar.mud-snackbar-success", css, StringComparison.Ordinal);
        Assert.DoesNotContain(".mud-snackbar.mud-snackbar-warning", css, StringComparison.Ordinal);
        Assert.DoesNotContain(".mud-snackbar.mud-snackbar-error", css, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(".mud-popover-top-left", "shadcn-popover-from-top")]
    [InlineData(".mud-popover-top-center", "shadcn-popover-from-top")]
    [InlineData(".mud-popover-top-right", "shadcn-popover-from-top")]
    [InlineData(".mud-popover-bottom-left", "shadcn-popover-from-bottom")]
    [InlineData(".mud-popover-bottom-center", "shadcn-popover-from-bottom")]
    [InlineData(".mud-popover-bottom-right", "shadcn-popover-from-bottom")]
    [InlineData(".mud-popover-center-left", "shadcn-popover-from-left")]
    [InlineData(".mud-popover-center-right", "shadcn-popover-from-right")]
    public void OpenPopoversUseMudBlazorSideAwarePlacementAnimations(string selector, string animation)
    {
        var css = ReadAdapter();

        Assert.Matches($"{Regex.Escape(selector)}[\\s\\S]*?animation: {animation} 100ms", css);
    }

    [Theory]
    [InlineData("shadcn-popover-from-top", "translateY(-0.5rem)")]
    [InlineData("shadcn-popover-from-bottom", "translateY(0.5rem)")]
    [InlineData("shadcn-popover-from-left", "translateX(-0.5rem)")]
    [InlineData("shadcn-popover-from-right", "translateX(0.5rem)")]
    public void PopoverPlacementAnimationsMoveTowardTheirOrigins(string animation, string transform)
    {
        var css = ReadAdapter();

        Assert.Matches($"@keyframes {animation} \\{{[\\s\\S]*?transform: {Regex.Escape(transform)}", css);
    }

    [Fact]
    public void ExpansionHeaderContentBoxRemainsWithinTheThirtySixPixelDesktopControlHeight()
    {
        Assert.Matches(
            @"\.mud-expand-panel \.mud-expand-panel-header\s*\{[\s\S]*?min-height:\s*var\(--shadcn-control-height\);[\s\S]*?padding-block:\s*0\.5rem;[\s\S]*?line-height:\s*1\.25rem;",
            ReadAdapter());
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
