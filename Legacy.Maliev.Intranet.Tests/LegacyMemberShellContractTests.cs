namespace Legacy.Maliev.Intranet.Tests;

public sealed class LegacyMemberShellContractTests
{
    [Fact]
    public void MemberShell_UsesProfileMenuForLegacyProfileAndSignOut()
    {
        var root = FindRoot();
        var rail = File.ReadAllText(Path.Combine(
            root,
            "Legacy.Maliev.Intranet.Client",
            "Components",
            "Shell",
            "LegacyNavigationRail.razor"));

        Assert.Contains("legacy-profile-menu", rail, StringComparison.Ordinal);
        Assert.Contains("legacy-profile", rail, StringComparison.Ordinal);
        Assert.Contains("aria-haspopup=\"dialog\"", rail, StringComparison.Ordinal);
        Assert.Contains("role=\"dialog\"", rail, StringComparison.Ordinal);
        Assert.Contains("legacy-profile-preferences", rail, StringComparison.Ordinal);
        Assert.Contains("<LegacyLanguageSelector />", rail, StringComparison.Ordinal);
        Assert.Contains("Href=\"/hr/profile\"", rail, StringComparison.Ordinal);
        Assert.Contains("Role=\"LegacyLinkRole.Navigation\"", rail, StringComparison.Ordinal);
        Assert.Contains("Sign out", rail, StringComparison.Ordinal);
        Assert.DoesNotContain("signup", rail, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void MemberShell_ProfileMenuIsKeyboardAndResponsiveSafe()
    {
        var root = FindRoot();
        var rail = File.ReadAllText(Path.Combine(
            root,
            "Legacy.Maliev.Intranet.Client",
            "Components",
            "Shell",
            "LegacyNavigationRail.razor"));
        var css = File.ReadAllText(Path.Combine(root, "Legacy.Maliev.Intranet.Client", "Components", "Shell", "LegacyNavigationRail.razor.css"));

        Assert.Contains("aria-expanded=\"@_profileMenuOpen\"", rail, StringComparison.Ordinal);
        Assert.Contains("Escape", rail, StringComparison.Ordinal);
        Assert.Contains("legacy-profile-popover", css, StringComparison.Ordinal);
        Assert.DoesNotContain("legacy-signout-button", css, StringComparison.Ordinal);
        Assert.Contains("@media (max-width: 48rem)", css, StringComparison.Ordinal);
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
