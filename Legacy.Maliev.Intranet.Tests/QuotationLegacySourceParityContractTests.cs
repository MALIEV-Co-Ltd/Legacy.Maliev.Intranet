namespace Legacy.Maliev.Intranet.Tests;

public sealed class QuotationLegacySourceParityContractTests
{
    [Fact]
    public void Create_UsesLegacyBootstrapFieldOrderAndSafeBffWorkflow()
    {
        var root = FindRoot();
        var source = File.ReadAllText(Path.Combine(root, "Legacy.Maliev.Intranet.Client.Features.Quotations", "Pages", "Quotations", "Create.razor"));
        var mapper = File.ReadAllText(Path.Combine(root, "Legacy.Maliev.Intranet.Bff", "Quotations", "QuotationCreateEndpointMapper.cs"));
        var program = File.ReadAllText(Path.Combine(root, "Legacy.Maliev.Intranet.Bff", "Program.cs"));

        Assert.Contains("id=\"order-search-result\"", source, StringComparison.Ordinal);
        Assert.Contains("id=\"customer-id\"", source, StringComparison.Ordinal);
        Assert.Contains("id=\"customer-fullname\"", source, StringComparison.Ordinal);
        Assert.Contains("id=\"customer-company-name\"", source, StringComparison.Ordinal);
        Assert.Contains("id=\"customer-company-tax-number\"", source, StringComparison.Ordinal);
        Assert.Contains("id=\"order-list\"", source, StringComparison.Ordinal);
        Assert.Contains("id=\"order-subtotal\"", source, StringComparison.Ordinal);
        Assert.Contains("id=\"order-vat\"", source, StringComparison.Ordinal);
        Assert.Contains("id=\"order-total\"", source, StringComparison.Ordinal);
        Assert.Contains("id=\"quotation-withholding-tax\"", source, StringComparison.Ordinal);
        Assert.Contains("id=\"order-quoted-amount\"", source, StringComparison.Ordinal);
        Assert.Contains("id=\"currency\"", source, StringComparison.Ordinal);
        Assert.Contains("id=\"btn-submit\"", source, StringComparison.Ordinal);
        Assert.Contains("/bff/quotations/create", source, StringComparison.Ordinal);
        Assert.Contains("/bff/quotations/create/orders", source, StringComparison.Ordinal);
        Assert.Contains("MapGet(\"/bff/quotations/create/orders\"", program, StringComparison.Ordinal);
        Assert.Contains("detail.Company?.Name", mapper, StringComparison.Ordinal);
        Assert.Contains("detail.Company?.TaxNumber", mapper, StringComparison.Ordinal);
        Assert.Contains("X-CSRF-TOKEN", source, StringComparison.Ordinal);
        Assert.Contains("Idempotency-Key", source, StringComparison.Ordinal);
        Assert.DoesNotContain("<Mud", source, StringComparison.Ordinal);
        Assert.DoesNotContain("access_token", source, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Estimate_PreservesLegacyTabsIdsAndBootstrapResponsiveColumns()
    {
        var root = FindRoot();
        var source = File.ReadAllText(Path.Combine(root, "Legacy.Maliev.Intranet.Client.Features.Quotations", "Pages", "Quotations", "Estimate.razor"));

        Assert.Contains("id=\"nav-3d-printing\"", source, StringComparison.Ordinal);
        Assert.Contains("id=\"nav-cnc-machining\"", source, StringComparison.Ordinal);
        Assert.Contains("id=\"nav-3d-scanning\"", source, StringComparison.Ordinal);
        Assert.Contains("id=\"nav-3d-designing\"", source, StringComparison.Ordinal);
        Assert.Contains("id=\"cnc-machining-machine-cost\"", source, StringComparison.Ordinal);
        Assert.Contains("id=\"cnc-machining-stock-cost-all-parts\"", source, StringComparison.Ordinal);
        Assert.Contains("id=\"cnc-machining-grand-total\"", source, StringComparison.Ordinal);
        Assert.Contains("col-12 col-lg-8", source, StringComparison.Ordinal);
        Assert.Contains("col-12 col-lg-4", source, StringComparison.Ordinal);
        Assert.DoesNotContain("<Mud", source, StringComparison.Ordinal);
    }

    [Fact]
    public void QuotationRequestView_MatchesLegacyReadOnlyCustomerAndUploadSurface()
    {
        var root = FindRoot();
        var source = File.ReadAllText(Path.Combine(root, "Legacy.Maliev.Intranet.Client.Features.Quotations", "Pages", "QuotationRequests", "View.razor"));
        var contracts = File.ReadAllText(Path.Combine(root, "Legacy.Maliev.Intranet.Contracts", "QuotationRequestContracts.cs"));

        Assert.Contains("value=\"false\">Ongoing", source, StringComparison.Ordinal);
        Assert.Contains("value=\"true\">Finished", source, StringComparison.Ordinal);
        Assert.Contains("id=\"request-message\"", source, StringComparison.Ordinal);
        Assert.Contains("readonly disabled", source, StringComparison.Ordinal);
        Assert.Contains("<th>Bucket</th>", source, StringComparison.Ordinal);
        Assert.Contains("<th>Object name</th>", source, StringComparison.Ordinal);
        Assert.Contains("ServiceFinderMetadataEnvelope.TryRead", source, StringComparison.Ordinal);
        Assert.Contains("MergeOperatorComment", source, StringComparison.Ordinal);
        Assert.Contains("string? Bucket = null", contracts, StringComparison.Ordinal);
        Assert.Contains("X-CSRF-TOKEN", source, StringComparison.Ordinal);
        Assert.DoesNotContain("<Mud", source, StringComparison.Ordinal);
    }

    [Fact]
    public void QuotationView_PreservesLegacyPdfLinkBeforeEmployeeInformation()
    {
        var root = FindRoot();
        var source = File.ReadAllText(Path.Combine(root, "Legacy.Maliev.Intranet.Client.Features.Quotations", "Pages", "Quotations", "View.razor"));

        Assert.Contains("quotationPage.Files.LastOrDefault()?.Uri", source, StringComparison.Ordinal);
        Assert.Contains("View PDF", source, StringComparison.Ordinal);
        Assert.Contains("target=\"_blank\" rel=\"noopener\"", source, StringComparison.Ordinal);
        Assert.DoesNotContain("<Mud", source, StringComparison.Ordinal);
    }

    private static string FindRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Legacy.Maliev.Intranet.slnx"))) directory = directory.Parent;
        return directory?.FullName ?? throw new DirectoryNotFoundException();
    }
}
