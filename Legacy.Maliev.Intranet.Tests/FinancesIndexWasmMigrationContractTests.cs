using System.Text.Json;

namespace Legacy.Maliev.Intranet.Tests;

public sealed class FinancesIndexWasmMigrationContractTests
{
    [Fact]
    public void FinanceIndex_IsLazyBrowserSafeAndAccountingReadOnly()
    {
        var root = FindRoot();
        var feature = Path.Combine(root, "Legacy.Maliev.Intranet.Client.Features.Accounting");
        var pagePath = Path.Combine(feature, "Pages", "Finances.razor");
        var clientProject = File.ReadAllText(Path.Combine(root, "Legacy.Maliev.Intranet.Client", "Legacy.Maliev.Intranet.Client.csproj"));
        var app = File.ReadAllText(Path.Combine(root, "Legacy.Maliev.Intranet.Client", "App.razor"));
        var bff = File.ReadAllText(Path.Combine(root, "Legacy.Maliev.Intranet.Bff", "Program.cs"));
        var auth = File.ReadAllText(Path.Combine(root, "Legacy.Maliev.Intranet.Server", "Auth", "AuthContracts.cs"));

        Assert.True(File.Exists(Path.Combine(feature, "Legacy.Maliev.Intranet.Client.Features.Accounting.csproj")));
        Assert.True(File.Exists(pagePath));
        Assert.True(File.Exists(Path.ChangeExtension(pagePath, ".resx")));
        var page = File.ReadAllText(pagePath);
        Assert.Contains("@page \"/Finances/Index\"", page, StringComparison.Ordinal);
        Assert.Contains("/bff/finances", page, StringComparison.Ordinal);
        Assert.Contains("MudTable", page, StringComparison.Ordinal);
        Assert.Contains("Legacy.Maliev.Intranet.Client.Features.Accounting.wasm", clientProject, StringComparison.Ordinal);
        Assert.Contains("Finances/", app, StringComparison.Ordinal);
        Assert.Contains("LegacyEmployeePermissions.AccountingRead", bff, StringComparison.Ordinal);
        Assert.Contains("MapGet(\"/bff/finances\"", bff, StringComparison.Ordinal);
        Assert.Contains("legacy.accounting.read", auth, StringComparison.Ordinal);
        Assert.DoesNotContain("legacy.accounting.create", page, StringComparison.Ordinal);
        Assert.DoesNotContain("access_token", page, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("jquery", page, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void FinanceContracts_PreserveDecimalAndDateWireTypesWithoutServiceNavigations()
    {
        var assembly = typeof(Legacy.Maliev.Intranet.Contracts.EmployeeSessionSummary).Assembly;
        var itemType = assembly.GetType("Legacy.Maliev.Intranet.Contracts.FinancePaymentItem");
        Assert.NotNull(itemType);
        var json = """{"id":7,"employeeId":3,"paymentDirectionId":1,"paymentTypeId":2,"description":"Thai fixture","paymentMethodId":4,"amount":1234.56,"currencyId":1,"recipient":"MALIEV","transactionNumber":"TX-7","paymentDate":"2030-07-18T00:00:00Z","createdDate":"2030-07-17T00:00:00Z","modifiedDate":null}""";
        var value = JsonSerializer.Deserialize(json, itemType, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.NotNull(value);
        var wire = JsonSerializer.SerializeToElement(value, itemType, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.Equal(1234.56m, wire.GetProperty("amount").GetDecimal());
        Assert.Equal(DateTime.Parse("2030-07-18T00:00:00Z").ToUniversalTime(), wire.GetProperty("paymentDate").GetDateTime().ToUniversalTime());
        Assert.False(wire.TryGetProperty("paymentDirection", out _));
        Assert.False(wire.TryGetProperty("paymentFiles", out _));
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
