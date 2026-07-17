namespace Legacy.Maliev.Intranet.Tests;

public sealed class OrdersViewWasmMigrationContractTests
{
    [Fact]
    public void OrderDetailDto_PreservesTheCompleteEditableLegacyContract()
    {
        var contracts = typeof(Legacy.Maliev.Intranet.Contracts.OrderListItem).Assembly;
        var detail = contracts.GetType("Legacy.Maliev.Intranet.Contracts.OrderDetailItem");
        var update = contracts.GetType("Legacy.Maliev.Intranet.Contracts.OrderUpdateRequest");

        Assert.NotNull(detail);
        Assert.NotNull(update);
        Assert.Equal(
            [
                "AllowCancellation", "AllowPayment", "AllowSocialMedia", "ColorId", "Comment",
                "CreatedDate", "CurrencyId", "CustomerId", "Description", "DiscountPercent", "EmployeeId",
                "FinishedDate", "Id", "LeadTime", "Manufactured", "MaterialId", "ModifiedDate", "Name",
                "ProcessId", "PromisedDate", "Quantity", "Remaining", "Subtotal", "SurfaceFinishId",
                "TrackingNumber", "Turnaround", "UnitPrice",
            ],
            detail.GetProperties().Select(property => property.Name).Order(StringComparer.Ordinal).ToArray());
        Assert.Equal(
            [
                "AllowCancellation", "AllowPayment", "AllowSocialMedia", "ColorId", "Comment", "CurrencyId",
                "CustomerId", "Description", "DiscountPercent", "EmployeeId", "FinishedDate", "LeadTime",
                "Manufactured", "MaterialId", "ModifiedDate", "Name", "ProcessId", "PromisedDate", "Quantity",
                "SurfaceFinishId", "TrackingNumber", "UnitPrice",
            ],
            update.GetProperties().Select(property => property.Name).Order(StringComparer.Ordinal).ToArray());
    }

    [Fact]
    public void OrdersView_IsLazyAuthorizedLocalizedAndKeepsTheRazorFallback()
    {
        var root = FindRoot();
        var pagePath = Path.Combine(root, "Legacy.Maliev.Intranet.Client.Features.Orders", "Pages", "OrderDetail.razor");
        var resourcePath = Path.Combine(root, "Legacy.Maliev.Intranet.Client.Features.Orders", "Pages", "OrderDetail.resx");

        Assert.True(File.Exists(pagePath), "The lazy WASM Orders/View route is missing.");
        Assert.True(File.Exists(resourcePath), "The Orders/View localization resource is missing.");
        var page = File.ReadAllText(pagePath);
        var resource = File.ReadAllText(resourcePath);

        Assert.Contains("@page \"/Orders/View\"", page, StringComparison.Ordinal);
        Assert.Contains("@attribute [Authorize]", page, StringComparison.Ordinal);
        Assert.Contains("[SupplyParameterFromQuery(Name = \"id\")]", page, StringComparison.Ordinal);
        Assert.Contains("MudProgress", page, StringComparison.Ordinal);
        Assert.Contains("MudAlert", page, StringComparison.Ordinal);
        Assert.Contains("MudForm", page, StringComparison.Ordinal);
        Assert.Contains("/bff/orders/", page, StringComparison.Ordinal);
        Assert.Contains("X-CSRF-TOKEN", page, StringComparison.Ordinal);
        Assert.Contains("HttpStatusCode.Conflict", page, StringComparison.Ordinal);
        Assert.Contains("multipart/form-data", page, StringComparison.Ordinal);
        Assert.Contains("BackToOrders", resource, StringComparison.Ordinal);
        Assert.Contains("OrderSaved", resource, StringComparison.Ordinal);
        Assert.Contains("Conflict", resource, StringComparison.Ordinal);

        Assert.True(File.Exists(Path.Combine(root, "Legacy.Maliev.Intranet", "Pages", "Orders", "View.cshtml")));
        Assert.True(File.Exists(Path.Combine(root, "Legacy.Maliev.Intranet", "Pages", "Orders", "View.cshtml.cs")));
    }

