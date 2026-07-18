namespace Legacy.Maliev.Intranet.Tests;

public sealed class EmployeeRecoveryWasmMigrationContractTests
{
    [Fact]
    public void RecoveryRoutes_AreAnonymousLazyWasmPagesWithRazorRollback()
    {
        var root = FindRoot();
        var feature = Path.Combine(root, "Legacy.Maliev.Intranet.Client.Features.Employees", "Pages");
        var forgot = File.ReadAllText(Path.Combine(feature, "EmployeeForgotPassword.razor"));
        var reset = File.ReadAllText(Path.Combine(feature, "EmployeeResetPassword.razor"));
        var confirm = File.ReadAllText(Path.Combine(feature, "EmployeeEmailConfirmation.razor"));
        var app = File.ReadAllText(Path.Combine(root, "Legacy.Maliev.Intranet.Client", "App.razor"));
        var bff = File.ReadAllText(Path.Combine(root, "Legacy.Maliev.Intranet.Bff", "Program.cs"));

        Assert.Contains("@page \"/Employees/ForgotPassword\"", forgot, StringComparison.Ordinal);
        Assert.Contains("@page \"/Employees/ResetPassword\"", reset, StringComparison.Ordinal);
        Assert.Contains("@page \"/Employees/EmailConfirmation\"", confirm, StringComparison.Ordinal);
        Assert.All(new[] { forgot, reset, confirm }, page =>
            Assert.Contains("[AllowAnonymous]", page, StringComparison.Ordinal));
        Assert.Contains("Legacy.Maliev.Intranet.Client.Features.Employees.wasm", app, StringComparison.Ordinal);
        Assert.Contains("/bff/employee-recovery/password-reset/request", bff, StringComparison.Ordinal);
        Assert.Contains("/bff/employee-recovery/password-reset/complete", bff, StringComparison.Ordinal);
        Assert.Contains("/bff/employee-recovery/email-confirmation/complete", bff, StringComparison.Ordinal);
        Assert.Contains("AddEndpointFilter<AntiforgeryValidationFilter>()", bff, StringComparison.Ordinal);
        Assert.Contains("RequireRateLimiting(\"employee-recovery\")", bff, StringComparison.Ordinal);

        Assert.True(File.Exists(Path.Combine(root, "Legacy.Maliev.Intranet", "Pages", "AnonymousLegacyRoute.cshtml")));
        var razorHost = File.ReadAllText(Path.Combine(root, "Legacy.Maliev.Intranet", "Program.cs"));
        Assert.Contains("AddPageRoute(\"/AnonymousLegacyRoute\", route)", razorHost, StringComparison.Ordinal);
    }

    private static string FindRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Legacy.Maliev.Intranet.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new DirectoryNotFoundException("Could not find repository root.");
    }
}
