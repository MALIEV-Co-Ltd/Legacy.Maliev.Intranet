using System.Text.Json;

namespace Legacy.Maliev.Intranet.Tests;

public sealed class SuppliersIndexWasmMigrationContractTests
{
    [Fact]
    public void SuppliersIndex_IsLazyAuthorizedLocalizedAndKeepsTheRazorFallback()
    {
        var root = FindRoot();
        var featureProject = Path.Combine(root, "Legacy.Maliev.Intranet.Client.Features.Procurement", "Legacy.Maliev.Intranet.Client.Features.Procurement.csproj");
        var pagePath = Path.Combine(root, "Legacy.Maliev.Intranet.Client.Features.Procurement", "Pages", "Suppliers.razor");
        var resourcePath = Path.Combine(root, "Legacy.Maliev.Intranet.Client.Features.Procurement", "Pages", "Suppliers.resx");

        Assert.True(File.Exists(featureProject), "The lazy Procurement feature assembly is missing.");
        Assert.True(File.Exists(pagePath), "The WASM Suppliers/Index route is missing.");
        Assert.True(File.Exists(resourcePath), "The Suppliers/Index localization resource is missing.");

        var page = File.ReadAllText(pagePath);
        var app = File.ReadAllText(Path.Combine(root, "Legacy.Maliev.Intranet.Client", "App.razor"));
        var clientProject = File.ReadAllText(Path.Combine(root, "Legacy.Maliev.Intranet.Client", "Legacy.Maliev.Intranet.Client.csproj"));
        var solution = File.ReadAllText(Path.Combine(root, "Legacy.Maliev.Intranet.slnx"));
        var bff = File.ReadAllText(Path.Combine(root, "Legacy.Maliev.Intranet.Bff", "Program.cs"));
        var authContracts = File.ReadAllText(Path.Combine(root, "Legacy.Maliev.Intranet.Server", "Auth", "AuthContracts.cs"));

        Assert.Contains("@page \"/Suppliers/Index\"", page, StringComparison.Ordinal);
        Assert.Contains("@attribute [Authorize]", page, StringComparison.Ordinal);
        Assert.Contains("[SupplyParameterFromQuery", page, StringComparison.Ordinal);
        Assert.Contains("/bff/suppliers", page, StringComparison.Ordinal);
        Assert.Contains("MudTable", page, StringComparison.Ordinal);
        Assert.Contains("/Suppliers/Create", page, StringComparison.Ordinal);
        Assert.Contains("/Suppliers/View?id=", page, StringComparison.Ordinal);
        Assert.Contains("Navigation.NavigateTo(\"/Suppliers/Create\")", page, StringComparison.Ordinal);
        Assert.Contains("Navigation.NavigateTo($\"/Suppliers/View?id={id}\")", page, StringComparison.Ordinal);
        Assert.Equal(0, CountOccurrences(page, "forceLoad: true"));
        Assert.Contains("HttpStatusCode.Unauthorized", page, StringComparison.Ordinal);
        Assert.Contains("HttpStatusCode.Forbidden", page, StringComparison.Ordinal);
        Assert.Contains("HttpStatusCode.TooManyRequests", page, StringComparison.Ordinal);
        Assert.Contains("Math.Clamp", page, StringComparison.Ordinal);
        Assert.Contains("Enum.IsDefined(parsedSort)", page, StringComparison.Ordinal);
        Assert.Contains("Uri.EscapeDataString", page, StringComparison.Ordinal);

        Assert.Contains("Legacy.Maliev.Intranet.Client.Features.Procurement.wasm", app, StringComparison.Ordinal);
        Assert.Contains("Legacy.Maliev.Intranet.Client.Features.Procurement.wasm", clientProject, StringComparison.Ordinal);
        Assert.Contains("Legacy.Maliev.Intranet.Client.Features.Procurement", solution, StringComparison.Ordinal);
        Assert.Contains("legacy-procurement.suppliers.read", authContracts, StringComparison.Ordinal);
        Assert.Contains("LegacyEmployeePermissions.SuppliersRead", bff, StringComparison.Ordinal);
        Assert.Contains("MapGet(\"/bff/suppliers\"", bff, StringComparison.Ordinal);
        Assert.Contains("AddHttpMessageHandler<LegacyServiceAuthenticationHandler>()", bff, StringComparison.Ordinal);
        Assert.Contains("AddEndpointFilter<AntiforgeryValidationFilter>()", bff, StringComparison.Ordinal);
        Assert.DoesNotContain("DbContext", bff, StringComparison.Ordinal);
        Assert.DoesNotContain("jquery", page, StringComparison.OrdinalIgnoreCase);
        Assert.True(File.Exists(Path.Combine(root, "Legacy.Maliev.Intranet", "Pages", "Suppliers", "Index.cshtml")),
            "The compatibility Razor fallback must remain in this slice.");
        Assert.True(File.Exists(Path.Combine(root, "Legacy.Maliev.Intranet", "Pages", "Suppliers", "Create.cshtml")),
            "Create must retain its Razor fallback while only the index route is migrated.");
        Assert.True(File.Exists(Path.Combine(root, "Legacy.Maliev.Intranet", "Pages", "Suppliers", "View.cshtml")),
            "View must retain its Razor fallback while only the index route is migrated.");
    }

    [Fact]
    public void SupplierDtos_PreserveDisplayedLegacyFieldsWithoutExposingUnusedData()
    {
        var assembly = typeof(Legacy.Maliev.Intranet.Contracts.EmployeeListPage).Assembly;
        var supplierType = assembly.GetType("Legacy.Maliev.Intranet.Contracts.SupplierListItem");
        var pageType = assembly.GetType("Legacy.Maliev.Intranet.Contracts.SupplierListPage");

        Assert.NotNull(supplierType);
        Assert.NotNull(pageType);
        Assert.Equal(
            ["Email", "Id", "Name", "Telephone"],
            supplierType.GetProperties().Select(property => property.Name).Order(StringComparer.Ordinal).ToArray());
        Assert.Equal(
            ["HasNextPage", "HasPreviousPage", "Items", "PageIndex", "TotalPages", "TotalRecords"],
            pageType.GetProperties().Select(property => property.Name).Order(StringComparer.Ordinal).ToArray());

        const string json = """{"items":[{"id":42,"name":"Thai supplier","website":"https://secret.example","taxNumber":"123","email":"supplier@example.com","note":"private note","addressId":7,"telephone":"02-123-4567","mobile":"089-000-0000","fax":"02-987-6543","createdDate":"2030-07-15T00:00:00","modifiedDate":null}],"pageIndex":1,"totalPages":1,"totalRecords":1,"hasNextPage":false,"hasPreviousPage":false}""";
        var page = JsonSerializer.Deserialize(json, pageType, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.NotNull(page);
        var wire = JsonSerializer.SerializeToElement(page, pageType, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.Equal(42, wire.GetProperty("items")[0].GetProperty("id").GetInt32());
        Assert.Equal("Thai supplier", wire.GetProperty("items")[0].GetProperty("name").GetString());
        Assert.Equal("supplier@example.com", wire.GetProperty("items")[0].GetProperty("email").GetString());
        Assert.Equal("02-123-4567", wire.GetProperty("items")[0].GetProperty("telephone").GetString());
        Assert.False(wire.GetProperty("items")[0].TryGetProperty("website", out _));
        Assert.False(wire.GetProperty("items")[0].TryGetProperty("taxNumber", out _));
        Assert.False(wire.GetProperty("items")[0].TryGetProperty("note", out _));
        Assert.False(wire.GetProperty("items")[0].TryGetProperty("mobile", out _));
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