    [Fact]
    public void OrdersViewBff_UsesExactPermissionsCsrfAndDownstreamContracts()
    {
        var root = FindRoot();
        var program = File.ReadAllText(Path.Combine(root, "Legacy.Maliev.Intranet.Bff", "Program.cs"));
        var proxyPath = Path.Combine(root, "Legacy.Maliev.Intranet.Bff", "Orders", "OrderDetailProxy.cs");

        Assert.True(File.Exists(proxyPath), "The server-only order detail proxy is missing.");
        var proxy = File.ReadAllText(proxyPath);
        Assert.Contains("MapGet(\"/bff/orders/{id:int}\"", program, StringComparison.Ordinal);
        Assert.Contains("MapPut(\"/bff/orders/{id:int}\"", program, StringComparison.Ordinal);
        Assert.Contains("MapPost(\"/bff/orders/{id:int}/status/{statusId:int}\"", program, StringComparison.Ordinal);
        Assert.Contains("AddEndpointFilter<AntiforgeryValidationFilter>()", program, StringComparison.Ordinal);
        Assert.Contains("LegacyEmployeePermissions.OrdersUpdate", program, StringComparison.Ordinal);
        Assert.Contains("LegacyEmployeePermissions.OrderStatusRead", program, StringComparison.Ordinal);
        Assert.Contains("LegacyEmployeePermissions.OrderStatusWrite", program, StringComparison.Ordinal);
        Assert.Contains("X-Expected-Modified-Date", proxy, StringComparison.Ordinal);
        Assert.Contains("Idempotency-Key", proxy, StringComparison.Ordinal);
        Assert.DoesNotContain("AllowCancellation = false", program, StringComparison.Ordinal);
        Assert.DoesNotContain("Accepted", program, StringComparison.Ordinal);
    }

    [Fact]
    public void OrdersViewFilesAndLabel_KeepStorageAndDocumentCredentialsServerSide()
    {
        var root = FindRoot();
        var program = File.ReadAllText(Path.Combine(root, "Legacy.Maliev.Intranet.Bff", "Program.cs"));
        var filesPath = Path.Combine(root, "Legacy.Maliev.Intranet.Bff", "Orders", "OrderFileProxy.cs");
        var documentPath = Path.Combine(root, "Legacy.Maliev.Intranet.Bff", "Orders", "OrderDocumentProxy.cs");

        Assert.True(File.Exists(filesPath), "The FileService proxy is missing.");
        Assert.True(File.Exists(documentPath), "The DocumentService proxy is missing.");
        var files = File.ReadAllText(filesPath);
        var document = File.ReadAllText(documentPath);
        var endpoint = File.ReadAllText(Path.Combine(root, "Legacy.Maliev.Intranet.Bff", "Orders", "OrderDetailEndpointMapper.cs"));
        Assert.Contains("/Uploads?bucket=maliev.com", files, StringComparison.Ordinal);
        Assert.Contains("/uploads/SignedUrl", files, StringComparison.Ordinal);
        Assert.Contains("Path.GetFileName", files, StringComparison.Ordinal);
        Assert.Contains("/Pdfs/orderlabel", document, StringComparison.Ordinal);
        Assert.Contains("application/pdf", document, StringComparison.Ordinal);
        Assert.Contains("Results.File(pdf, \"application/pdf\"", endpoint, StringComparison.Ordinal);
        Assert.Contains("MapPost(\"/bff/orders/{id:int}/files\"", program, StringComparison.Ordinal);
        Assert.Contains("MapDelete(\"/bff/orders/{id:int}/files/{fileId:int}\"", program, StringComparison.Ordinal);
        Assert.Contains("MapGet(\"/bff/orders/{id:int}/label\"", program, StringComparison.Ordinal);
        Assert.Contains("LegacyEmployeePermissions.OrderFilesWrite", program, StringComparison.Ordinal);
        Assert.Contains("LegacyEmployeePermissions.OrderFilesDelete", program, StringComparison.Ordinal);
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
