using System.Text.RegularExpressions;

namespace Legacy.Maliev.Intranet.Tests;

public sealed partial class NativeShadcnComponentConformanceTests
{
    [Fact]
    public void EmployeeFacingRazorControls_UseComponentPrimitives()
    {
        var root = FindRoot();
        var violations = Directory.EnumerateFiles(root, "*.razor", SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            .Select(path => new
            {
                Path = Path.GetRelativePath(root, path),
                Source = File.ReadAllText(path),
            })
            .Select(result => new
            {
                result.Path,
                Matches = NativeInteractiveElement().Matches(result.Source)
                    .Select(match => match.Value)
                    .Concat(result.Source.Contains("<ShadcnNativeSelect", StringComparison.Ordinal)
                        ? ["<ShadcnNativeSelect"]
                        : [])
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray(),
            })
            .Where(result => result.Matches.Length > 0)
            .Select(result => $"{result.Path}: {string.Join(", ", result.Matches)}")
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.True(violations.Length == 0,
            $"Employee-facing controls must use reviewed component primitives:{Environment.NewLine}{string.Join(Environment.NewLine, violations)}");
    }

    [Fact]
    public void CoreMigrationSliceUsesOnlyNativeShadcnComponents()
    {
        string[] files =
        [
            "Legacy.Maliev.Intranet.Client/LoginRedirect.razor",
            "Legacy.Maliev.Intranet.Client/Pages/AccessDenied.razor",
            "Legacy.Maliev.Intranet.Client/Pages/CompatibilityDetailRedirect.razor",
            "Legacy.Maliev.Intranet.Client/Pages/Dashboard.razor",
            "Legacy.Maliev.Intranet.Client/Pages/Foundation.razor",
            "Legacy.Maliev.Intranet.Client/Pages/Home.razor",
            "Legacy.Maliev.Intranet.Client/Pages/NotFound.razor",
            "Legacy.Maliev.Intranet.Client.Features.Diagnostics/Pages/ErrorReport.razor",
            "Legacy.Maliev.Intranet.Client.Features.Orders/Components/Shared/PrimaryButton.razor",
            "Legacy.Maliev.Intranet.Client.Features.Orders/Components/Shared/SecondaryButton.razor",
            "Legacy.Maliev.Intranet.Client.Features.Orders/Components/Shared/ProgressiveSkeleton.razor"
        ];

        var violations = files
            .Where(file => File.ReadAllText(Path.Combine(FindRoot(), file.Replace('/', Path.DirectorySeparatorChar)))
                .Contains("<Mud", StringComparison.Ordinal))
            .ToArray();

        Assert.True(violations.Length == 0, $"Core migration files still contain Mud components: {string.Join(", ", violations)}");
    }

    [GeneratedRegex(@"<\s*(?:form|input|select|textarea|button|details|summary|table)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
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
