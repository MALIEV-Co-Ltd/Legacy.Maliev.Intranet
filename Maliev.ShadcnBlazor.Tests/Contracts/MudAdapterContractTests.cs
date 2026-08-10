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
