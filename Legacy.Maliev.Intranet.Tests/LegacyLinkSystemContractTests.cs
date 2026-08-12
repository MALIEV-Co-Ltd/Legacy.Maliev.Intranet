namespace Legacy.Maliev.Intranet.Tests;

public sealed class LegacyLinkSystemContractTests
{
    [Fact]
    public void SharedLink_ExposesFourExplicitRoles()
    {
        var source = File.ReadAllText(Path.Combine(Root, "Legacy.Maliev.Intranet.Client.Shared", "Components", "LegacyLink.razor"));
        var role = File.ReadAllText(Path.Combine(Root, "Legacy.Maliev.Intranet.Client.Shared", "Components", "LegacyLinkRole.cs"));
        Assert.Contains("Inline", role);
        Assert.Contains("Record", role);
        Assert.Contains("Navigation", role);
        Assert.Contains("External", role);
        Assert.Contains("[Parameter] public LegacyLinkRole Role", source);
        Assert.Contains("[Parameter] public bool Disabled", source);
        Assert.Contains("aria-disabled", source);
    }

    private static string Root => FindRoot();

    private static string FindRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Legacy.Maliev.Intranet.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new DirectoryNotFoundException("Could not locate the repository root.");
    }
}
