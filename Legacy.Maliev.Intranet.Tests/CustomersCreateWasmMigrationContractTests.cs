namespace Legacy.Maliev.Intranet.Tests;

public sealed class CustomersCreateWasmMigrationContractTests
{
    [Fact]
    public void CustomersCreateSlice_IsLazyAuthorizedCsrfProtectedAndPreservesRollback()
    {
        var root = FindRoot();
        var featurePage = Path.Combine(root, "Legacy.Maliev.Intranet.Client.Features.Customers", "Pages", "CustomerCreate.razor");
        var workflow = Path.Combine(root, "Legacy.Maliev.Intranet.Server", "Customers", "CustomerAccountCreationService.cs");
        var clients = Path.Combine(root, "Legacy.Maliev.Intranet.Server", "Customers", "CustomerAccountCreationClients.cs");
        var bffProgram = File.ReadAllText(Path.Combine(root, "Legacy.Maliev.Intranet.Bff", "Program.cs"));

        Assert.True(File.Exists(featurePage), "The WASM customer-account creation route is missing.");
        var page = File.ReadAllText(featurePage);
        Assert.Contains("@page \"/Customers/Create\"", page, StringComparison.Ordinal);
        Assert.Contains("[Authorize", page, StringComparison.Ordinal);
        Assert.Contains("MudForm", page, StringComparison.Ordinal);
        Assert.Contains("model.Password", page, StringComparison.Ordinal);
        Assert.Contains("model.ConfirmPassword", page, StringComparison.Ordinal);
        Assert.Contains("submitting", page, StringComparison.Ordinal);
        Assert.Contains("X-CSRF-TOKEN", page, StringComparison.Ordinal);
        Assert.Contains("JsonContent.Create(model)", page, StringComparison.Ordinal);
        Assert.Contains("HttpMethod.Post, \"/bff/customers\"", page, StringComparison.Ordinal);
        Assert.Contains("HttpStatusCode.Unauthorized", page, StringComparison.Ordinal);
        Assert.Contains("HttpStatusCode.Forbidden", page, StringComparison.Ordinal);
        Assert.Contains("HttpStatusCode.BadRequest", page, StringComparison.Ordinal);
        Assert.Contains("HttpStatusCode.Conflict", page, StringComparison.Ordinal);
        Assert.Contains("HttpStatusCode.TooManyRequests", page, StringComparison.Ordinal);
        Assert.Contains("NavigateToLogin", page, StringComparison.Ordinal);
        Assert.Contains("aria-live=\"assertive\"", page, StringComparison.Ordinal);

        Assert.True(File.Exists(workflow), "The server-side profile/identity orchestration service is missing.");
        Assert.True(File.Exists(clients), "The server-only CustomerService and AuthService clients are missing.");
        var clientSource = File.ReadAllText(clients);
        Assert.Contains("/customers", clientSource, StringComparison.Ordinal);
        Assert.Contains("/auth/v1/customer-identities/", clientSource, StringComparison.Ordinal);
        Assert.Contains("JsonContent.Create", clientSource, StringComparison.Ordinal);
        Assert.Contains("MapPost(\"/bff/customers\"", bffProgram, StringComparison.Ordinal);
        Assert.Contains("CustomerAccountCreationService", bffProgram, StringComparison.Ordinal);
        Assert.Contains("AntiforgeryValidationFilter", bffProgram, StringComparison.Ordinal);
        Assert.Contains("LegacyEmployeePermissions.CustomersCreate", bffProgram, StringComparison.Ordinal);

        Assert.True(File.Exists(Path.Combine(root, "Legacy.Maliev.Intranet", "Pages", "Customers", "Create.cshtml")),
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
