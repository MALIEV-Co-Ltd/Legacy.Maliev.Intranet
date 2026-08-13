namespace Legacy.Maliev.Intranet.Tests;

public sealed class OperationalTableAdoptionContractTests
{
    public static TheoryData<string, string, string, string?, string[], string[], string> OperationalWaveInventory => new()
    {
        { "Legacy.Maliev.Intranet.Client.Features.Accounting", "Pages/Invoices.razor", "int", "/Invoices/View?id=", ["Id", "Number", "CustomerId", "Paid", "Outstanding", "Total"], ["ReceiptId", "PurchaseOrder", "Subtotal", "Vat", "WithholdingTax", "PaymentDate", "CreatedDate"], "DetailAndQuickView" },
        { "Legacy.Maliev.Intranet.Client.Features.Accounting", "Pages/Finances.razor", "int", "/Finances/View?id=", ["Id", "Direction", "Type", "Amount", "PaymentDate"], ["Description", "Method", "Recipient", "TransactionNumber"], "DetailAndQuickView" },
        { "Legacy.Maliev.Intranet.Client.Features.Procurement", "Pages/PurchaseOrders.razor", "int", "/PurchaseOrders/View?id=", ["Id", "Employee", "Fob", "CreatedDate"], ["Terms", "ShippingMethod"], "DetailAndQuickView" },
        { "Legacy.Maliev.Intranet.Client.Features.Procurement", "Pages/Suppliers.razor", "int", "/Suppliers/View?id=", ["Id", "Name", "Email"], ["Telephone"], "DetailAndQuickView" },
        { "Legacy.Maliev.Intranet.Client.Features.Catalog", "Pages/Materials.razor", "int", "/Materials/View?id=", ["Id", "Number", "Name", "Group"], ["Density", "Machinable", "Printable"], "DetailAndQuickView" },
        { "Legacy.Maliev.Intranet.Client.Features.Diagnostics", "Pages/ErrorReport.razor", "long", null, ["Timestamp", "Level", "Code", "Category"], ["Path", "CorrelationId"], "QuickViewOnly" },
    };

    public static TheoryData<string, string> OperationalTableExceptions => new()
    {
        { "Legacy.Maliev.Intranet.Client.Features.Accounting/Pages/Finances.razor", "AnalyticalMonthlySummary" },
        { "Legacy.Maliev.Intranet.Client.Features.Accounting/Pages/NetProfitChart.razor", "AnalyticalChart" },
        { "Legacy.Maliev.Intranet.Client.Features.Accounting/Pages/YearlyActivityChart.razor", "AnalyticalChart" },
        { "Legacy.Maliev.Intranet.Client.Features.Accounting/Pages/InvoiceCreate.razor", "FormSubTable" },
        { "Legacy.Maliev.Intranet.Client.Features.Accounting/Pages/InvoiceView.razor", "DetailSubTable" },
    };

    public static TheoryData<string> AliasLandingPages => new()
    {
        "Legacy.Maliev.Intranet.Client.Features.Accounting/Pages/Invoices.razor",
        "Legacy.Maliev.Intranet.Client.Features.Procurement/Pages/PurchaseOrders.razor",
        "Legacy.Maliev.Intranet.Client.Features.Catalog/Pages/Materials.razor",
    };

    [Theory]
    [MemberData(nameof(AliasLandingPages))]
    public void BreadcrumbIntermediateDestinations_DoNotPointBackToTheCurrentComponentAlias(string relativePath)
    {
        var source = File.ReadAllText(Path.Combine(FindRoot(), relativePath.Replace('/', Path.DirectorySeparatorChar)));
        var routeAliases = System.Text.RegularExpressions.Regex.Matches(source, "@page \\\"([^\\\"]+)\\\"")
            .Select(match => match.Groups[1].Value)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var breadcrumbBlock = System.Text.RegularExpressions.Regex.Match(
            source,
            "private IReadOnlyList<PageBreadcrumbItem> Breadcrumbs =>(?<body>[\\s\\S]*?)\\];").Groups["body"].Value;
        var intermediateDestinations = System.Text.RegularExpressions.Regex.Matches(
            breadcrumbBlock,
            "new\\(Text\\[\\\"[^\\\"]+\\\"\\], \\\"([^\\\"]+)\\\"\\)")
            .Select(match => match.Groups[1].Value)
            .ToArray();

        Assert.NotEmpty(routeAliases);
        Assert.Equal(["/Dashboard"], intermediateDestinations);
        Assert.DoesNotContain(intermediateDestinations, routeAliases.Contains);
    }

