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
        var productionRoots = Directory.EnumerateDirectories(root)
            .Where(path => Path.GetFileName(path) is "Legacy.Maliev.Intranet.Client"
                or "Legacy.Maliev.Intranet.Client.Shared"
                or "Maliev.ShadcnBlazor"
                or "Maliev.ShadcnBlazor.Showcase"
                || Path.GetFileName(path).StartsWith("Legacy.Maliev.Intranet.Client.Features.", StringComparison.Ordinal))
            .ToArray();
        var productionFiles = productionRoots
            .SelectMany(path => Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            .Where(path => new[] { ".razor", ".cs", ".csproj", ".html", ".css", ".svg" }.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase))
            .ToArray();

        var files = productionFiles.Select(path => new InventoryFile(
            Path.GetRelativePath(root, path).Replace('\\', '/'),
            File.ReadAllText(path)));
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

    private static IReadOnlyList<string> FindIconInventoryViolations(IEnumerable<InventoryFile> files)
    {
        var violations = new List<string>();
        foreach (var file in files)
        {
            var isSvg = string.Equals(Path.GetExtension(file.Path), ".svg", StringComparison.OrdinalIgnoreCase);
            if ((isSvg && !ApprovedSvgAssets.ContainsKey(file.Path))
                || (!isSvg && HasUnapprovedSvgReference(file.Source))
                || Regex.IsMatch(file.Source, @"Icons\.(?!Material\.)", RegexOptions.CultureInvariant)
                || Regex.IsMatch(file.Source, @"Font[ -]?Awesome|\bfa-[a-z]", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)
                || file.Source.Contains("fonts.googleapis.com", StringComparison.OrdinalIgnoreCase))
            {
                violations.Add(file.Path);
            }
        }

        return violations;
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
}
