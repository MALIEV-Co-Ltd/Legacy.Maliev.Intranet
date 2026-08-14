using System.Globalization;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;

namespace Legacy.Maliev.Intranet.Tests;

public sealed class BreadcrumbAndMaterialIconContractTests : BunitContext
{
    private const string ComponentNamespace = "Legacy.Maliev.Intranet.Client.Shared.Components";
    private static readonly IReadOnlyDictionary<string, ApprovedSvgAsset> ApprovedSvgAssets =
        new Dictionary<string, ApprovedSvgAsset>(StringComparer.OrdinalIgnoreCase)
        {
            ["Legacy.Maliev.Intranet.Client/wwwroot/images/favicon.svg"] = new(
                "38826C0FF34521B380507797760ED5FB5FCB615991C293F689F2E748D4DF66B3",
                "MALIEV brand favicon"),
            ["Legacy.Maliev.Intranet.Client/wwwroot/images/MALIEV_BLACK.svg"] = new(
                "47C53B1592579432004376ABDF04905FCC2D4E26245E967169D0D1F0873BD1FB",
                "MALIEV dark wordmark"),
            ["Legacy.Maliev.Intranet.Client/wwwroot/images/MALIEV_WHITE.svg"] = new(
                "B1A0B6CC690D17A9DC0135A56E5914BC0644822812CA9FC377B808CEEB90909E",
                "MALIEV light wordmark"),
        };
    private static readonly IReadOnlyList<ApprovedSvgReference> ApprovedSvgReferences =
    [
        new("Legacy.Maliev.Intranet.Client/wwwroot/index.html", "images/favicon.svg", "<link rel=\"icon\" type=\"image/svg+xml\" href=\"images/favicon.svg\" />", "Browser favicon link"),
        new("Legacy.Maliev.Intranet.Client/Layout/LegacyTopBar.razor", "images/MALIEV_BLACK.svg", "<img class=\"legacy-logo-image legacy-logo-image--light\" src=\"images/MALIEV_BLACK.svg\" alt=\"MALIEV\" />", "Light-theme top-bar wordmark"),
        new("Legacy.Maliev.Intranet.Client/Layout/LegacyTopBar.razor", "images/MALIEV_WHITE.svg", "<img class=\"legacy-logo-image legacy-logo-image--dark\" src=\"images/MALIEV_WHITE.svg\" alt=\"\" aria-hidden=\"true\" />", "Dark-theme top-bar wordmark"),
        new("Legacy.Maliev.Intranet.Client/Pages/Login.razor", "images/MALIEV_WHITE.svg", "<img src=\"images/MALIEV_WHITE.svg\" alt=\"MALIEV\" class=\"legacy-login-brand-image\" />", "Visible login brand wordmark"),
        new("Legacy.Maliev.Intranet.Client/Pages/Login.razor", "images/MALIEV_BLACK.svg", "<img src=\"images/MALIEV_BLACK.svg\" alt=\"\" aria-hidden=\"true\" class=\"legacy-login-title-logo legacy-logo-image--light\" />", "Decorative light-theme login title wordmark"),
        new("Legacy.Maliev.Intranet.Client/Pages/Login.razor", "images/MALIEV_WHITE.svg", "<img src=\"images/MALIEV_WHITE.svg\" alt=\"\" aria-hidden=\"true\" class=\"legacy-login-title-logo legacy-logo-image--dark\" />", "Decorative dark-theme login title wordmark"),
        new("Legacy.Maliev.Intranet.Client/Components/Shell/LegacyNavigationRail.razor", "images/MALIEV_BLACK.svg", "<img class=\"legacy-logo-image legacy-logo-image--light\" src=\"images/MALIEV_BLACK.svg\" alt=\"MALIEV\" />", "Light-theme navigation wordmark"),
        new("Legacy.Maliev.Intranet.Client/Components/Shell/LegacyNavigationRail.razor", "images/MALIEV_WHITE.svg", "<img class=\"legacy-logo-image legacy-logo-image--dark\" src=\"images/MALIEV_WHITE.svg\" alt=\"\" aria-hidden=\"true\" />", "Dark-theme navigation wordmark"),
    ];
    private static readonly IReadOnlyList<ApprovedInlineSvg> ApprovedInlineSvgs =
    [
        new(
            "Legacy.Maliev.Intranet/Pages/Shared/_Layout.cshtml",
            "<svg viewBox=\"0 0 24 24\" aria-hidden=\"true\"><path d=\"M4 7h16M4 12h16M4 17h16\" /></svg>",
            "Decorative navigation-menu indicator for the localized button"),
        new(
            "Legacy.Maliev.Intranet/Pages/Shared/_Layout.cshtml",
            "<svg viewBox=\"0 0 24 24\" aria-hidden=\"true\"><circle cx=\"11\" cy=\"11\" r=\"6\" /><path d=\"m16 16 4 4\" /></svg>",
            "Decorative search indicator for the explicitly labelled search field"),
        new(
            "Legacy.Maliev.Intranet.Client/wwwroot/index.html",
            """
            <svg class="loading-progress" aria-hidden="true" focusable="false">
                            <circle r="40%" cx="50%" cy="50%" />
                            <circle r="40%" cx="50%" cy="50%" />
                        </svg>
            """,
            "Structural loading-progress graphic inside the named live status region"),
        new(
            "Legacy.Maliev.Intranet.Client/wwwroot/css/module-pages.css",
            "data:image/svg+xml,%3Csvg width='16' height='16' viewBox='0 0 16 16' fill='none' xmlns='http://www.w3.org/2000/svg'%3E%3Cpath d='M4 6L8 10L12 6' stroke='%23657380' stroke-width='1.7' stroke-linecap='round' stroke-linejoin='round'/%3E%3C/svg%3E",
            "Decorative native-select disclosure indicator with no interactive semantics"),
    ];
    private static readonly IReadOnlySet<string> DependencyManifestNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "Directory.Build.props",
        "Directory.Build.targets",
        "Directory.Packages.props",
        "packages.lock.json",
        "package-lock.json",
        "pnpm-lock.yaml",
        "yarn.lock",
    };
    private static readonly IReadOnlySet<string> ScannedTextExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        ".cshtml", ".razor", ".cs", ".js", ".mjs", ".css", ".scss", ".html", ".csproj", ".props", ".targets",
        ".svg", ".json", ".xml", ".resx", ".yaml", ".yml", ".lock", ".md", ".txt",
    };
    private static readonly IReadOnlyDictionary<string, ApprovedBinaryAsset> ApprovedBinaryAssets =
        new Dictionary<string, ApprovedBinaryAsset>(StringComparer.OrdinalIgnoreCase)
        {
            ["Legacy.Maliev.Intranet/wwwroot/fonts/ibm-plex-sans-thai/IBMPlexSansThai-Regular.woff2"] = new(
            "0350508969DAD82FFD2B7608D8299454BF19EC54E464A16A66E5A9733E83654D",
            "Self-hosted IBM Plex Sans Thai regular text font"),
            ["Legacy.Maliev.Intranet/wwwroot/fonts/ibm-plex-sans-thai/IBMPlexSansThai-SemiBold.woff2"] = new(
            "83F9D86C099E0006077854CAC1CF6F9D3177FB0C4F356254A7D56D047C097E52",
            "Self-hosted IBM Plex Sans Thai semibold text font"),
            ["Legacy.Maliev.Intranet.Client/wwwroot/fonts/ibm-plex-sans-thai/IBMPlexSansThai-Regular.woff2"] = new(
            "0350508969DAD82FFD2B7608D8299454BF19EC54E464A16A66E5A9733E83654D",
            "Self-hosted IBM Plex Sans Thai regular text font"),
            ["Legacy.Maliev.Intranet.Client/wwwroot/fonts/ibm-plex-sans-thai/IBMPlexSansThai-SemiBold.woff2"] = new(
            "83F9D86C099E0006077854CAC1CF6F9D3177FB0C4F356254A7D56D047C097E52",
            "Self-hosted IBM Plex Sans Thai semibold text font"),
        };
    private static readonly IReadOnlyDictionary<string, ApprovedTextVendorAsset> ApprovedTextVendorAssets =
        new Dictionary<string, ApprovedTextVendorAsset>(StringComparer.OrdinalIgnoreCase)
        {
            ["Legacy.Maliev.Intranet/wwwroot/css/ibm-plex-sans-thai.css"] = new(
                "31D529DDFE8C39FA94A1F5659923C0500479D5C7FA7E4803A470D7B206F18581",
                "Exact IBM Plex Sans Thai text-font face declarations for the server shell"),
            ["Legacy.Maliev.Intranet.Client/wwwroot/css/ibm-plex-sans-thai.css"] = new(
                "31D529DDFE8C39FA94A1F5659923C0500479D5C7FA7E4803A470D7B206F18581",
                "Exact IBM Plex Sans Thai text-font face declarations for the Blazor client"),
        };
    private static readonly IReadOnlySet<string> ApprovedExtensionlessTextFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "Legacy.Maliev.Intranet/Dockerfile",
        "Maliev.ShadcnBlazor/licenses/MudBlazor-LICENSE",
    };

    [Fact]
    public void Breadcrumbs_render_explicit_link_hierarchy_and_current_page_semantics()
    {
        Services.AddLocalization();
        var assembly = typeof(Client.Shared.Components.LegacyLink).Assembly;
        var itemType = assembly.GetType($"{ComponentNamespace}.PageBreadcrumbItem");
        var componentType = assembly.GetType($"{ComponentNamespace}.PageBreadcrumbs");

        Assert.NotNull(itemType);
        Assert.NotNull(componentType);
        Assert.True(itemType!.IsSealed);
        Assert.True(typeof(IReadOnlyList<>).MakeGenericType(itemType).IsAssignableFrom(
            componentType!.GetProperty("Items")!.PropertyType));

        var items = CreateItems(itemType,
            ("Sales", "/sales"),
            ("Customers", "/customers"),
            ("Customer 69738", null));
        var cut = RenderDynamicComponent(componentType, items);

        var navigation = cut.Find("nav.page-breadcrumbs");
        Assert.Equal("Breadcrumbs", navigation.GetAttribute("aria-label"));
        var links = cut.FindAll("a[data-link-role='navigation']");
        Assert.Equal(2, links.Count);
        Assert.Equal("/sales", links[0].GetAttribute("href"));
        Assert.Equal("/customers", links[1].GetAttribute("href"));
        Assert.Equal("Customer 69738", cut.Find("li[aria-current='page']").TextContent.Trim());
        Assert.DoesNotContain("NavigationManager", File.ReadAllText(ComponentPath("PageBreadcrumbs.razor")), StringComparison.Ordinal);
    }

    [Fact]
    public void Breadcrumbs_render_nothing_for_zero_items_and_reject_missing_intermediate_destination()
    {
        Services.AddLocalization();
        var assembly = typeof(Client.Shared.Components.LegacyLink).Assembly;
        var itemType = assembly.GetType($"{ComponentNamespace}.PageBreadcrumbItem");
        var componentType = assembly.GetType($"{ComponentNamespace}.PageBreadcrumbs");

        Assert.NotNull(itemType);
        Assert.NotNull(componentType);
        Assert.Empty(RenderDynamicComponent(componentType!, Array.CreateInstance(itemType!, 0)).Markup.Trim());

        var invalidItems = CreateItems(itemType!, ("Sales", null), ("Customers", null));
        Assert.ThrowsAny<InvalidOperationException>(() => RenderDynamicComponent(componentType!, invalidItems));
    }

    [Fact]
    public void Breadcrumb_resources_have_exact_English_Thai_key_parity()
    {
        var english = ReadResources(ComponentPath("PageBreadcrumbsResources.resx"));
        var thai = ReadResources(ComponentPath("PageBreadcrumbsResources.th.resx"));

        Assert.Equal(new[] { "BreadcrumbLabel", "More" }, english.Keys.Order(StringComparer.Ordinal));
        Assert.Equal(english.Keys.Order(StringComparer.Ordinal), thai.Keys.Order(StringComparer.Ordinal));
        Assert.Equal("Breadcrumbs", english["BreadcrumbLabel"]);
        Assert.Equal("เส้นทางนำทาง", thai["BreadcrumbLabel"]);
        Assert.False(string.IsNullOrWhiteSpace(english["More"]));
        Assert.False(string.IsNullOrWhiteSpace(thai["More"]));
    }

    [Fact]
    public void Production_icon_inventory_uses_embedded_Material_paths_without_runtime_icon_fonts()
    {
        var root = FindRepositoryRoot();
        var files = EnumerateProductionInventoryFiles(root);
        var violations = FindProductionIconInventoryViolations(files);

        Assert.Empty(violations);
    }

    [Theory]
    [InlineData("Client/Page.razor", "@Icons.Custom.Refresh")]
    [InlineData("Package/Component.razor", "<i class=\"fa-solid fa-user\"></i>")]
    [InlineData("Package/Package.csproj", "<PackageReference Include=\"FontAwesome.Free\" />")]
    [InlineData("Client/wwwroot/index.html", "<link href=\"https://fonts.googleapis.com/css2?family=Roboto\" rel=\"stylesheet\">")]
    [InlineData("Client/wwwroot/index.html", "<link href=\"https://fonts.googleapis.com/icon?family=Material+Icons\" rel=\"stylesheet\">")]
    [InlineData("Client/Page.razor", "<img src=\"images/refresh.svg\" alt=\"\">")]
    [InlineData("Server/Pages/Shared/_Layout.cshtml", "<svg viewBox=\"0 0 24 24\"><path d=\"M0 0\" /></svg>")]
    [InlineData("Client/Page.razor", "<img src=\"data:image/svg+xml,%3Csvg%3E%3C/svg%3E\" alt=\"\">")]
    [InlineData("Client/IconAliases.cs", "using Glyphs = MudBlazor.Icons; var icon = Glyphs.Custom.Home;")]
    [InlineData("Client/wwwroot/js/icons.js", "const icons = 'https://fonts.googleapis.com/css2?family=Material+Symbols+Outlined';")]
    [InlineData("packages.lock.json", "{ \"dependencies\": { \"FontAwesome.Free\": { \"type\": \"Transitive\" } } }")]
    public void Icon_inventory_rejects_non_Material_aliases_Font_Awesome_and_any_Google_Fonts_runtime_dependency(
        string path,
        string source)
    {
        Assert.NotEmpty(FindIconInventoryViolations([new(path, source)]));
    }

    [Fact]
    public void Icon_inventory_rejects_unapproved_self_hosted_svg_but_allows_exact_brand_assets()
    {
        Assert.NotEmpty(FindIconInventoryViolations([new("Legacy.Maliev.Intranet.Client/wwwroot/icons/refresh.svg", "<svg />")]));
        var root = FindRepositoryRoot();
        var approvedAssets = ApprovedSvgAssets.Keys.Select(path => new InventoryFile(
            path,
            File.ReadAllText(Path.Combine(root, path.Replace('/', Path.DirectorySeparatorChar)))));
        Assert.Empty(FindIconInventoryViolations(approvedAssets));
    }

    [Fact]
    public void Approved_svg_asset_content_reference_source_and_inline_block_are_fail_closed()
    {
        var root = FindRepositoryRoot();
        const string assetPath = "Legacy.Maliev.Intranet.Client/wwwroot/images/favicon.svg";
        var assetSource = File.ReadAllText(Path.Combine(root, assetPath.Replace('/', Path.DirectorySeparatorChar)));
        Assert.NotEmpty(FindIconInventoryViolations([new(assetPath, assetSource.Replace("</svg>", "<path d=\"M0 0\" /></svg>", StringComparison.Ordinal))]));

        Assert.NotEmpty(FindIconInventoryViolations([
            new("Legacy.Maliev.Intranet.Client/Pages/Unapproved.razor", "<img src=\"images/favicon.svg\" alt=\"Action\">")
        ]));
        const string indexPath = "Legacy.Maliev.Intranet.Client/wwwroot/index.html";
        var indexSource = File.ReadAllText(Path.Combine(root, indexPath.Replace('/', Path.DirectorySeparatorChar)));
        Assert.NotEmpty(FindIconInventoryViolations([
            new(indexPath, indexSource.Replace("</body>", "<img src=\"images/favicon.svg\" alt=\"Action\"></body>", StringComparison.Ordinal))
        ]));

        const string loadingPath = "Legacy.Maliev.Intranet.Client/wwwroot/index.html";
        var loading = File.ReadAllText(Path.Combine(root, loadingPath.Replace('/', Path.DirectorySeparatorChar)));
        var mutatedLoading = loading.Replace(
            "</svg>",
            "<path d=\"M0 0\" /></svg>",
            StringComparison.Ordinal);
        Assert.NotEmpty(FindIconInventoryViolations([new(loadingPath, mutatedLoading)]));
    }

    [Fact]
    public void Production_inventory_fails_on_unclassified_file_extension()
    {
        Assert.Equal(
            [
                "Legacy.Maliev.Intranet.Client/wwwroot/icons/generated.unknowntext",
                "Legacy.Maliev.Intranet.Client/wwwroot/fonts/unapproved-icon.woff2"
            ],
            FindUnclassifiedProductionFiles([
                "Legacy.Maliev.Intranet.Client/wwwroot/icons/generated.unknowntext",
                "Legacy.Maliev.Intranet.Client/wwwroot/fonts/unapproved-icon.woff2",
                "Legacy.Maliev.Intranet.Client/wwwroot/app.mjs",
                "Legacy.Maliev.Intranet.Client/wwwroot/tokens.json",
                "Legacy.Maliev.Intranet.Client/wwwroot/styles.scss",
                "Legacy.Maliev.Intranet.Client/Resources/Text.resx",
                "Legacy.Maliev.Intranet.Client/Config.xml"
            ]));
    }

    [Fact]
    public void Approval_graph_rejects_missing_assets_sources_and_unresolved_targets()
    {
        var files = EnumerateProductionInventoryFiles(FindRepositoryRoot());

        Assert.NotEmpty(FindApprovalGraphViolations(files.Where(file =>
            file.Path != "Legacy.Maliev.Intranet.Client/wwwroot/images/favicon.svg")));
        Assert.NotEmpty(FindApprovalGraphViolations(files.Where(file =>
            file.Path != "Legacy.Maliev.Intranet.Client/wwwroot/index.html")));
        Assert.NotEmpty(FindApprovalGraphViolations(
            files,
            [.. ApprovedSvgReferences, new(
                "Legacy.Maliev.Intranet.Client/wwwroot/index.html",
                "images/missing.svg",
                "<link rel=\"icon\" href=\"images/missing.svg\" />",
                "Deliberately unresolved test target")],
            ApprovedSvgAssets));
        Assert.NotEmpty(FindApprovalGraphViolations(files.Select(file =>
            file.Path == "Legacy.Maliev.Intranet.Client/wwwroot/css/ibm-plex-sans-thai.css"
                ? file with { Source = file.Source + "/* mutated */" }
                : file)));
    }

    [Fact]
    public void Deployable_library_sources_are_scanned_and_icon_font_css_is_rejected()
    {
        var libraryCss = Path.Combine(
            FindRepositoryRoot(),
            "Legacy.Maliev.Intranet.Client", "wwwroot", "lib", "vendor", "icons.css");

        Assert.False(IsGeneratedOrVendorPath(libraryCss));
        Assert.NotEmpty(FindIconInventoryViolations([
            new(
                "Legacy.Maliev.Intranet.Client/wwwroot/lib/vendor/icons.css",
                "@font-face { font-family: 'Material Icons'; src: url('icons.woff2'); }")
        ]));
    }

    [Theory]
    [InlineData("Client/wwwroot/lib/acme-icons.css", "@font-face{font-family:'AcmeGlyphs';src:url(data:font/woff2;base64,AA==)}.icon::before{font-family:'AcmeGlyphs';content:'\\e001'}")]
    [InlineData("Client/wwwroot/lib/acme-icons.css", "@font-face{font-family:'AcmeGlyphs';src:url('/fonts/acme.woff2')}[class^='acme-']::before{font-family:'AcmeGlyphs';content:'\\f101'}")]
    [InlineData("Client/wwwroot/lib/acme-icons.js", "style.textContent=\"@font-face{font-family:AcmeGlyphs;src:local('Acme Glyphs')} .acme::before{font-family:AcmeGlyphs;content:'\\\\e123'}\";")]
    public void Icon_inventory_rejects_custom_glyph_font_constructs_independent_of_family_name(
        string path,
        string source)
    {
        Assert.NotEmpty(FindIconInventoryViolations([new(path, source)]));
    }

    [Fact]
    public void Server_layout_inline_svg_allowlist_is_exact_and_rejects_an_unmapped_third_icon()
    {
        const string path = "Legacy.Maliev.Intranet/Pages/Shared/_Layout.cshtml";
        var source = File.ReadAllText(Path.Combine(FindRepositoryRoot(), path.Replace('/', Path.DirectorySeparatorChar)));

        Assert.Equal(2, Regex.Matches(source, @"<svg\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant).Count);
        Assert.Equal(2, ApprovedInlineSvgs.Count(approved => approved.Path == path));
        Assert.All(ApprovedInlineSvgs.Where(approved => approved.Path == path), approved =>
        {
            Assert.False(string.IsNullOrWhiteSpace(approved.Purpose));
            Assert.Contains(approved.Markup, source, StringComparison.Ordinal);
        });
        Assert.Empty(FindIconInventoryViolations([new(path, source)]));

        var sourceWithUnmappedIcon = source.Replace(
            "</body>",
            "<svg viewBox=\"0 0 24 24\" aria-hidden=\"true\"><path d=\"M0 0\" /></svg></body>",
            StringComparison.Ordinal);
        Assert.NotEmpty(FindIconInventoryViolations([new(path, sourceWithUnmappedIcon)]));
    }

    [Fact]
    public void Production_inventory_maps_server_rendered_JS_package_and_dependency_boundaries()
    {
        var files = EnumerateProductionInventoryFiles(FindRepositoryRoot())
            .Select(file => file.Path)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        Assert.Contains("Legacy.Maliev.Intranet/Pages/Shared/_Layout.cshtml", files);
        Assert.Contains("Legacy.Maliev.Intranet/wwwroot/js/compat-shell.js", files);
        Assert.Contains("Maliev.ShadcnBlazor/wwwroot/css/shadcn-mudblazor.css", files);
        Assert.Contains("Directory.Build.props", files);
        Assert.Empty(FindUnclassifiedProductionFiles(EnumerateProductionFilePaths(FindRepositoryRoot())));
    }

    private static IReadOnlyList<InventoryFile> EnumerateProductionInventoryFiles(string root)
    {
        var productionFiles = EnumerateProductionFilePaths(root).ToArray();
        var unclassified = FindUnclassifiedProductionFiles(productionFiles);
        Assert.Empty(unclassified);

        return productionFiles
            .Select(path => new InventoryFile(
                path,
                IsScannedTextFile(path)
                    ? File.ReadAllText(Path.Combine(root, path.Replace('/', Path.DirectorySeparatorChar)))
                    : string.Empty,
                IsScannedTextFile(path)
                    ? null
                    : Sha256(File.ReadAllBytes(Path.Combine(root, path.Replace('/', Path.DirectorySeparatorChar))))))
            .ToArray();
    }

    private static IEnumerable<string> EnumerateProductionFilePaths(string root)
    {
        var productionRoots = Directory.EnumerateDirectories(root)
            .Where(path => Path.GetFileName(path) is "Legacy.Maliev.Intranet"
                or "Legacy.Maliev.Intranet.Client"
                or "Legacy.Maliev.Intranet.Client.Shared"
                or "Maliev.ShadcnBlazor"
                or "Maliev.ShadcnBlazor.Showcase"
                || Path.GetFileName(path).StartsWith("Legacy.Maliev.Intranet.Client.Features.", StringComparison.Ordinal))
            .ToArray();
        var sourceFiles = productionRoots
            .SelectMany(path => Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
            .Where(path => !IsGeneratedOrVendorPath(path));
        var dependencyFiles = Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
            .Where(path => !IsGeneratedOrVendorPath(path))
            .Where(path => DependencyManifestNames.Contains(Path.GetFileName(path)));

        return sourceFiles.Concat(dependencyFiles)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(path => Path.GetRelativePath(root, path).Replace('\\', '/'));
    }

    private static IReadOnlyList<string> FindUnclassifiedProductionFiles(IEnumerable<string> paths) => paths
        .Where(path => !IsScannedTextFile(path)
            && !ApprovedBinaryAssets.ContainsKey(path)
            && !ApprovedExtensionlessTextFiles.Contains(path))
        .ToArray();

    private static bool IsScannedTextFile(string path) =>
        ScannedTextExtensions.Contains(Path.GetExtension(path))
        || DependencyManifestNames.Contains(Path.GetFileName(path))
        || ApprovedExtensionlessTextFiles.Contains(path);

    private static bool IsGeneratedOrVendorPath(string path) =>
        path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase)
        || path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase)
        || path.Contains($"{Path.DirectorySeparatorChar}node_modules{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase)
        || path.Contains($"{Path.DirectorySeparatorChar}.git{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase)
        || path.Contains($"{Path.DirectorySeparatorChar}.superpowers{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase);

    private static IReadOnlyList<string> FindProductionIconInventoryViolations(IReadOnlyList<InventoryFile> files)
    {
        var graphViolations = FindApprovalGraphViolations(files);
        return graphViolations.Count > 0 ? graphViolations : FindIconInventoryViolations(files);
    }

    private static IReadOnlyList<string> FindIconInventoryViolations(IEnumerable<InventoryFile> files)
    {
        var violations = new List<string>();
        foreach (var file in files)
        {
            var isSvg = string.Equals(Path.GetExtension(file.Path), ".svg", StringComparison.OrdinalIgnoreCase);
            if ((isSvg && !IsApprovedSvgAsset(file))
                || (!isSvg && HasUnapprovedSvgReference(file))
                || (!isSvg && HasUnapprovedInlineOrDataSvg(file))
                || HasMudIconAlias(file.Source)
                || Regex.IsMatch(file.Source, @"Icons\.(?!Material\.)", RegexOptions.CultureInvariant)
                || Regex.IsMatch(file.Source, @"Font[ -]?Awesome|\bfa-[a-z]", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)
                || Regex.IsMatch(
                    file.Source,
                    "font-family\\s*:\\s*['\\\"]?(?:Material\\s+(?:Icons|Symbols)|Font\\s*Awesome)",
                    RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)
                || HasUnapprovedFontConstruct(file)
                || file.Source.Contains("fonts.googleapis.com", StringComparison.OrdinalIgnoreCase))
            {
                violations.Add(file.Path);
            }
        }

        return violations;
    }

    private static IReadOnlyList<string> FindApprovalGraphViolations(
        IEnumerable<InventoryFile> files,
        IReadOnlyList<ApprovedSvgReference>? references = null,
        IReadOnlyDictionary<string, ApprovedSvgAsset>? assets = null)
    {
        references ??= ApprovedSvgReferences;
        assets ??= ApprovedSvgAssets;
        var inventory = files.ToArray();
        var violations = new List<string>();
        var filesByPath = inventory
            .GroupBy(file => file.Path, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.OrdinalIgnoreCase);

        foreach (var file in inventory)
        {
            if (!string.Equals(file.Path, NormalizeRelativePath(file.Path), StringComparison.Ordinal)
                || filesByPath[file.Path].Length != 1)
            {
                violations.Add(file.Path);
            }
        }

        foreach (var (path, approval) in assets)
        {
            if (!string.Equals(path, NormalizeRelativePath(path), StringComparison.Ordinal)
                || string.IsNullOrWhiteSpace(approval.Purpose)
                || !filesByPath.TryGetValue(path, out var approvedFiles)
                || approvedFiles.Length != 1
                || !IsApprovedSvgAsset(approvedFiles[0]))
            {
                violations.Add(path);
            }
        }

        foreach (var (path, approval) in ApprovedBinaryAssets)
        {
            if (!string.Equals(path, NormalizeRelativePath(path), StringComparison.Ordinal)
                || string.IsNullOrWhiteSpace(approval.Purpose)
                || !filesByPath.TryGetValue(path, out var approvedFiles)
                || approvedFiles.Length != 1
                || !string.Equals(approvedFiles[0].ContentSha256, approval.Sha256, StringComparison.Ordinal))
            {
                violations.Add(path);
            }
        }

        foreach (var (path, approval) in ApprovedTextVendorAssets)
        {
            if (!string.Equals(path, NormalizeRelativePath(path), StringComparison.Ordinal)
                || string.IsNullOrWhiteSpace(approval.Purpose)
                || !filesByPath.TryGetValue(path, out var approvedFiles)
                || approvedFiles.Length != 1
                || !string.Equals(Sha256(approvedFiles[0].Source), approval.Sha256, StringComparison.Ordinal))
            {
                violations.Add(path);
            }
        }

        foreach (var duplicate in references.GroupBy(
                     approval => $"{approval.SourcePath}\n{approval.Marker}",
                     StringComparer.OrdinalIgnoreCase).Where(group => group.Count() != 1))
        {
            violations.Add(duplicate.Key);
        }

        foreach (var duplicate in ApprovedInlineSvgs.GroupBy(
                     approval => $"{approval.Path}\n{approval.Markup}",
                     StringComparer.OrdinalIgnoreCase).Where(group => group.Count() != 1))
        {
            violations.Add(duplicate.Key);
        }

        foreach (var approval in ApprovedInlineSvgs)
        {
            if (!string.Equals(approval.Path, NormalizeRelativePath(approval.Path), StringComparison.Ordinal)
                || string.IsNullOrWhiteSpace(approval.Purpose)
                || !filesByPath.TryGetValue(approval.Path, out var sourceFiles)
                || sourceFiles.Length != 1
                || CountOccurrences(sourceFiles[0].Source, approval.Markup) != 1)
            {
                violations.Add(approval.Path);
            }
        }

        foreach (var approval in references)
        {
            var target = ResolveReferenceTarget(approval);
            if (!string.Equals(approval.SourcePath, NormalizeRelativePath(approval.SourcePath), StringComparison.Ordinal)
                || string.IsNullOrWhiteSpace(approval.Purpose)
                || !filesByPath.TryGetValue(approval.SourcePath, out var sourceFiles)
                || sourceFiles.Length != 1
                || CountOccurrences(sourceFiles[0].Source, approval.Marker) != 1
                || target is null
                || !assets.ContainsKey(target)
                || !filesByPath.TryGetValue(target, out var targetFiles)
                || targetFiles.Length != 1
                || !IsApprovedSvgAsset(targetFiles[0]))
            {
                violations.Add($"{approval.SourcePath} -> {approval.Target}");
            }
        }

        return violations.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static string? ResolveReferenceTarget(ApprovedSvgReference approval)
    {
        var source = NormalizeRelativePath(approval.SourcePath);
        if (source is null)
        {
            return null;
        }

        var project = source.Split('/')[0];
        var sourceDirectory = source.Contains("/wwwroot/", StringComparison.OrdinalIgnoreCase)
            ? source[..source.LastIndexOf('/')]
            : $"{project}/wwwroot";
        var target = approval.Target.StartsWith('/')
            ? $"{project}/wwwroot/{approval.Target.TrimStart('/')}"
            : $"{sourceDirectory}/{approval.Target}";
        return NormalizeRelativePath(target);
    }

    private static string? NormalizeRelativePath(string path)
    {
        var segments = new List<string>();
        foreach (var segment in path.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            if (segment == ".")
            {
                continue;
            }

            if (segment == "..")
            {
                if (segments.Count == 0)
                {
                    return null;
                }

                segments.RemoveAt(segments.Count - 1);
                continue;
            }

            segments.Add(segment);
        }

        return string.Join('/', segments);
    }

    private static bool HasUnapprovedFontConstruct(InventoryFile file)
    {
        if (file.Source.Contains("data:font/", StringComparison.OrdinalIgnoreCase)
            || HasPrivateUseGlyph(file.Source))
        {
            return true;
        }

        if (!file.Source.Contains("@font-face", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return !ApprovedTextVendorAssets.TryGetValue(file.Path, out var approved)
            || string.IsNullOrWhiteSpace(approved.Purpose)
            || !string.Equals(Sha256(file.Source), approved.Sha256, StringComparison.Ordinal);
    }

    private static bool HasPrivateUseGlyph(string source)
    {
        if (source.EnumerateRunes().Any(rune => rune.Value is >= 0xE000 and <= 0xF8FF
                or >= 0xF0000 and <= 0xFFFFD
                or >= 0x100000 and <= 0x10FFFD))
        {
            return true;
        }

        return Regex.Matches(source, @"\\(?<hex>[0-9a-f]{1,6})\s?", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)
            .Select(match => match.Groups["hex"].Value)
            .Any(value => int.TryParse(value, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var codePoint)
                && codePoint is >= 0xE000 and <= 0xF8FF
                    or >= 0xF0000 and <= 0xFFFFD
                    or >= 0x100000 and <= 0x10FFFD);
    }

    private static bool HasUnapprovedInlineOrDataSvg(InventoryFile file)
    {
        var sourceWithoutApprovals = file.Source;
        foreach (var approved in ApprovedInlineSvgs.Where(approved =>
                     string.Equals(approved.Path, file.Path, StringComparison.OrdinalIgnoreCase)))
        {
            if (CountOccurrences(sourceWithoutApprovals, approved.Markup) != 1)
            {
                return true;
            }

            sourceWithoutApprovals = sourceWithoutApprovals.Replace(approved.Markup, string.Empty, StringComparison.Ordinal);
        }

        foreach (Match match in Regex.Matches(
                     sourceWithoutApprovals,
                     @"<svg\b[^>]*>.*?</svg>",
                     RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.CultureInvariant))
        {
            var approved = ApprovedInlineSvgs.SingleOrDefault(candidate =>
                string.Equals(candidate.Path, file.Path, StringComparison.OrdinalIgnoreCase)
                && string.Equals(candidate.Markup, match.Value, StringComparison.Ordinal));
            if (approved is null || string.IsNullOrWhiteSpace(approved.Purpose))
            {
                return true;
            }

            sourceWithoutApprovals = sourceWithoutApprovals.Replace(match.Value, string.Empty, StringComparison.Ordinal);
        }

        return sourceWithoutApprovals.Contains("data:image/svg+xml", StringComparison.OrdinalIgnoreCase)
            || Regex.IsMatch(sourceWithoutApprovals, @"<svg\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }

    private static bool HasMudIconAlias(string source) =>
        Regex.IsMatch(
            source,
            @"\b(?:global\s+)?using\s+(?:static\s+)?[A-Za-z_][A-Za-z0-9_]*\s*=\s*(?:global::)?MudBlazor\.Icons(?:\.|\s*;)",
            RegexOptions.CultureInvariant)
        || Regex.IsMatch(
            source,
            @"\b(?:global\s+)?using\s+static\s+(?:global::)?MudBlazor\.Icons(?:\.|\s*;)",
            RegexOptions.CultureInvariant);

    private static int CountOccurrences(string source, string value)
    {
        var count = 0;
        var offset = 0;
        while ((offset = source.IndexOf(value, offset, StringComparison.Ordinal)) >= 0)
        {
            count++;
            offset += value.Length;
        }

        return count;
    }

    private static bool IsApprovedSvgAsset(InventoryFile file) =>
        ApprovedSvgAssets.TryGetValue(file.Path, out var approved)
        && !string.IsNullOrWhiteSpace(approved.Purpose)
        && string.Equals(approved.Sha256, Sha256(file.Source), StringComparison.Ordinal);

    private static bool HasUnapprovedSvgReference(InventoryFile file)
    {
        var sourceWithoutApprovals = file.Source;
        foreach (var approved in ApprovedSvgReferences.Where(approved =>
                     string.Equals(approved.SourcePath, file.Path, StringComparison.OrdinalIgnoreCase)))
        {
            if (string.IsNullOrWhiteSpace(approved.Purpose)
                || !approved.Marker.Contains(approved.Target, StringComparison.OrdinalIgnoreCase)
                || CountOccurrences(sourceWithoutApprovals, approved.Marker) != 1)
            {
                return true;
            }

            sourceWithoutApprovals = sourceWithoutApprovals.Replace(approved.Marker, string.Empty, StringComparison.Ordinal);
        }

        return Regex.IsMatch(
                sourceWithoutApprovals,
                @"(?<path>/?(?:[A-Za-z0-9_.-]+/)*[A-Za-z0-9_.-]+\.svg)(?:[?#][^\""'\s)]*)?",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }

    private static string Sha256(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    private static string Sha256(byte[] value) => Convert.ToHexString(SHA256.HashData(value));

    private IRenderedComponent<IComponent> RenderDynamicComponent(Type componentType, Array items) => Render(builder =>
    {
        builder.OpenComponent(0, componentType);
        builder.AddAttribute(1, "Items", items);
        builder.CloseComponent();
    });

    private static Array CreateItems(Type itemType, params (string Label, string? Href)[] values)
    {
        var items = Array.CreateInstance(itemType, values.Length);
        for (var index = 0; index < values.Length; index++)
        {
            items.SetValue(Activator.CreateInstance(itemType, values[index].Label, values[index].Href), index);
        }

        return items;
    }

    private static Dictionary<string, string> ReadResources(string path) =>
        XDocument.Load(path).Root!.Elements("data").ToDictionary(
            element => (string)element.Attribute("name")!,
            element => element.Element("value")!.Value,
            StringComparer.Ordinal);

    private static string ComponentPath(string name) => Path.Combine(
        FindRepositoryRoot(), "Legacy.Maliev.Intranet.Client.Shared", "Components", name);

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Legacy.Maliev.Intranet.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new DirectoryNotFoundException("Repository root was not found.");
    }

    private sealed record InventoryFile(string Path, string Source, string? ContentSha256 = null);
    private sealed record ApprovedInlineSvg(string Path, string Markup, string Purpose);
    private sealed record ApprovedSvgAsset(string Sha256, string Purpose);
    private sealed record ApprovedBinaryAsset(string Sha256, string Purpose);
    private sealed record ApprovedTextVendorAsset(string Sha256, string Purpose);
    private sealed record ApprovedSvgReference(string SourcePath, string Target, string Marker, string Purpose);
}
