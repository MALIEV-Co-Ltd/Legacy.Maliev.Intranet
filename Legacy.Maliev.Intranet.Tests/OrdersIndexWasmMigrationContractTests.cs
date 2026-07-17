using System.Text.Json;

namespace Legacy.Maliev.Intranet.Tests;

public sealed class OrdersIndexWasmMigrationContractTests
{
    [Fact]
    public void OrdersIndex_IsLazyAuthorizedLocalizedAndKeepsTheRazorFallback()
    {
        var root = FindRoot();
        var featureProject = Path.Combine(root, "Legacy.Maliev.Intranet.Client.Features.Orders", "Legacy.Maliev.Intranet.Client.Features.Orders.csproj");
        var pagePath = Path.Combine(root, "Legacy.Maliev.Intranet.Client.Features.Orders", "Pages", "Orders.razor");
        var resourcePath = Path.Combine(root, "Legacy.Maliev.Intranet.Client.Features.Orders", "Pages", "Orders.resx");

        Assert.True(File.Exists(featureProject), "The lazy Orders feature assembly is missing.");
        Assert.True(File.Exists(pagePath), "The WASM Orders/Index route is missing.");
        Assert.True(File.Exists(resourcePath), "The Orders/Index localization resource is missing.");

        var page = File.ReadAllText(pagePath);
        var app = File.ReadAllText(Path.Combine(root, "Legacy.Maliev.Intranet.Client", "App.razor"));
        var clientProject = File.ReadAllText(Path.Combine(root, "Legacy.Maliev.Intranet.Client", "Legacy.Maliev.Intranet.Client.csproj"));
        var solution = File.ReadAllText(Path.Combine(root, "Legacy.Maliev.Intranet.slnx"));
        var bff = File.ReadAllText(Path.Combine(root, "Legacy.Maliev.Intranet.Bff", "Program.cs"));
        var authContracts = File.ReadAllText(Path.Combine(root, "Legacy.Maliev.Intranet.Server", "Auth", "AuthContracts.cs"));

        Assert.Contains("@page \"/Orders/Index\"", page, StringComparison.Ordinal);
        Assert.Contains("@attribute [Authorize]", page, StringComparison.Ordinal);
        Assert.Contains("[SupplyParameterFromQuery", page, StringComparison.Ordinal);
        Assert.Contains("/bff/orders", page, StringComparison.Ordinal);
        Assert.Contains("/bff/orders/pending", page, StringComparison.Ordinal);
        Assert.Contains("/bff/order-processes", page, StringComparison.Ordinal);
        Assert.Contains("/bff/employees", page, StringComparison.Ordinal);
        Assert.Contains("/bff/session", page, StringComparison.Ordinal);
        Assert.Contains("Task.WhenAll", page, StringComparison.Ordinal);
        Assert.Contains("AssignedOrders", page, StringComparison.Ordinal);
        Assert.Contains("UnassignedOrders", page, StringComparison.Ordinal);
        Assert.Contains("MudTable", page, StringComparison.Ordinal);
        Assert.Contains("/Orders/Create", page, StringComparison.Ordinal);
        Assert.Contains("/Orders/View?id=", page, StringComparison.Ordinal);
        Assert.Contains("HttpStatusCode.Unauthorized", page, StringComparison.Ordinal);
        Assert.Contains("HttpStatusCode.Forbidden", page, StringComparison.Ordinal);
        Assert.Contains("HttpStatusCode.TooManyRequests", page, StringComparison.Ordinal);
        Assert.Contains("Math.Clamp", page, StringComparison.Ordinal);
        Assert.Contains("Uri.EscapeDataString", page, StringComparison.Ordinal);

        Assert.Contains("Legacy.Maliev.Intranet.Client.Features.Orders.wasm", app, StringComparison.Ordinal);
        Assert.Contains("Legacy.Maliev.Intranet.Client.Features.Orders.wasm", clientProject, StringComparison.Ordinal);
        Assert.Contains("Legacy.Maliev.Intranet.Client.Features.Orders", solution, StringComparison.Ordinal);
        Assert.Contains("legacy.orders.read", authContracts, StringComparison.Ordinal);
        Assert.Contains("legacy.order-catalog.read", authContracts, StringComparison.Ordinal);
        Assert.Contains("LegacyEmployeePermissions.OrdersRead", bff, StringComparison.Ordinal);
        Assert.Contains("LegacyEmployeePermissions.OrderCatalogRead", bff, StringComparison.Ordinal);
        Assert.Contains("MapGet(\"/bff/orders\"", bff, StringComparison.Ordinal);
        Assert.Contains("MapGet(\"/bff/orders/pending\"", bff, StringComparison.Ordinal);
        Assert.Contains("MapGet(\"/bff/order-processes\"", bff, StringComparison.Ordinal);
        Assert.DoesNotContain("DbContext", bff, StringComparison.Ordinal);
        Assert.True(File.Exists(Path.Combine(root, "Legacy.Maliev.Intranet", "Pages", "Orders", "Index.cshtml")),
            "The compatibility Razor fallback must remain in this slice.");
    }

