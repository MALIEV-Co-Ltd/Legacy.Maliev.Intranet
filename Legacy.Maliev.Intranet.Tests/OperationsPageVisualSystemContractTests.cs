namespace Legacy.Maliev.Intranet.Tests;

public sealed class OperationsPageVisualSystemContractTests
{
    [Fact]
    public void OperationsStylesFollowThePackageAdapterAndRemainGeometryOnly()
    {
        var index = Read("Legacy.Maliev.Intranet.Client", "wwwroot", "index.html");
        var operations = Read("Legacy.Maliev.Intranet.Client", "wwwroot", "css", "operations-pages.css");

        Assert.True(index.IndexOf("_content/Maliev.ShadcnBlazor/css/shadcn-mudblazor.css", StringComparison.Ordinal) <
                    index.IndexOf("css/operations-pages.css", StringComparison.Ordinal));
        Assert.Contains("overflow-wrap: anywhere", operations, StringComparison.Ordinal);
        Assert.Contains("@media (max-width: 900px)", operations, StringComparison.Ordinal);
        Assert.Contains("@media (max-width: 720px)", operations, StringComparison.Ordinal);
        Assert.Contains("@media (pointer: coarse)", operations, StringComparison.Ordinal);
        Assert.DoesNotContain("background:", operations, StringComparison.Ordinal);
        Assert.DoesNotContain("border:", operations, StringComparison.Ordinal);
        Assert.DoesNotContain("font-family:", operations, StringComparison.Ordinal);
    }

    [Fact]
    public void OperationsGeometryPreservesListFormAndResponsiveRecordLayout()
    {
        var operations = Read("Legacy.Maliev.Intranet.Client", "wwwroot", "css", "operations-pages.css");

        Assert.Contains(".legacy-page-container .mud-table-container", operations, StringComparison.Ordinal);
        Assert.Contains(".legacy-page-container .mud-table-body .mud-table-row", operations, StringComparison.Ordinal);
        Assert.Contains(".legacy-page-container .mud-form", operations, StringComparison.Ordinal);
        Assert.Contains(".legacy-page-container .mud-grid", operations, StringComparison.Ordinal);
        Assert.Contains(".legacy-page-container .mud-tabs-toolbar", operations, StringComparison.Ordinal);
        Assert.DoesNotContain(".mud-button-root {\n        display: none", operations, StringComparison.Ordinal);
        Assert.DoesNotContain(".mud-table-body {\n        display: none", operations, StringComparison.Ordinal);
    }

    [Fact]
    public void DashboardCompactTablesRemainSemanticContainedAndCompleteAtNarrowWidths()
    {
        var page = Read("Legacy.Maliev.Intranet.Client", "Pages", "Dashboard.razor");
        var styles = Read("Legacy.Maliev.Intranet.Client", "Pages", "Dashboard.razor.css");

        Assert.Equal(4, System.Text.RegularExpressions.Regex.Matches(page, "<MudSimpleTable").Count);
        Assert.True(System.Text.RegularExpressions.Regex.Matches(page, "<LegacyLink Href=\"@").Count >= 4);
        Assert.Contains("dashboard-table-scroll { overflow-x: auto; }", styles, StringComparison.Ordinal);
        Assert.DoesNotContain(".dashboard-table-scroll { overflow-x: visible; }", styles, StringComparison.Ordinal);
        Assert.DoesNotContain(".dashboard-table thead { display: none; }", styles, StringComparison.Ordinal);
        Assert.DoesNotContain("content: attr(data-label)", styles, StringComparison.Ordinal);
        Assert.Contains("<PageBreadcrumbs Items=\"@Breadcrumbs\"", page, StringComparison.Ordinal);
        Assert.Contains("new(Text[\"Dashboard\"])", page, StringComparison.Ordinal);
        Assert.DoesNotContain("<QuickViewContent", page, StringComparison.Ordinal);
        foreach (var field in new[] { "Order", "Part", "Quantity", "Remaining", "Promised", "Progress", "Quote", "Total", "Expires", "Decision", "Payment", "Recipient", "Amount", "Date", "Customer", "Company", "Email" })
        {
            Assert.Contains($"Text[\"{field}\"]", page, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void SpecializedCustomerHistoryRemainsSemanticContainedAndCompleteWithoutQuickView()
    {
        var page = Read("Legacy.Maliev.Intranet.Client.Features.Customers", "Components", "CustomerHistoryTable.razor");
        var styles = Read("Legacy.Maliev.Intranet.Client.Features.Customers", "Components", "CustomerHistoryTable.razor.css");

        Assert.Equal(3, System.Text.RegularExpressions.Regex.Matches(page, "<MudTable").Count);
        Assert.Equal(3, System.Text.RegularExpressions.Regex.Matches(page, "Breakpoint=\"Breakpoint.None\"").Count);
        Assert.DoesNotContain("Breakpoint=\"Breakpoint.Sm\"", page, StringComparison.Ordinal);
        Assert.Contains("overflow-x: auto", styles, StringComparison.Ordinal);
        Assert.Contains("min-height: 44px", styles, StringComparison.Ordinal);
        Assert.DoesNotContain("<QuickViewContent", page, StringComparison.Ordinal);
        Assert.DoesNotContain("operational-table__toggle", page, StringComparison.Ordinal);
        foreach (var route in new[] { "/Orders/View?id=", "/Quotations/View?id=", "/Invoices/View?id=" })
        {
            Assert.Contains(route, page, StringComparison.Ordinal);
        }
        foreach (var field in new[] { "Record", "Name", "Status", "PromisedDate", "ExpirationDate", "Period", "Total", "CreatedDate" })
        {
            Assert.Contains($"Localize(\"{field}\")", page, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void DashboardAndCustomerDetailDeclareExactLocalizedBreadcrumbHierarchy()
    {
        var dashboard = Read("Legacy.Maliev.Intranet.Client", "Pages", "Dashboard.razor");
        var customer = Read("Legacy.Maliev.Intranet.Client.Features.Customers", "Pages", "CustomerView.razor");

        Assert.Contains("new(Text[\"Dashboard\"])", dashboard, StringComparison.Ordinal);
        Assert.Contains("<PageBreadcrumbs Items=\"@Breadcrumbs\"", dashboard, StringComparison.Ordinal);
        Assert.Contains("new(Text[\"Customers\"], \"/customers\")", customer, StringComparison.Ordinal);
        Assert.Contains("new(customer!.FullName)", customer, StringComparison.Ordinal);
        Assert.DoesNotContain("new(Text[\"Home\"]", dashboard, StringComparison.Ordinal);
        Assert.DoesNotContain("new(Text[\"Home\"]", customer, StringComparison.Ordinal);
    }

    private static string Read(params string[] segments) => File.ReadAllText(Path.Combine([FindRoot(), .. segments]));

    private static string FindRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Legacy.Maliev.Intranet.slnx"))) directory = directory.Parent;
        return directory?.FullName ?? throw new DirectoryNotFoundException("Could not find repository root.");
    }
}
