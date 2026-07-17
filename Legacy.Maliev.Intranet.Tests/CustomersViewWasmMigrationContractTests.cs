namespace Legacy.Maliev.Intranet.Tests;

public sealed class CustomersViewWasmMigrationContractTests
{
    [Fact]
    public void CustomerViewSlice_PreservesRouteAuthorizationDtoAndRollbackContracts()
    {
        var root = FindRoot();
        var featurePage = Path.Combine(
            root,
            "Legacy.Maliev.Intranet.Client.Features.Customers",
            "Pages",
            "CustomerView.razor");
        var contracts = Path.Combine(
            root,
            "Legacy.Maliev.Intranet.Contracts",
            "CustomerDetailContracts.cs");
        var proxy = File.ReadAllText(Path.Combine(
            root,
            "Legacy.Maliev.Intranet.Bff",
            "Customers",
            "CustomersProxy.cs"));
        var bffProgram = File.ReadAllText(Path.Combine(
            root,
            "Legacy.Maliev.Intranet.Bff",
            "Program.cs"));

        Assert.True(File.Exists(featurePage), "The lazy WASM customer detail route is missing.");
        var page = File.ReadAllText(featurePage);
        Assert.Contains("@page \"/Customers/View\"", page, StringComparison.Ordinal);
        Assert.Contains("[Authorize", page, StringComparison.Ordinal);
        Assert.Contains("[SupplyParameterFromQuery(Name = \"id\")]", page, StringComparison.Ordinal);
        Assert.Contains("/bff/customers/{Id}", page, StringComparison.Ordinal);
        Assert.Contains("HttpStatusCode.Unauthorized", page, StringComparison.Ordinal);
        Assert.Contains("HttpStatusCode.Forbidden", page, StringComparison.Ordinal);
        Assert.Contains("HttpStatusCode.NotFound", page, StringComparison.Ordinal);
        Assert.Contains("HttpStatusCode.TooManyRequests", page, StringComparison.Ordinal);
        Assert.Contains("aria-live=\"polite\"", page, StringComparison.Ordinal);
        Assert.Contains("Href=\"/Customers/Index\"", page, StringComparison.Ordinal);

        Assert.True(File.Exists(contracts), "The browser-safe full customer detail DTO is missing.");
        var contractSource = File.ReadAllText(contracts);
        Assert.Contains("CustomerDetail", contractSource, StringComparison.Ordinal);
        Assert.Contains("CustomerCompanyDetail", contractSource, StringComparison.Ordinal);
        Assert.Contains("CustomerAddressDetail", contractSource, StringComparison.Ordinal);

        Assert.Contains("GetByIdAsync", proxy, StringComparison.Ordinal);
        Assert.Contains("$\"/customers/{id}\"", proxy, StringComparison.Ordinal);
        Assert.Contains("MapGet(\"/bff/customers/{id:int}\"", bffProgram, StringComparison.Ordinal);
        Assert.Contains("LegacyEmployeePermissions.CustomersRead", bffProgram, StringComparison.Ordinal);

        Assert.True(File.Exists(Path.Combine(root, "Legacy.Maliev.Intranet", "Pages", "Customers", "View.cshtml")),
            "The Razor customer detail rollback page must remain in this slice.");
        Assert.True(File.Exists(Path.Combine(root, "Legacy.Maliev.Intranet", "Pages", "Customers", "View.cshtml.cs")),
            "The Razor customer detail rollback PageModel must remain in this slice.");
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
