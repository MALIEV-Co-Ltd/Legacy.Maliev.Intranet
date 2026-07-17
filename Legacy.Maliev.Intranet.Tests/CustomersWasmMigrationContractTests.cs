namespace Legacy.Maliev.Intranet.Tests;

public sealed class CustomersWasmMigrationContractTests
{
    [Fact]
    public void CustomersIndexSlice_IsLazyAuthorizedAndPreservesTheLegacyBoundary()
    {
        var root = FindRoot();
        var featureProject = Path.Combine(root, "Legacy.Maliev.Intranet.Client.Features.Customers", "Legacy.Maliev.Intranet.Client.Features.Customers.csproj");
        var featurePage = Path.Combine(root, "Legacy.Maliev.Intranet.Client.Features.Customers", "Pages", "Customers.razor");
        var bffProgram = File.ReadAllText(Path.Combine(root, "Legacy.Maliev.Intranet.Bff", "Program.cs"));
        var authContracts = File.ReadAllText(Path.Combine(root, "Legacy.Maliev.Intranet.Server", "Auth", "AuthContracts.cs"));
        var app = File.ReadAllText(Path.Combine(root, "Legacy.Maliev.Intranet.Client", "App.razor"));
        var solution = File.ReadAllText(Path.Combine(root, "Legacy.Maliev.Intranet.slnx"));

        Assert.True(File.Exists(featureProject), "The lazy Customers feature assembly is missing.");
        Assert.True(File.Exists(featurePage), "The WASM Customers route is missing.");
        var page = File.ReadAllText(featurePage);
        Assert.Contains("@page \"/Customers/Index\"", page, StringComparison.Ordinal);
        Assert.Contains("[Authorize", page, StringComparison.Ordinal);
        Assert.Contains("CustomerCreatedDate_Descending", page, StringComparison.Ordinal);
        Assert.Contains("/bff/customers", page, StringComparison.Ordinal);
        Assert.Contains("MudTable", page, StringComparison.Ordinal);
        Assert.Contains("MudProgress", page, StringComparison.Ordinal);
        Assert.Contains("MudAlert", page, StringComparison.Ordinal);
        Assert.Contains("/Customers/View?id=", page, StringComparison.Ordinal);
        Assert.Contains("SupplyParameterFromQuery", page, StringComparison.Ordinal);
        Assert.Contains("HttpStatusCode.Unauthorized", page, StringComparison.Ordinal);
        Assert.Contains("HttpStatusCode.Forbidden", page, StringComparison.Ordinal);
        Assert.Contains("HttpStatusCode.TooManyRequests", page, StringComparison.Ordinal);
        Assert.Contains("Math.Clamp", page, StringComparison.Ordinal);
        Assert.Contains("Uri.EscapeDataString", page, StringComparison.Ordinal);
        Assert.Contains("PageSummary", page, StringComparison.Ordinal);

        Assert.Contains("LegacyEmployeePermissions.CustomersList", bffProgram, StringComparison.Ordinal);
        Assert.Contains("public const string CustomersList = \"legacy-customer.customers.list\";", authContracts, StringComparison.Ordinal);
        Assert.Contains("MapGet(\"/bff/customers\"", bffProgram, StringComparison.Ordinal);
        Assert.Contains("Services:Customer", bffProgram, StringComparison.Ordinal);
        Assert.Contains("AddHttpMessageHandler<LegacyServiceAuthenticationHandler>()", bffProgram, StringComparison.Ordinal);
        Assert.DoesNotContain("DbContext", bffProgram, StringComparison.Ordinal);

        Assert.Contains("Legacy.Maliev.Intranet.Client.Features.Customers.wasm", app, StringComparison.Ordinal);
        Assert.Contains("Legacy.Maliev.Intranet.Client.Features.Customers", solution, StringComparison.Ordinal);
        Assert.True(File.Exists(Path.Combine(root, "Legacy.Maliev.Intranet", "Pages", "Customers", "Index.cshtml")),
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
