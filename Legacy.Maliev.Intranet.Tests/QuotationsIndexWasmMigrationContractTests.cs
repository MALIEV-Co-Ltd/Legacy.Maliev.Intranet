using System.Text.Json;

namespace Legacy.Maliev.Intranet.Tests;

public sealed class QuotationsIndexWasmMigrationContractTests
{
    [Fact]
    public void QuotationIndex_IsLazyBrowserSafeAndUsesTheQuotationBff()
    {
        var root = FindRoot();
        var page = Path.Combine(root, "Legacy.Maliev.Intranet.Client.Features.Quotations", "Pages", "Quotations", "Index.razor");
        var app = File.ReadAllText(Path.Combine(root, "Legacy.Maliev.Intranet.Client", "App.razor"));
        var bff = File.ReadAllText(Path.Combine(root, "Legacy.Maliev.Intranet.Bff", "Program.cs"));

        Assert.True(File.Exists(page));
        Assert.True(File.Exists(Path.ChangeExtension(page, ".resx")));
        var source = File.ReadAllText(page);
        Assert.Contains("@page \"/Quotations/Index\"", source, StringComparison.Ordinal);
        Assert.Contains("/bff/quotations", source, StringComparison.Ordinal);
        Assert.Contains("MudTable", source, StringComparison.Ordinal);
        Assert.Contains("MudChart", source, StringComparison.Ordinal);
        Assert.Contains("Quotations/", app, StringComparison.Ordinal);
        Assert.Contains("MapGet(\"/bff/quotations\"", bff, StringComparison.Ordinal);
        Assert.Contains("LegacyEmployeePermissions.QuotationsRead", bff, StringComparison.Ordinal);
        Assert.DoesNotContain("access_token", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("jquery", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Prediction", source, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void QuotationContracts_PreserveDeterministicDecimalAndDateWireTypes()
    {
        var assembly = typeof(Legacy.Maliev.Intranet.Contracts.EmployeeSessionSummary).Assembly;
        var itemType = assembly.GetType("Legacy.Maliev.Intranet.Contracts.QuotationListItem");
        Assert.NotNull(itemType);
        var json = """{"id":7,"customerId":3,"employeeId":2,"invoiceId":null,"period":14,"expirationDate":"2030-08-01T00:00:00Z","subtotal":1000.25,"vat":70.02,"total":1070.27,"withholdingTax":30.00,"quotedAmount":1040.27,"currencyId":1,"comment":"fixture","fob":"Bangkok","shippedVia":"Courier","terms":"Net 7","accepted":null,"createdDate":"2030-07-18T00:00:00Z","modifiedDate":null}""";
        var value = JsonSerializer.Deserialize(json, itemType, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.NotNull(value);
        var wire = JsonSerializer.SerializeToElement(value, itemType, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.Equal(1040.27m, wire.GetProperty("quotedAmount").GetDecimal());
        Assert.Equal(DateTime.Parse("2030-08-01T00:00:00Z").ToUniversalTime(), wire.GetProperty("expirationDate").GetDateTime().ToUniversalTime());
        Assert.False(wire.TryGetProperty("orderItems", out _));
        Assert.False(wire.TryGetProperty("files", out _));
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
