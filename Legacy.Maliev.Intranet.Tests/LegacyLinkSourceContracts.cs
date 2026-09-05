using System.Text.RegularExpressions;

namespace Legacy.Maliev.Intranet.Tests;

internal static class LegacyLinkSourceContracts
{
    private static readonly SpecializedOwner[] SpecializedOwners =
    [
        Raw("Legacy.Maliev.Intranet.Client/Layout/MainLayout.razor", "href=\"#main-content\"", "class=\"legacy-skip-link\"", "@Text[\"Skip to content\"]"),
        Raw("Legacy.Maliev.Intranet.Client/Layout/LegacyTopBar.razor", "href=\"/Login\"", "class=\"legacy-signin-link\"", "@Text[\"Sign in\"]"),
        Raw("Legacy.Maliev.Intranet.Client/Components/Shell/LegacyNavigationRail.razor", "href=\"/Dashboard\"", "class=\"legacy-rail-logo legacy-logo-link\"", "@Text[\"MALIEV dashboard\"]"),
        ShadcnSidebarMenuButton("Legacy.Maliev.Intranet.Client/Components/Shell/LegacyNavigationRail.razor", 2, "Href=\"@item.Href\"", "legacy-rail-link", "Active=\"@IsItemPageCurrent(item)\"", "OnClick=\"NavigateAsync\""),
        ShadcnSidebarMenuSubButton("Legacy.Maliev.Intranet.Client/Components/Shell/LegacyNavigationRail.razor", "Href=\"@child.Href\"", "legacy-rail-link--child", "Active=\"@IsItemPageCurrent(child)\"", "@onclick=\"NavigateAsync\""),
        ShadcnButton("Legacy.Maliev.Intranet.Client/Components/Shell/LegacyQuickActions.razor", "Href=\"@item.Href\"", "Class=\"@QuickActionClass(item)\"", "aria-label=\"@Text[item.Label]\"", "@Text[item.Label]"),
        Raw("Legacy.Maliev.Intranet.Client/Pages/Login.razor", "href=\"/\"", "class=\"legacy-login-brand\"", "aria-label=\"@Text[\"HomeLabel\"]\""),

        ShadcnButton("Legacy.Maliev.Intranet.Client.Features.Orders/Components/Shared/PrimaryButton.razor", "Href=\"@Href\"", "Class=\"@CssClass\"", "Variant=\"ShadcnButtonVariant.Default\""),
        ShadcnButton("Legacy.Maliev.Intranet.Client.Features.Orders/Components/Shared/SecondaryButton.razor", "Href=\"@Href\"", "Class=\"@CssClass\"", "download=\"@Download\"", "Variant=\"ShadcnButtonVariant.Outline\""),
        ShadcnButton("Legacy.Maliev.Intranet.Client/Pages/AccessDenied.razor", "Href=\"/Login\"", "@Text[\"ReturnToSignIn\"]"),
        ShadcnButton("Legacy.Maliev.Intranet.Client/Pages/NotFound.razor", "Href=\"/Dashboard\"", "@Text[\"Dashboard\"]"),
        ShadcnButton("Legacy.Maliev.Intranet.Client.Features.Customers/Pages/Customers.razor", "Href=\"/Customers/Create\"", "@Text[\"CreateCustomer\"]"),
        ShadcnButton("Legacy.Maliev.Intranet.Client.Features.Employees/Pages/Employees.razor", "Href=\"/Employees/Create\"", "@Text[\"CreateEmployee\"]"),
        ShadcnButton("Legacy.Maliev.Intranet.Client.Features.Catalog/Pages/Materials.razor", "Href=\"/Materials/Create\"", "@Text[\"CreateMaterial\"]"),
        ShadcnButton("Legacy.Maliev.Intranet.Client.Features.Accounting/Pages/Invoices.razor", "Href=\"/Invoices/Create\"", "@Text[\"Create\"]"),
        ShadcnButton("Legacy.Maliev.Intranet.Client.Features.Accounting/Pages/Finances.razor", "Href=\"/Finances/Create\"", "@Text[\"Create\"]"),
        ShadcnButton("Legacy.Maliev.Intranet.Client.Features.Accounting/Pages/Finances.razor", "Href=\"/Finances/YearlyActivityChart\"", "Variant=\"ShadcnButtonVariant.Outline\"", "@Text[\"YearlyActivity\"]"),
        ShadcnButton("Legacy.Maliev.Intranet.Client.Features.Accounting/Pages/Finances.razor", "/Finances/NetProfitChart?year={BangkokYear}", "Variant=\"ShadcnButtonVariant.Outline\"", "@Text[\"NetProfit\"]"),
        ShadcnButton("Legacy.Maliev.Intranet.Client.Features.Quotations/Pages/Quotations/Index.razor", "Href=\"/Quotations/Create\"", "@Text[\"Create\"]"),
        ShadcnButton("Legacy.Maliev.Intranet.Client.Features.Quotations/Pages/Quotations/Index.razor", "Href=\"/Quotations/Estimate\"", "Variant=\"ShadcnButtonVariant.Outline\"", "@Text[\"Estimate\"]"),
        ShadcnButton("Legacy.Maliev.Intranet.Client.Features.Quotations/Pages/Quotations/View.razor", "Href=\"@quotationUri.ToString()\"", "Target=\"_blank\"", "Rel=\"noopener\"", "@Text[\"ViewPdf\"]"),
        ShadcnButton("Legacy.Maliev.Intranet.Client.Features.Orders/Pages/OrderDetail.razor", "/bff/orders/{page.Order.Id}/label", "Target=\"_blank\"", "Rel=\"noopener\"", "@Text[\"OrderLabel\"]"),
        ShadcnButton("Legacy.Maliev.Intranet.Client.Features.Orders/Pages/OrderDetail.razor", "/Customers/View?id={customerId}", "Variant=\"ShadcnButtonVariant.Outline\"", "@Text[\"CustomerInfo\"]"),
        ShadcnButton("Legacy.Maliev.Intranet.Client.Features.Procurement/Pages/PurchaseOrderView.razor", "Href=\"@download.Url.AbsoluteUri\"", "Target=\"_blank\"", "Rel=\"noopener\"", "Variant=\"ShadcnButtonVariant.Outline\"", "@Text[\"DownloadPdf\"]"),
    ];

