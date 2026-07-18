using System.Text.Json;

namespace Legacy.Maliev.Intranet.Tests;

public sealed class InvoicesIndexWasmMigrationContractTests
{
    [Fact]
    public void InvoiceIndex_IsLazyBrowserSafeAndUsesTheAccountingBff()
    {
        var root = FindRoot();
        var pagePath = Path.Combine(root, "Legacy.Maliev.Intranet.Client.Features.Accounting", "Pages", "Invoices.razor");
        var app = File.ReadAllText(Path.Combine(root, "Legacy.Maliev.Intranet.Client", "App.razor"));
        var bff = File.ReadAllText(Path.Combine(root, "Legacy.Maliev.Intranet.Bff", "Program.cs"));

        Assert.True(File.Exists(pagePath));
        Assert.True(File.Exists(Path.ChangeExtension(pagePath, ".resx")));
        var page = File.ReadAllText(pagePath);
        Assert.Contains("@page \"/Invoices/Index\"", page, StringComparison.Ordinal);
        Assert.Contains("/bff/invoices", page, StringComparison.Ordinal);
        Assert.Contains("paid=true", page, StringComparison.Ordinal);
        Assert.Contains("paid=false", page, StringComparison.Ordinal);
        Assert.Contains("MudTable", page, StringComparison.Ordinal);
        Assert.Contains("Invoices/", app, StringComparison.Ordinal);
        Assert.Contains("MapGet(\"/bff/invoices\"", bff, StringComparison.Ordinal);
        Assert.Contains("LegacyEmployeePermissions.AccountingRead", bff, StringComparison.Ordinal);
        Assert.DoesNotContain("access_token", page, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("jquery", page, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void InvoiceContracts_PreserveLegacyMoneyAndDateWireTypes()
    {
        var assembly = typeof(Legacy.Maliev.Intranet.Contracts.EmployeeSessionSummary).Assembly;
        var itemType = assembly.GetType("Legacy.Maliev.Intranet.Contracts.InvoiceListItem");
        Assert.NotNull(itemType);
        var json = """{"id":7,"customerId":3,"number":"INV-7","currency":"THB","purchaseOrderNumber":"PO-7","subtotal":1000.25,"vat":70.02,"total":1070.27,"withholdingTax":30.00,"outstanding":1040.27,"isPaid":false,"receiptId":null,"paymentDate":null,"createdDate":"2030-07-18T00:00:00Z"}""";
        var value = JsonSerializer.Deserialize(json, itemType, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.NotNull(value);
        var wire = JsonSerializer.SerializeToElement(value, itemType, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.Equal(1040.27m, wire.GetProperty("outstanding").GetDecimal());
        Assert.Equal(DateTime.Parse("2030-07-18T00:00:00Z").ToUniversalTime(), wire.GetProperty("createdDate").GetDateTime().ToUniversalTime());
        Assert.False(wire.TryGetProperty("invoiceFiles", out _));
        Assert.False(wire.TryGetProperty("invoiceOrderItems", out _));
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
