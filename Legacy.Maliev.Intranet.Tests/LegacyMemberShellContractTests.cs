namespace Legacy.Maliev.Intranet.Tests;

public sealed class LegacyMemberShellContractTests
{
    [Fact]
    public void MemberShell_UsesProfileMenuForLegacyProfileAndSignOut()
    {
        var root = FindRoot();
        var topbar = File.ReadAllText(Path.Combine(
            root,
            "Legacy.Maliev.Intranet.Client",
            "Layout",
            "LegacyTopBar.razor"));

        Assert.Contains("legacy-profile-menu", topbar, StringComparison.Ordinal);
        Assert.Contains("legacy-profile", topbar, StringComparison.Ordinal);
        Assert.Contains("aria-haspopup=\"dialog\"", topbar, StringComparison.Ordinal);
        Assert.Contains("role=\"dialog\"", topbar, StringComparison.Ordinal);
        Assert.Contains("legacy-profile-preferences", topbar, StringComparison.Ordinal);
        Assert.Contains("<LegacyLanguageSelector />", topbar, StringComparison.Ordinal);
        Assert.Contains("Href=\"/hr/profile\"", topbar, StringComparison.Ordinal);
        Assert.Contains("Role=\"LegacyLinkRole.Navigation\"", topbar, StringComparison.Ordinal);
        Assert.Contains("Sign out", topbar, StringComparison.Ordinal);
        Assert.DoesNotContain("signup", topbar, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void MemberShell_ProfileMenuIsKeyboardAndResponsiveSafe()
    {
        var root = FindRoot();
        var topbar = File.ReadAllText(Path.Combine(
            root,
            "Legacy.Maliev.Intranet.Client",
            "Layout",
            "LegacyTopBar.razor"));
        var css = File.ReadAllText(Path.Combine(
            root,
            "Legacy.Maliev.Intranet.Client",
            "Layout",
            "LegacyTopBar.razor.css"));

        Assert.Contains("aria-expanded=\"@_profileMenuOpen\"", topbar, StringComparison.Ordinal);
        Assert.Contains("Escape", topbar, StringComparison.Ordinal);
        Assert.Contains("legacy-profile-popover", css, StringComparison.Ordinal);
        Assert.DoesNotContain("legacy-signout-button", css, StringComparison.Ordinal);
        Assert.Contains("@media (max-width: 720px)", css, StringComparison.Ordinal);
        Assert.Contains("border-radius: 9999px", css, StringComparison.Ordinal);
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
