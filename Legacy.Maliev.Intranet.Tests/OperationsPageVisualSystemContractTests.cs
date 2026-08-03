namespace Legacy.Maliev.Intranet.Tests;

public sealed class OperationsPageVisualSystemContractTests
{
    [Fact]
    public void OperationsPageStyles_AreLoadedAfterTheBaseAndMudOverrides()
    {
        var root = FindRoot();
        var index = Read(root, "Legacy.Maliev.Intranet.Client", "wwwroot", "index.html");

        var baseIndex = index.IndexOf("css/app.css", StringComparison.Ordinal);
        var mudIndex = index.IndexOf("css/mudblazor-overrides.css", StringComparison.Ordinal);
        var operationsIndex = index.IndexOf("css/operations-pages.css", StringComparison.Ordinal);

        Assert.True(baseIndex >= 0, "The base application stylesheet must be loaded.");
        Assert.True(mudIndex > baseIndex, "MudBlazor overrides must follow the base stylesheet.");
        Assert.True(operationsIndex > mudIndex, "Operations page styles must load last so their responsive contracts win.");
    }

    [Fact]
    public void OperationsPageStyles_CoverResponsiveAccessibleAndLocalizedContent()
    {
        var root = FindRoot();
        var css = Read(root, "Legacy.Maliev.Intranet.Client", "wwwroot", "css", "operations-pages.css");

        Assert.Contains("font-family: var(--maliev-font-sans)", css, StringComparison.Ordinal);
        Assert.Contains("overflow-wrap: anywhere", css, StringComparison.Ordinal);
        Assert.Contains("@media (max-width: 900px)", css, StringComparison.Ordinal);
        Assert.Contains("@media (max-width: 720px)", css, StringComparison.Ordinal);
        Assert.Contains("@media (max-width: 420px)", css, StringComparison.Ordinal);
        Assert.Contains("@media (pointer: coarse)", css, StringComparison.Ordinal);
        Assert.Contains("min-height: 44px", css, StringComparison.Ordinal);
        Assert.Contains("min-width: 2.75rem", css, StringComparison.Ordinal);
        Assert.Contains(".mud-picker .mud-icon-button-root", css, StringComparison.Ordinal);
        Assert.Contains(".finance-summary-grid.mud-grid-spacing-xs-6", css, StringComparison.Ordinal);
        Assert.Contains("@media (prefers-reduced-motion: reduce)", css, StringComparison.Ordinal);
        Assert.Contains("@media (forced-colors: active)", css, StringComparison.Ordinal);
        Assert.Contains(":focus-visible", css, StringComparison.Ordinal);
        Assert.Contains("env(safe-area-inset-bottom)", Read(
            root,
            "Legacy.Maliev.Intranet.Client.Features.Orders",
            "Components",
            "Shared",
            "PageBody.razor.css"), StringComparison.Ordinal);
    }

    [Fact]
    public void OperationsPageStyles_ProvideListFormDetailAndStateContracts()
    {
        var root = FindRoot();
        var css = Read(root, "Legacy.Maliev.Intranet.Client", "wwwroot", "css", "operations-pages.css");

        Assert.Contains(".legacy-page-container .mud-table-container", css, StringComparison.Ordinal);
        Assert.Contains(".legacy-page-container .mud-table-body .mud-table-row", css, StringComparison.Ordinal);
        Assert.Contains(".legacy-page-container .mud-table-body .mud-table-cell::before", css, StringComparison.Ordinal);
        Assert.Contains(".legacy-page-container .mud-form", css, StringComparison.Ordinal);
        Assert.Contains(".legacy-page-container .mud-grid", css, StringComparison.Ordinal);
        Assert.Contains(".legacy-page-container .mud-alert", css, StringComparison.Ordinal);
        Assert.Contains(".legacy-page-container .mud-progress-linear", css, StringComparison.Ordinal);
        Assert.Contains(".legacy-page-container .mud-list-item", css, StringComparison.Ordinal);
        Assert.Contains(".legacy-page-container .mud-tabs-toolbar", css, StringComparison.Ordinal);
        Assert.DoesNotContain(".mud-button-root {\n        display: none", css, StringComparison.Ordinal);
        Assert.DoesNotContain(".mud-table-body {\n        display: none", css, StringComparison.Ordinal);
    }

    [Fact]
    public void EveryFeatureRoute_UsesAMarkupContractCoveredByTheOperationsLayer()
    {
        var root = FindRoot();
        var featureDirectories = Directory.GetDirectories(root, "Legacy.Maliev.Intranet.Client.Features.*");
        var routedPages = featureDirectories
            .SelectMany(directory => Directory.GetFiles(directory, "*.razor", SearchOption.AllDirectories))
            .Select(path => new { Path = path, Source = File.ReadAllText(path) })
            .Where(page => page.Source.Contains("@page ", StringComparison.Ordinal))
            .ToArray();

        Assert.NotEmpty(routedPages);
        Assert.All(routedPages, page => Assert.True(
            page.Source.Contains("<Mud", StringComparison.Ordinal) ||
            page.Source.Contains("mlv-module-shell", StringComparison.Ordinal),
            $"{Path.GetRelativePath(root, page.Path)} must use a MudBlazor or mlv module contract covered by operations-pages.css."));
    }

    [Fact]
    public void OrdersSpecializedPage_PreservesDenseDesktopAndReadableMobileRecords()
    {
        var root = FindRoot();
        var css = Read(
            root,
            "Legacy.Maliev.Intranet.Client.Features.Orders",
            "Pages",
            "Orders.razor.css");

        Assert.Contains("min-width: 920px", css, StringComparison.Ordinal);
        Assert.Contains(".orders-table-scroll:focus-visible", css, StringComparison.Ordinal);
        Assert.Contains("content: attr(data-label)", css, StringComparison.Ordinal);
        Assert.Contains("@media (max-width: 420px)", css, StringComparison.Ordinal);
        Assert.Contains("@media (pointer: coarse)", css, StringComparison.Ordinal);
        Assert.Contains("min-height: 44px", css, StringComparison.Ordinal);
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
