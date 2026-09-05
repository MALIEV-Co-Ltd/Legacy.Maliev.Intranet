using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace Legacy.Maliev.Intranet.Tests;

public sealed partial class NativeShadcnMigrationContractTests
{
    private const string PackageVersion = "2.2.0";

    private static readonly string[] ConsumerProjectDirectories =
    [
        "Legacy.Maliev.Intranet.Client",
        "Legacy.Maliev.Intranet.Client.Shared",
        "Legacy.Maliev.Intranet.Client.Features.Accounting",
        "Legacy.Maliev.Intranet.Client.Features.Catalog",
        "Legacy.Maliev.Intranet.Client.Features.Customers",
        "Legacy.Maliev.Intranet.Client.Features.Diagnostics",
        "Legacy.Maliev.Intranet.Client.Features.Employees",
        "Legacy.Maliev.Intranet.Client.Features.Orders",
        "Legacy.Maliev.Intranet.Client.Features.Procurement",
        "Legacy.Maliev.Intranet.Client.Features.Quotations"
    ];

    [Fact]
    public void ConsumerProjectsUseTheReleasedPackageAtOneExactVersion()
    {
        foreach (var projectPath in ConsumerProjectPaths())
        {
            var project = XDocument.Load(projectPath);
            var packageReferences = project.Descendants("PackageReference")
                .Where(reference => string.Equals(
                    (string?)reference.Attribute("Include"),
                    "Maliev.ShadcnBlazor",
                    StringComparison.Ordinal))
                .ToArray();

            var packageReference = Assert.Single(packageReferences);
            Assert.Equal(PackageVersion, (string?)packageReference.Attribute("Version"));
            Assert.DoesNotContain(
                project.Descendants("ProjectReference"),
                reference => ((string?)reference.Attribute("Include"))?.Contains(
                    "Maliev.ShadcnBlazor.csproj",
                    StringComparison.OrdinalIgnoreCase) is true);
        }
    }

    [Fact]
    public void ConsumerProjectsDoNotReferenceMudBlazorDirectly()
    {
        foreach (var projectPath in ConsumerProjectPaths())
        {
            var project = XDocument.Load(projectPath);

            Assert.DoesNotContain(
                project.Descendants("PackageReference"),
                reference => string.Equals(
                    (string?)reference.Attribute("Include"),
                    "MudBlazor",
                    StringComparison.OrdinalIgnoreCase));
        }
    }

    [Fact]
    public void MigrationLedgerCapturesTheMeasuredBaseline()
    {
        var ledgerPath = Path.Combine(FindRoot(), "docs", "native-shadcn-migration-ledger.json");
        Assert.True(
            File.Exists(ledgerPath),
            "The native Shadcn migration ledger must exist and remain the fail-closed inventory source.");

        using var ledger = JsonDocument.Parse(File.ReadAllText(ledgerPath));
        Assert.Equal(PackageVersion, ledger.RootElement.GetProperty("packageVersion").GetString());
        var baseline = ledger.RootElement.GetProperty("baseline");

        Assert.Equal(1_485, baseline.GetProperty("renderSites").GetInt32());
        Assert.Equal(36, baseline.GetProperty("componentTypes").GetInt32());
        Assert.Equal(60, baseline.GetProperty("razorFiles").GetInt32());
    }

    [Fact]
    public void MeasuredSourceInventoryMatchesTheCurrentLedger()
    {
        var matches = ConsumerProjectDirectories
            .SelectMany(directory => Directory.EnumerateFiles(
                Path.Combine(FindRoot(), directory),
                "*.razor",
                SearchOption.AllDirectories))
            .SelectMany(file => MudComponentRegex().Matches(File.ReadAllText(file)).Select(match => match.Groups[1].Value))
            .ToArray();

        using var ledger = JsonDocument.Parse(File.ReadAllText(
            Path.Combine(FindRoot(), "docs", "native-shadcn-migration-ledger.json")));
        var current = ledger.RootElement.GetProperty("current");
        var files = ledger.RootElement.GetProperty("files");

        Assert.Equal(current.GetProperty("renderSites").GetInt32(), matches.Length);
        Assert.Equal(0, current.GetProperty("componentTypes").GetInt32());
        Assert.Equal(current.GetProperty("razorFiles").GetInt32(), files.EnumerateArray().Count(file =>
            file.GetProperty("remainingRenderSites").GetInt32() > 0));
        Assert.Equal(60, files.GetArrayLength());
        Assert.Equal(matches.Length, files.EnumerateArray().Sum(file =>
            file.GetProperty("remainingRenderSites").GetInt32()));
        Assert.All(files.EnumerateArray(), file =>
        {
            Assert.Equal("migrated", file.GetProperty("status").GetString());
            Assert.NotEmpty(file.GetProperty("replacementFamilies").EnumerateArray());
        });
    }

    [Fact]
    public void ConsumerSourcesDoNotLoadOrImportTheLegacyUiSurface()
    {
        var violations = ConsumerProjectDirectories
            .SelectMany(directory => Directory.EnumerateFiles(
                Path.Combine(FindRoot(), directory),
                "*",
                SearchOption.AllDirectories))
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            .Where(path => Path.GetExtension(path) is ".razor" or ".cs" or ".css" or ".html" or ".csproj")
            .Where(path =>
            {
                var source = File.ReadAllText(path);
                return MudComponentRegex().IsMatch(source)
                    || source.Contains("@using " + "MudBlazor", StringComparison.Ordinal)
                    || source.Contains("using " + "MudBlazor", StringComparison.Ordinal)
                    || source.Contains("_content/" + "MudBlazor", StringComparison.Ordinal)
                    || source.Contains(".mud-", StringComparison.OrdinalIgnoreCase);
            })
            .Select(path => Path.GetRelativePath(FindRoot(), path))
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Empty(violations);
    }

    private static IEnumerable<string> ConsumerProjectPaths() =>
        ConsumerProjectDirectories.Select(directory =>
            Path.Combine(FindRoot(), directory, $"{directory}.csproj"));

    private static string FindRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Legacy.Maliev.Intranet.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new DirectoryNotFoundException("Could not find repository root.");
    }

    [GeneratedRegex("<Mud([A-Za-z0-9_]+)", RegexOptions.CultureInvariant)]
    private static partial Regex MudComponentRegex();
}
