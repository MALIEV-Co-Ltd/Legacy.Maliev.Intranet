using Legacy.Maliev.Intranet.Contracts;
using Legacy.Maliev.Intranet.Server.Quotations;

namespace Legacy.Maliev.Intranet.Tests;

public sealed class QuotationCreateWasmMigrationContractTests
{
    [Fact]
    public void CreateContracts_PreserveBrowserInputsAndKeepAuthoritativeTotalsServerOwned()
    {
        Assert.Equal(
            ["Comment", "CurrencyId", "CustomerId", "EmployeeId", "Fob", "Lines", "Period", "ShippedVia", "Terms", "WithholdingTaxEnabled"],
            typeof(QuotationCreateRequest).GetProperties().Select(property => property.Name).Order(StringComparer.Ordinal).ToArray());
        Assert.Equal(
            ["Description", "DiscountPercent", "OrderId", "Quantity", "UnitPrice"],
            typeof(QuotationCreateLine).GetProperties().Select(property => property.Name).Order(StringComparer.Ordinal).ToArray());
        Assert.Equal(
            ["Id", "Warning"],
            typeof(QuotationCreatedResult).GetProperties().Select(property => property.Name).Order(StringComparer.Ordinal).ToArray());
        Assert.DoesNotContain(typeof(QuotationCreateRequest).GetProperties(), property =>
            property.Name is "Subtotal" or "Vat" or "Total" or "WithholdingTax" or "QuotedAmount");
    }

    [Fact]
    public void Pricing_PreservesLegacyRoundingVatAndCurrentWithholdingBehavior()
    {
        var input = Request(
            withholding: true,
            new(42, "First", 1, 1000.125m, 5m),
            new(null, "Second", 1, 200m, 0m));

        var priced = QuotationPricing.Calculate(input, new DateTimeOffset(2026, 7, 18, 0, 0, 0, TimeSpan.Zero));

        Assert.Equal(1150.12m, priced.Subtotal);
        Assert.Equal(80.51m, priced.Vat);
        Assert.Equal(1230.63m, priced.Total);
        Assert.Equal(34.50m, priced.WithholdingTax);
        Assert.Equal(1196.13m, priced.QuotedAmount);
        Assert.Equal([950.12m, 200m], priced.Lines.Select(line => line.Subtotal).ToArray());
        Assert.Equal([950.12m, 200m], priced.Lines.Select(line => line.UnitPrice).ToArray());
    }

    [Fact]
    public void CreatePage_IsLazyLocalizedAuthorizedCsrfProtectedAndPreservesRazorRollback()
    {
        var root = FindRoot();
        var pagePath = Path.Combine(root, "Legacy.Maliev.Intranet.Client.Features.Quotations", "Pages", "Quotations", "Create.razor");
        var resourcePath = Path.ChangeExtension(pagePath, ".resx");

        Assert.True(File.Exists(pagePath));
        Assert.True(File.Exists(resourcePath));
        var page = File.ReadAllText(pagePath);
        Assert.Contains("@page \"/Quotations/Create\"", page, StringComparison.Ordinal);
        Assert.Contains("@attribute [Authorize]", page, StringComparison.Ordinal);
        Assert.Contains("MudForm", page, StringComparison.Ordinal);
        Assert.Contains("X-CSRF-TOKEN", page, StringComparison.Ordinal);
        Assert.Contains("Idempotency-Key", page, StringComparison.Ordinal);
        Assert.Contains("/bff/quotations/create", page, StringComparison.Ordinal);
        Assert.Contains("/Quotations/View?id=", page, StringComparison.Ordinal);
        Assert.DoesNotContain("jquery", page, StringComparison.OrdinalIgnoreCase);
        var rollbackRoutes = File.ReadAllText(Path.Combine(root, "Legacy.Maliev.Intranet", "LegacyRoutes.cs"));
        Assert.Contains("/Quotations/Create", rollbackRoutes, StringComparison.Ordinal);
    }

    [Fact]
    public void CreateBff_UsesExactWritePermissionsAntiforgeryAndServerWorkflow()
    {
        var root = FindRoot();
        var program = File.ReadAllText(Path.Combine(root, "Legacy.Maliev.Intranet.Bff", "Program.cs"));
        var auth = File.ReadAllText(Path.Combine(root, "Legacy.Maliev.Intranet.Server", "Auth", "AuthContracts.cs"));

        Assert.Contains("MapGet(\"/bff/quotations/create\"", program, StringComparison.Ordinal);
        Assert.Contains("MapPost(\"/bff/quotations\"", program, StringComparison.Ordinal);
        Assert.Contains("AddEndpointFilter<AntiforgeryValidationFilter>()", program, StringComparison.Ordinal);
        Assert.Contains("LegacyEmployeePermissions.QuotationsCreate", program, StringComparison.Ordinal);
        Assert.Contains("LegacyEmployeePermissions.QuotationLinesWrite", program, StringComparison.Ordinal);
        Assert.Contains("LegacyEmployeePermissions.QuotationOrdersWrite", program, StringComparison.Ordinal);
        Assert.Contains("legacy.quotations.create", auth, StringComparison.Ordinal);
        Assert.Contains("legacy.quotation-lines.write", auth, StringComparison.Ordinal);
        Assert.Contains("legacy.quotation-orders.write", auth, StringComparison.Ordinal);
    }

    private static QuotationCreateRequest Request(bool withholding, params QuotationCreateLine[] lines) =>
        new(3, 2, 1, 30, "Courier", "Bangkok", "Net 30", "fixture", withholding, lines);

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
