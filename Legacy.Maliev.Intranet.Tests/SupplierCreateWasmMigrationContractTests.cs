using System.ComponentModel.DataAnnotations;

namespace Legacy.Maliev.Intranet.Tests;

public sealed class SupplierCreateWasmMigrationContractTests
{
    [Fact]
    public void SupplierCreate_IsLazyAuthorizedLocalizedAndKeepsRazorFallback()
    {
        var root = FindRoot();
        var pagePath = Path.Combine(root, "Legacy.Maliev.Intranet.Client.Features.Procurement", "Pages", "SupplierCreate.razor");
        var resourcePath = Path.ChangeExtension(pagePath, ".resx");

        Assert.True(File.Exists(pagePath));
        Assert.True(File.Exists(resourcePath));

        var page = File.ReadAllText(pagePath);
        Assert.Contains("@page \"/Suppliers/Create\"", page, StringComparison.Ordinal);
        Assert.Contains("@attribute [Authorize]", page, StringComparison.Ordinal);
        Assert.Contains("MudForm", page, StringComparison.Ordinal);
        Assert.Contains("/bff/suppliers", page, StringComparison.Ordinal);
        Assert.Contains("X-CSRF-TOKEN", page, StringComparison.Ordinal);
        Assert.Contains("HttpStatusCode.Unauthorized", page, StringComparison.Ordinal);
        Assert.Contains("HttpStatusCode.Forbidden", page, StringComparison.Ordinal);
        Assert.Contains("HttpStatusCode.Conflict", page, StringComparison.Ordinal);
        Assert.Contains("HttpStatusCode.TooManyRequests", page, StringComparison.Ordinal);
        Assert.DoesNotContain("forceLoad: true", page, StringComparison.Ordinal);
        Assert.DoesNotContain("jquery", page, StringComparison.OrdinalIgnoreCase);
        Assert.True(File.Exists(Path.Combine(root, "Legacy.Maliev.Intranet", "Pages", "Suppliers", "Create.cshtml")));
    }

    [Fact]
    public void SupplierCreateRequest_PreservesLegacyFieldsAndValidation()
    {
        var type = typeof(Legacy.Maliev.Intranet.Contracts.SupplierCreateRequest);
        Assert.Equal(
            ["Address1", "Address2", "Building", "City", "CountryId", "Email", "Fax", "Mobile", "Name", "Note", "PostalCode", "State", "TaxNumber", "Telephone", "Website"],
            type.GetProperties().Select(property => property.Name).Order(StringComparer.Ordinal).ToArray());

        var invalid = new Legacy.Maliev.Intranet.Contracts.SupplierCreateRequest();
        var results = new List<ValidationResult>();
        Assert.False(Validator.TryValidateObject(invalid, new ValidationContext(invalid), results, true));
        Assert.Contains(results, result => result.MemberNames.Contains("Name", StringComparer.Ordinal));
        Assert.Contains(results, result => result.MemberNames.Contains("Address1", StringComparer.Ordinal));
        Assert.Contains(results, result => result.MemberNames.Contains("CountryId", StringComparer.Ordinal));
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
