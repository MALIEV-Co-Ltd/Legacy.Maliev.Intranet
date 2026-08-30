namespace Legacy.Maliev.Intranet.Tests;

public sealed class DependabotConfigurationContractTests
{
    private static readonly string[] ExpectedDirectories =
    [
        "/Legacy.Maliev.Intranet.Client",
        "/Legacy.Maliev.Intranet.Client.Features.Accounting",
        "/Legacy.Maliev.Intranet.Client.Features.Catalog",
        "/Legacy.Maliev.Intranet.Client.Features.Customers",
        "/Legacy.Maliev.Intranet.Client.Features.Diagnostics",
        "/Legacy.Maliev.Intranet.Client.Features.Employees",
        "/Legacy.Maliev.Intranet.Client.Features.Orders",
        "/Legacy.Maliev.Intranet.Client.Features.Procurement",
        "/Legacy.Maliev.Intranet.Client.Features.Quotations",
        "/Legacy.Maliev.Intranet.Client.Shared",
        "/Legacy.Maliev.Intranet.Contracts",
        "/Legacy.Maliev.Intranet.Server",
    ];

    [Fact]
    public void NuGetUpdater_ScansOnlyIndependentlyResolvableProjectDirectories()
    {
        var source = ReadNuGetBlock();

        Assert.DoesNotContain("    directory: /", source, StringComparison.Ordinal);
        foreach (var directory in ExpectedDirectories)
        {
            Assert.Contains($"      - {directory}", source, StringComparison.Ordinal);
        }

        Assert.Equal(ExpectedDirectories.Length, source.Split("\n      - /Legacy.Maliev.Intranet.", StringSplitOptions.None).Length - 1);
    }

    private static string ReadNuGetBlock()
    {
        var source = File.ReadAllText(FindRepositoryFile(".github", "dependabot.yml"));
        var start = source.IndexOf("  - package-ecosystem: nuget", StringComparison.Ordinal);
        var end = source.IndexOf("  - package-ecosystem: docker", start, StringComparison.Ordinal);

        Assert.True(start >= 0);
        Assert.True(end > start);
        return source[start..end];
    }

    private static string FindRepositoryFile(params string[] segments)
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);

        while (directory is not null)
        {
            var candidate = Path.Combine([directory.FullName, .. segments]);
            if (File.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException($"Could not find repository file '{Path.Combine(segments)}'.");
    }
}
