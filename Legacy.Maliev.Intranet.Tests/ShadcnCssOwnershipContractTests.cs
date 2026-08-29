namespace Legacy.Maliev.Intranet.Tests;

public sealed class ShadcnCssOwnershipContractTests
{
    [Fact]
    public void ConsumerStylesUseNamedApplicationSelectorsWithoutPrivateComponentHooks()
    {
        var root = FindRoot();
        var violations = Directory.EnumerateFiles(root, "*.css", SearchOption.AllDirectories)
            .Where(path => IsConsumerSource(root, path))
            .Where(path => File.ReadAllText(path).Contains(".mud-", StringComparison.OrdinalIgnoreCase))
            .Select(path => Path.GetRelativePath(root, path))
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Empty(violations);
    }

    private static bool IsConsumerSource(string root, string path)
    {
        var relative = Path.GetRelativePath(root, path);
        if (relative.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase)
            || relative.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return relative.StartsWith($"Legacy.Maliev.Intranet.Client{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase)
            || relative.StartsWith($"Legacy.Maliev.Intranet.Client.Shared{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase)
            || relative.StartsWith("Legacy.Maliev.Intranet.Client.Features.", StringComparison.OrdinalIgnoreCase);
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
