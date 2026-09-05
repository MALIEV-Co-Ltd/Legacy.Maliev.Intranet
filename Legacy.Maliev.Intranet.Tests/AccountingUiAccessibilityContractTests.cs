using System.Xml.Linq;

namespace Legacy.Maliev.Intranet.Tests;

public sealed class AccountingUiAccessibilityContractTests
{
    [Fact]
    public void AccountingPages_UseSemanticHeadersAndLocalizedResourcePairs()
    {
        var pages = AccountingPages();

        foreach (var page in pages)
        {
            var source = File.ReadAllText(page);
            Assert.Contains("operations-page-header", source, StringComparison.Ordinal);
            Assert.DoesNotContain("Typo.overline", source, StringComparison.Ordinal);

            var english = Path.ChangeExtension(page, ".resx");
            var thai = Path.Combine(
                Path.GetDirectoryName(page)!,
                $"{Path.GetFileNameWithoutExtension(page)}.th.resx");
            Assert.True(File.Exists(english), $"Missing English resource file for {page}.");
            Assert.True(File.Exists(thai), $"Missing Thai resource file for {page}.");
            Assert.Equal(ResourceKeys(english), ResourceKeys(thai));
        }
    }

    [Fact]
    public void FinanceCharts_ExposeLocalizedThbTablesAndNativeAccessibleCharts()
    {
        var root = FindRoot();
        var activity = ReadPage(root, "YearlyActivityChart.razor");
        var netProfit = ReadPage(root, "NetProfitChart.razor");

        Assert.Contains("<ShadcnChart", activity, StringComparison.Ordinal);
        Assert.Contains("CategoryLabel=", activity, StringComparison.Ordinal);
        Assert.Contains("ActivityRows", activity, StringComparison.Ordinal);
        Assert.Contains("IncomeThb", activity, StringComparison.Ordinal);
        Assert.Contains("ExpenseThb", activity, StringComparison.Ordinal);
        Assert.Contains("<ShadcnChart", netProfit, StringComparison.Ordinal);
        Assert.Contains("CategoryLabel=", netProfit, StringComparison.Ordinal);
        Assert.Contains("IncomeRows", netProfit, StringComparison.Ordinal);
        Assert.Contains("DailyThb", netProfit, StringComparison.Ordinal);
        Assert.Contains("CumulativeThb", netProfit, StringComparison.Ordinal);
        Assert.Contains("Asia/Bangkok", activity, StringComparison.Ordinal);
        Assert.Contains("Asia/Bangkok", netProfit, StringComparison.Ordinal);
        Assert.Contains("THB", activity, StringComparison.Ordinal);
        Assert.Contains("THB", netProfit, StringComparison.Ordinal);
        Assert.DoesNotContain("aria-hidden=\"true\"", activity, StringComparison.Ordinal);
        Assert.DoesNotContain("aria-hidden=\"true\"", netProfit, StringComparison.Ordinal);
    }

    [Fact]
    public void AccountingListsAndDetails_ExposeStatusTableAndSafeActionSemantics()
    {
        var root = FindRoot();
        var finances = ReadPage(root, "Finances.razor");
        var financeView = ReadPage(root, "FinanceView.razor");
        var invoiceCreate = ReadPage(root, "InvoiceCreate.razor");
        var invoices = ReadPage(root, "Invoices.razor");
        var invoiceView = ReadPage(root, "InvoiceView.razor");

        Assert.Contains("YearlyTrendTable", finances, StringComparison.Ordinal);
        Assert.Contains("OperationalDataTable", finances, StringComparison.Ordinal);
        Assert.Contains("finance-summary-grid", finances, StringComparison.Ordinal);
        Assert.Contains("PaidStatus", invoices, StringComparison.Ordinal);
        Assert.Contains("UnpaidStatus", invoices, StringComparison.Ordinal);
        Assert.Contains("OperationalDataTable", invoices, StringComparison.Ordinal);
        Assert.Contains("OrderItemsTable", invoiceCreate, StringComparison.Ordinal);
        Assert.Contains("OrderItemsTable", invoiceView, StringComparison.Ordinal);
        Assert.Contains("role=\"group\"", financeView, StringComparison.Ordinal);
        Assert.Contains("DeleteConfirmation", financeView, StringComparison.Ordinal);
        Assert.Contains("RemoveFileConfirmation", financeView, StringComparison.Ordinal);
        Assert.Contains("role=\"group\"", invoiceView, StringComparison.Ordinal);
        Assert.Contains("EmailReceiptConfirmation", invoiceView, StringComparison.Ordinal);
        Assert.Contains("RemoveReceiptConfirmation", invoiceView, StringComparison.Ordinal);
        Assert.Contains("DeleteConfirmation", invoiceView, StringComparison.Ordinal);
        Assert.Contains("Rel=\"noopener\"", financeView, StringComparison.Ordinal);
        Assert.Contains("Rel=\"noopener\"", invoiceView, StringComparison.Ordinal);
    }

    [Fact]
    public void AccountingLoadingAndMessages_AreAnnounced()
    {
        foreach (var page in AccountingPages())
        {
            var source = File.ReadAllText(page);
            Assert.True(
                source.Contains("aria-live=\"polite\"", StringComparison.Ordinal)
                || source.Contains("aria-live=\"assertive\"", StringComparison.Ordinal),
                $"{Path.GetFileName(page)} has no live-region announcement.");
        }
    }

    private static IReadOnlyList<string> AccountingPages()
    {
        var root = FindRoot();
        var pages = Path.Combine(root, "Legacy.Maliev.Intranet.Client.Features.Accounting", "Pages");
        return Directory.GetFiles(pages, "*.razor", SearchOption.TopDirectoryOnly)
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();
    }

    private static string ReadPage(string root, string name) => File.ReadAllText(Path.Combine(
        root,
        "Legacy.Maliev.Intranet.Client.Features.Accounting",
        "Pages",
        name));

    private static string[] ResourceKeys(string path) => XDocument.Load(path)
        .Root!
        .Elements("data")
        .Select(element => (string?)element.Attribute("name"))
        .Where(name => !string.IsNullOrWhiteSpace(name))
        .Select(name => name!)
        .OrderBy(name => name, StringComparer.Ordinal)
        .ToArray();

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
