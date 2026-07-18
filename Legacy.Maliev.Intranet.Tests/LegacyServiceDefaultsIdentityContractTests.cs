namespace Legacy.Maliev.Intranet.Tests;

public sealed class LegacyServiceDefaultsIdentityContractTests
{
    [Fact]
    public void HostsConsumeLegacyServiceDefaultsWithoutNewPlatformRepositoryCollision()
    {
        var root = FindRoot();
        foreach (var project in new[]
        {
            Path.Combine(root, "Legacy.Maliev.Intranet", "Legacy.Maliev.Intranet.csproj"),
            Path.Combine(root, "Legacy.Maliev.Intranet.Bff", "Legacy.Maliev.Intranet.Bff.csproj"),
        })
        {
            var source = File.ReadAllText(project);
            Assert.Contains("Legacy.Maliev.ServiceDefaults\\src\\Legacy.Maliev.ServiceDefaults\\Legacy.Maliev.ServiceDefaults.csproj", source, StringComparison.Ordinal);
            Assert.Contains("PackageReference Include=\"Legacy.Maliev.ServiceDefaults\"", source, StringComparison.Ordinal);
            Assert.DoesNotContain("Maliev.Aspire\\Maliev.Aspire.ServiceDefaults", source, StringComparison.Ordinal);
            Assert.DoesNotContain("PackageReference Include=\"Maliev.Aspire.ServiceDefaults\"", source, StringComparison.Ordinal);
        }

        var workflow = File.ReadAllText(Path.Combine(root, ".github", "workflows", "_build-and-test.yml"));
        Assert.Contains("repository: MALIEV-Co-Ltd/Legacy.Maliev.ServiceDefaults", workflow, StringComparison.Ordinal);
        Assert.Contains("path: .dependencies/Legacy.Maliev.ServiceDefaults", workflow, StringComparison.Ordinal);
        Assert.Contains("repository: MALIEV-Co-Ltd/Legacy.Maliev.CompatibilityContracts", workflow, StringComparison.Ordinal);
        Assert.Contains("path: .dependencies/Legacy.Maliev.CompatibilityContracts", workflow, StringComparison.Ordinal);
        Assert.DoesNotContain("repository: MALIEV-Co-Ltd/Maliev.Aspire", workflow, StringComparison.Ordinal);
        Assert.DoesNotContain("repository: MALIEV-Co-Ltd/Maliev.MessagingContracts", workflow, StringComparison.Ordinal);
    }

    [Fact]
    public void CompatibilityNamespaceRemainsStableWhileAssemblyAndPackageOwnershipChange()
    {
        var root = FindRoot();
        var webProgram = File.ReadAllText(Path.Combine(root, "Legacy.Maliev.Intranet", "Program.cs"));
        var bffProgram = File.ReadAllText(Path.Combine(root, "Legacy.Maliev.Intranet.Bff", "Program.cs"));
        Assert.Contains("using Maliev.Aspire.ServiceDefaults;", webProgram, StringComparison.Ordinal);
        Assert.Contains("using Maliev.Aspire.ServiceDefaults;", bffProgram, StringComparison.Ordinal);
        Assert.Contains("builder.AddServiceDefaults();", webProgram, StringComparison.Ordinal);
        Assert.Contains("builder.AddServiceDefaults();", bffProgram, StringComparison.Ordinal);
    }

    private static string FindRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Legacy.Maliev.Intranet.slnx"))) directory = directory.Parent;
        return directory?.FullName ?? throw new DirectoryNotFoundException("Could not find repository root.");
    }
}
