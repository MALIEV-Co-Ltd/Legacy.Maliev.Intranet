using System.Text.RegularExpressions;

namespace Legacy.Maliev.Intranet.Tests;

public sealed partial class MudBlazorComponentConformanceTests
{
    [Fact]
    public void EmployeeFacingRazorControls_UseMudBlazorPrimitives()
    {
        var root = FindRoot();
        var violations = Directory.EnumerateFiles(root, "*.razor", SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            .Select(path => new
            {
                Path = Path.GetRelativePath(root, path),
                Matches = NativeInteractiveElement().Matches(File.ReadAllText(path)).Select(match => match.Value).Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
            })
            .Where(result => result.Matches.Length > 0)
            .Select(result => $"{result.Path}: {string.Join(", ", result.Matches)}")
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.True(violations.Length == 0,
            $"Employee-facing controls must use MudBlazor primitives:{Environment.NewLine}{string.Join(Environment.NewLine, violations)}");
    }

    [GeneratedRegex(@"<\s*(?:form|input|select|textarea|button|table|details|summary)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex NativeInteractiveElement();

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
