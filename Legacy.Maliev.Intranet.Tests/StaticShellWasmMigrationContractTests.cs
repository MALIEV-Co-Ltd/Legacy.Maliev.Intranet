namespace Legacy.Maliev.Intranet.Tests;

public sealed class StaticShellWasmMigrationContractTests
{
    [Fact]
    public void RootAndAccessDenied_AreAnonymousLocalizedWasmRoutesWithRazorFallbacks()
    {
        var root = FindRoot();
        var pages = Path.Combine(root, "Legacy.Maliev.Intranet.Client", "Pages");
        var homePath = Path.Combine(pages, "Home.razor");
        var deniedPath = Path.Combine(pages, "AccessDenied.razor");
        Assert.True(File.Exists(homePath), "The WASM root route is missing.");
        Assert.True(File.Exists(Path.ChangeExtension(homePath, ".resx")), "The localized root resource is missing.");
        Assert.True(File.Exists(deniedPath), "The WASM AccessDenied route is missing.");
        Assert.True(File.Exists(Path.ChangeExtension(deniedPath, ".resx")), "The localized AccessDenied resource is missing.");
        var home = File.ReadAllText(homePath);
        var denied = File.ReadAllText(deniedPath);
        Assert.Contains("@page \"/\"", home, StringComparison.Ordinal);
        Assert.Contains("@attribute [AllowAnonymous]", home, StringComparison.Ordinal);
        Assert.Contains("IStringLocalizer<Home>", home, StringComparison.Ordinal);
        Assert.Contains("Href=\"/Login\"", home, StringComparison.Ordinal);
        Assert.DoesNotContain("forceLoad: true", home, StringComparison.Ordinal);
        Assert.Contains("@page \"/AccessDenied\"", denied, StringComparison.Ordinal);
        Assert.Contains("@attribute [AllowAnonymous]", denied, StringComparison.Ordinal);
        Assert.Contains("IStringLocalizer<AccessDenied>", denied, StringComparison.Ordinal);
        Assert.DoesNotContain("@context.User", denied, StringComparison.Ordinal);
        Assert.DoesNotContain("forceLoad: true", denied, StringComparison.Ordinal);
        Assert.True(File.Exists(Path.Combine(root, "Legacy.Maliev.Intranet", "Pages", "Index.cshtml")));
        Assert.True(File.Exists(Path.Combine(root, "Legacy.Maliev.Intranet", "Pages", "AccessDenied.cshtml")));
    }

    private static string FindRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Legacy.Maliev.Intranet.slnx"))) directory = directory.Parent;
        return directory?.FullName ?? throw new DirectoryNotFoundException("Could not find repository root.");
    }
}
