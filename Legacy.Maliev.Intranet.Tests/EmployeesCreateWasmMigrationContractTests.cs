namespace Legacy.Maliev.Intranet.Tests;

public sealed class EmployeesCreateWasmMigrationContractTests
{
    [Fact]
    public void EmployeesCreateSlice_IsLazyAuthorizedCsrfProtectedAndPreservesRollback()
    {
        var root = FindRoot();
        var featurePage = Path.Combine(root, "Legacy.Maliev.Intranet.Client.Features.Employees", "Pages", "EmployeeCreate.razor");
        var workflow = Path.Combine(root, "Legacy.Maliev.Intranet.Server", "Employees", "EmployeeAccountCreationService.cs");
        var clients = Path.Combine(root, "Legacy.Maliev.Intranet.Server", "Employees", "EmployeeAccountCreationClients.cs");
        var bffProgram = File.ReadAllText(Path.Combine(root, "Legacy.Maliev.Intranet.Bff", "Program.cs"));

        Assert.True(File.Exists(featurePage), "The WASM employee-account creation route is missing.");
        var page = File.ReadAllText(featurePage);
        Assert.Contains("@page \"/Employees/Create\"", page, StringComparison.Ordinal);
        Assert.Contains("[Authorize", page, StringComparison.Ordinal);
        Assert.Contains("MudForm", page, StringComparison.Ordinal);
        Assert.Contains("model.Password", page, StringComparison.Ordinal);
        Assert.Contains("model.ConfirmPassword", page, StringComparison.Ordinal);
        Assert.Contains("submitting", page, StringComparison.Ordinal);
        Assert.Contains("X-CSRF-TOKEN", page, StringComparison.Ordinal);
        Assert.Contains("JsonContent.Create(model)", page, StringComparison.Ordinal);
        Assert.Contains("HttpMethod.Post, \"/bff/employees\"", page, StringComparison.Ordinal);
        Assert.Contains("NavigateToLogin", page, StringComparison.Ordinal);
        Assert.Contains("aria-live=\"assertive\"", page, StringComparison.Ordinal);

        Assert.True(File.Exists(workflow), "The server-side profile/identity orchestration service is missing.");
        Assert.True(File.Exists(clients), "The server-only EmployeeService and AuthService clients are missing.");
        var clientSource = File.ReadAllText(clients);
        Assert.Contains("/employees", clientSource, StringComparison.Ordinal);
        Assert.Contains("/auth/v1/employee-identities/", clientSource, StringComparison.Ordinal);
        Assert.Contains("PropertyNamingPolicy = null", clientSource, StringComparison.Ordinal);
        Assert.Contains("MapPost(\"/bff/employees\"", bffProgram, StringComparison.Ordinal);
        Assert.Contains("EmployeeAccountCreationService", bffProgram, StringComparison.Ordinal);
        Assert.Contains("AntiforgeryValidationFilter", bffProgram, StringComparison.Ordinal);
        Assert.Contains("LegacyEmployeePermissions.EmployeesCreate", bffProgram, StringComparison.Ordinal);

        Assert.True(File.Exists(Path.Combine(root, "Legacy.Maliev.Intranet", "Pages", "Employees", "Create.cshtml")),
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
