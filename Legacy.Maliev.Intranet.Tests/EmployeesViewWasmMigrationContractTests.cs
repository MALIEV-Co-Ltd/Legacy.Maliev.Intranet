namespace Legacy.Maliev.Intranet.Tests;

public sealed class EmployeesViewWasmMigrationContractTests
{
    [Fact]
    public void EmployeesViewSlice_IsLazyAuthorizedAndPreservesTheCompleteProfileBoundary()
    {
        var root = FindRoot();
        var featurePage = Path.Combine(root, "Legacy.Maliev.Intranet.Client.Features.Employees", "Pages", "EmployeeView.razor");
        var featureResources = Path.Combine(root, "Legacy.Maliev.Intranet.Client.Features.Employees", "Pages", "EmployeeView.resx");
        var contracts = Path.Combine(root, "Legacy.Maliev.Intranet.Contracts", "EmployeeDetailContracts.cs");
        var proxy = Path.Combine(root, "Legacy.Maliev.Intranet.Bff", "Employees", "EmployeesProxy.cs");
        var bffProgram = File.ReadAllText(Path.Combine(root, "Legacy.Maliev.Intranet.Bff", "Program.cs"));
        var authContracts = File.ReadAllText(Path.Combine(root, "Legacy.Maliev.Intranet.Server", "Auth", "AuthContracts.cs"));

        Assert.True(File.Exists(featurePage), "The lazy WASM employee detail route is missing.");
        Assert.True(File.Exists(featureResources), "The employee detail localization resources are missing.");
        Assert.True(File.Exists(contracts), "The browser-safe employee detail contract is missing.");
        Assert.True(File.Exists(proxy), "The EmployeeService BFF proxy is missing.");

        var page = File.ReadAllText(featurePage);
        Assert.Contains("@page \"/Employees/View\"", page, StringComparison.Ordinal);
        Assert.Contains("[Authorize", page, StringComparison.Ordinal);
        Assert.Contains("SupplyParameterFromQuery(Name = \"id\")", page, StringComparison.Ordinal);
        Assert.Contains("/bff/employees/", page, StringComparison.Ordinal);
        Assert.Contains("MudProgress", page, StringComparison.Ordinal);
        Assert.Contains("MudAlert", page, StringComparison.Ordinal);
        Assert.Contains("yyyy-MM-dd", page, StringComparison.Ordinal);
        Assert.Contains("detail.Role?.Name", page, StringComparison.Ordinal);
        Assert.Contains("detail.HomeAddress?.AddressLine1", page, StringComparison.Ordinal);
        Assert.Contains("HttpStatusCode.Unauthorized", page, StringComparison.Ordinal);
        Assert.Contains("HttpStatusCode.Forbidden", page, StringComparison.Ordinal);
        Assert.Contains("HttpStatusCode.NotFound", page, StringComparison.Ordinal);
        Assert.Contains("HttpStatusCode.TooManyRequests", page, StringComparison.Ordinal);
        Assert.Contains("/Employees/Index", page, StringComparison.Ordinal);

        var contractSource = File.ReadAllText(contracts);
        Assert.Contains("EmployeeDetail", contractSource, StringComparison.Ordinal);
        Assert.Contains("EmployeeAddressDetail", contractSource, StringComparison.Ordinal);
        Assert.Contains("EmployeeRoleDetail", contractSource, StringComparison.Ordinal);
        Assert.Contains("MapGet(\"/bff/employees/{id:int}\"", bffProgram, StringComparison.Ordinal);
        Assert.Contains("LegacyEmployeePermissions.EmployeesRead", bffProgram, StringComparison.Ordinal);
        Assert.Contains("public const string EmployeesRead = \"legacy-employee.employees.read\";", authContracts, StringComparison.Ordinal);
        Assert.True(File.Exists(Path.Combine(root, "Legacy.Maliev.Intranet", "Pages", "Employees", "View.cshtml")),
            "The compatibility Razor fallback must remain in this slice.");
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