    [Theory]
    [MemberData(nameof(OperationalWaveInventory))]
    public void OperationalWavePages_DeclareExactTypedAdaptersAndCompletePriorityInventory(
        string project, string relativePage, string keyType, string? detailRoute,
        string[] essentialResources, string[] supportingResources, string disposition)
    {
        var root = FindRoot();
        var source = File.ReadAllText(Path.Combine(root, project, relativePage.Replace('/', Path.DirectorySeparatorChar)));

        Assert.Contains("<OperationalTable TItem=", source, StringComparison.Ordinal);
        Assert.Contains($"TKey=\"{keyType}\"", source, StringComparison.Ordinal);
        Assert.Contains("<PageBreadcrumbs Items=", source, StringComparison.Ordinal);
        Assert.Contains("new(Text[\"Operations\"], \"/Dashboard\")", source, StringComparison.Ordinal);
        Assert.Contains($"OperationalTableState<{keyType}>", source, StringComparison.Ordinal);
        Assert.Contains("tableState.Clear();", source, StringComparison.Ordinal);
        Assert.Contains("<QuickViewContent", source, StringComparison.Ordinal);
        Assert.Contains("ExpandAriaLabel=", source, StringComparison.Ordinal);
        Assert.Contains("CollapseAriaLabel=", source, StringComparison.Ordinal);
        if (relativePage != "Pages/Finances.razor")
        {
            Assert.DoesNotContain("<MudTable", source, StringComparison.Ordinal);
            Assert.DoesNotContain("Breakpoint=\"Breakpoint.Sm\"", source, StringComparison.Ordinal);
        }
        else
        {
            Assert.Contains("AnalyticalMonthlySummary", source, StringComparison.Ordinal);
        }

        if (disposition == "QuickViewOnly")
        {
            Assert.Contains("DetailHref=\"@(_ => null)\"", source, StringComparison.Ordinal);
            Assert.DoesNotContain("/Server/ErrorReport/View", source, StringComparison.Ordinal);
        }
        else
        {
            Assert.NotNull(detailRoute);
            Assert.Contains(detailRoute, source, StringComparison.Ordinal);
        }

        foreach (var resource in essentialResources)
        {
            Assert.Contains($"Text[\"{resource}\"]", source, StringComparison.Ordinal);
            Assert.Contains("data-priority=\"essential\"", source, StringComparison.Ordinal);
        }
        foreach (var resource in supportingResources)
        {
            Assert.Contains($"Text[\"{resource}\"]", source, StringComparison.Ordinal);
            Assert.Contains("data-priority=\"supporting\"", source, StringComparison.Ordinal);
        }
    }

    [Theory]
    [MemberData(nameof(OperationalTableExceptions))]
    public void OperationalTableExceptionLedger_RecordsNonListTables(string relativePath, string disposition)
    {
        var root = FindRoot();
        var source = File.ReadAllText(Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar)));
        var expectedDisposition = relativePath switch
        {
            "Legacy.Maliev.Intranet.Client.Features.Accounting/Pages/Finances.razor" => "AnalyticalMonthlySummary",
            "Legacy.Maliev.Intranet.Client.Features.Accounting/Pages/NetProfitChart.razor" => "AnalyticalChart",
            "Legacy.Maliev.Intranet.Client.Features.Accounting/Pages/YearlyActivityChart.razor" => "AnalyticalChart",
            "Legacy.Maliev.Intranet.Client.Features.Accounting/Pages/InvoiceCreate.razor" => "FormSubTable",
            "Legacy.Maliev.Intranet.Client.Features.Accounting/Pages/InvoiceView.razor" => "DetailSubTable",
            _ => string.Empty,
        };
        Assert.Equal(expectedDisposition, disposition);
        if (disposition == "AnalyticalMonthlySummary")
        {
            Assert.Contains("AnalyticalMonthlySummary", source, StringComparison.Ordinal);
            Assert.Contains("<OperationalTable", source, StringComparison.Ordinal);
        }
        else
        {
            Assert.DoesNotContain("<OperationalTable", source, StringComparison.Ordinal);
        }
    }

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
