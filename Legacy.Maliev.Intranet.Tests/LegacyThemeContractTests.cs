namespace Legacy.Maliev.Intranet.Tests;

public sealed class LegacyThemeContractTests
{
    [Fact]
    public void ThemeService_IsRegisteredAndUsesTheSharedBlockingBootstrap()
    {
        var root = FindRoot();
        var program = File.ReadAllText(Path.Combine(root, "Legacy.Maliev.Intranet.Client", "Program.cs"));
        var service = File.ReadAllText(Path.Combine(root, "Legacy.Maliev.Intranet.Client", "LegacyThemeService.cs"));
        var index = File.ReadAllText(Path.Combine(root, "Legacy.Maliev.Intranet.Client", "wwwroot", "index.html"));

        Assert.Contains("AddScoped<LegacyThemeService>()", program, StringComparison.Ordinal);
        Assert.Contains("malievTheme.isDark", service, StringComparison.Ordinal);
        Assert.Contains("malievTheme.toggle", service, StringComparison.Ordinal);
        Assert.Contains("localStorage", index, StringComparison.Ordinal);
        Assert.Contains("dataset.malievTheme", index, StringComparison.Ordinal);
        Assert.Contains("dataset.shadcnTheme", index, StringComparison.Ordinal);
    }

    [Fact]
    public void WorkspaceShell_BindsTheSharedProviderAndExposesAccessibleToggle()
    {
        var root = FindRoot();
        var layout = File.ReadAllText(Path.Combine(root, "Legacy.Maliev.Intranet.Client", "Layout", "MainLayout.razor"));
        var topbar = File.ReadAllText(Path.Combine(root, "Legacy.Maliev.Intranet.Client", "Layout", "LegacyTopBar.razor"));
        var css = File.ReadAllText(Path.Combine(root, "Legacy.Maliev.Intranet.Client", "wwwroot", "css", "design-tokens.css"));

        Assert.Contains("IsDarkMode=\"@ThemeService.IsDarkMode\"", layout, StringComparison.Ordinal);
        Assert.Contains("<ShadcnThemeProvider", layout, StringComparison.Ordinal);
        Assert.Contains("Direction=\"ShadcnDirection.LeftToRight\"", layout, StringComparison.Ordinal);
        Assert.DoesNotContain("new MudTheme", layout, StringComparison.Ordinal);
        Assert.Contains("ThemeLabel", topbar, StringComparison.Ordinal);
        Assert.Contains("<ShadcnButton Class=\"legacy-theme-toggle\"", topbar, StringComparison.Ordinal);
        Assert.Contains("Size=\"ShadcnButtonSize.Icon\"", topbar, StringComparison.Ordinal);
        Assert.Contains("<ShadcnIcon Icon=\"@(ThemeService.IsDarkMode ? SunIcon : MoonIcon)\"", topbar, StringComparison.Ordinal);
        Assert.Contains("OnClick=\"ToggleThemeAsync\"", topbar, StringComparison.Ordinal);
        Assert.Contains(":root[data-maliev-theme=\"dark\"]", css, StringComparison.Ordinal);
        Assert.Contains("--legacy-background: var(--shadcn-background)", css, StringComparison.Ordinal);
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
