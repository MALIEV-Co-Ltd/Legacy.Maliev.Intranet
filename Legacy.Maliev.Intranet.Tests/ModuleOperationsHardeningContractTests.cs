using System.Xml.Linq;

namespace Legacy.Maliev.Intranet.Tests;

public sealed class ModuleOperationsHardeningContractTests
{
    private static readonly string[] OwnedProjects =
    [
        "Legacy.Maliev.Intranet.Client.Features.Catalog",
        "Legacy.Maliev.Intranet.Client.Features.Customers",
        "Legacy.Maliev.Intranet.Client.Features.Employees",
        "Legacy.Maliev.Intranet.Client.Features.Procurement",
        "Legacy.Maliev.Intranet.Client.Features.Orders",
        "Legacy.Maliev.Intranet.Client.Features.Diagnostics",
    ];

    [Fact]
    public void RoutedPages_DoNotUseEyebrowOrOverlineHeadings()
    {
        var root = FindRoot();
        var pages = OwnedProjects
            .SelectMany(project => Directory.GetFiles(Path.Combine(root, project), "*.razor", SearchOption.AllDirectories))
            .Where(path => File.ReadAllText(path).Contains("@page ", StringComparison.Ordinal))
            .ToArray();

        Assert.NotEmpty(pages);
        Assert.All(pages, path => Assert.DoesNotContain("Typo.overline", File.ReadAllText(path), StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("Legacy.Maliev.Intranet.Client.Features.Catalog", "Pages", "Materials.razor", "MaterialsTable")]
    [InlineData("Legacy.Maliev.Intranet.Client.Features.Customers", "Pages", "Customers.razor", "CustomersTable")]
    [InlineData("Legacy.Maliev.Intranet.Client.Features.Employees", "Pages", "Employees.razor", "EmployeesTable")]
    [InlineData("Legacy.Maliev.Intranet.Client.Features.Procurement", "Pages", "Suppliers.razor", "SuppliersTable")]
    [InlineData("Legacy.Maliev.Intranet.Client.Features.Procurement", "Pages", "PurchaseOrders.razor", "PurchaseOrdersTable")]
    [InlineData("Legacy.Maliev.Intranet.Client.Features.Diagnostics", "Pages", "ErrorReport.razor", "DiagnosticsTable")]
    public void OperationalTables_HaveLocalizedAccessibleNames(string project, string folder, string file, string resourceKey)
    {
        var root = FindRoot();
        var page = Read(root, project, folder, file);
        Assert.Contains($"aria-label=\"@Text[\"{resourceKey}\"]\"", page, StringComparison.Ordinal);

        var resourceStem = Path.GetFileNameWithoutExtension(file);
        AssertResourcePairContains(root, project, folder, resourceStem, resourceKey);
    }

    [Fact]
    public void MaterialsPagination_IsSemanticAndAnnouncesChanges()
    {
        var source = Read(FindRoot(), "Legacy.Maliev.Intranet.Client.Features.Catalog", "Pages", "Materials.razor");
        Assert.Contains("<nav class=", source, StringComparison.Ordinal);
        Assert.Contains("aria-label=\"@Text[\"MaterialPages\"]\"", source, StringComparison.Ordinal);
        Assert.Contains("aria-live=\"polite\">@Text[\"PageSummary\"", source, StringComparison.Ordinal);
    }

    [Fact]
    public void ExternalOrderDocuments_AreIsolatedFromTheIntranetWindow()
    {
        var source = Read(FindRoot(), "Legacy.Maliev.Intranet.Client.Features.Orders", "Pages", "OrderDetail.razor");
        Assert.DoesNotContain("Target=\"_blank\" Variant=", source, StringComparison.Ordinal);
        var externalLinks = Count(source, "Target=\"_blank\"");
        Assert.True(externalLinks > 0);
        Assert.Equal(externalLinks, Count(source, "rel=\"noopener\"") + Count(source, "Rel=\"noopener\""));
    }

    [Fact]
    public void Orders_RetainsItsModuleHeaderAndNamesEveryOperationalTable()
    {
        var source = Read(FindRoot(), "Legacy.Maliev.Intranet.Client.Features.Orders", "Pages", "Orders.razor");
        Assert.Contains("<ModuleHeader", source, StringComparison.Ordinal);
        Assert.Contains("TableLabel=\"@section.Title\"", source, StringComparison.Ordinal);
        Assert.Contains("operations-status-pill", Read(FindRoot(), "Legacy.Maliev.Intranet.Client.Features.Orders", "Pages", "OrderDetail.razor"), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("SupplierView.razor")]
    [InlineData("PurchaseOrderView.razor")]
    public void DestructiveProcurementActions_RequireFocusManagedConfirmation(string file)
    {
        var source = Read(FindRoot(), "Legacy.Maliev.Intranet.Client.Features.Procurement", "Pages", file);
        Assert.Contains("@inject IDialogService Dialogs", source, StringComparison.Ordinal);
        Assert.Contains("Dialogs.ShowMessageBoxAsync", source, StringComparison.Ordinal);
        Assert.Contains("cancelText: Text[\"Cancel\"]", source, StringComparison.Ordinal);
        Assert.Contains("if (confirmed is true)", source, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Legacy.Maliev.Intranet.Client.Features.Customers", "CustomerCreate.razor")]
    [InlineData("Legacy.Maliev.Intranet.Client.Features.Employees", "EmployeeCreate.razor")]
    [InlineData("Legacy.Maliev.Intranet.Client.Features.Procurement", "SupplierCreate.razor")]
    [InlineData("Legacy.Maliev.Intranet.Client.Features.Orders", "OrderCreate.razor")]
    [InlineData("Legacy.Maliev.Intranet.Client.Features.Orders", "OrderDetail.razor")]
    public void CriticalForms_UsePageHeadersAndLabeledResponsiveSections(string project, string file)
    {
        var source = Read(FindRoot(), project, "Pages", file);
        Assert.Contains("operations-page-header", source, StringComparison.Ordinal);
        Assert.Contains("HtmlTag=\"h2\"", source, StringComparison.Ordinal);
        if (file == "OrderDetail.razor")
        {
            Assert.Contains("<MudExpansionPanels", source, StringComparison.Ordinal);
            Assert.Contains("Text=\"@Text[", source, StringComparison.Ordinal);
        }
        else
        {
            Assert.Contains("aria-labelledby=", source, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void OwnedEnglishAndThaiResources_HaveMatchingKeys()
    {
        var root = FindRoot();
        foreach (var project in OwnedProjects)
        {
            foreach (var english in Directory.GetFiles(Path.Combine(root, project), "*.resx", SearchOption.AllDirectories)
                         .Where(path => !path.EndsWith(".th.resx", StringComparison.OrdinalIgnoreCase)))
            {
                var thai = Path.Combine(
                    Path.GetDirectoryName(english)!,
                    $"{Path.GetFileNameWithoutExtension(english)}.th.resx");
                Assert.True(File.Exists(thai), $"Missing Thai resource pair for {Path.GetRelativePath(root, english)}.");
                Assert.Equal(ReadKeys(english), ReadKeys(thai));
            }
        }
    }

    private static void AssertResourcePairContains(string root, string project, string folder, string stem, string key)
    {
        Assert.Contains(key, ReadKeys(Path.Combine(root, project, folder, $"{stem}.resx")));
        Assert.Contains(key, ReadKeys(Path.Combine(root, project, folder, $"{stem}.th.resx")));
    }

    private static string[] ReadKeys(string path) => XDocument.Load(path)
        .Root!
        .Elements("data")
        .Select(element => (string?)element.Attribute("name"))
        .Where(name => !string.IsNullOrWhiteSpace(name))
        .Cast<string>()
        .Order(StringComparer.Ordinal)
        .ToArray();

    private static int Count(string source, string value) =>
        (source.Length - source.Replace(value, string.Empty, StringComparison.Ordinal).Length) / value.Length;

    private static string Read(string root, params string[] segments) => File.ReadAllText(Path.Combine([root, .. segments]));

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
