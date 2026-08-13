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
        var productionFiles = Directory.EnumerateDirectories(root, "Legacy.Maliev.Intranet.Client*")
            .Where(path => !path.EndsWith(".Tests", StringComparison.OrdinalIgnoreCase))
            .SelectMany(path => Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            .Where(path => new[] { ".razor", ".cs", ".csproj", ".html", ".css" }.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase))
            .ToArray();

        var violations = new List<string>();
        foreach (var path in productionFiles)
        {
            var source = File.ReadAllText(path);
            if (Regex.IsMatch(source, @"Icons\.(?!Material\.)", RegexOptions.CultureInvariant)
                || Regex.IsMatch(source, @"Font[ -]?Awesome|\bfa-[a-z]", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)
                || Regex.IsMatch(source, @"fonts\.googleapis\.com[^\""']*(Material|icon)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
            {
                violations.Add(Path.GetRelativePath(root, path));
            }
        }

        Assert.Empty(violations);
    }

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
}
