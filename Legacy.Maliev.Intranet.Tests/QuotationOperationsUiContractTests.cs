using System.Xml.Linq;

namespace Legacy.Maliev.Intranet.Tests;

public sealed class QuotationOperationsUiContractTests
{
    [Fact]
    public void QuotationPages_UseTheOperationsHeaderAndAccessibleStateContracts()
    {
        var root = FindRoot();
        var feature = Path.Combine(root, "Legacy.Maliev.Intranet.Client.Features.Quotations", "Pages");
        var pages = Directory.GetFiles(feature, "*.razor", SearchOption.AllDirectories);

        Assert.NotEmpty(pages);
        foreach (var path in pages)
        {
            var source = File.ReadAllText(path);
            Assert.DoesNotContain("Typo.overline", source, StringComparison.Ordinal);
        }

        var quotationIndex = Read(feature, "Quotations", "Index.razor");
        Assert.Contains("operations-page-header", quotationIndex, StringComparison.Ordinal);
        Assert.Contains("quotation-decision-data", quotationIndex, StringComparison.Ordinal);
        Assert.Contains("<ShadcnChart", quotationIndex, StringComparison.Ordinal);
        Assert.Contains("aria-label=\"@Text[\"TableCaption\"]\"", quotationIndex, StringComparison.Ordinal);
        Assert.Contains("<nav", quotationIndex, StringComparison.Ordinal);
        Assert.Contains("aria-live=\"polite\"", quotationIndex, StringComparison.Ordinal);

        var requestIndex = Read(feature, "QuotationRequests", "Index.razor");
        Assert.Contains("<ListToolbar", requestIndex, StringComparison.Ordinal);
        var sharedToolbar = File.ReadAllText(Path.Combine(
            root,
            "Legacy.Maliev.Intranet.Client.Shared",
            "Components",
            "ListToolbar.razor"));
        Assert.Contains("role=\"search\"", sharedToolbar, StringComparison.Ordinal);
        Assert.Contains("quotation-request-status", requestIndex, StringComparison.Ordinal);
        Assert.Contains("aria-label=\"@Text[\"PaginationLabel\"]\"", requestIndex, StringComparison.Ordinal);
        Assert.Contains("AriaDescribedBy=\"quotation-request-table-summary\"", requestIndex, StringComparison.Ordinal);
        Assert.DoesNotContain("<section class=\"quotation-request-table-region\" aria-describedby=", requestIndex, StringComparison.Ordinal);
        Assert.Contains("AriaDescribedBy=\"quotation-table-caption\"", quotationIndex, StringComparison.Ordinal);

        var requestView = Read(feature, "QuotationRequests", "View.razor");
        Assert.Contains("X-CSRF-TOKEN", requestView, StringComparison.Ordinal);
        Assert.Contains("HttpMethod.Put", requestView, StringComparison.Ordinal);
        Assert.Contains("CustomerDetails", requestView, StringComparison.Ordinal);
        Assert.Contains("RequestDetails", requestView, StringComparison.Ordinal);
        Assert.Contains("Rel=\"noopener\"", requestView, StringComparison.Ordinal);
        Assert.Contains("aria-busy", requestView, StringComparison.Ordinal);
    }

    [Fact]
    public void EstimateAndQuotationDetails_PreserveRoutesMoneyAndExternalLinkSafety()
    {
        var root = FindRoot();
        var feature = Path.Combine(root, "Legacy.Maliev.Intranet.Client.Features.Quotations", "Pages", "Quotations");
        var estimate = File.ReadAllText(Path.Combine(feature, "Estimate.razor"));
        var estimateCss = File.ReadAllText(Path.Combine(feature, "Estimate.razor.css"));
        var view = File.ReadAllText(Path.Combine(feature, "View.razor"));

        Assert.Contains("new(Text[\"Quotations\"], href: \"/Quotations/Index\")", estimate, StringComparison.Ordinal);
        Assert.Contains("HtmlTag=\"h1\"", estimate, StringComparison.Ordinal);
        Assert.Contains("HtmlTag=\"h2\"", estimate, StringComparison.Ordinal);
        Assert.Contains("SummaryCaption", estimate, StringComparison.Ordinal);
        Assert.Contains("THB", estimate, StringComparison.Ordinal);
        Assert.Contains("aria-atomic=\"true\"", estimate, StringComparison.Ordinal);
        Assert.Contains("@media (min-width: 1280px)", estimateCss, StringComparison.Ordinal);
        Assert.Contains("position: sticky", estimateCss, StringComparison.Ordinal);

        Assert.Contains("@page \"/Quotations/View\"", view, StringComparison.Ordinal);
        Assert.Contains("/bff/quotations/{Id.Value}", view, StringComparison.Ordinal);
        Assert.Contains("quotation-price-grid", view, StringComparison.Ordinal);
        Assert.Contains("DecisionClass", view, StringComparison.Ordinal);
        Assert.Contains("AuthenticationStateProvider", view, StringComparison.Ordinal);
        Assert.Contains("LegacyQuotationPermissions.Update", view, StringComparison.Ordinal);
        Assert.Contains("/bff/quotations/{Id.Value}/decision", view, StringComparison.Ordinal);
        Assert.Contains("/bff/session", view, StringComparison.Ordinal);
        Assert.Contains("X-CSRF-TOKEN", view, StringComparison.Ordinal);
        Assert.Contains("aria-busy", view, StringComparison.Ordinal);
        Assert.DoesNotContain("ReadOnlyNotice", view, StringComparison.Ordinal);
        var externalLinks = Count(view, "Target=\"_blank\"");
        Assert.True(externalLinks > 0);
        Assert.Equal(externalLinks, Count(view, "rel=\"noopener\"") + Count(view, "Rel=\"noopener\""));
    }

    [Fact]
    public void EveryQuotationResourcePair_HasIdenticalKeys()
    {
        var root = FindRoot();
        var feature = Path.Combine(root, "Legacy.Maliev.Intranet.Client.Features.Quotations", "Pages");
        foreach (var englishPath in Directory.GetFiles(feature, "*.resx", SearchOption.AllDirectories)
                     .Where(path => !path.EndsWith(".th.resx", StringComparison.OrdinalIgnoreCase)))
        {
            var thaiPath = Path.ChangeExtension(englishPath, ".th.resx");
            Assert.True(File.Exists(thaiPath), $"Missing Thai resource for {englishPath}");
            Assert.Equal(Keys(englishPath), Keys(thaiPath));
        }
    }

    private static string Read(string root, params string[] parts)
    {
        var path = root;
        foreach (var part in parts)
        {
            path = Path.Combine(path, part);
        }

        return File.ReadAllText(path);
    }

    private static int Count(string source, string value) =>
        source.Split(value, StringSplitOptions.None).Length - 1;

    private static string[] Keys(string path) =>
        XDocument.Load(path).Root!.Elements("data")
            .Select(element => (string?)element.Attribute("name"))
            .Where(name => name is not null)
            .Select(name => name!)
            .Order(StringComparer.Ordinal)
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
