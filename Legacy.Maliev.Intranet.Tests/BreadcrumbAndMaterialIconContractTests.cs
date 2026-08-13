using System.Reflection;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;

namespace Legacy.Maliev.Intranet.Tests;

public sealed class BreadcrumbAndMaterialIconContractTests : BunitContext
{
    private const string ComponentNamespace = "Legacy.Maliev.Intranet.Client.Shared.Components";
    private static readonly IReadOnlyDictionary<string, string> ApprovedSvgAssets =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Legacy.Maliev.Intranet.Client/wwwroot/images/favicon.svg"] = "MALIEV brand favicon",
            ["Legacy.Maliev.Intranet.Client/wwwroot/images/MALIEV_BLACK.svg"] = "MALIEV dark wordmark",
            ["Legacy.Maliev.Intranet.Client/wwwroot/images/MALIEV_WHITE.svg"] = "MALIEV light wordmark",
        };
    private static readonly IReadOnlySet<string> ApprovedSvgReferences = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "images/favicon.svg",
        "images/MALIEV_BLACK.svg",
        "images/MALIEV_WHITE.svg",
    };
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
            "<svg class=\"loading-progress\" aria-hidden=\"true\" focusable=\"false\">",
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
        var violations = FindIconInventoryViolations(files);

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
        Assert.Empty(FindIconInventoryViolations([
            new("Legacy.Maliev.Intranet.Client/wwwroot/images/favicon.svg", "<svg />"),
            new("Legacy.Maliev.Intranet.Client/wwwroot/images/MALIEV_BLACK.svg", "<svg />"),
            new("Legacy.Maliev.Intranet.Client/wwwroot/images/MALIEV_WHITE.svg", "<svg />")
        ]));
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
    }

    private static IReadOnlyList<InventoryFile> EnumerateProductionInventoryFiles(string root)
    {
        var productionRoots = Directory.EnumerateDirectories(root)
            .Where(path => Path.GetFileName(path) is "Legacy.Maliev.Intranet"
                or "Legacy.Maliev.Intranet.Client"
                or "Legacy.Maliev.Intranet.Client.Shared"
                or "Maliev.ShadcnBlazor"
                or "Maliev.ShadcnBlazor.Showcase"
                || Path.GetFileName(path).StartsWith("Legacy.Maliev.Intranet.Client.Features.", StringComparison.Ordinal))
            .ToArray();
        var sourceExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".cshtml", ".razor", ".cs", ".js", ".css", ".html", ".csproj", ".svg",
        };
        var sourceFiles = productionRoots
            .SelectMany(path => Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
            .Where(path => !IsGeneratedOrTaskPath(path))
            .Where(path => sourceExtensions.Contains(Path.GetExtension(path)));
        var dependencyFiles = Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
            .Where(path => !IsGeneratedOrTaskPath(path))
            .Where(path => DependencyManifestNames.Contains(Path.GetFileName(path)));

        return sourceFiles.Concat(dependencyFiles)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(path => new InventoryFile(
                Path.GetRelativePath(root, path).Replace('\\', '/'),
                File.ReadAllText(path)))
            .ToArray();
    }

    private static bool IsGeneratedOrTaskPath(string path) =>
        path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase)
        || path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase)
        || path.Contains($"{Path.DirectorySeparatorChar}.git{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase)
        || path.Contains($"{Path.DirectorySeparatorChar}.superpowers{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase);

    private static IReadOnlyList<string> FindIconInventoryViolations(IEnumerable<InventoryFile> files)
    {
        var violations = new List<string>();
        foreach (var file in files)
        {
            var isSvg = string.Equals(Path.GetExtension(file.Path), ".svg", StringComparison.OrdinalIgnoreCase);
            if ((isSvg && !ApprovedSvgAssets.ContainsKey(file.Path))
                || (!isSvg && HasUnapprovedSvgReference(file.Source))
                || (!isSvg && HasUnapprovedInlineOrDataSvg(file))
                || HasMudIconAlias(file.Source)
                || Regex.IsMatch(file.Source, @"Icons\.(?!Material\.)", RegexOptions.CultureInvariant)
                || Regex.IsMatch(file.Source, @"Font[ -]?Awesome|\bfa-[a-z]", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)
                || file.Source.Contains("fonts.googleapis.com", StringComparison.OrdinalIgnoreCase))
            {
                violations.Add(file.Path);
            }
        }

        return violations;
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

    private static bool HasUnapprovedSvgReference(string source) =>
        Regex.Matches(
                source,
                @"(?<path>/?(?:[A-Za-z0-9_.-]+/)*[A-Za-z0-9_.-]+\.svg)(?:[?#][^\""'\s)]*)?",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)
            .Cast<Match>()
            .Select(match => match.Groups["path"].Value.TrimStart('/'))
            .Any(path => !ApprovedSvgReferences.Contains(path));

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

    private sealed record InventoryFile(string Path, string Source);
    private sealed record ApprovedInlineSvg(string Path, string Markup, string Purpose);
}
