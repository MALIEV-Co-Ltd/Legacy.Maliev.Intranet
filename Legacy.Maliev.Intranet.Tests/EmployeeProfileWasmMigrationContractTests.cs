namespace Legacy.Maliev.Intranet.Tests;

public sealed class EmployeeProfileWasmMigrationContractTests
{
    [Fact]
    public void CurrentHrProfileRouteLoadsTheEmployeesFeatureAndUsesTheIdFreeSelfProfileContract()
    {
        var root = FindRoot();
        var app = File.ReadAllText(Path.Combine(root, "Legacy.Maliev.Intranet.Client", "App.razor"));
        var page = File.ReadAllText(Path.Combine(
            root,
            "Legacy.Maliev.Intranet.Client.Features.Employees",
            "Pages",
            "EmployeeProfile.razor"));

        Assert.Contains("path.StartsWith(\"hr/profile\"", app, StringComparison.Ordinal);
        Assert.Contains("Legacy.Maliev.Intranet.Client.Features.Employees.wasm", app, StringComparison.Ordinal);
        Assert.Contains("@page \"/hr/profile\"", page, StringComparison.Ordinal);
        Assert.Contains("[Authorize]", page, StringComparison.Ordinal);
        Assert.Contains("Http.GetForPresentationAsync(\"/bff/profile\")", page, StringComparison.Ordinal);
        Assert.DoesNotContain("Http.GetAsync(\"/bff/profile\")", page, StringComparison.Ordinal);
        Assert.Contains("/bff/session", page, StringComparison.Ordinal);
        Assert.Contains("new EmployeeSelfProfileUpdateRequest(", page, StringComparison.Ordinal);
        Assert.Contains("new HttpRequestMessage(HttpMethod.Put, \"/bff/profile\")", page, StringComparison.Ordinal);
        Assert.Contains("X-CSRF-TOKEN", page, StringComparison.Ordinal);
        Assert.DoesNotContain("LegacyDatabaseId", page, StringComparison.Ordinal);
        Assert.DoesNotContain("/bff/employees/", page, StringComparison.Ordinal);
        Assert.DoesNotContain("AccessToken", page, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("RefreshToken", page, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("HttpMethod.Patch", page, StringComparison.Ordinal);
    }

    [Fact]
    public void EmployeeProfilePageKeepsAdministrativeFieldsReadOnlyAndLocalizesDates()
    {
        var root = FindRoot();
        var page = File.ReadAllText(Path.Combine(
            root,
            "Legacy.Maliev.Intranet.Client.Features.Employees",
            "Pages",
            "EmployeeProfile.razor"));

        Assert.Contains("profile.Email", page, StringComparison.Ordinal);
        Assert.Contains("profile.Role?.Name", page, StringComparison.Ordinal);
        Assert.Contains("AddressLine(profile.HomeAddress)", page, StringComparison.Ordinal);
        Assert.DoesNotContain("model.Email", page, StringComparison.Ordinal);
        Assert.DoesNotContain("model.Role", page, StringComparison.Ordinal);
        Assert.DoesNotContain("model.HomeAddress", page, StringComparison.Ordinal);
        Assert.Contains("CultureInfo.CurrentCulture.DateTimeFormat.ShortDatePattern", page, StringComparison.Ordinal);
        Assert.Contains("HttpStatusCode.Forbidden", page, StringComparison.Ordinal);
        Assert.Contains("HttpStatusCode.NotFound", page, StringComparison.Ordinal);
        Assert.Contains("HttpStatusCode.TooManyRequests", page, StringComparison.Ordinal);
    }

    private static string FindRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Legacy.Maliev.Intranet.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new DirectoryNotFoundException();
    }
}
