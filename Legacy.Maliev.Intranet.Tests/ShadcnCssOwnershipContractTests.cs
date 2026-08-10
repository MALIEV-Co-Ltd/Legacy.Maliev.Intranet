using System.Text;
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

    private static readonly string[] ExpandableSelectorFunctionNames = ["is", "where"];
    private static readonly string[] NegatedSelectorFunctionNames = ["not"];

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
                Assert.True(HasApprovedMudSelectorHook(branch),
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
                      !HasApprovedMudSelectorHook(branch));
    }

    [Fact]
    public void NestedFunctionalSelectorBranchesExposeUnapprovedMudSelectors()
    {
        var branches = ExpandSelectorBranches(":where(.legacy-shell, :not(.mud-menu-root)) .mud-button-root");

        Assert.Contains(".legacy-shell .mud-button-root", branches);
        Assert.Contains(":not(.mud-menu-root) .mud-button-root", branches);
        Assert.Contains(branches,
            branch => branch.Contains(".mud-", StringComparison.Ordinal) &&
                      !HasApprovedMudSelectorHook(branch));
    }

    [Fact]
    public void NestedFunctionalSelectorBranchesRetainAllowedMudScopes()
    {
        var branches = ExpandSelectorBranches(":where(.legacy-shell, .operations-shell :not(.is-collapsed)) .mud-button-root");

        Assert.Contains(".legacy-shell .mud-button-root", branches);
        Assert.Contains(".operations-shell :not(.is-collapsed) .mud-button-root", branches);
        Assert.All(branches.Where(branch => branch.Contains(".mud-", StringComparison.Ordinal)),
            branch => Assert.True(HasApprovedMudSelectorHook(branch)));
    }

    [Fact]
    public void NegatedApprovedHookCannotApproveAnExpandedMudBranch()
    {
        var branches = ExpandSelectorBranches(":where(.legacy-shell, :not(.legacy-shell)) .mud-button-root");

        Assert.Contains(branches, branch =>
            branch == ".legacy-shell .mud-button-root" &&
            HasApprovedMudSelectorHook(branch));
        Assert.Contains(branches, branch =>
            branch == ":not(.legacy-shell) .mud-button-root" &&
            !HasApprovedMudSelectorHook(branch));
    }

    [Theory]
    [InlineData(":not(:where(.legacy-shell, .operations-shell)) .mud-button-root")]
    [InlineData(":not(:is(.legacy-shell, :where(.operations-shell, .mlv-shell))) .mud-button-root")]
    public void NestedNegatedSelectorFunctionsCannotApproveMudBranches(string branch)
    {
        Assert.False(HasApprovedMudSelectorHook(branch));
    }

    [Theory]
    [InlineData(".legacy-shell :not(.is-collapsed) .mud-button-root")]
    [InlineData(".operations-shell :not(:where(.legacy-shell, .mlv-shell)) .mud-button-root")]
    public void PositiveApprovedAncestorsRemainValidOutsideNegations(string branch)
    {
        Assert.True(HasApprovedMudSelectorHook(branch));
    }

    [Theory]
    [InlineData(@":n\6ft(.legacy-shell) .mud-button-root")]
    [InlineData(@":\6e ot(.legacy-shell) .mud-button-root")]
    [InlineData(@":n\00006f t(.legacy-shell) .mud-button-root")]
    [InlineData(@":no\t(.legacy-shell) .mud-button-root")]
    [InlineData(@":N\4FT(.legacy-shell) .mud-button-root")]
    public void EscapedNegatedFunctionNamesCannotApproveMudBranches(string branch)
    {
        Assert.False(HasApprovedMudSelectorHook(branch));
    }

    [Theory]
    [InlineData(@".legacy-shell :n\6ft(.is-collapsed) .mud-button-root")]
    [InlineData(@".operations-shell :\6e ot(:w\68 ere(.legacy-shell, .mlv-shell)) .mud-button-root")]
    public void PositiveApprovedAncestorsRemainValidOutsideEscapedNegations(string branch)
    {
        Assert.True(HasApprovedMudSelectorHook(branch));
    }

    [Fact]
    public void EscapedIsFunctionNamesExpandEveryEffectiveBranch()
    {
        var branches = ExpandSelectorBranches(@":i\73(.legacy-shell, .mud-table-root)").ToList();

        Assert.Contains(".legacy-shell", branches);
        Assert.Contains(".mud-table-root", branches);
        Assert.Contains(branches,
            branch => branch.Contains(".mud-", StringComparison.Ordinal) &&
                      !HasApprovedMudSelectorHook(branch));
    }

    [Fact]
    public void EscapedWhereFunctionsCannotHideEscapedNegatedHooks()
    {
        var branches = ExpandSelectorBranches(@":w\68 ere(.legacy-shell, :n\6ft(.operations-shell)) .mud-button-root").ToList();

        Assert.Contains(".legacy-shell .mud-button-root", branches);
        Assert.Contains(branches,
            branch => branch == @":n\6ft(.operations-shell) .mud-button-root" &&
                      !HasApprovedMudSelectorHook(branch));
    }

    [Fact]
    public void EscapedParenthesesDoNotChangeFunctionalSelectorBalance()
    {
        var branches = ExpandSelectorBranches(@":is(.legacy-shell, [data-label=\(] .operations-shell) .mud-button-root").ToList();

        Assert.Contains(".legacy-shell .mud-button-root", branches);
        Assert.Contains(@"[data-label=\(] .operations-shell .mud-button-root", branches);
    }

    [Fact]
    public void UnbalancedEscapedExpandableFunctionIsRejected()
    {
        Assert.Throws<InvalidDataException>(() =>
            ExpandSelectorBranches(@":i\73(.legacy-shell, .mud-table-root").ToList());
    }

    [Fact]
    public void MalformedPseudoFunctionEscapeIsRejectedWithoutUnboundedScanning()
    {
        Assert.Throws<InvalidDataException>(() =>
            HasApprovedMudSelectorHook(":n\\\not(.legacy-shell) .mud-button-root"));
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
        if (!TryFindExpandableFunction(selector, out var functionStart, out var argumentsStart, out var functionEnd))
        {
            yield return selector;
            yield break;
        }

        foreach (var option in SplitTopLevel(selector[(argumentsStart + 1)..functionEnd], ','))
        {
            foreach (var expanded in ExpandFunctionalBranches(
                         string.Concat(selector.AsSpan(0, functionStart), option.Trim(), selector.AsSpan(functionEnd + 1))))
            {
                yield return expanded;
            }
        }
    }

    private static bool HasApprovedMudSelectorHook(string selector)
    {
        var positiveScope = StripNegatedSelectorFunctions(selector);
        return ApprovedMudSelectorHooks.Any(prefix => positiveScope.Contains(prefix, StringComparison.Ordinal));
    }

    private static string StripNegatedSelectorFunctions(string selector)
    {
        while (TryFindNegatedSelectorFunction(selector, out var functionStart, out var functionEnd))
        {
            selector = string.Concat(selector.AsSpan(0, functionStart), selector.AsSpan(functionEnd + 1));
        }

        return selector;
    }

    private static bool TryFindExpandableFunction(string selector, out int functionStart, out int argumentsStart, out int functionEnd)
    {
        return TryFindSelectorFunction(selector, ExpandableSelectorFunctionNames,
            out functionStart, out argumentsStart, out functionEnd);
    }

    private static bool TryFindNegatedSelectorFunction(string selector, out int functionStart, out int functionEnd)
    {
        return TryFindSelectorFunction(selector, NegatedSelectorFunctionNames,
            out functionStart, out _, out functionEnd);
    }

    private static bool TryFindSelectorFunction(
        string selector,
        IReadOnlyCollection<string> acceptedNames,
        out int functionStart,
        out int argumentsStart,
        out int functionEnd)
    {
        for (var index = 0; index < selector.Length; index++)
        {
            if (selector[index] == '\\')
            {
                index = SkipCssEscape(selector, index) - 1;
                continue;
            }

            if (selector[index] is '\'' or '"')
            {
                index = SkipQuotedValue(selector, index) - 1;
                continue;
            }

            if (selector[index] != ':' ||
                !TryReadPseudoFunctionName(selector, index + 1, out var decodedName, out argumentsStart) ||
                !acceptedNames.Contains(decodedName, StringComparer.OrdinalIgnoreCase))
            {
                continue;
            }

            functionStart = index;
            functionEnd = FindMatchingParenthesis(selector, argumentsStart);
            return true;
        }

        functionStart = argumentsStart = functionEnd = -1;
        return false;
    }

    private static bool TryReadPseudoFunctionName(
        string selector,
        int start,
        out string decodedName,
        out int argumentsStart)
    {
        var decoded = new StringBuilder();
        var cursor = start;

        while (cursor < selector.Length)
        {
            var character = selector[cursor];
            if (IsCssIdentifierCharacter(character))
            {
                decoded.Append(character);
                cursor++;
                continue;
            }

            if (character != '\\')
            {
                break;
            }

            cursor = DecodeCssEscape(selector, cursor, decoded);
        }

        if (decoded.Length == 0 || cursor >= selector.Length || selector[cursor] != '(')
        {
            decodedName = string.Empty;
            argumentsStart = -1;
            return false;
        }

        decodedName = decoded.ToString();
        argumentsStart = cursor;
        return true;
    }

    private static bool IsCssIdentifierCharacter(char character) =>
        character is '-' or '_' || char.IsLetterOrDigit(character) || character >= '\u0080';

    private static int DecodeCssEscape(string value, int escapeStart, StringBuilder? decoded)
    {
        var cursor = escapeStart + 1;
        if (cursor >= value.Length || IsCssNewLine(value[cursor]))
        {
            throw new InvalidDataException($"Malformed CSS escape in selector: {value}");
        }

        if (!IsHexDigit(value[cursor]))
        {
            decoded?.Append(value[cursor]);
            return cursor + 1;
        }

        var codePoint = 0;
        var digits = 0;
        while (cursor < value.Length && digits < 6 && IsHexDigit(value[cursor]))
        {
            codePoint = (codePoint * 16) + HexValue(value[cursor]);
            cursor++;
            digits++;
        }

        if (cursor < value.Length && IsCssWhitespace(value[cursor]))
        {
            if (value[cursor] == '\r' && cursor + 1 < value.Length && value[cursor + 1] == '\n')
            {
                cursor++;
            }

            cursor++;
        }

        if (codePoint == 0 || codePoint > 0x10FFFF || codePoint is >= 0xD800 and <= 0xDFFF)
        {
            codePoint = 0xFFFD;
        }

        decoded?.Append(char.ConvertFromUtf32(codePoint));
        return cursor;
    }

    private static int SkipCssEscape(string value, int escapeStart) =>
        DecodeCssEscape(value, escapeStart, decoded: null);

    private static int SkipQuotedValue(string value, int quoteStart)
    {
        var quote = value[quoteStart];
        for (var cursor = quoteStart + 1; cursor < value.Length; cursor++)
        {
            if (value[cursor] == '\\')
            {
                cursor = SkipCssEscape(value, cursor) - 1;
            }
            else if (value[cursor] == quote)
            {
                return cursor + 1;
            }
        }

        throw new InvalidDataException($"Unbalanced quoted selector value: {value}");
    }

    private static int FindMatchingParenthesis(string selector, int argumentsStart)
    {
        var depth = 1;
        for (var cursor = argumentsStart + 1; cursor < selector.Length; cursor++)
        {
            if (selector[cursor] == '\\')
            {
                cursor = SkipCssEscape(selector, cursor) - 1;
            }
            else if (selector[cursor] is '\'' or '"')
            {
                cursor = SkipQuotedValue(selector, cursor) - 1;
            }
            else if (selector[cursor] == '(')
            {
                depth++;
            }
            else if (selector[cursor] == ')' && --depth == 0)
            {
                return cursor;
            }
        }

        throw new InvalidDataException($"Unbalanced selector function: {selector}");
    }

    private static bool IsHexDigit(char character) =>
        character is >= '0' and <= '9' or >= 'a' and <= 'f' or >= 'A' and <= 'F';

    private static int HexValue(char character) => character switch
    {
        >= '0' and <= '9' => character - '0',
        >= 'a' and <= 'f' => character - 'a' + 10,
        _ => character - 'A' + 10
    };

    private static bool IsCssWhitespace(char character) => character is ' ' or '\t' or '\r' or '\n' or '\f';

    private static bool IsCssNewLine(char character) => character is '\r' or '\n' or '\f';

    private static IEnumerable<string> SplitTopLevel(string value, char delimiter)
    {
        var depth = 0;
        var start = 0;
        for (var index = 0; index < value.Length; index++)
        {
            if (value[index] == '\\')
            {
                index = SkipCssEscape(value, index) - 1;
            }
            else if (value[index] is '\'' or '"')
            {
                index = SkipQuotedValue(value, index) - 1;
            }
            else if (value[index] == '(') depth++;
            else if (value[index] == ')' && --depth < 0)
            {
                throw new InvalidDataException($"Unbalanced selector: {value}");
            }
            else if (value[index] == delimiter && depth == 0)
            {
                yield return value[start..index];
                start = index + 1;
            }
        }

        if (depth != 0)
        {
            throw new InvalidDataException($"Unbalanced selector: {value}");
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