    [Fact]
    public void OrderDtos_PreserveDisplayedLegacyFieldsWithoutExposingUnusedData()
    {
        var assembly = typeof(Legacy.Maliev.Intranet.Contracts.EmployeeListPage).Assembly;
        var orderType = assembly.GetType("Legacy.Maliev.Intranet.Contracts.OrderListItem");
        var pageType = assembly.GetType("Legacy.Maliev.Intranet.Contracts.OrderListPage");
        var processType = assembly.GetType("Legacy.Maliev.Intranet.Contracts.OrderProcessItem");

        Assert.NotNull(orderType);
        Assert.NotNull(pageType);
        Assert.NotNull(processType);
        Assert.Equal(
            [
                "AllowSocialMedia", "CustomerId", "EmployeeId", "Id", "Manufactured", "Name", "ProcessId",
                "PromisedDate", "Quantity", "Remaining", "Subtotal",
            ],
            orderType.GetProperties().Select(property => property.Name).Order(StringComparer.Ordinal).ToArray());
        Assert.Equal(
            ["HasNextPage", "HasPreviousPage", "Items", "PageIndex", "TotalPages", "TotalRecords"],
            pageType.GetProperties().Select(property => property.Name).Order(StringComparer.Ordinal).ToArray());
        Assert.Equal(
            ["CategoryId", "CreatedDate", "Id", "ModifiedDate", "Name"],
            processType.GetProperties().Select(property => property.Name).Order(StringComparer.Ordinal).ToArray());

        const string json = """{"items":[{"id":84,"customerId":42,"employeeId":7,"name":"Thai fixture","description":"ไม้เอก ไม้โท","processId":3,"materialId":5,"surfaceFinishId":6,"colorId":4,"quantity":2,"manufactured":1,"remaining":1,"unitPrice":125,"discountPercent":10,"subtotal":225,"currencyId":1,"leadTime":3,"promisedDate":"2030-07-20T00:00:00","finishedDate":null,"turnaround":null,"comment":"note","allowSocialMedia":false,"allowCancellation":true,"allowPayment":false,"trackingNumber":"TRACK-1","createdDate":"2030-07-15T00:00:00","modifiedDate":null}],"pageIndex":1,"totalPages":1,"totalRecords":1,"hasNextPage":false,"hasPreviousPage":false}""";
        var page = JsonSerializer.Deserialize(json, pageType, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.NotNull(page);
        var wire = JsonSerializer.SerializeToElement(page, pageType, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.Equal(84, wire.GetProperty("items")[0].GetProperty("id").GetInt32());
        Assert.Equal("Thai fixture", wire.GetProperty("items")[0].GetProperty("name").GetString());
        Assert.False(wire.GetProperty("items")[0].GetProperty("allowSocialMedia").GetBoolean());
        Assert.Equal(1, wire.GetProperty("totalRecords").GetInt32());
        Assert.False(wire.GetProperty("items")[0].TryGetProperty("description", out _));
        Assert.False(wire.GetProperty("items")[0].TryGetProperty("comment", out _));
        Assert.False(wire.GetProperty("items")[0].TryGetProperty("trackingNumber", out _));
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