    internal static bool IsSpecializedOwner(string relativePath, string kind, string markup) =>
        SpecializedOwners.Any(owner => owner.Matches(relativePath, kind, markup));

    internal static IReadOnlyList<string> AuditInventory(IEnumerable<SourceDocument> documents)
    {
        var violations = new List<string>();
        var ownerCounts = new int[SpecializedOwners.Length];

        foreach (var document in documents)
        {
            if (!document.RelativePath.EndsWith("/Components/LegacyLink.razor", StringComparison.Ordinal))
            {
                AuditElements(document, "a", "RawAnchor", requireHref: false, ownerCounts, violations);
            }

            foreach (var mudLink in FindElements(document.Source, "MudLink"))
            {
                violations.Add($"{document.RelativePath}: MudLink {SingleLine(mudLink)}");
            }

            if (Regex.IsMatch(document.Source, @"OpenComponent\s*<\s*MudLink\s*>", RegexOptions.IgnoreCase))
            {
                violations.Add($"{document.RelativePath}: builder-created MudLink");
            }

            AuditElements(document, "MudButton", "MudButton", requireHref: true, ownerCounts, violations);
            AuditElements(document, "ShadcnButton", "ShadcnButton", requireHref: true, ownerCounts, violations);
            AuditElements(document, "ShadcnHoverCardTrigger", "ShadcnHoverCardTrigger", requireHref: true, ownerCounts, violations);
            AuditElements(document, "ShadcnSidebarMenuButton", "ShadcnSidebarMenuButton", requireHref: true, ownerCounts, violations);
            AuditElements(document, "ShadcnSidebarMenuSubButton", "ShadcnSidebarMenuSubButton", requireHref: true, ownerCounts, violations);
        }

        for (var index = 0; index < SpecializedOwners.Length; index++)
        {
            if (ownerCounts[index] != SpecializedOwners[index].ExpectedCount)
            {
                violations.Add($"{SpecializedOwners[index].Description}: expected {SpecializedOwners[index].ExpectedCount}, found {ownerCounts[index]}");
            }
        }

        return violations;
    }

