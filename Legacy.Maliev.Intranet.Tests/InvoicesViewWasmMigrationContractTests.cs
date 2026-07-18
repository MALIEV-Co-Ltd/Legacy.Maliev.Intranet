using System.Text.Json;

namespace Legacy.Maliev.Intranet.Tests;

public sealed class InvoicesViewWasmMigrationContractTests
{
    [Fact]
    public void InvoiceView_IsLazyBrowserSafeAndUsesOwnedBffBoundaries()
    {
        var root = FindRoot();
        var pagePath = Path.Combine(root, "Legacy.Maliev.Intranet.Client.Features.Accounting", "Pages", "InvoiceView.razor");
        var program = File.ReadAllText(Path.Combine(root, "Legacy.Maliev.Intranet.Bff", "Program.cs"));
        var proxy = File.ReadAllText(Path.Combine(root, "Legacy.Maliev.Intranet.Bff", "Accounting", "InvoiceDetailProxy.cs"));
        var files = File.ReadAllText(Path.Combine(root, "Legacy.Maliev.Intranet.Bff", "Accounting", "InvoiceFileProxy.cs"));

        Assert.True(File.Exists(pagePath));
        Assert.True(File.Exists(Path.ChangeExtension(pagePath, ".resx")));
        var page = File.ReadAllText(pagePath);
        Assert.Contains("@page \"/Invoices/View\"", page, StringComparison.Ordinal);
        Assert.Contains("/bff/invoices/", page, StringComparison.Ordinal);
        Assert.Contains("X-CSRF-TOKEN", page, StringComparison.Ordinal);
        Assert.Contains("If-Unmodified-Since", proxy, StringComparison.Ordinal);
        Assert.Contains("/uploads/SignedUrl", files, StringComparison.Ordinal);
        Assert.Contains("AddEndpointFilter<AntiforgeryValidationFilter>()", program, StringComparison.Ordinal);
        Assert.Contains("LegacyEmployeePermissions.AccountingRead", program, StringComparison.Ordinal);
        Assert.Contains("LegacyEmployeePermissions.AccountingUpdate", program, StringComparison.Ordinal);
        Assert.Contains("LegacyEmployeePermissions.AccountingDelete", program, StringComparison.Ordinal);
        Assert.DoesNotContain("access_token", page, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("jquery", page, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void InvoiceDetailContract_PreservesMoneyDatesAndExcludesStorageCredentials()
    {
        var assembly = typeof(Legacy.Maliev.Intranet.Contracts.InvoiceListItem).Assembly;
        var detailType = assembly.GetType("Legacy.Maliev.Intranet.Contracts.InvoiceDetailPage");
        Assert.NotNull(detailType);
        const string json = """{"invoice":{"id":7,"customerId":3,"number":"INV-7","currency":"THB","subtotal":1000.25,"vat":70.02,"total":1070.27,"withholdingTax":30.00,"outstanding":1040.27,"isPaid":true,"receiptId":9,"paymentDate":"2030-07-18T00:00:00Z","modifiedDate":"2030-07-19T00:00:00Z"},"orderItems":[{"id":2,"invoiceId":7,"description":"Thai tone mark: น้ำ","quantity":2,"unitPrice":500.125,"subtotal":1000.25,"modifiedDate":"2030-07-19T00:00:00Z"}],"invoiceFiles":[{"id":4,"invoiceId":7,"objectName":"accounting/invoices/7/invoice.pdf","uri":"https://storage.test/signed"}],"receiptFiles":[{"id":5,"receiptId":9,"objectName":"accounting/receipts/9/receipt.pdf","uri":"https://storage.test/receipt"}]}""";

        var value = JsonSerializer.Deserialize(json, detailType, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.NotNull(value);
        var wire = JsonSerializer.SerializeToElement(value, detailType, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.Equal(1040.27m, wire.GetProperty("invoice").GetProperty("outstanding").GetDecimal());
        Assert.Equal(500.125m, wire.GetProperty("orderItems")[0].GetProperty("unitPrice").GetDecimal());
        Assert.Equal("Thai tone mark: น้ำ", wire.GetProperty("orderItems")[0].GetProperty("description").GetString());
        Assert.False(wire.GetProperty("invoiceFiles")[0].TryGetProperty("bucket", out _));
        Assert.False(wire.GetProperty("receiptFiles")[0].TryGetProperty("bucket", out _));
        Assert.DoesNotContain("token", wire.ToString(), StringComparison.OrdinalIgnoreCase);
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
