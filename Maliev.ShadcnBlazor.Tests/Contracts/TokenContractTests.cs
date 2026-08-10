using System.Text;
namespace Maliev.ShadcnBlazor.Tests.Contracts;

public sealed class TokenContractTests
{
    [Fact]
    public void StylesheetExposesPinnedNeutralTokensThroughRootAndDarkThemeRules()
    {
        var stylesheet = CssStylesheet.Load(FindCss());

        var root = stylesheet.GetRequiredRule(":root");
        Assert.Equal("oklch(1 0 0)", root.GetRequiredDeclaration("--shadcn-background"));
        Assert.Equal("oklch(0.145 0 0)", root.GetRequiredDeclaration("--shadcn-foreground"));
        Assert.Equal("oklch(0.205 0 0)", root.GetRequiredDeclaration("--shadcn-primary"));
        Assert.Equal("oklch(0.985 0 0)", root.GetRequiredDeclaration("--shadcn-primary-foreground"));
        Assert.Equal("oklch(0.922 0 0)", root.GetRequiredDeclaration("--shadcn-border"));
        Assert.Equal("oklch(0.922 0 0)", root.GetRequiredDeclaration("--shadcn-input"));
        Assert.Equal("oklch(0.708 0 0)", root.GetRequiredDeclaration("--shadcn-ring"));
        Assert.Equal("0.625rem", root.GetRequiredDeclaration("--shadcn-radius"));
        Assert.Equal("2.25rem", root.GetRequiredDeclaration("--shadcn-control-height"));

        var dark = stylesheet.GetRequiredRule("[data-shadcn-theme=\"dark\"]");
        Assert.Equal("oklch(0.145 0 0)", dark.GetRequiredDeclaration("--shadcn-background"));
        Assert.Equal("oklch(0.985 0 0)", dark.GetRequiredDeclaration("--shadcn-foreground"));
        Assert.DoesNotContain(stylesheet.GetAllDeclarationNames(), name => name.StartsWith("--mud-", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void StylesheetAppliesAccessibilityContractsInTheirMediaQueries()
    {
        var stylesheet = CssStylesheet.Load(FindCss());

        var coarsePointer = stylesheet.GetRequiredMedia("pointer: coarse")
            .GetRequiredRule(".shadcn-scope :where(button, [role=\"button\"], input, select, textarea)");
        Assert.Equal("2.75rem", coarsePointer.GetRequiredDeclaration("min-height"));

        var reducedMotion = stylesheet.GetRequiredMedia("prefers-reduced-motion: reduce")
            .GetRequiredRule(".shadcn-scope *");
        Assert.Equal("0.01ms !important", reducedMotion.GetRequiredDeclaration("animation-duration"));
        Assert.Equal("0.01ms !important", reducedMotion.GetRequiredDeclaration("transition-duration"));

        var forcedColors = stylesheet.GetRequiredMedia("forced-colors: active")
            .GetRequiredRule(".shadcn-scope :focus-visible");
        Assert.Equal("2px solid Highlight", forcedColors.GetRequiredDeclaration("outline"));
        Assert.Equal("2px", forcedColors.GetRequiredDeclaration("outline-offset"));
    }

    private static string FindCss()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Legacy.Maliev.Intranet.slnx")))
            directory = directory.Parent;

        return Path.Combine(directory!.FullName, "Maliev.ShadcnBlazor", "wwwroot", "css", "shadcn-base.css");
    }

    private sealed class CssStylesheet
    {
        private readonly IReadOnlyList<CssRule> _rules;
        private readonly IReadOnlyList<CssMediaRule> _mediaRules;

        private CssStylesheet(IReadOnlyList<CssRule> rules, IReadOnlyList<CssMediaRule> mediaRules)
        {
            _rules = rules;
            _mediaRules = mediaRules;
        }

        public static CssStylesheet Load(string path) => Parse(File.ReadAllText(path));

        public CssRule GetRequiredRule(string selector) =>
            _rules.SingleOrDefault(rule => rule.Selectors.Contains(selector, StringComparer.Ordinal))
            ?? throw new Xunit.Sdk.XunitException($"Missing CSS rule for selector '{selector}'.");

        public CssMediaRule GetRequiredMedia(string query) =>
            _mediaRules.SingleOrDefault(media => string.Equals(media.Query, query, StringComparison.Ordinal))
            ?? throw new Xunit.Sdk.XunitException($"Missing CSS media query '{query}'.");

        public IEnumerable<string> GetAllDeclarationNames() =>
            _rules.SelectMany(rule => rule.Declarations.Keys)
                .Concat(_mediaRules.SelectMany(media => media.Rules.SelectMany(rule => rule.Declarations.Keys)));

        private static CssStylesheet Parse(string css)
        {
            var rules = new List<CssRule>();
            var mediaRules = new List<CssMediaRule>();
            var index = 0;

            while (TryReadBlock(css, ref index, out var prelude, out var body))
            {
                if (prelude.StartsWith("@media", StringComparison.Ordinal))
                {
                    mediaRules.Add(new CssMediaRule(
                        NormalizeMediaQuery(prelude["@media".Length..]),
                        ParseRules(body)));
                }
                else if (!prelude.StartsWith('@'))
                {
                    rules.Add(CreateRule(prelude, body));
                }
            }

            return new CssStylesheet(rules, mediaRules);
        }

        private static IReadOnlyList<CssRule> ParseRules(string css)
        {
            var rules = new List<CssRule>();
            var index = 0;
            while (TryReadBlock(css, ref index, out var prelude, out var body))
            {
                if (!prelude.StartsWith('@'))
                    rules.Add(CreateRule(prelude, body));
            }

            return rules;
        }

        private static CssRule CreateRule(string selectorList, string declarations)
        {
            var selectors = SplitSelectors(selectorList)
                .ToArray();
            var properties = declarations.Split(';', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
                .Select(declaration => declaration.Split(':', 2, StringSplitOptions.TrimEntries))
                .Where(parts => parts.Length == 2)
                .ToDictionary(parts => parts[0], parts => Normalize(parts[1]), StringComparer.Ordinal);

            return new CssRule(selectors, properties);
        }

        private static IEnumerable<string> SplitSelectors(string selectorList)
        {
            var start = 0;
            var parenthesisDepth = 0;
            var attributeDepth = 0;

            for (var index = 0; index < selectorList.Length; index++)
            {
                switch (selectorList[index])
                {
                    case '(':
                        parenthesisDepth++;
                        break;
                    case ')':
                        parenthesisDepth--;
                        break;
                    case '[':
                        attributeDepth++;
                        break;
                    case ']':
                        attributeDepth--;
                        break;
                    case ',' when parenthesisDepth == 0 && attributeDepth == 0:
                        yield return Normalize(selectorList[start..index]);
                        start = index + 1;
                        break;
                }
            }

            yield return Normalize(selectorList[start..]);
        }

        private static bool TryReadBlock(string css, ref int index, out string prelude, out string body)
        {
            while (index < css.Length && char.IsWhiteSpace(css[index]))
                index++;

            if (index >= css.Length)
            {
                prelude = string.Empty;
                body = string.Empty;
                return false;
            }

            var openBrace = css.IndexOf('{', index);
            if (openBrace < 0)
            {
                prelude = string.Empty;
                body = string.Empty;
                return false;
            }

            prelude = Normalize(css[index..openBrace]);
            var depth = 1;
            var cursor = openBrace + 1;
            for (; cursor < css.Length && depth > 0; cursor++)
            {
                if (css[cursor] == '{') depth++;
                if (css[cursor] == '}') depth--;
            }

            if (depth != 0)
                throw new Xunit.Sdk.XunitException("CSS contains an unclosed block.");

            body = css[(openBrace + 1)..(cursor - 1)];
            index = cursor;
            return true;
        }

        private static string Normalize(string value) => string.Join(' ', value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

        private static string NormalizeMediaQuery(string value)
        {
            var normalized = Normalize(value);
            return normalized.StartsWith('(') && normalized.EndsWith(')')
                ? normalized[1..^1]
                : normalized;
        }
    }

    private sealed record CssRule(IReadOnlyList<string> Selectors, IReadOnlyDictionary<string, string> Declarations)
    {
        public string GetRequiredDeclaration(string property) =>
            Declarations.TryGetValue(property, out var value)
                ? value
                : throw new Xunit.Sdk.XunitException($"Missing CSS declaration '{property}'.");
    }

    private sealed record CssMediaRule(string Query, IReadOnlyList<CssRule> Rules)
    {
        public CssRule GetRequiredRule(string selector) =>
            Rules.SingleOrDefault(rule => rule.Selectors.Contains(selector, StringComparer.Ordinal))
            ?? throw new Xunit.Sdk.XunitException($"Missing CSS rule for selector '{selector}' in media query '{Query}'.");
    }
}
