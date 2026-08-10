using System.Text.RegularExpressions;

namespace Legacy.Maliev.Intranet.Tests;

public sealed class ShadcnCssOwnershipContractTests
{
    private static readonly string[] AppearanceProperties =
    [
        "background", "background-color", "color", "border", "border-color", "border-width",
        "border-radius", "box-shadow", "outline", "fill", "stroke", "font-family", "font-size",
        "font-weight", "letter-spacing", "opacity", "text-transform"
    ];

    private static readonly string[] ApprovedMudSelectorHooks =
    [
        ".legacy-", ".mlv-", ".operations-", ".list-toolbar", ".dashboard-",
        ".customer-", ".order-", ".purchase-", ".supplier-", ".quotation-"
    ];

    [Fact]
    public void ProductSemanticLayerContainsNoGenericMudAppearance()
    {
        var rules = ReadRules("Legacy.Maliev.Intranet.Client", "wwwroot", "css", "shadcn.css");

        Assert.DoesNotContain(rules, rule =>
            rule.Selector.Contains(".mud-", StringComparison.Ordinal) &&
            !rule.Selector.Contains(".legacy-", StringComparison.Ordinal) &&
            !rule.Selector.Contains(".mlv-", StringComparison.Ordinal));
    }

    [Fact]
    public void OperationsAndScopedStylesUseMudSelectorsForGeometryOnly()
    {
        foreach (var rule in ReadAllProductMudRules())
        {
            foreach (var branch in ExpandSelectorBranches(rule.Selector)
                         .Where(branch => branch.Contains(".mud-", StringComparison.Ordinal)))
            {
                Assert.True(ApprovedMudSelectorHooks.Any(
                        prefix => branch.Contains(prefix, StringComparison.Ordinal)),
                    $"Mud selector branch lacks an approved semantic hook: {branch}");
                Assert.False(AppearanceProperties.Any(
                        property => Regex.IsMatch(rule.Declarations, $@"(^|;)\s*{Regex.Escape(property)}\s*:", RegexOptions.IgnoreCase)),
                    $"Mud selector redefines appearance: {branch}");
            }
        }
    }

    [Fact]
    public void MixedAllowedAndUnapprovedFunctionalSelectorBranchesAreRejected()
    {
        var branches = ExpandSelectorBranches(".legacy-shell :where(.mud-input-control, .mud-button-root), :is(.legacy-shell, .mud-table-root)");

        Assert.Contains(".legacy-shell .mud-input-control", branches);
        Assert.Contains(".legacy-shell .mud-button-root", branches);
        Assert.Contains(".mud-table-root", branches);
        Assert.Contains(branches,
            branch => branch.Contains(".mud-", StringComparison.Ordinal) &&
                      !ApprovedMudSelectorHooks.Any(prefix => branch.Contains(prefix, StringComparison.Ordinal)));
    }

    [Fact]
    public void DesignTokensAliasesPackageVariablesWithoutRedefiningShadcnValues()
    {
        var tokens = Read("Legacy.Maliev.Intranet.Client", "wwwroot", "css", "design-tokens.css");

        Assert.DoesNotMatch(new Regex(@"--shadcn-[\w-]+\s*:"), tokens);
        Assert.Contains("--maliev-surface-page: var(--shadcn-background)", tokens, StringComparison.Ordinal);
        Assert.Contains("--maliev-surface-card: var(--shadcn-card)", tokens, StringComparison.Ordinal);
        Assert.Contains("--maliev-text-primary: var(--shadcn-foreground)", tokens, StringComparison.Ordinal);
        Assert.Contains("--maliev-text-secondary: var(--shadcn-muted-foreground)", tokens, StringComparison.Ordinal);
        Assert.Contains("--maliev-action-primary: var(--shadcn-primary)", tokens, StringComparison.Ordinal);
        Assert.Contains("--maliev-action-primary-text: var(--shadcn-primary-foreground)", tokens, StringComparison.Ordinal);
        Assert.Contains("--maliev-focus-color: var(--shadcn-ring)", tokens, StringComparison.Ordinal);
        Assert.Contains("--legacy-background: var(--shadcn-background)", tokens, StringComparison.Ordinal);
        Assert.Contains("--legacy-surface: var(--shadcn-card)", tokens, StringComparison.Ordinal);
        Assert.Contains("--legacy-primary: var(--shadcn-primary)", tokens, StringComparison.Ordinal);
    }