    internal static IReadOnlyList<string> FindRecordAccessibleNameViolations(string relativePath, string source)
    {
        var violations = new List<string>();
        foreach (var markup in FindElements(source, "LegacyLink")
                     .Where(value => value.Contains("Role=\"LegacyLinkRole.Record\"", StringComparison.Ordinal)))
        {
            var href = AttributeSection(markup, "Href", "Role");
            var ariaLabel = AttributeSection(markup, "AriaLabel", null);
            if (href is null || ariaLabel is null ||
                !HasSharedRecordReference(href, ariaLabel) ||
                !HasLocalizedOrDisplayName(ariaLabel))
            {
                violations.Add($"{relativePath}: record Href and AriaLabel must share the record context and use localized or record-display text: {SingleLine(markup)}");
            }
        }

        foreach (Match match in Regex.Matches(
                     source,
                     @"(?<builder>[A-Za-z_]\w*)\.OpenComponent\s*<\s*LegacyLink\s*>\s*\([^;]*\);(?<body>.*?)\k<builder>\.CloseComponent\s*\(\s*\)\s*;",
                     RegexOptions.IgnoreCase | RegexOptions.Singleline))
        {
            var body = match.Groups["body"].Value;
            if (!body.Contains("LegacyLinkRole.Record", StringComparison.Ordinal))
            {
                continue;
            }

            var href = BuilderAttribute(body, "Href");
            var ariaLabel = BuilderAttribute(body, "AriaLabel");
            if (href is null || ariaLabel is null ||
                !HasSharedRecordReference(href, ariaLabel) ||
                !HasLocalizedOrDisplayName(ariaLabel))
            {
                violations.Add($"{relativePath}: builder-created record link needs a call-local AriaLabel associated with its Href");
            }
        }

        return violations;
    }

    internal static int CountRecordLinks(string source) =>
        FindElements(source, "LegacyLink").Count(value => value.Contains("Role=\"LegacyLinkRole.Record\"", StringComparison.Ordinal)) +
        Regex.Matches(source, @"OpenComponent\s*<\s*LegacyLink\s*>", RegexOptions.IgnoreCase).Count;

    internal static bool MatchesExpectedLink(string source, string href, params string[] localFragments) =>
        CountExpectedLinks(source, href, localFragments) > 0;

    internal static int CountExpectedLinks(string source, string href, params string[] localFragments) =>
        LinkOwnerTags.Sum(tag => FindElements(source, tag).Count(markup =>
            markup.Contains(href, StringComparison.Ordinal) &&
            localFragments.All(fragment => markup.Contains(fragment, StringComparison.Ordinal))));

    internal static bool MatchesExpectedBuilderLink(string source, string href, params string[] localFragments) =>
        Regex.Matches(
                source,
                @"(?<builder>[A-Za-z_]\w*)\.OpenComponent\s*<\s*LegacyLink\s*>\s*\([^;]*\);(?<body>.*?)\k<builder>\.CloseComponent\s*\(\s*\)\s*;",
                RegexOptions.IgnoreCase | RegexOptions.Singleline)
            .Select(match => match.Groups["body"].Value)
            .Any(body => body.Contains(href, StringComparison.Ordinal) &&
                         localFragments.All(fragment => body.Contains(fragment, StringComparison.Ordinal)));

    internal static bool MatchesExpectedContainer(
        string source,
        string containerTag,
        string href,
        params string[] containerFragments) =>
        FindElements(source, containerTag).Any(container =>
            MatchesExpectedLink(container, href) &&
            containerFragments.All(fragment => container.Contains(fragment, StringComparison.Ordinal)));

    internal static bool MatchesExpectedConditionalLink(
        string source,
        string condition,
        string href,
        params string[] localFragments)
    {
        var conditionIndex = source.IndexOf(condition, StringComparison.Ordinal);
        if (conditionIndex < 0)
        {
            return false;
        }

        var openBrace = source.IndexOf('{', conditionIndex + condition.Length);
        if (openBrace < 0)
        {
            return false;
        }

        var depth = 0;
        for (var index = openBrace; index < source.Length; index++)
        {
            if (source[index] == '{')
            {
                depth++;
            }
            else if (source[index] == '}' && --depth == 0)
            {
                return MatchesExpectedLink(source[openBrace..(index + 1)], href, localFragments);
            }
        }

        return false;
    }

    internal static IReadOnlyList<string> FindElements(string source, string tagName) =>
        Regex.Matches(
                source,
                $@"<{Regex.Escape(tagName)}\b(?:(?!<{Regex.Escape(tagName)}\b).)*?</{Regex.Escape(tagName)}>",
                RegexOptions.IgnoreCase | RegexOptions.Singleline)
            .Select(match => match.Value)
            .ToArray();

    private static readonly string[] LinkOwnerTags = ["LegacyLink", "MudButton", "ShadcnButton", "ShadcnHoverCardTrigger", "ShadcnSidebarMenuButton", "ShadcnSidebarMenuSubButton", "PrimaryButton", "SecondaryButton", "a"];

