using Legacy.Maliev.Intranet;
using System.Text.RegularExpressions;

namespace Legacy.Maliev.Intranet.Tests;

public sealed partial class BlazorRouteOwnershipCompletionTests
{
    private static readonly string[] AllowedNonLegacyRoutes = ["/", "/migration-foundation", "/Server/ErrorReport"];

    [Fact]
    public void EveryActiveLegacyRoute_HasExactlyOneBlazorOwner()
    {
        var owners = DiscoverRouteOwners();

        Assert.Equal(41, LegacyRoutes.All.Count);
        Assert.Equal(39, LegacyRoutes.ActiveMigrationCandidates.Count);
        Assert.Equal(2, LegacyRoutes.Retired.Count);

        foreach (var route in LegacyRoutes.ActiveMigrationCandidates)
        {
            Assert.True(owners.TryGetValue(route, out var routeOwners), $"Active legacy route {route} has no Blazor owner.");
            Assert.Single(routeOwners!);
        }

        foreach (var route in LegacyRoutes.Retired)
        {
            Assert.False(owners.ContainsKey(route), $"Retired legacy route {route} must not be reintroduced by Blazor.");
        }

        var nonLegacyRoutes = owners.Keys
            .Except(LegacyRoutes.All, StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        Assert.Equal(AllowedNonLegacyRoutes.Order(StringComparer.OrdinalIgnoreCase), nonLegacyRoutes);
    }

    [Fact]
    public void EveryLegacyRoute_HasAnExplicitAndConsistentAuthorizationOwner()
    {
        var owners = DiscoverRouteOwners();

        foreach (var route in LegacyRoutes.ActiveMigrationCandidates)
        {
            var owner = Assert.Single(owners[route]);
            var allowsAnonymous = owner.Source.Contains("@attribute [AllowAnonymous]", StringComparison.Ordinal);
            var requiresEmployee = owner.Source.Contains("@attribute [Authorize]", StringComparison.Ordinal);

            Assert.NotEqual(allowsAnonymous, requiresEmployee);
            Assert.Equal(LegacyRoutes.Anonymous.Contains(route), allowsAnonymous);
        }
    }

    private static Dictionary<string, List<RouteOwner>> DiscoverRouteOwners()
    {
        var root = FindRoot();
        var owners = new Dictionary<string, List<RouteOwner>>(StringComparer.OrdinalIgnoreCase);
        var clientRoots = Directory.EnumerateDirectories(root, "Legacy.Maliev.Intranet.Client*", SearchOption.TopDirectoryOnly);

        foreach (var path in clientRoots.SelectMany(clientRoot => Directory.EnumerateFiles(clientRoot, "*.razor", SearchOption.AllDirectories)))
        {
            if (path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase)
                || path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var source = File.ReadAllText(path);
            foreach (Match match in PageDirective().Matches(source))
            {
                var route = match.Groups["route"].Value;
                if (!owners.TryGetValue(route, out var routeOwners))
                {
                    routeOwners = [];
                    owners.Add(route, routeOwners);
                }

                routeOwners.Add(new(path, source));
            }
        }

        return owners;
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

    [GeneratedRegex("^@page\\s+\"(?<route>/[^\"]*)\"", RegexOptions.Multiline | RegexOptions.CultureInvariant)]
    private static partial Regex PageDirective();

    private sealed record RouteOwner(string Path, string Source);
}
