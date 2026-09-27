namespace Legacy.Maliev.Intranet.Tests;

public sealed class CustomersUpdateWasmMigrationContractTests
{
    [Fact]
    public void CustomerUpdateSlice_PreservesLegacyIntegerWriteBoundaryAndNoRetryPolicy()
    {
        var root = FindRepositoryRoot();
        var view = File.ReadAllText(Path.Combine(root, "Legacy.Maliev.Intranet.Client.Features.Customers", "Pages", "CustomerView.razor"));
        var contracts = File.ReadAllText(Path.Combine(root, "Legacy.Maliev.Intranet.Contracts", "CustomerUpdateContracts.cs"));
        var bff = File.ReadAllText(Path.Combine(root, "Legacy.Maliev.Intranet.Bff", "Program.cs"));
        var proxy = File.ReadAllText(Path.Combine(root, "Legacy.Maliev.Intranet.Bff", "Customers", "CustomerUpdateProxy.cs"));
        var mapper = File.ReadAllText(Path.Combine(root, "Legacy.Maliev.Intranet.Bff", "Customers", "CustomerUpdateEndpointMapper.cs"));
        var auth = File.ReadAllText(Path.Combine(root, "Legacy.Maliev.Intranet.Server", "Auth", "AuthContracts.cs"));
        var english = File.ReadAllText(Path.Combine(root, "Legacy.Maliev.Intranet.Client.Features.Customers", "Pages", "CustomerView.resx"));
        var thai = File.ReadAllText(Path.Combine(root, "Legacy.Maliev.Intranet.Client.Features.Customers", "Pages", "CustomerView.th.resx"));

        Assert.Contains("CustomerUpdateRequest", view, StringComparison.Ordinal);
        Assert.Contains("HttpMethod.Put", view, StringComparison.Ordinal);
        Assert.Contains("/bff/customers/{Id}/versioned", view, StringComparison.Ordinal);
        Assert.Contains("response.Headers.ETag", view, StringComparison.Ordinal);
        Assert.Contains("request.Headers.TryAddWithoutValidation(\"If-Match\", customerRevision)", view, StringComparison.Ordinal);
        Assert.Contains("HttpStatusCode.PreconditionFailed", view, StringComparison.Ordinal);
        Assert.Contains("ReloadLatest", view, StringComparison.Ordinal);
        Assert.Contains("@if (operationConflict)", view, StringComparison.Ordinal);
        Assert.Contains("<ShadcnButton OnClick=\"LoadAsync\">@Text[\"ReloadLatest\"]</ShadcnButton>", view, StringComparison.Ordinal);
        Assert.Contains("operationConflict = true;\n                return;", view.Replace("\r\n", "\n", StringComparison.Ordinal), StringComparison.Ordinal);
        Assert.Contains("name=\"ConflictReload\"", english, StringComparison.Ordinal);
        Assert.Contains("name=\"ReloadLatest\"", english, StringComparison.Ordinal);
        Assert.Contains("name=\"ConflictReload\"", thai, StringComparison.Ordinal);
        Assert.Contains("name=\"ReloadLatest\"", thai, StringComparison.Ordinal);
        Assert.Contains("/bff/customers/{id:int}", bff, StringComparison.Ordinal);
        Assert.Contains("/bff/customers/{id:int}/versioned", bff, StringComparison.Ordinal);
        Assert.Contains("CustomerUpdateEndpointMapper.UpdateAsync", bff, StringComparison.Ordinal);
        Assert.Contains("AddEndpointFilter<AntiforgeryValidationFilter>()", bff, StringComparison.Ordinal);
        Assert.Contains("LegacyEmployeePermissions.CustomersUpdate", bff, StringComparison.Ordinal);
        Assert.Contains("RemoveAllResilienceHandlers", bff, StringComparison.Ordinal);
        Assert.Contains("HttpMethod.Put", proxy, StringComparison.Ordinal);
        Assert.Contains("CompanyId", proxy, StringComparison.Ordinal);
        Assert.Contains("BillingAddressId", proxy, StringComparison.Ordinal);
        Assert.Contains("ShippingAddressId", proxy, StringComparison.Ordinal);
        Assert.Contains("Validator.TryValidateObject", mapper, StringComparison.Ordinal);
        Assert.Contains("Results.Conflict", mapper, StringComparison.Ordinal);
        Assert.Contains("CustomersUpdate = \"legacy-customer.customers.update\"", auth, StringComparison.Ordinal);
        Assert.Contains("[Required, EmailAddress, StringLength(320)]", contracts, StringComparison.Ordinal);
        Assert.DoesNotContain("Guid", proxy, StringComparison.Ordinal);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Legacy.Maliev.Intranet.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new DirectoryNotFoundException("Legacy.Maliev.Intranet repository root was not found.");
    }
}
