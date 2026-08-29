namespace Legacy.Maliev.Intranet.Tests;

public sealed class QuotationLegacySourceParityContractTests
{
    [Fact]
    public void Create_PreservesOrderSearchAndSafeBffWorkflow()
    {
        var root = FindRoot();
        var source = File.ReadAllText(Path.Combine(root, "Legacy.Maliev.Intranet.Client.Features.Quotations", "Pages", "Quotations", "Create.razor"));
        var mapper = File.ReadAllText(Path.Combine(root, "Legacy.Maliev.Intranet.Bff", "Quotations", "QuotationCreateEndpointMapper.cs"));
        var program = File.ReadAllText(Path.Combine(root, "Legacy.Maliev.Intranet.Bff", "Program.cs"));

        Assert.Contains("SearchOrdersAsync", source, StringComparison.Ordinal);
        Assert.Contains("orderSearch", source, StringComparison.Ordinal);
        Assert.Contains("page = page with { Orders = orders ?? [] }", source, StringComparison.Ordinal);
        Assert.Contains("/bff/quotations/create", source, StringComparison.Ordinal);
        Assert.Contains("/bff/quotations/create/orders", source, StringComparison.Ordinal);
        Assert.Contains("MapGet(\"/bff/quotations/create/orders\"", program, StringComparison.Ordinal);
        Assert.Contains("SearchOrdersAsync", mapper, StringComparison.Ordinal);
        Assert.Contains("GetCustomerAsync", mapper, StringComparison.Ordinal);
        Assert.Contains("X-CSRF-TOKEN", source, StringComparison.Ordinal);
        Assert.Contains("Idempotency-Key", source, StringComparison.Ordinal);
        Assert.Contains("<EditForm", source, StringComparison.Ordinal);
        Assert.DoesNotContain("<Mud", source, StringComparison.Ordinal);
        Assert.DoesNotContain("access_token", source, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Estimate_PreservesWorkflowTabsAndResponsiveColumns()
    {
        var root = FindRoot();
        var source = File.ReadAllText(Path.Combine(root, "Legacy.Maliev.Intranet.Client.Features.Quotations", "Pages", "Quotations", "Estimate.razor"));

        Assert.Contains("<ShadcnTabsContent", source, StringComparison.Ordinal);
        Assert.Contains("Text[\"Printing\"]", source, StringComparison.Ordinal);
        Assert.Contains("Text[\"CncMachining\"]", source, StringComparison.Ordinal);
        Assert.Contains("Text[\"Scanning\"]", source, StringComparison.Ordinal);
        Assert.Contains("Text[\"Designing\"]", source, StringComparison.Ordinal);
        Assert.Contains("quotation-estimate-inputs", source, StringComparison.Ordinal);
        Assert.Contains("quotation-estimate-summary", source, StringComparison.Ordinal);
        Assert.Contains("@Text[\"GrandTotal\"]", source, StringComparison.Ordinal);
        Assert.Contains("CncEstimateCalculator", source, StringComparison.Ordinal);
    }

    [Fact]
    public void QuotationRequestView_PreservesEditableStatusAndUploadSurface()
    {
        var root = FindRoot();
        var source = File.ReadAllText(Path.Combine(root, "Legacy.Maliev.Intranet.Client.Features.Quotations", "Pages", "QuotationRequests", "View.razor"));
        var contracts = File.ReadAllText(Path.Combine(root, "Legacy.Maliev.Intranet.Contracts", "QuotationRequestContracts.cs"));

        Assert.Contains("<QuotationSelectField TValue=\"bool?\"", source, StringComparison.Ordinal);
        Assert.Contains("Text[\"Open\"]", source, StringComparison.Ordinal);
        Assert.Contains("Text[\"Done\"]", source, StringComparison.Ordinal);
        Assert.Contains("detail.Files", source, StringComparison.Ordinal);
        Assert.Contains("QuotationRequestDetail", source, StringComparison.Ordinal);
        Assert.Contains("QuotationRequestFileItem", contracts, StringComparison.Ordinal);
        Assert.Contains("X-CSRF-TOKEN", source, StringComparison.Ordinal);
        Assert.Contains("<EditForm", source, StringComparison.Ordinal);
        Assert.DoesNotContain("<Mud", source, StringComparison.Ordinal);
    }

    [Fact]
    public void QuotationView_PreservesLegacyPdfLinkBeforeEmployeeInformation()
    {
        var root = FindRoot();
        var source = File.ReadAllText(Path.Combine(root, "Legacy.Maliev.Intranet.Client.Features.Quotations", "Pages", "Quotations", "View.razor"));

        Assert.Contains("page.Files.LastOrDefault()?.Uri", source, StringComparison.Ordinal);
        Assert.Contains("@Text[\"ViewPdf\"]", source, StringComparison.Ordinal);
        Assert.Contains("Target=\"_blank\"", source, StringComparison.Ordinal);
        Assert.Contains("Rel=\"noopener\"", source, StringComparison.Ordinal);
        Assert.Contains("@Text[\"Files\"]", source, StringComparison.Ordinal);
    }

    private static string FindRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Legacy.Maliev.Intranet.slnx"))) directory = directory.Parent;
        return directory?.FullName ?? throw new DirectoryNotFoundException();
    }
}
