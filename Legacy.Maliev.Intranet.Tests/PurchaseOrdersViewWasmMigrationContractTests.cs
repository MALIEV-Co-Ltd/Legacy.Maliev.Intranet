namespace Legacy.Maliev.Intranet.Tests;

public sealed class PurchaseOrdersViewWasmMigrationContractTests
{
    [Fact]
    public void PurchaseOrderDetailDto_ExposesDisplayedFieldsWithoutStorageCredentials()
    {
        var assembly = typeof(Legacy.Maliev.Intranet.Contracts.PurchaseOrderListPage).Assembly;
        var detail = assembly.GetType("Legacy.Maliev.Intranet.Contracts.PurchaseOrderDetail");
        var line = assembly.GetType("Legacy.Maliev.Intranet.Contracts.PurchaseOrderDetailLine");
        var download = assembly.GetType("Legacy.Maliev.Intranet.Contracts.PurchaseOrderDownloadLink");
        Assert.NotNull(detail);
        Assert.NotNull(line);
        Assert.NotNull(download);
        Assert.Equal(["CreatedDate", "Downloads", "Fob", "Id", "Items", "Notes", "OrderedBy", "ShippingMethod", "SupplierContactPerson", "SupplierName", "Terms"],
            detail.GetProperties().Select(value => value.Name).Order(StringComparer.Ordinal).ToArray());
        Assert.Equal(["Description", "PartNumber", "Quantity", "Subtotal", "UnitPrice"],
            line.GetProperties().Select(value => value.Name).Order(StringComparer.Ordinal).ToArray());
        Assert.Equal(["Name", "Url"], download.GetProperties().Select(value => value.Name).Order(StringComparer.Ordinal).ToArray());
    }

    [Fact]
    public void PurchaseOrdersView_IsLazyAuthorizedLocalizedAndKeepsRazorFallback()
    {
        var root = FindRoot();
        var pagePath = Path.Combine(root, "Legacy.Maliev.Intranet.Client.Features.Procurement", "Pages", "PurchaseOrderView.razor");
        Assert.True(File.Exists(pagePath), "The lazy WASM PurchaseOrders/View route is missing.");
        Assert.True(File.Exists(Path.ChangeExtension(pagePath, ".resx")), "The localized PurchaseOrders/View resource is missing.");
        var page = File.ReadAllText(pagePath);
        var program = File.ReadAllText(Path.Combine(root, "Legacy.Maliev.Intranet.Bff", "Program.cs"));
        var auth = File.ReadAllText(Path.Combine(root, "Legacy.Maliev.Intranet.Server", "Auth", "AuthContracts.cs"));
        Assert.Contains("@page \"/PurchaseOrders/View\"", page, StringComparison.Ordinal);
        Assert.Contains("[SupplyParameterFromQuery", page, StringComparison.Ordinal);
        Assert.Contains("@attribute [Authorize]", page, StringComparison.Ordinal);
        Assert.Contains("/bff/purchase-orders/{Id}", page, StringComparison.Ordinal);
        Assert.Contains("HttpMethod.Delete", page, StringComparison.Ordinal);
        Assert.Contains("X-CSRF-TOKEN", page, StringComparison.Ordinal);
        Assert.Contains("MudTable", page, StringComparison.Ordinal);
        Assert.Contains("MudDialog", page, StringComparison.Ordinal);
        Assert.DoesNotContain("forceLoad: true", page, StringComparison.Ordinal);
        Assert.DoesNotContain("jquery", page, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("MapGet(\"/bff/purchase-orders/{id:int}\"", program, StringComparison.Ordinal);
        Assert.Contains("MapDelete(\"/bff/purchase-orders/{id:int}\"", program, StringComparison.Ordinal);
        Assert.Contains("LegacyEmployeePermissions.PurchaseOrdersDelete", program, StringComparison.Ordinal);
        Assert.Contains("AddEndpointFilter<AntiforgeryValidationFilter>()", program, StringComparison.Ordinal);
        Assert.Contains("legacy-procurement.purchase-orders.delete", auth, StringComparison.Ordinal);
        Assert.True(File.Exists(Path.Combine(root, "Legacy.Maliev.Intranet", "Pages", "PurchaseOrders", "View.cshtml")));
        Assert.True(File.Exists(Path.Combine(root, "Legacy.Maliev.Intranet", "Pages", "PurchaseOrders", "View.cshtml.cs")));
    }

    private static string FindRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Legacy.Maliev.Intranet.slnx"))) directory = directory.Parent;
        return directory?.FullName ?? throw new DirectoryNotFoundException("Could not find repository root.");
    }
}
