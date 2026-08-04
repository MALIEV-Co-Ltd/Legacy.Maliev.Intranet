using System.Text.RegularExpressions;

namespace Legacy.Maliev.Intranet.Tests;

public sealed class TypographySystemContractTests
{
    private static readonly Regex FontWeightDeclaration = new(
        @"font-weight\s*:\s*(?<value>[^;}{]+)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    [Fact]
    public void RuntimeHosts_SelfHostIbmPlexSansThaiWithoutExternalFontRequests()
    {
        var root = FindRoot();
        var clientIndex = Read(root, "Legacy.Maliev.Intranet.Client", "wwwroot", "index.html");
        var serverLayout = Read(root, "Legacy.Maliev.Intranet", "Pages", "Shared", "_Layout.cshtml");

        Assert.Contains("css/ibm-plex-sans-thai.css", clientIndex, StringComparison.Ordinal);
        Assert.Contains("~/css/ibm-plex-sans-thai.css", serverLayout, StringComparison.Ordinal);

        foreach (var productionFile in ProductionTypographyFiles(root))
        {
            var source = File.ReadAllText(productionFile);
            Assert.DoesNotContain("fonts.googleapis.com", source, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("fonts.gstatic.com", source, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("'Inter'", source, StringComparison.Ordinal);
            Assert.DoesNotContain("family=Inter", source, StringComparison.Ordinal);
            Assert.DoesNotContain("Noto Sans Thai", source, StringComparison.Ordinal);
            Assert.DoesNotContain("Geist Mono", source, StringComparison.Ordinal);
        }
    }

    [Theory]
    [InlineData("Legacy.Maliev.Intranet.Client")]
    [InlineData("Legacy.Maliev.Intranet")]
    public void RuntimeHost_ShipsLicensedRegularAndSemiboldFontAssets(string project)
    {
        var root = FindRoot();
        var fontRoot = Path.Combine(root, project, "wwwroot", "fonts", "ibm-plex-sans-thai");
        var css = Read(root, project, "wwwroot", "css", "ibm-plex-sans-thai.css");

        Assert.Contains("font-family: 'IBM Plex Sans Thai'", css, StringComparison.Ordinal);
        Assert.Contains("font-weight: 400", css, StringComparison.Ordinal);
        Assert.Contains("font-weight: 600", css, StringComparison.Ordinal);

        Assert.True(new FileInfo(Path.Combine(fontRoot, "IBMPlexSansThai-Regular.woff2")).Length > 0);
        Assert.True(new FileInfo(Path.Combine(fontRoot, "IBMPlexSansThai-SemiBold.woff2")).Length > 0);
        Assert.True(new FileInfo(Path.Combine(fontRoot, "LICENSE.txt")).Length > 0);
    }

    [Fact]
    public void TypographyTokens_UseOneBilingualFamilyAndTwoWeightRoles()
    {
        var root = FindRoot();
        var tokens = Read(root, "Legacy.Maliev.Intranet.Client", "wwwroot", "css", "design-tokens.css");
        var rail = Read(root, "Legacy.Maliev.Intranet.Client", "Components", "Shell", "LegacyNavigationRail.razor.css");

        Assert.Contains("--maliev-font-sans: 'IBM Plex Sans Thai', sans-serif", tokens, StringComparison.Ordinal);
        Assert.Contains("--maliev-font-weight-body: 400", tokens, StringComparison.Ordinal);
        Assert.Contains("--maliev-font-weight-heading: 600", tokens, StringComparison.Ordinal);
        Assert.Contains("font-weight: var(--maliev-font-weight-body)", rail, StringComparison.Ordinal);
        Assert.Contains("font-weight: var(--maliev-font-weight-heading)", rail, StringComparison.Ordinal);
    }

    [Fact]
    public void AuthoredStyles_UseOnlyBodyOrHeadingWeightRoles()
    {
        var root = FindRoot();
        var invalid = new List<string>();

        foreach (var file in Directory.EnumerateFiles(root, "*.css", SearchOption.AllDirectories)
                     .Where(IsProductionStyle))
        {
            var source = File.ReadAllText(file);
            foreach (Match match in FontWeightDeclaration.Matches(source))
            {
                var value = match.Groups["value"].Value.Trim();
                if (value is "400" or "600" or "var(--maliev-font-weight-body)" or "var(--maliev-font-weight-heading)")
                {
                    continue;
                }

                invalid.Add($"{Path.GetRelativePath(root, file)}: {value}");
            }
        }

        Assert.True(invalid.Count == 0, $"Unexpected font-weight declarations:{Environment.NewLine}{string.Join(Environment.NewLine, invalid)}");
    }

    private static IEnumerable<string> ProductionTypographyFiles(string root) =>
        Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
            .Where(path => IsProductionPath(path)
                && Path.GetExtension(path) is ".css" or ".html" or ".cshtml");

    private static bool IsProductionStyle(string path) =>
        IsProductionPath(path) && path.EndsWith(".css", StringComparison.OrdinalIgnoreCase);

    private static bool IsProductionPath(string path) =>
        !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase)
        && !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase)
        && !path.Contains($"{Path.DirectorySeparatorChar}TestResults{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase)
        && !path.Contains($"{Path.DirectorySeparatorChar}Legacy.Maliev.Intranet.Tests{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase)
        && (path.Contains($"{Path.DirectorySeparatorChar}Legacy.Maliev.Intranet.Client{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase)
            || path.Contains($"{Path.DirectorySeparatorChar}Legacy.Maliev.Intranet.Client.Features.", StringComparison.OrdinalIgnoreCase)
            || path.Contains($"{Path.DirectorySeparatorChar}Legacy.Maliev.Intranet{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase));

    private static string Read(string root, params string[] segments) =>
        File.ReadAllText(Path.Combine([root, .. segments]));

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
