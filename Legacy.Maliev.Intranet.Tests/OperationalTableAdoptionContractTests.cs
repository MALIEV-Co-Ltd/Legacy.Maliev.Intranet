namespace Legacy.Maliev.Intranet.Tests;

public sealed class OperationalTableAdoptionContractTests
{
    public static TheoryData<string, string, string, string, string[], string[], string[]> SalesWaveInventory => new()
    {
        {
            "Legacy.Maliev.Intranet.Client.Features.Customers", "Pages/Customers.razor", "int", "/Customers/View?id=",
            ["Id", "FullName", "Email", "Company"],
            ["Id", "Name", "Email", "Actions"], ["Company"]
        },
        {
            "Legacy.Maliev.Intranet.Client.Features.Employees", "Pages/Employees.razor", "int", "/Employees/View?id=",
            ["Id", "FullName", "Email", "Role"],
            ["Id", "Name", "Role", "Actions"], ["Email"]
        },
        {
            "Legacy.Maliev.Intranet.Client.Features.Quotations", "Pages/QuotationRequests/Index.razor", "int", "/QuotationRequests/View?id=",
            ["Id", "FirstName", "LastName", "CompanyName", "Done", "CreatedDate"],
            ["Id", "Customer", "Status", "Created", "Actions"], ["Company"]
        },
        {
            "Legacy.Maliev.Intranet.Client.Features.Quotations", "Pages/Quotations/Index.razor", "int", "/Quotations/View?id=",
            ["Id", "CustomerId", "EmployeeId", "Period", "ExpirationDate", "Subtotal", "Vat", "Total", "WithholdingTax", "QuotedAmount", "Fob", "ShippedVia", "Terms", "Accepted"],
            ["Id", "CustomerId", "Total", "QuotedAmount", "Decision", "Actions"],
            ["Employee", "Period", "ExpirationDate", "Subtotal", "Vat", "WithholdingTax", "Fob", "ShippedVia", "Terms"]
        },
    };

    [Theory]
    [MemberData(nameof(SalesWaveInventory))]
    public void SalesWavePages_DeclareTypedOperationalTableBreadcrumbsAndCompletePriorityInventory(
        string project,
        string relativePage,
        string keyType,
        string detailRoute,
        string[] expectedFields,
        string[] essentialResources,
        string[] supportingResources)
    {
        var root = FindRoot();
        var pagePath = Path.Combine(root, project, relativePage.Replace('/', Path.DirectorySeparatorChar));
        var source = File.ReadAllText(pagePath);

        Assert.Contains($"<OperationalTable TItem=", source, StringComparison.Ordinal);
        Assert.Contains($"TKey=\"{keyType}\"", source, StringComparison.Ordinal);
        Assert.Contains("<PageBreadcrumbs Items=", source, StringComparison.Ordinal);
        Assert.Contains("new(Text[\"Operations\"], \"/Dashboard\")", source, StringComparison.Ordinal);
        Assert.Contains("OperationalTableState<int>", source, StringComparison.Ordinal);
        Assert.Contains("tableState.Clear();", source, StringComparison.Ordinal);
        Assert.Contains(detailRoute, source, StringComparison.Ordinal);
        Assert.Contains("<QuickViewContent", source, StringComparison.Ordinal);
        Assert.Contains("ExpandAriaLabel=", source, StringComparison.Ordinal);
        Assert.Contains("CollapseAriaLabel=", source, StringComparison.Ordinal);
        Assert.DoesNotContain("<MudTable", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Breakpoint=\"Breakpoint.Sm\"", source, StringComparison.Ordinal);
        Assert.DoesNotContain("grid-template-areas", File.Exists(Path.ChangeExtension(pagePath, ".razor.css"))
            ? File.ReadAllText(Path.ChangeExtension(pagePath, ".razor.css"))
            : string.Empty, StringComparison.Ordinal);

        foreach (var field in expectedFields)
        {
            Assert.Contains(field, source, StringComparison.Ordinal);
        }

        foreach (var resource in essentialResources)
        {
            Assert.Contains($"Text[\"{resource}\"]", source, StringComparison.Ordinal);
        }

        foreach (var resource in supportingResources)
        {
            Assert.Contains($"Text[\"{resource}\"]", source, StringComparison.Ordinal);
            Assert.Contains("data-priority=\"supporting\"", source, StringComparison.Ordinal);
        }
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
