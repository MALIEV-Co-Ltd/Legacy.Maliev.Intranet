namespace Legacy.Maliev.Intranet.Tests;

public sealed class SupplierViewWasmMigrationContractTests
{
    [Fact]
    public void SupplierView_IsAuthorizedLocalizedAndUsesCsrfProtectedBffWrites()
    {
        var root = FindRoot();
        var pagePath = Path.Combine(root, "Legacy.Maliev.Intranet.Client.Features.Procurement", "Pages", "SupplierView.razor");
        var resourcePath = Path.ChangeExtension(pagePath, ".resx");
        Assert.True(File.Exists(pagePath));
        Assert.True(File.Exists(resourcePath));
        var page = File.ReadAllText(pagePath);
        Assert.Contains("@page \"/Suppliers/View\"", page, StringComparison.Ordinal);
        Assert.Contains("@attribute [Authorize]", page, StringComparison.Ordinal);
        Assert.Contains("[SupplyParameterFromQuery", page, StringComparison.Ordinal);
        Assert.Contains("$\"/bff/suppliers/{Id}\"", page, StringComparison.Ordinal);
        Assert.Contains("HttpMethod.Put", page, StringComparison.Ordinal);
        Assert.Contains("HttpMethod.Delete", page, StringComparison.Ordinal);
        Assert.Equal(1, Count(page, "X-CSRF-TOKEN"));
        Assert.Contains("MudForm", page, StringComparison.Ordinal);
        Assert.Contains("Fields[\"Supplier\"]", page, StringComparison.Ordinal);
        Assert.Contains("Fields[\"Address\"]", page, StringComparison.Ordinal);
        Assert.Contains("InputType.Telephone", page, StringComparison.Ordinal);
        Assert.DoesNotContain("forceLoad: true", page, StringComparison.Ordinal);
        Assert.DoesNotContain("jquery", page, StringComparison.OrdinalIgnoreCase);
        Assert.True(File.Exists(Path.Combine(root, "Legacy.Maliev.Intranet", "Pages", "Suppliers", "View.cshtml")));
        var bff = File.ReadAllText(Path.Combine(root, "Legacy.Maliev.Intranet.Bff", "Program.cs"));
        Assert.Contains("MapGet(\"/bff/suppliers/{id:int}\"", bff, StringComparison.Ordinal);
        Assert.Contains("MapPut(\"/bff/suppliers/{id:int}\"", bff, StringComparison.Ordinal);
        Assert.Contains("MapDelete(\"/bff/suppliers/{id:int}\"", bff, StringComparison.Ordinal);
        Assert.Contains("LegacyEmployeePermissions.SuppliersUpdate", bff, StringComparison.Ordinal);
        Assert.Contains("LegacyEmployeePermissions.SuppliersDelete", bff, StringComparison.Ordinal);
        Assert.True(Count(bff, "AddEndpointFilter<AntiforgeryValidationFilter>()") >= 2);
    }

    [Fact]
    public void SupplierDetail_ExposesOnlyEditableLegacyFields()
    {
        var properties = typeof(Legacy.Maliev.Intranet.Contracts.SupplierDetail).GetProperties()
            .Select(property => property.Name).Order(StringComparer.Ordinal).ToArray();
        Assert.Equal(["Address1", "Address2", "Building", "City", "CountryId", "Email", "Fax", "Id", "Mobile", "Name", "Note", "PostalCode", "State", "TaxNumber", "Telephone", "Website"], properties);
    }

    private static int Count(string value, string search) => value.Split(search, StringSplitOptions.None).Length - 1;
    private static string FindRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Legacy.Maliev.Intranet.slnx"))) directory = directory.Parent;
        return directory?.FullName ?? throw new DirectoryNotFoundException();
    }
}
