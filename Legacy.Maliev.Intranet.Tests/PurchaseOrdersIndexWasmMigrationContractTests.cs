using System.Text.Json;

namespace Legacy.Maliev.Intranet.Tests;

public sealed class PurchaseOrdersIndexWasmMigrationContractTests
{
    [Fact]
    public void PurchaseOrdersIndex_IsLazyAuthorizedLocalizedAndKeepsRazorFallbacks()
    {
        var root = FindRoot();
        var pagePath = Path.Combine(root, "Legacy.Maliev.Intranet.Client.Features.Procurement", "Pages", "PurchaseOrders.razor");
        var resourcePath = Path.Combine(root, "Legacy.Maliev.Intranet.Client.Features.Procurement", "Pages", "PurchaseOrders.resx");

        Assert.True(File.Exists(pagePath), "The WASM PurchaseOrders/Index route is missing.");
        Assert.True(File.Exists(resourcePath), "The PurchaseOrders/Index localization resource is missing.");

        var page = File.ReadAllText(pagePath);
        var app = File.ReadAllText(Path.Combine(root, "Legacy.Maliev.Intranet.Client", "App.razor"));
        var bff = File.ReadAllText(Path.Combine(root, "Legacy.Maliev.Intranet.Bff", "Program.cs"));
        var authContracts = File.ReadAllText(Path.Combine(root, "Legacy.Maliev.Intranet.Server", "Auth", "AuthContracts.cs"));

        Assert.Contains("@page \"/PurchaseOrders/Index\"", page, StringComparison.Ordinal);
        Assert.Contains("@attribute [Authorize]", page, StringComparison.Ordinal);
        Assert.Contains("[SupplyParameterFromQuery", page, StringComparison.Ordinal);
        Assert.Contains("/bff/purchase-orders", page, StringComparison.Ordinal);
        Assert.Contains("/bff/employees", page, StringComparison.Ordinal);
        Assert.Contains("Task.WhenAll", page, StringComparison.Ordinal);
        Assert.Contains("MudTable", page, StringComparison.Ordinal);
        Assert.Contains("Navigation.NavigateTo(\"/PurchaseOrders/Create\")", page, StringComparison.Ordinal);
        Assert.Contains("Navigation.NavigateTo($\"/PurchaseOrders/View?id={id}\", forceLoad: true)", page, StringComparison.Ordinal);
        Assert.Equal(1, CountOccurrences(page, "forceLoad: true"));
        Assert.Contains("HttpStatusCode.Unauthorized", page, StringComparison.Ordinal);
        Assert.Contains("HttpStatusCode.Forbidden", page, StringComparison.Ordinal);
        Assert.Contains("HttpStatusCode.TooManyRequests", page, StringComparison.Ordinal);
        Assert.Contains("Math.Clamp", page, StringComparison.Ordinal);
        Assert.Contains("Enum.IsDefined(parsedSort)", page, StringComparison.Ordinal);
        Assert.Contains("Uri.EscapeDataString", page, StringComparison.Ordinal);

        Assert.Contains("PurchaseOrders/", app, StringComparison.Ordinal);
        Assert.Contains("Legacy.Maliev.Intranet.Client.Features.Procurement.wasm", app, StringComparison.Ordinal);
        Assert.Contains("legacy-procurement.purchase-orders.read", authContracts, StringComparison.Ordinal);
        Assert.Contains("LegacyEmployeePermissions.PurchaseOrdersRead", bff, StringComparison.Ordinal);
        Assert.Contains("MapGet(\"/bff/purchase-orders\"", bff, StringComparison.Ordinal);
        Assert.Contains("AddHttpMessageHandler<LegacyServiceAuthenticationHandler>()", bff, StringComparison.Ordinal);
        Assert.Contains("AddEndpointFilter<AntiforgeryValidationFilter>()", bff, StringComparison.Ordinal);
        Assert.DoesNotContain("DbContext", bff, StringComparison.Ordinal);
        Assert.DoesNotContain("jquery", page, StringComparison.OrdinalIgnoreCase);

        foreach (var route in new[] { "Index", "Create", "View" })
        {
            Assert.True(File.Exists(Path.Combine(root, "Legacy.Maliev.Intranet", "Pages", "PurchaseOrders", $"{route}.cshtml")),
                $"The {route} Razor fallback must remain while only PurchaseOrders/Index is migrated.");
        }
    }

    [Fact]
    public void PurchaseOrderDtos_PreserveDisplayedLegacyFieldsWithoutExposingUnusedData()
    {
        var assembly = typeof(Legacy.Maliev.Intranet.Contracts.EmployeeListPage).Assembly;
        var orderType = assembly.GetType("Legacy.Maliev.Intranet.Contracts.PurchaseOrderListItem");
        var pageType = assembly.GetType("Legacy.Maliev.Intranet.Contracts.PurchaseOrderListPage");

        Assert.NotNull(orderType);
        Assert.NotNull(pageType);
        Assert.Equal(
            ["CreatedDate", "EmployeeId", "Fob", "Id", "ShippingMethod", "Terms"],
            orderType.GetProperties().Select(property => property.Name).Order(StringComparer.Ordinal).ToArray());
        Assert.Equal(
            ["HasNextPage", "HasPreviousPage", "Items", "PageIndex", "TotalPages", "TotalRecords"],
            pageType.GetProperties().Select(property => property.Name).Order(StringComparer.Ordinal).ToArray());

        const string json = """{"items":[{"id":42,"supplierId":3,"supplierContactPerson":"secret","shippingAddressId":7,"shippingContactPerson":"hidden","shippingTelephone":"02","shippingMobile":"08","shippingFax":"fax","billingAddressId":8,"billingContactPerson":"hidden","billingTelephone":"02","billingMobile":"08","billingFax":"fax","fob":"Bangkok","terms":"Net 30","shippingMethod":"Courier","employeeId":9,"notes":"private note","createdDate":"2030-07-15T10:30:00","modifiedDate":null}],"pageIndex":1,"totalPages":1,"totalRecords":1,"hasNextPage":false,"hasPreviousPage":false}""";
        var page = JsonSerializer.Deserialize(json, pageType, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.NotNull(page);
        var wire = JsonSerializer.SerializeToElement(page, pageType, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        var item = wire.GetProperty("items")[0];
        Assert.Equal(42, item.GetProperty("id").GetInt32());
        Assert.Equal(9, item.GetProperty("employeeId").GetInt32());
        Assert.Equal("Bangkok", item.GetProperty("fob").GetString());
        Assert.Equal("Net 30", item.GetProperty("terms").GetString());
        Assert.Equal("Courier", item.GetProperty("shippingMethod").GetString());
        Assert.False(item.TryGetProperty("supplierId", out _));
        Assert.False(item.TryGetProperty("supplierContactPerson", out _));
        Assert.False(item.TryGetProperty("notes", out _));
        Assert.False(item.TryGetProperty("modifiedDate", out _));
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

    private static int CountOccurrences(string value, string search) =>
        value.Split(search, StringSplitOptions.None).Length - 1;
}
