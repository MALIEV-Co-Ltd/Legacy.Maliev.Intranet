namespace Legacy.Maliev.Intranet.Tests;

public sealed class EmployeesWasmMigrationContractTests
{
    [Fact]
    public void EmployeesIndexSlice_IsLazyAuthorizedAndPreservesTheLegacyBoundary()
    {
        var root = FindRoot();
        var featureProject = Path.Combine(root, "Legacy.Maliev.Intranet.Client.Features.Employees", "Legacy.Maliev.Intranet.Client.Features.Employees.csproj");
        var featurePage = Path.Combine(root, "Legacy.Maliev.Intranet.Client.Features.Employees", "Pages", "Employees.razor");
        var featureResources = Path.Combine(root, "Legacy.Maliev.Intranet.Client.Features.Employees", "Pages", "Employees.resx");
        var bffProgram = File.ReadAllText(Path.Combine(root, "Legacy.Maliev.Intranet.Bff", "Program.cs"));
        var authContracts = File.ReadAllText(Path.Combine(root, "Legacy.Maliev.Intranet.Server", "Auth", "AuthContracts.cs"));
        var app = File.ReadAllText(Path.Combine(root, "Legacy.Maliev.Intranet.Client", "App.razor"));
        var clientProject = File.ReadAllText(Path.Combine(root, "Legacy.Maliev.Intranet.Client", "Legacy.Maliev.Intranet.Client.csproj"));
        var solution = File.ReadAllText(Path.Combine(root, "Legacy.Maliev.Intranet.slnx"));

        Assert.True(File.Exists(featureProject), "The lazy Employees feature assembly is missing.");
        Assert.True(File.Exists(featurePage), "The WASM Employees route is missing.");
        Assert.True(File.Exists(featureResources), "The Employees localization resources are missing.");

        var page = File.ReadAllText(featurePage);
        Assert.Contains("@page \"/Employees/Index\"", page, StringComparison.Ordinal);
        Assert.Contains("[Authorize", page, StringComparison.Ordinal);
        Assert.Contains("EmployeeId_Descending", page, StringComparison.Ordinal);
        Assert.Contains("/bff/employees", page, StringComparison.Ordinal);
        Assert.Contains("MudTable", page, StringComparison.Ordinal);
        Assert.Contains("MudProgress", page, StringComparison.Ordinal);
        Assert.Contains("MudAlert", page, StringComparison.Ordinal);
        Assert.Contains("/Employees/Create", page, StringComparison.Ordinal);
        Assert.Contains("/Employees/View?id=", page, StringComparison.Ordinal);
        Assert.Contains("SupplyParameterFromQuery", page, StringComparison.Ordinal);
        Assert.Contains("HttpStatusCode.Unauthorized", page, StringComparison.Ordinal);
        Assert.Contains("HttpStatusCode.Forbidden", page, StringComparison.Ordinal);
        Assert.Contains("HttpStatusCode.TooManyRequests", page, StringComparison.Ordinal);
        Assert.Contains("Math.Clamp", page, StringComparison.Ordinal);
        Assert.Contains("Uri.EscapeDataString", page, StringComparison.Ordinal);
        Assert.Contains("PageSummary", page, StringComparison.Ordinal);
        Assert.DoesNotContain("HubConnection", page, StringComparison.Ordinal);

        Assert.Contains("LegacyEmployeePermissions.EmployeesList", bffProgram, StringComparison.Ordinal);
        Assert.Contains("public const string EmployeesList = \"legacy-employee.employees.list\";", authContracts, StringComparison.Ordinal);
        Assert.Contains("MapGet(\"/bff/employees\"", bffProgram, StringComparison.Ordinal);
        Assert.Contains("Services:Employee", bffProgram, StringComparison.Ordinal);
        Assert.Contains("AddHttpMessageHandler<LegacyServiceAuthenticationHandler>()", bffProgram, StringComparison.Ordinal);
        Assert.DoesNotContain("DbContext", bffProgram, StringComparison.Ordinal);

        Assert.Contains("Legacy.Maliev.Intranet.Client.Features.Employees.wasm", app, StringComparison.Ordinal);
        Assert.Contains("Legacy.Maliev.Intranet.Client.Features.Employees.wasm", clientProject, StringComparison.Ordinal);
        Assert.Contains("Legacy.Maliev.Intranet.Client.Features.Employees", solution, StringComparison.Ordinal);
        Assert.True(File.Exists(Path.Combine(root, "Legacy.Maliev.Intranet", "Pages", "Employees", "Index.cshtml")),
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
