using Legacy.Maliev.Intranet.Contracts;

namespace Legacy.Maliev.Intranet.Tests;

public sealed class PaginationQueryDefaultsTests
{
    [Theory]
    [InlineData(0, 1)]
    [InlineData(-1, 1)]
    [InlineData(2, 2)]
    public void NormalizeIndex_UsesOneBasedDefault(int value, int expected)
    {
        Assert.Equal(expected, PaginationQueryDefaults.NormalizeIndex(value));
    }

    [Theory]
    [InlineData(0, 25, 250, 25)]
    [InlineData(-1, 100, 250, 100)]
    [InlineData(1, 25, 250, 1)]
    [InlineData(250, 25, 250, 250)]
    [InlineData(500, 25, 250, 250)]
    public void NormalizeSize_PreservesFallbackAndClampsExplicitValues(
        int value,
        int fallback,
        int maximum,
        int expected)
    {
        Assert.Equal(expected, PaginationQueryDefaults.NormalizeSize(value, fallback, maximum));
    }

    [Fact]
    public void NormalizeSize_RejectsInvalidBounds()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => PaginationQueryDefaults.NormalizeSize(0, 0, 250));
        Assert.Throws<ArgumentOutOfRangeException>(() => PaginationQueryDefaults.NormalizeSize(0, 25, 24));
    }

    [Theory]
    [InlineData("Legacy.Maliev.Intranet.Client.Features.Accounting/Pages/Finances.razor", "fallback: 25", "maximum: 100")]
    [InlineData("Legacy.Maliev.Intranet.Client.Features.Catalog/Pages/Materials.razor", "fallback: 10", "maximum: 250")]
    [InlineData("Legacy.Maliev.Intranet.Client.Features.Customers/Pages/Customers.razor", "fallback: 25", "maximum: 250")]
    [InlineData("Legacy.Maliev.Intranet.Client.Features.Diagnostics/Pages/ErrorReport.razor", "fallback: 10", "maximum: 100")]
    [InlineData("Legacy.Maliev.Intranet.Client.Features.Employees/Pages/Employees.razor", "fallback: 25", "maximum: 250")]
    [InlineData("Legacy.Maliev.Intranet.Client.Features.Orders/Pages/Orders.razor", "fallback: 10", "maximum: 250")]
    [InlineData("Legacy.Maliev.Intranet.Client.Features.Procurement/Pages/PurchaseOrders.razor", "fallback: 25", "maximum: 250")]
    [InlineData("Legacy.Maliev.Intranet.Client.Features.Procurement/Pages/Suppliers.razor", "fallback: 25", "maximum: 250")]
    [InlineData("Legacy.Maliev.Intranet.Client.Features.Quotations/Pages/QuotationRequests/Index.razor", "fallback: 25", "maximum: 250")]
    public void PaginatedFeaturePages_UseExplicitQueryDefaults(string relativePath, string fallback, string maximum)
    {
        var root = FindRoot();
        var source = File.ReadAllText(Path.Combine(
            root,
            relativePath.Replace('/', Path.DirectorySeparatorChar)));

        Assert.Contains("PaginationQueryDefaults.NormalizeIndex", source, StringComparison.Ordinal);
        Assert.Contains("PaginationQueryDefaults.NormalizeSize", source, StringComparison.Ordinal);
        Assert.Contains(fallback, source, StringComparison.Ordinal);
        Assert.Contains(maximum, source, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Legacy.Maliev.Intranet.Client.Features.Accounting/Pages/Invoices.razor")]
    [InlineData("Legacy.Maliev.Intranet.Client.Features.Quotations/Pages/Quotations/Index.razor")]
    public void ResponsiveRecordLists_DefaultToTenItems(string relativePath)
    {
        var root = FindRoot();
        var source = File.ReadAllText(Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar)));

        Assert.Contains("public int Size { get; set; } = 10", source, StringComparison.Ordinal);
        Assert.Contains("private int sizeInput = 10", source, StringComparison.Ordinal);
        Assert.Contains("Value=\"10\"", source, StringComparison.Ordinal);
    }

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
