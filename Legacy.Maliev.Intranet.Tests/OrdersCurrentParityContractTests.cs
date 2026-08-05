namespace Legacy.Maliev.Intranet.Tests;

public sealed class OrdersCurrentParityContractTests
{
    [Fact]
    public void OrdersPage_UsesCurrentModuleShellAndResponsiveInteractionSurface()
    {
        var source = File.ReadAllText(Path.Combine(FindRoot(),
            "Legacy.Maliev.Intranet.Client.Features.Orders",
            "Pages",
            "Orders.razor"));

        Assert.Contains("<PageTitle>", source, StringComparison.Ordinal);
        Assert.Contains("mlv-module-shell", source, StringComparison.Ordinal);
        Assert.Contains("<ModuleHeader", source, StringComparison.Ordinal);
        Assert.Contains("<PageBody", source, StringComparison.Ordinal);
        Assert.Contains("<PanelCard", source, StringComparison.Ordinal);
        Assert.Contains("<ListToolbar", source, StringComparison.Ordinal);
        Assert.Contains("OnRequest=\"HandleToolbarRequestAsync\"", source, StringComparison.Ordinal);
        Assert.Contains("<ProgressiveSkeleton", source, StringComparison.Ordinal);
        Assert.Contains("<PrimaryButton", source, StringComparison.Ordinal);
        Assert.Contains("<SecondaryButton", source, StringComparison.Ordinal);
        Assert.Contains("mlv-table", source, StringComparison.Ordinal);
        Assert.Contains("aria-live", source, StringComparison.Ordinal);
        Assert.DoesNotContain("<MudContainer", source, StringComparison.Ordinal);
    }

    [Fact]
    public void OrdersPage_PreservesLegacyBffContractsWhileUsingCurrentInteractions()
    {
        var source = File.ReadAllText(Path.Combine(FindRoot(),
            "Legacy.Maliev.Intranet.Client.Features.Orders",
            "Pages",
            "Orders.razor"));

        Assert.Contains("/bff/orders?", source, StringComparison.Ordinal);
        Assert.Contains("/bff/orders/pending?", source, StringComparison.Ordinal);
        Assert.Contains("/bff/order-processes", source, StringComparison.Ordinal);
        Assert.Contains("/bff/employees?", source, StringComparison.Ordinal);
        Assert.Contains("/bff/session", source, StringComparison.Ordinal);
        Assert.Contains("OrderListPage", source, StringComparison.Ordinal);
        Assert.Contains("Search", source, StringComparison.Ordinal);
        Assert.Contains("Refresh", source, StringComparison.Ordinal);
        Assert.DoesNotContain("api/v1/", source, StringComparison.Ordinal);
    }

    [Fact]
    public void WasmShell_LoadsTheSharedCurrentModuleStyles()
    {
        var index = File.ReadAllText(Path.Combine(FindRoot(),
            "Legacy.Maliev.Intranet.Client",
            "wwwroot",
            "index.html"));

        Assert.Contains("css/design-tokens.css", index, StringComparison.Ordinal);
        Assert.Contains("css/module-pages.css", index, StringComparison.Ordinal);
        Assert.Contains("css/utilities.css", index, StringComparison.Ordinal);
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
