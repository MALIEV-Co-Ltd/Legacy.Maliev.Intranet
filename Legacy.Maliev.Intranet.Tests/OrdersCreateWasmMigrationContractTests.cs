namespace Legacy.Maliev.Intranet.Tests;

public sealed class OrdersCreateWasmMigrationContractTests
{
    [Fact]
    public void CreateOrderContract_PreservesValidatedLegacyInputAndSafeResult()
    {
        var contracts = typeof(Legacy.Maliev.Intranet.Contracts.OrderListItem).Assembly;
        var request = contracts.GetType("Legacy.Maliev.Intranet.Contracts.OrderCreateRequest");
        var result = contracts.GetType("Legacy.Maliev.Intranet.Contracts.OrderCreatedResult");

        Assert.NotNull(request);
        Assert.NotNull(result);
        Assert.Equal(
            [
                "AllowSocialMedia", "ColorId", "CustomerId", "Description", "MaterialId", "Name",
                "ProcessId", "Quantity", "SendConfirmationEmail", "SurfaceFinishId",
            ],
            request.GetProperties().Select(property => property.Name).Order(StringComparer.Ordinal).ToArray());
        Assert.Equal(["Id", "Warning"], result.GetProperties().Select(property => property.Name).Order(StringComparer.Ordinal).ToArray());
    }

    [Fact]
    public void OrdersCreate_IsLazyAuthorizedLocalizedMultipartAndKeepsRazorFallback()
    {
        var root = FindRoot();
        var pagePath = Path.Combine(root, "Legacy.Maliev.Intranet.Client.Features.Orders", "Pages", "OrderCreate.razor");
        var resourcePath = Path.Combine(root, "Legacy.Maliev.Intranet.Client.Features.Orders", "Pages", "OrderCreate.resx");

        Assert.True(File.Exists(pagePath), "The lazy WASM Orders/Create route is missing.");
        Assert.True(File.Exists(resourcePath), "The Orders/Create localization resource is missing.");
        var page = File.ReadAllText(pagePath);
        var resource = File.ReadAllText(resourcePath);

        Assert.Contains("@page \"/Orders/Create\"", page, StringComparison.Ordinal);
        Assert.Contains("@attribute [Authorize]", page, StringComparison.Ordinal);
        Assert.Contains("[SupplyParameterFromQuery(Name = \"customerId\")]", page, StringComparison.Ordinal);
        Assert.Contains("MudForm", page, StringComparison.Ordinal);
        Assert.Contains("InputFile", page, StringComparison.Ordinal);
        Assert.Contains("MultipartFormDataContent", page, StringComparison.Ordinal);
        Assert.Contains("X-CSRF-TOKEN", page, StringComparison.Ordinal);
        Assert.Contains("Idempotency-Key", page, StringComparison.Ordinal);
        Assert.Contains("/bff/orders/create/materials/", page, StringComparison.Ordinal);
        Assert.Contains("/Orders/View?id=", page, StringComparison.Ordinal);
        Assert.DoesNotContain("jquery", page, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("CustomerNotFound", resource, StringComparison.Ordinal);
        Assert.Contains("NotificationWarning", resource, StringComparison.Ordinal);
        Assert.True(File.Exists(Path.Combine(root, "Legacy.Maliev.Intranet", "Pages", "Orders", "Create.cshtml")));
        Assert.True(File.Exists(Path.Combine(root, "Legacy.Maliev.Intranet", "Pages", "Orders", "Create.cshtml.cs")));
    }

    [Fact]
    public void OrdersCreateBff_DocumentsAuth41AndUsesExactPermissionCsrfAndServiceBoundaries()
    {
        var root = FindRoot();
        var auth = File.ReadAllText(Path.Combine(root, "Legacy.Maliev.Intranet.Server", "Auth", "AuthContracts.cs"));
        var program = File.ReadAllText(Path.Combine(root, "Legacy.Maliev.Intranet.Bff", "Program.cs"));
        var proxyPath = Path.Combine(root, "Legacy.Maliev.Intranet.Bff", "Orders", "OrderCreateProxies.cs");

        Assert.Contains("OrdersCreate", auth, StringComparison.Ordinal);
        Assert.Contains("legacy.orders.create", auth, StringComparison.Ordinal);
        Assert.Contains("AuthService issue #41", auth, StringComparison.Ordinal);
        Assert.True(File.Exists(proxyPath), "The server-only create workflow proxies are missing.");
        Assert.Contains("MapGet(\"/bff/orders/create\"", program, StringComparison.Ordinal);
        Assert.Contains("MapGet(\"/bff/orders/create/materials/{materialId:int}\"", program, StringComparison.Ordinal);
        Assert.Contains("MapPost(\"/bff/orders\"", program, StringComparison.Ordinal);
        Assert.Contains("AddEndpointFilter<AntiforgeryValidationFilter>()", program, StringComparison.Ordinal);
        Assert.Contains("LegacyEmployeePermissions.OrdersCreate", program, StringComparison.Ordinal);
        Assert.Contains("RequestSizeLimitAttribute(201L * 1024 * 1024)", program, StringComparison.Ordinal);
        Assert.Contains("uploads.Sum(file => file.Length) > 200L * 1024 * 1024", File.ReadAllText(Path.Combine(root, "Legacy.Maliev.Intranet.Bff", "Orders", "OrderCreateEndpointMapper.cs")), StringComparison.Ordinal);
        Assert.Contains("RequestFormLimitsAttribute", program, StringComparison.Ordinal);
    }

    [Fact]
    public void OrderCreationWorkflow_IsServerOwnedCompensatedAndNotificationIsPostCommit()
    {
        var root = FindRoot();
        var workflowPath = Path.Combine(root, "Legacy.Maliev.Intranet.Server", "Orders", "OrderCreationWorkflow.cs");

        Assert.True(File.Exists(workflowPath), "The server application workflow is missing.");
        var workflow = File.ReadAllText(workflowPath);
        Assert.Contains("CancellationTokenSource(TimeSpan.FromSeconds(10))", workflow, StringComparison.Ordinal);
        Assert.Contains("TryCompensateAsync", workflow, StringComparison.Ordinal);
        Assert.Contains("DeleteOrder", workflow, StringComparison.Ordinal);
        Assert.Contains("DeleteStoredFile", workflow, StringComparison.Ordinal);
        Assert.Contains("DeleteOrderFile", workflow, StringComparison.Ordinal);
        Assert.Contains("CreateInitialStatus", workflow, StringComparison.Ordinal);
        Assert.Contains("SendNotification", workflow, StringComparison.Ordinal);
        Assert.Contains("Order created, but the confirmation notification failed.", workflow, StringComparison.Ordinal);
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
