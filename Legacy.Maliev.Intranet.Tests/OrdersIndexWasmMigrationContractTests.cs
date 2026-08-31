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
        Assert.Contains("session.LegacyDatabaseId", page, StringComparison.Ordinal);
        Assert.DoesNotContain("session.DisplayName", page, StringComparison.Ordinal);
        Assert.Contains("Task.WhenAll", page, StringComparison.Ordinal);
        Assert.Contains("AssignedOrders", page, StringComparison.Ordinal);
        Assert.Contains("UnassignedOrders", page, StringComparison.Ordinal);
        Assert.Contains("mlv-module-shell", page, StringComparison.Ordinal);
        Assert.Contains("<ModuleHeader", page, StringComparison.Ordinal);
        Assert.Contains("<OperationalDataTable", page, StringComparison.Ordinal);
        Assert.Contains("HandleDataTableRequestAsync", page, StringComparison.Ordinal);
        Assert.Contains("<ProgressiveSkeleton", page, StringComparison.Ordinal);
        Assert.Contains("<PageBreadcrumbs", page, StringComparison.Ordinal);
        Assert.Contains("new(Text[\"Operations\"], \"/Dashboard\")", page, StringComparison.Ordinal);
        Assert.DoesNotContain("new(Text[\"Operations\"], \"/\")", page, StringComparison.Ordinal);
        Assert.Contains("<OperationalDataTable TItem=\"OrderListItem\"", page, StringComparison.Ordinal);
        Assert.Contains("ShadcnDataTableState", page, StringComparison.Ordinal);
        Assert.Contains("State=\"@dataTableState\"", page, StringComparison.Ordinal);
        Assert.Contains("DetailHref=\"@(order => $\"/Orders/View?id={order.Id}\")\"", page, StringComparison.Ordinal);
        Assert.Contains("DetailAriaLabel=\"@(order => Text[\"ViewOrder\", order.Id])\"", page, StringComparison.Ordinal);
        Assert.Contains("DetailsAriaLabel=\"@(order => Text[\"ExpandOrder\", order.Id])\"", page, StringComparison.Ordinal);
        Assert.Contains("ShadcnDataTableColumn", page, StringComparison.Ordinal);
        Assert.Contains("QuickViewContent", page, StringComparison.Ordinal);
        Assert.DoesNotContain("<MudSimpleTable", page, StringComparison.Ordinal);
        Assert.DoesNotContain("data-label=", page, StringComparison.Ordinal);
        Assert.Contains("LoadingText=", page, StringComparison.Ordinal);
        Assert.Contains("/Orders/Create", page, StringComparison.Ordinal);
        Assert.Contains("/Orders/View?id=", page, StringComparison.Ordinal);
        Assert.Contains("ShadcnDataTableRequest", page, StringComparison.Ordinal);
        Assert.Contains("LatestRequestGate", page, StringComparison.Ordinal);
        Assert.Contains("HttpStatusCode.Unauthorized", page, StringComparison.Ordinal);
        Assert.Contains("HttpStatusCode.Forbidden", page, StringComparison.Ordinal);
        Assert.Contains("HttpStatusCode.TooManyRequests", page, StringComparison.Ordinal);
        Assert.Contains("Math.Clamp", page, StringComparison.Ordinal);
        Assert.Contains("Uri.EscapeDataString", page, StringComparison.Ordinal);
        Assert.Contains("BuildDataTableState", page, StringComparison.Ordinal);

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
    public void OrdersIndex_PreservesAllDisplayedFieldsAndRejectsMobileCardConversion()
    {
        var root = FindRoot();
        var page = File.ReadAllText(Path.Combine(root, "Legacy.Maliev.Intranet.Client.Features.Orders", "Pages", "Orders.razor"));
        var css = File.ReadAllText(Path.Combine(root, "Legacy.Maliev.Intranet.Client.Features.Orders", "Pages", "Orders.razor.css"));

        foreach (var field in new[]
                 {
                     "order.Id", "order.CustomerId", "order.ProcessId", "order.Name", "order.Quantity",
                     "order.Manufactured", "order.Remaining", "order.EmployeeId", "order.Subtotal", "order.PromisedDate",
                     "order.AllowSocialMedia",
                 })
        {
            Assert.Contains(field, page, StringComparison.Ordinal);
        }

        Assert.Contains("--operational-table-min-width", css, StringComparison.Ordinal);
        Assert.DoesNotContain("display: block", css, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("content: attr(data-label)", css, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("overflow-wrap: anywhere", css, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void OrdersIndex_LocalizedRowActionsHaveExactEnglishAndThaiParity()
    {
        var root = FindRoot();
        var resources = ReadResourceNames(Path.Combine(root, "Legacy.Maliev.Intranet.Client.Features.Orders", "Pages", "Orders.resx"));
        var thaiResources = ReadResourceNames(Path.Combine(root, "Legacy.Maliev.Intranet.Client.Features.Orders", "Pages", "Orders.th.resx"));

        Assert.Equal(resources, thaiResources);
        Assert.Contains("ViewOrder", resources);
        Assert.Contains("ExpandOrder", resources);
        Assert.Contains("CollapseOrder", resources);
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
                "AllowSocialMedia", "CreatedDate", "CustomerId", "EmployeeId", "Id", "Manufactured", "ModifiedDate",
                "Name", "ProcessId", "PromisedDate", "Quantity", "Remaining", "Subtotal",
            ],
            orderType.GetProperties().Select(property => property.Name).Order(StringComparer.Ordinal).ToArray());
        Assert.Equal(
            ["HasNextPage", "HasPreviousPage", "Items", "PageIndex", "TotalPages", "TotalRecords"],
            pageType.GetProperties().Select(property => property.Name).Order(StringComparer.Ordinal).ToArray());
        Assert.Equal(
            ["Id", "Name"],
            processType.GetProperties().Select(property => property.Name).Order(StringComparer.Ordinal).ToArray());

        const string json = """{"items":[{"id":84,"customerId":42,"employeeId":7,"name":"Thai fixture","description":"ไม้เอก ไม้โท","processId":3,"materialId":5,"surfaceFinishId":6,"colorId":4,"quantity":2,"manufactured":1,"remaining":1,"unitPrice":125,"discountPercent":10,"subtotal":225,"currencyId":1,"leadTime":3,"promisedDate":"2030-07-20T00:00:00","finishedDate":null,"turnaround":null,"comment":"note","allowSocialMedia":false,"allowCancellation":true,"allowPayment":false,"trackingNumber":"TRACK-1","createdDate":"2030-07-15T00:00:00","modifiedDate":null}],"pageIndex":1,"totalPages":1,"totalRecords":1,"hasNextPage":false,"hasPreviousPage":false}""";
        var page = JsonSerializer.Deserialize(json, pageType, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.NotNull(page);
        var wire = JsonSerializer.SerializeToElement(page, pageType, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.Equal(84, wire.GetProperty("items")[0].GetProperty("id").GetInt32());
        Assert.Equal("Thai fixture", wire.GetProperty("items")[0].GetProperty("name").GetString());
        Assert.False(wire.GetProperty("items")[0].GetProperty("allowSocialMedia").GetBoolean());
        Assert.Equal(
            new DateTime(2030, 7, 15),
            wire.GetProperty("items")[0].GetProperty("createdDate").GetDateTime());
        Assert.Equal(JsonValueKind.Null, wire.GetProperty("items")[0].GetProperty("modifiedDate").ValueKind);
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

    private static string[] ReadResourceNames(string path) =>
        System.Xml.Linq.XDocument.Load(path)
            .Root!
            .Elements("data")
            .Select(element => (string?)element.Attribute("name"))
            .Where(name => name is not null)
            .Select(name => name!)
            .Order(StringComparer.Ordinal)
            .ToArray();
}
