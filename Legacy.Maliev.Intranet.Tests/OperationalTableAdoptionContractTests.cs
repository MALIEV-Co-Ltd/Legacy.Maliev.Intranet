namespace Legacy.Maliev.Intranet.Tests;

public sealed class OperationalTableAdoptionContractTests
{
    public static TheoryData<string, string, string?> RecordListPages => new()
    {
        { "Legacy.Maliev.Intranet.Client.Features.Customers/Pages/Customers.razor", "CustomerListItem", "/Customers/View?id=" },
        { "Legacy.Maliev.Intranet.Client.Features.Employees/Pages/Employees.razor", "EmployeeListItem", "/Employees/View?id=" },
        { "Legacy.Maliev.Intranet.Client.Features.Quotations/Pages/QuotationRequests/Index.razor", "QuotationRequestItem", "/QuotationRequests/View?id=" },
        { "Legacy.Maliev.Intranet.Client.Features.Quotations/Pages/Quotations/Index.razor", "QuotationListItem", "/Quotations/View?id=" },
        { "Legacy.Maliev.Intranet.Client.Features.Orders/Pages/Orders.razor", "OrderListItem", "/Orders/View?id=" },
        { "Legacy.Maliev.Intranet.Client.Features.Accounting/Pages/Invoices.razor", "InvoiceListItem", "/Invoices/View?id=" },
        { "Legacy.Maliev.Intranet.Client.Features.Accounting/Pages/Finances.razor", "FinancePaymentItem", "/Finances/View?id=" },
        { "Legacy.Maliev.Intranet.Client.Features.Catalog/Pages/Materials.razor", "CatalogMaterialListItem", "/Materials/View?id=" },
        { "Legacy.Maliev.Intranet.Client.Features.Procurement/Pages/PurchaseOrders.razor", "PurchaseOrderListItem", "/PurchaseOrders/View?id=" },
        { "Legacy.Maliev.Intranet.Client.Features.Procurement/Pages/Suppliers.razor", "SupplierListItem", "/Suppliers/View?id=" },
        { "Legacy.Maliev.Intranet.Client.Features.Diagnostics/Pages/ErrorReport.razor", "DiagnosticEventItem", null },
    };

    [Theory]
    [MemberData(nameof(RecordListPages))]
    public void RecordListsUseTheReleasedDataTableContract(string relativePath, string itemType, string? detailRoute)
    {
        var source = Read(relativePath);

        Assert.Contains($"<OperationalDataTable TItem=\"{itemType}\"", source, StringComparison.Ordinal);
        Assert.Contains("Columns=\"@Columns\"", source, StringComparison.Ordinal);
        Assert.Contains("State=\"@dataTableState\"", source, StringComparison.Ordinal);
        Assert.Contains("StateChanged=\"HandleDataTableStateChangedAsync\"", source, StringComparison.Ordinal);
        Assert.Contains("RequestChanged=\"HandleDataTableRequestAsync\"", source, StringComparison.Ordinal);
        Assert.Contains("TotalCount=", source, StringComparison.Ordinal);
        Assert.Contains("RowKey=", source, StringComparison.Ordinal);
        Assert.Contains("ShadcnDataTableState", source, StringComparison.Ordinal);
        Assert.Contains("ShadcnDataTableColumn", source, StringComparison.Ordinal);
        Assert.Contains("<QuickViewContent", source, StringComparison.Ordinal);
        Assert.DoesNotContain("<OperationalTable ", source, StringComparison.Ordinal);
        Assert.DoesNotContain("<ListToolbar ", source, StringComparison.Ordinal);

        if (detailRoute is null)
        {
            Assert.Contains("DetailHref=\"@(_ => null)\"", source, StringComparison.Ordinal);
        }
        else
        {
            Assert.Contains(detailRoute, source, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void SharedAdapterComposesTheReleasedMalievDataTableAndRowActions()
    {
        var source = Read("Legacy.Maliev.Intranet.Client.Shared/Components/OperationalDataTable.razor");

        Assert.Contains("<ShadcnDataTable", source, StringComparison.Ordinal);
        Assert.Contains("Manual=\"true\"", source, StringComparison.Ordinal);
        Assert.Contains("<RowActionTemplate", source, StringComparison.Ordinal);
        Assert.Contains("<ShadcnPopover", source, StringComparison.Ordinal);
        Assert.Contains("<LegacyLink", source, StringComparison.Ordinal);
    }

    [Fact]
    public void AnalyticalFinanceTableRemainsASeparateShadcnTable()
    {
        var finances = Read("Legacy.Maliev.Intranet.Client.Features.Accounting/Pages/Finances.razor");
        Assert.Contains("AnalyticalMonthlySummary", finances, StringComparison.Ordinal);
        Assert.Contains("<ShadcnTable", finances, StringComparison.Ordinal);
    }

    private static string Read(string relativePath) =>
        File.ReadAllText(Path.Combine(FindRoot(), relativePath.Replace('/', Path.DirectorySeparatorChar)));

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
