namespace Legacy.Maliev.Intranet.Tests;

public sealed class LegacyServiceDefaultsIdentityContractTests
{
    private const string DotNetPatchVersion = "10.0.12";

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
        Assert.Contains("ref: 9c4ac9d44a08bcd0aa2088348790ab863814669c", workflow, StringComparison.Ordinal);
        Assert.Contains("repository: MALIEV-Co-Ltd/Legacy.Maliev.CompatibilityContracts", workflow, StringComparison.Ordinal);
        Assert.Contains("path: .dependencies/Legacy.Maliev.CompatibilityContracts", workflow, StringComparison.Ordinal);
        Assert.DoesNotContain("repository: MALIEV-Co-Ltd/Maliev.Aspire", workflow, StringComparison.Ordinal);
        Assert.DoesNotContain("repository: MALIEV-Co-Ltd/Maliev.MessagingContracts", workflow, StringComparison.Ordinal);
    }

    [Fact]
    public void DirectFrameworkPackages_UseOneSupportedPatchBoundary()
    {
        var root = FindRoot();
        var projectSources = Directory
            .GetFiles(root, "*.csproj", SearchOption.AllDirectories)
            .Where(path => !Path.GetRelativePath(root, path).StartsWith($".worktrees{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            .Select(File.ReadAllText)
            .ToArray();

        var source = string.Join(Environment.NewLine, projectSources);
        Assert.DoesNotMatch("Microsoft\\.[^\"]+\" Version=\"10\\.0\\.(?:[0-9]|1[01])\"", source);

        foreach (var package in new[]
        {
            "Microsoft.AspNetCore.Components.Authorization",
            "Microsoft.AspNetCore.Components.Web",
            "Microsoft.AspNetCore.Components.WebAssembly",
            "Microsoft.AspNetCore.Components.WebAssembly.DevServer",
            "Microsoft.AspNetCore.Components.WebAssembly.Server",
            "Microsoft.AspNetCore.DataProtection.StackExchangeRedis",
            "Microsoft.AspNetCore.Mvc.Testing",
            "Microsoft.Extensions.Caching.StackExchangeRedis",
            "Microsoft.Extensions.Localization",
            "Microsoft.Extensions.Localization.Abstractions",
        })
        {
            Assert.Contains($"Include=\"{package}\" Version=\"{DotNetPatchVersion}\"", source, StringComparison.Ordinal);
        }
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
