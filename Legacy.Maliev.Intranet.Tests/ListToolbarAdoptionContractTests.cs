using System.Xml.Linq;

namespace Legacy.Maliev.Intranet.Tests;

public sealed class ListToolbarAdoptionContractTests
{
    public static TheoryData<string, string> ListPages => new()
    {
        { "Legacy.Maliev.Intranet.Client.Features.Customers", "Pages/Customers.razor" },
        { "Legacy.Maliev.Intranet.Client.Features.Employees", "Pages/Employees.razor" },
        { "Legacy.Maliev.Intranet.Client.Features.Orders", "Pages/Orders.razor" },
        { "Legacy.Maliev.Intranet.Client.Features.Catalog", "Pages/Materials.razor" },
        { "Legacy.Maliev.Intranet.Client.Features.Procurement", "Pages/Suppliers.razor" },
        { "Legacy.Maliev.Intranet.Client.Features.Procurement", "Pages/PurchaseOrders.razor" },
        { "Legacy.Maliev.Intranet.Client.Features.Quotations", "Pages/QuotationRequests/Index.razor" },
        { "Legacy.Maliev.Intranet.Client.Features.Quotations", "Pages/Quotations/Index.razor" },
        { "Legacy.Maliev.Intranet.Client.Features.Accounting", "Pages/Invoices.razor" },
        { "Legacy.Maliev.Intranet.Client.Features.Accounting", "Pages/Finances.razor" },
        { "Legacy.Maliev.Intranet.Client.Features.Diagnostics", "Pages/ErrorReport.razor" },
    };

    [Theory]
    [MemberData(nameof(ListPages))]
    public void Every_list_page_uses_the_shared_automatic_toolbar_and_latest_request_gate(
        string project,
        string relativePage)
    {
        var source = File.ReadAllText(Path.Combine(FindRepositoryRoot(), project, relativePage));

        Assert.Contains("<ListToolbar", source, StringComparison.Ordinal);
        Assert.Contains("ListToolbarRequest", source, StringComparison.Ordinal);
        Assert.Contains("LatestRequestGate", source, StringComparison.Ordinal);
        Assert.Contains("lease.CancellationToken", source, StringComparison.Ordinal);
        Assert.Contains("replace: true", source, StringComparison.Ordinal);
        Assert.DoesNotContain("ApplyFiltersAsync", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Text[\"Apply\"]", source, StringComparison.Ordinal);
    }

    [Theory]
    [MemberData(nameof(ListPages))]
    public void Every_list_page_feature_references_the_shared_ui_project(
        string project,
        string relativePage)
    {
        _ = relativePage;
        var projectFile = Path.Combine(FindRepositoryRoot(), project, $"{project}.csproj");
        var source = File.ReadAllText(projectFile);

        Assert.Contains("Legacy.Maliev.Intranet.Client.Shared", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Shared_toolbar_localization_is_complete_and_natural_in_English_and_Thai()
    {
        var folder = Path.Combine(
            FindRepositoryRoot(),
            "Legacy.Maliev.Intranet.Client.Shared",
            "Components");
        var english = ReadResources(Path.Combine(folder, "ListToolbarResources.resx"));
        var thai = ReadResources(Path.Combine(folder, "ListToolbarResources.th.resx"));

        Assert.Equal(
            new[] { "Clear", "FilterLabel", "Filtering", "PerPage", "Refresh", "Search", "Sort" },
            english.Keys.Order(StringComparer.Ordinal));
        Assert.Equal(english.Keys.Order(StringComparer.Ordinal), thai.Keys.Order(StringComparer.Ordinal));
        Assert.Equal("เรียงตาม", thai["Sort"]);
        Assert.Equal("จำนวนต่อหน้า", thai["PerPage"]);
        Assert.Equal("ล้างตัวกรอง", thai["Clear"]);
        Assert.Equal("รีเฟรชข้อมูล", thai["Refresh"]);
    }

    private static Dictionary<string, string> ReadResources(string path) =>
        XDocument.Load(path)
            .Root!
            .Elements("data")
            .ToDictionary(
                element => (string)element.Attribute("name")!,
                element => element.Element("value")!.Value,
                StringComparer.Ordinal);

    private static string FindRepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (Directory.Exists(Path.Combine(current.FullName, "Legacy.Maliev.Intranet.Contracts")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate the Legacy.Maliev.Intranet repository root.");
    }
}