    [Fact]
    public void DarkThemeCompatibilityAliasesContinueToResolvePackageVariables()
    {
        var tokens = Read("Legacy.Maliev.Intranet.Client", "wwwroot", "css", "design-tokens.css");
        var darkRule = ScanRules(StripComments(tokens))
            .Single(rule => rule.Selector.Contains(":root[data-maliev-theme=\"dark\"]", StringComparison.Ordinal));

        Assert.Contains("--legacy-background: var(--shadcn-background)", darkRule.Declarations, StringComparison.Ordinal);
        Assert.DoesNotMatch(new Regex(@"--(?:maliev|legacy)-[\w-]+\s*:\s*#[0-9a-f]{3,8}", RegexOptions.IgnoreCase), darkRule.Declarations);
    }

    private sealed record CssRule(string Selector, string Declarations);

    private static IEnumerable<CssRule> ReadRules(params string[] segments) =>
        ScanRules(StripComments(Read(segments)));

    private static IEnumerable<CssRule> ReadAllProductMudRules()
    {
        var root = FindRoot();
        var productionRoots = new[]
        {
            "Legacy.Maliev.Intranet.Client",
            "Legacy.Maliev.Intranet.Client.Shared"
        }.Concat(Directory.GetDirectories(root, "Legacy.Maliev.Intranet.Client.Features.*")
            .Select(Path.GetFileName)
            .OfType<string>());

        return productionRoots.SelectMany(directory => Directory.EnumerateFiles(
                Path.Combine(root, directory), "*.css", SearchOption.AllDirectories)
            .Where(path => !path.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                .Any(segment => string.Equals(segment, "bin", StringComparison.OrdinalIgnoreCase) ||
                                string.Equals(segment, "obj", StringComparison.OrdinalIgnoreCase)))
            .SelectMany(path => ScanRules(StripComments(File.ReadAllText(path)))));
    }

    private static IEnumerable<CssRule> ScanRules(string css, int start = 0, int? limit = null)
    {
        var end = limit ?? css.Length;
        var cursor = start;
        while (cursor < end)
        {
            var open = css.IndexOf('{', cursor);
            if (open < 0 || open >= end)
            {
                yield break;
            }

            var selector = css[cursor..open].Trim();
            var depth = 1;
            var close = open + 1;
            while (close < end && depth > 0)
            {
                if (css[close] == '{')
                {
                    depth++;
                }
                else if (css[close] == '}')
                {
                    depth--;
                }

                close++;
            }

            if (depth != 0)
            {
                throw new InvalidDataException($"Unbalanced CSS block: {selector}");
            }

            if (selector.StartsWith('@'))
            {
                foreach (var nested in ScanRules(css, open + 1, close - 1))
                {
                    yield return nested;
                }
            }
            else
            {
                yield return new CssRule(selector, css[(open + 1)..(close - 1)]);
            }

            cursor = close;
        }
    }

    private static IEnumerable<string> ExpandSelectorBranches(string selector)
    {
        foreach (var branch in SplitTopLevel(selector, ','))
        {
            foreach (var expanded in ExpandFunctionalBranches(branch.Trim()))
            {
                yield return expanded.Trim();
            }
        }
    }

    private static IEnumerable<string> ExpandFunctionalBranches(string selector)
    {
        var match = Regex.Match(selector, @":(?:is|where)\(([^()]*)\)");
        if (!match.Success)
        {
            yield return selector;
            yield break;
        }

        foreach (var option in SplitTopLevel(match.Groups[1].Value, ','))
        {
            foreach (var expanded in ExpandFunctionalBranches(
                         string.Concat(selector.AsSpan(0, match.Index), option.Trim(), selector.AsSpan(match.Index + match.Length))))
            {
                yield return expanded;
            }
        }
    }

    private static IEnumerable<string> SplitTopLevel(string value, char delimiter)
    {
        var depth = 0;
        var start = 0;
        for (var index = 0; index < value.Length; index++)
        {
            if (value[index] == '(') depth++;
            else if (value[index] == ')') depth--;
            else if (value[index] == delimiter && depth == 0)
            {
                yield return value[start..index];
                start = index + 1;
            }
        }

        yield return value[start..];
    }

    private static string StripComments(string css) => Regex.Replace(css, @"/\*.*?\*/", string.Empty, RegexOptions.Singleline);

    private static string Read(params string[] segments) => File.ReadAllText(Path.Combine([FindRoot(), .. segments]));

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
