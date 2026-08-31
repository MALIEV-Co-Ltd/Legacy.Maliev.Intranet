using System.Xml.Linq;
using System.Text.RegularExpressions;

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
    public void Every_list_page_uses_the_released_data_table_request_contract_and_latest_request_gate(
        string project,
        string relativePage)
    {
        var source = File.ReadAllText(Path.Combine(FindRepositoryRoot(), project, relativePage));

        Assert.Contains("<OperationalDataTable", source, StringComparison.Ordinal);
        Assert.Contains("ShadcnDataTableRequest", source, StringComparison.Ordinal);
        Assert.Contains("LatestRequestGate", source, StringComparison.Ordinal);
        Assert.Contains("lease.CancellationToken", source, StringComparison.Ordinal);
        Assert.True(
            source.Contains("replace: true", StringComparison.Ordinal) ||
            source.Contains("malievNavigation.replaceCurrentUrl", StringComparison.Ordinal),
            "List pages must keep their URL state synchronized without adding a history entry.");
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

    [Fact]
    public void Shared_toolbar_exposes_one_localized_icon_only_refresh_action()
    {
        var folder = Path.Combine(FindRepositoryRoot(), "Legacy.Maliev.Intranet.Client.Shared", "Components");
        var markup = File.ReadAllText(Path.Combine(folder, "ListToolbar.razor"));
        var styles = File.ReadAllText(Path.Combine(folder, "ListToolbar.razor.css"));
        var refresh = Regex.Match(
            markup,
            @"<ShadcnButton\b(?=[^>]*\bClass=\""list-toolbar__refresh\"")[^>]*>(?<body>.*?)</ShadcnButton>",
            RegexOptions.Singleline | RegexOptions.CultureInvariant);

        Assert.True(refresh.Success, "Refresh must render as one native Shadcn icon button.");
        Assert.Contains("Variant=\"ShadcnButtonVariant.Ghost\"", refresh.Value, StringComparison.Ordinal);
        Assert.Contains("Size=\"ShadcnButtonSize.Icon\"", refresh.Value, StringComparison.Ordinal);
        Assert.Contains("<ShadcnIcon Icon=\"RefreshIcon\"", refresh.Groups["body"].Value, StringComparison.Ordinal);
        Assert.Contains("aria-label=\"@Text[\"Refresh\"]\"", refresh.Value, StringComparison.Ordinal);
        Assert.Contains("title=\"@Text[\"Refresh\"]\"", refresh.Value, StringComparison.Ordinal);
        Assert.DoesNotContain("<MudIconButton", markup, StringComparison.Ordinal);
        Assert.DoesNotContain(">@Text[\"Refresh\"]<", markup, StringComparison.Ordinal);
        Assert.Contains(".list-toolbar__refresh", styles, StringComparison.Ordinal);
        Assert.Contains("min-width: 2.25rem", styles, StringComparison.Ordinal);
        Assert.Contains("min-height: 2.25rem", styles, StringComparison.Ordinal);
        Assert.Contains("min-width: 2.75rem", styles, StringComparison.Ordinal);
        Assert.Contains("min-height: 2.75rem", styles, StringComparison.Ordinal);
        Assert.Contains("@media (pointer: coarse)", styles, StringComparison.Ordinal);
        Assert.Contains("button.list-toolbar__refresh svg", styles, StringComparison.Ordinal);
        Assert.Contains("width: 1.25rem", styles, StringComparison.Ordinal);
        Assert.Contains("height: 1.25rem", styles, StringComparison.Ordinal);
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
