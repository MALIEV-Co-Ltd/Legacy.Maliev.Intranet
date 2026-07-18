namespace Legacy.Maliev.Intranet.Tests;

public sealed class PurchaseOrdersCreateWasmMigrationContractTests
{
    [Fact]
    public void PurchaseOrderCreateRequest_PreservesLegacyFieldsAndValidationBoundary()
    {
        var assembly = typeof(Legacy.Maliev.Intranet.Contracts.PurchaseOrderListPage).Assembly;
        var requestType = assembly.GetType("Legacy.Maliev.Intranet.Contracts.PurchaseOrderCreateRequest");
        var itemType = assembly.GetType("Legacy.Maliev.Intranet.Contracts.PurchaseOrderCreateItem");

        Assert.NotNull(requestType);
        Assert.NotNull(itemType);
        Assert.Equal(
            [
                "BillingAddressId", "BillingCompanyName", "BillingContactPerson", "BillingFax", "BillingMobile",
                "BillingTelephone", "EmployeeId", "Fob", "Items", "Notes", "ShippingAddressId",
                "ShippingCompanyName", "ShippingContactPerson", "ShippingFax", "ShippingMethod", "ShippingMobile",
                "ShippingTelephone", "SupplierContactPerson", "SupplierId", "Terms",
            ],
            requestType.GetProperties().Select(property => property.Name).Order(StringComparer.Ordinal).ToArray());
        Assert.Equal(
            ["Description", "PartNumber", "Quantity", "UnitPrice"],
            itemType.GetProperties().Select(property => property.Name).Order(StringComparer.Ordinal).ToArray());
    }

    [Fact]
    public void PurchaseOrdersCreate_IsLazyAuthorizedLocalizedAndKeepsRazorFallback()
    {
        var root = FindRoot();
        var pagePath = Path.Combine(
            root,
            "Legacy.Maliev.Intranet.Client.Features.Procurement",
            "Pages",
            "PurchaseOrderCreate.razor");
        var resourcePath = Path.ChangeExtension(pagePath, ".resx");

        Assert.True(File.Exists(pagePath), "The lazy WASM PurchaseOrders/Create route is missing.");
        Assert.True(File.Exists(resourcePath), "The PurchaseOrders/Create localization resource is missing.");

        var page = File.ReadAllText(pagePath);
        var bff = File.ReadAllText(Path.Combine(root, "Legacy.Maliev.Intranet.Bff", "Program.cs"));
        var auth = File.ReadAllText(Path.Combine(root, "Legacy.Maliev.Intranet.Server", "Auth", "AuthContracts.cs"));

        Assert.Contains("@page \"/PurchaseOrders/Create\"", page, StringComparison.Ordinal);
        Assert.Contains("@attribute [Authorize]", page, StringComparison.Ordinal);
        Assert.Contains("/bff/purchase-orders/create-options", page, StringComparison.Ordinal);
        Assert.Contains("HttpMethod.Post", page, StringComparison.Ordinal);
        Assert.Contains("/bff/purchase-orders", page, StringComparison.Ordinal);
        Assert.Contains("X-CSRF-TOKEN", page, StringComparison.Ordinal);
        Assert.Contains("MudForm", page, StringComparison.Ordinal);
        Assert.Contains("MudSelect", page, StringComparison.Ordinal);
        Assert.Contains("MudNumericField", page, StringComparison.Ordinal);
        Assert.Contains("AddLineItem", page, StringComparison.Ordinal);
        Assert.Contains("RemoveLineItem", page, StringComparison.Ordinal);
        Assert.Contains("HttpStatusCode.Unauthorized", page, StringComparison.Ordinal);
        Assert.Contains("HttpStatusCode.Forbidden", page, StringComparison.Ordinal);
        Assert.Contains("HttpStatusCode.Conflict", page, StringComparison.Ordinal);
        Assert.Contains("HttpStatusCode.TooManyRequests", page, StringComparison.Ordinal);
        Assert.Contains("Navigation.NavigateTo($\"/PurchaseOrders/View?id={created.Id}\")", page, StringComparison.Ordinal);
        Assert.DoesNotContain("forceLoad: true", page, StringComparison.Ordinal);
        Assert.DoesNotContain("jquery", page, StringComparison.OrdinalIgnoreCase);

        Assert.Contains("legacy-procurement.purchase-orders.create", auth, StringComparison.Ordinal);
        Assert.Contains("MapGet(\"/bff/purchase-orders/create-options\"", bff, StringComparison.Ordinal);
        Assert.Contains("MapPost(\"/bff/purchase-orders\"", bff, StringComparison.Ordinal);
        Assert.Contains("LegacyEmployeePermissions.PurchaseOrdersCreate", bff, StringComparison.Ordinal);
        Assert.Contains("AddEndpointFilter<AntiforgeryValidationFilter>()", bff, StringComparison.Ordinal);

        Assert.True(
            File.Exists(Path.Combine(root, "Legacy.Maliev.Intranet", "Pages", "PurchaseOrders", "Create.cshtml")),
            "The compatibility Razor create page must remain until final cutover.");
        Assert.True(
            File.Exists(Path.Combine(root, "Legacy.Maliev.Intranet", "Pages", "PurchaseOrders", "Create.cshtml.cs")),
            "The compatibility Razor create PageModel must remain until final cutover.");
    }

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