    private static void AuditElements(
        SourceDocument document,
        string tagName,
        string kind,
        bool requireHref,
        int[] ownerCounts,
        List<string> violations)
    {
        foreach (var markup in FindElements(document.Source, tagName))
        {
            if (requireHref && !Regex.IsMatch(markup, @"\bHref\s*=", RegexOptions.IgnoreCase))
            {
                continue;
            }

            var matches = SpecializedOwners
                .Select((owner, index) => (owner, index))
                .Where(candidate => candidate.owner.Matches(document.RelativePath, kind, markup))
                .ToArray();
            if (matches.Length != 1)
            {
                violations.Add($"{document.RelativePath}: unreviewed {kind} {SingleLine(markup)}");
                continue;
            }

            ownerCounts[matches[0].index]++;
        }
    }

    private static string? AttributeSection(string markup, string attribute, string? nextAttribute)
    {
        var start = markup.IndexOf($"{attribute}=", StringComparison.Ordinal);
        if (start < 0)
        {
            return null;
        }

        var end = nextAttribute is null
            ? markup.IndexOf('>', start)
            : markup.IndexOf($"{nextAttribute}=", start, StringComparison.Ordinal);
        return end > start ? markup[start..end] : null;
    }

    private static string? BuilderAttribute(string body, string attribute)
    {
        var match = Regex.Match(
            body,
            $@"AddAttribute\s*\([^;]*nameof\s*\(\s*LegacyLink\.{attribute}\s*\)[^;]*\);",
            RegexOptions.IgnoreCase | RegexOptions.Singleline);
        return match.Success ? match.Value : null;
    }

    private static bool HasSharedRecordReference(string href, string ariaLabel)
    {
        var hrefKeys = ReferenceKeys(href);
        var ariaKeys = ReferenceKeys(ariaLabel);
        return hrefKeys.Overlaps(ariaKeys);
    }

    private static bool HasLocalizedOrDisplayName(string ariaLabel) =>
        ariaLabel.Contains("Text[", StringComparison.Ordinal) ||
        ariaLabel.Contains("FullName", StringComparison.Ordinal) ||
        ariaLabel.Contains("ActivityTitle", StringComparison.Ordinal);

    private static HashSet<string> ReferenceKeys(string value)
    {
        var keys = new HashSet<string>(StringComparer.Ordinal);
        foreach (Match match in Regex.Matches(value, @"\b[A-Za-z_]\w*(?:\.[A-Za-z_]\w*)*\b"))
        {
            var parts = match.Value.Split('.');
            if (parts.Length == 1)
            {
                if (char.IsLower(parts[0][0]) &&
                    !string.Equals(parts[0], "href", StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(parts[0], "ariaLabel", StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(parts[0], "text", StringComparison.OrdinalIgnoreCase))
                {
                    keys.Add(parts[0]);
                }

                continue;
            }

            for (var length = 1; length <= parts.Length; length++)
            {
                keys.Add(string.Join('.', parts.Take(length)));
            }
        }

        return keys;
    }

    private static SpecializedOwner Raw(string path, params string[] fragments) =>
        new(path, "RawAnchor", fragments, 1);

    private static SpecializedOwner Button(string path, params string[] fragments) =>
        new(path, "MudButton", fragments, 1);

    private static SpecializedOwner ShadcnButton(string path, params string[] fragments) =>
        new(path, "ShadcnButton", fragments, 1);

    private static SpecializedOwner ShadcnSidebarMenuButton(string path, int expectedCount, params string[] fragments) =>
        new(path, "ShadcnSidebarMenuButton", fragments, expectedCount);

    private static SpecializedOwner ShadcnSidebarMenuSubButton(string path, params string[] fragments) =>
        new(path, "ShadcnSidebarMenuSubButton", fragments, 1);

    private static string SingleLine(string value) => Regex.Replace(value, @"\s+", " ").Trim();

    internal sealed record SourceDocument(string RelativePath, string Source);

    private sealed record SpecializedOwner(string Path, string Kind, string[] RequiredFragments, int ExpectedCount)
    {
        internal string Description => $"{Path} {Kind} owner ({string.Join(", ", RequiredFragments)})";

        internal bool Matches(string relativePath, string kind, string markup) =>
            string.Equals(Path, relativePath, StringComparison.Ordinal) &&
            string.Equals(Kind, kind, StringComparison.Ordinal) &&
            RequiredFragments.All(fragment => markup.Contains(fragment, StringComparison.Ordinal));
    }
}
