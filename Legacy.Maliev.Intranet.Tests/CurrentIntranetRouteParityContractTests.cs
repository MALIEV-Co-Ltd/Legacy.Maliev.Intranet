using System.Text.Json;
using System.Text.RegularExpressions;

namespace Legacy.Maliev.Intranet.Tests;

public sealed partial class CurrentIntranetRouteParityContractTests
{
    [Fact]
    public void Baseline_AccountsForEveryCurrentRouteExactlyOnce()
    {
        var manifest = LoadManifest();

        Assert.Equal("d8e943b", manifest.SourceCommit);
        Assert.Equal(53, manifest.Routes.Count);
        Assert.Equal(53, manifest.Routes.Select(route => route.Path).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.Equal(18, manifest.Routes.Count(route => route.Status == "mapped"));
        Assert.Equal(35, manifest.Routes.Count(route => route.Status == "blocked"));
        Assert.All(manifest.Routes, route =>
        {
            Assert.Contains(route.Status, new[] { "mapped", "blocked" });
            Assert.False(string.IsNullOrWhiteSpace(route.Evidence));
        });
    }

    [Fact]
    public void MappedRoutesHaveAnExactLegacyBlazorOwner_AndBlockedRoutesDoNot()
    {
        var manifest = LoadManifest();
        var owners = DiscoverRouteOwners();

        foreach (var route in manifest.Routes)
        {
            var ownerCount = owners.TryGetValue(route.Path, out var routeOwners) ? routeOwners.Count : 0;
            if (route.Status == "mapped")
            {
                Assert.True(ownerCount > 0, $"Mapped current route {route.Path} has no exact legacy Blazor owner.");
            }
            else
            {
                Assert.Equal(0, ownerCount);
            }
        }
    }

    [Fact]
    public void WhenCurrentSourceIsAvailable_ManifestMatchesItsRouteDirectives()
    {
        var currentRoot = Environment.GetEnvironmentVariable("MALIEV_CURRENT_INTRANET_ROOT");
        if (string.IsNullOrWhiteSpace(currentRoot) || !Directory.Exists(currentRoot))
        {
            return;
        }

        var actual = DiscoverRoutes(Path.Combine(currentRoot, "Maliev.Intranet.Client", "Pages"));
        var expected = LoadManifest().Routes.Select(route => route.Path).ToHashSet(StringComparer.OrdinalIgnoreCase);

        Assert.Equal(expected.Order(StringComparer.OrdinalIgnoreCase), actual.Order(StringComparer.OrdinalIgnoreCase));
    }

    private static RouteManifest LoadManifest()
    {
        var root = FindRoot();
        var path = Path.Combine(root, "docs", "current-intranet-route-parity.json");
        var manifest = JsonSerializer.Deserialize<RouteManifest>(File.ReadAllText(path), JsonOptions);
        return manifest ?? throw new InvalidOperationException("Current Intranet route parity manifest is empty.");
    }

    private static Dictionary<string, List<string>> DiscoverRouteOwners()
    {
        var root = FindRoot();
        var owners = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var clientRoot in Directory.EnumerateDirectories(root, "Legacy.Maliev.Intranet.Client*", SearchOption.TopDirectoryOnly))
        {
            foreach (var file in Directory.EnumerateFiles(clientRoot, "*.razor", SearchOption.AllDirectories))
            {
                if (file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase)
                    || file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                foreach (var route in PageDirective().Matches(File.ReadAllText(file)).Select(match => match.Groups["route"].Value))
                {
                    if (!owners.TryGetValue(route, out var routeOwners))
                    {
                        routeOwners = [];
                        owners.Add(route, routeOwners);
                    }

                    routeOwners.Add(file);
                }
            }
        }

        return owners;
    }

    private static HashSet<string> DiscoverRoutes(string root)
    {
        if (!Directory.Exists(root))
        {
            throw new DirectoryNotFoundException($"Current Intranet route source was not found: {root}");
        }

        return Directory.EnumerateFiles(root, "*.razor", SearchOption.AllDirectories)
            .SelectMany(file => PageDirective().Matches(File.ReadAllText(file)).Select(match => match.Groups["route"].Value))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
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

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [GeneratedRegex("^@page\\s+\\\"(?<route>/[^\\\"]*)\\\"", RegexOptions.Multiline | RegexOptions.CultureInvariant)]
    private static partial Regex PageDirective();

    private sealed record RouteManifest(string SourceRepository, string SourceCommit, IReadOnlyList<RouteEntry> Routes);

    private sealed record RouteEntry(string Path, string Status, string Evidence);
}
