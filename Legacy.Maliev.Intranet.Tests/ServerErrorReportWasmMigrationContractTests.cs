namespace Legacy.Maliev.Intranet.Tests;

public sealed class ServerErrorReportWasmMigrationContractTests
{
    [Fact]
    public void ErrorReportSlice_IsLazyAuthorizedAndRedacted()
    {
        var root = FindRoot();
        var featureProject = Path.Combine(root, "Legacy.Maliev.Intranet.Client.Features.Diagnostics", "Legacy.Maliev.Intranet.Client.Features.Diagnostics.csproj");
        var featurePage = Path.Combine(root, "Legacy.Maliev.Intranet.Client.Features.Diagnostics", "Pages", "ErrorReport.razor");
        var bffProgram = File.ReadAllText(Path.Combine(root, "Legacy.Maliev.Intranet.Bff", "Program.cs"));
        var app = File.ReadAllText(Path.Combine(root, "Legacy.Maliev.Intranet.Client", "App.razor"));
        var clientProject = File.ReadAllText(Path.Combine(root, "Legacy.Maliev.Intranet.Client", "Legacy.Maliev.Intranet.Client.csproj"));
        var legacyRoutes = File.ReadAllText(Path.Combine(root, "Legacy.Maliev.Intranet", "LegacyRoutes.cs"));

        Assert.True(File.Exists(featureProject), "The lazy Diagnostics feature assembly is missing.");
        Assert.True(File.Exists(featurePage), "The WASM error-report route is missing.");

        var page = File.ReadAllText(featurePage);
        Assert.Contains("@page \"/Server/ErrorReport\"", page, StringComparison.Ordinal);
        Assert.Contains("[Authorize", page, StringComparison.Ordinal);
        Assert.Contains("/bff/diagnostics/events", page, StringComparison.Ordinal);
        Assert.Contains("SupplyParameterFromQuery", page, StringComparison.Ordinal);
        Assert.Contains("LogTimestamp_Descending", page, StringComparison.Ordinal);
        Assert.Contains("Math.Clamp", page, StringComparison.Ordinal);
        Assert.Contains("MudTable", page, StringComparison.Ordinal);
        Assert.DoesNotContain("StackTrace", page, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Username", page, StringComparison.OrdinalIgnoreCase);

        Assert.Contains("MapGet(\"/bff/diagnostics/events\"", bffProgram, StringComparison.Ordinal);
        Assert.Contains("RequireAuthorization()", bffProgram, StringComparison.Ordinal);
        Assert.Contains("Legacy.Maliev.Intranet.Client.Features.Diagnostics.wasm", app, StringComparison.Ordinal);
        Assert.Contains("Legacy.Maliev.Intranet.Client.Features.Diagnostics.wasm", clientProject, StringComparison.Ordinal);
        Assert.DoesNotContain("\"/Server/ErrorReport\"", legacyRoutes, StringComparison.Ordinal);
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
