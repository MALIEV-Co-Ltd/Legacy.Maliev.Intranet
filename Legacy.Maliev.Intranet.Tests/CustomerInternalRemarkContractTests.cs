using System.Reflection;
using Legacy.Maliev.Intranet.Contracts;

namespace Legacy.Maliev.Intranet.Tests;

public sealed class CustomerInternalRemarkContractTests
{
    [Fact]
    public void InternalRemarkContract_IsBoundedAndExcludedFromPublicCustomerProjections()
    {
        var contracts = typeof(CustomerDetail).Assembly;
        var response = contracts.GetType("Legacy.Maliev.Intranet.Contracts.CustomerInternalRemarkResponse");
        var update = contracts.GetType("Legacy.Maliev.Intranet.Contracts.CustomerInternalRemarkUpdateRequest");

        Assert.NotNull(response);
        Assert.NotNull(update);
        Assert.DoesNotContain(typeof(CustomerDetail).GetProperties(), HasRemarkName);
        Assert.DoesNotContain(typeof(CustomerListItem).GetProperties(), HasRemarkName);
        var remark = update.GetProperty("InternalRemark");
        Assert.NotNull(remark);
        var length = Assert.Single(remark.GetCustomAttributes(), attribute =>
            attribute.GetType().Name == "StringLengthAttribute");
        Assert.Equal(4000, length.GetType().GetProperty("MaximumLength")!.GetValue(length));
    }

    [Fact]
    public void CustomerRemarkUi_HasDedicatedLocalizedSecureStatesAndWriteBoundary()
    {
        var root = FindRepositoryRoot();
        var page = File.ReadAllText(Path.Combine(root, "Legacy.Maliev.Intranet.Client.Features.Customers", "Pages", "CustomerView.razor"));
        var componentPath = Path.Combine(root, "Legacy.Maliev.Intranet.Client.Features.Customers", "Components", "CustomerInternalRemark.razor");
        var mapperPath = Path.Combine(root, "Legacy.Maliev.Intranet.Bff", "Customers", "CustomerInternalRemarkEndpointMapper.cs");
        var english = File.ReadAllText(Path.Combine(root, "Legacy.Maliev.Intranet.Client.Features.Customers", "Pages", "CustomerView.resx"));
        var thai = File.ReadAllText(Path.Combine(root, "Legacy.Maliev.Intranet.Client.Features.Customers", "Pages", "CustomerView.th.resx"));

        Assert.True(File.Exists(componentPath));
        Assert.True(File.Exists(mapperPath));
        Assert.Contains("/bff/customers/{Id}/internal-remark", page, StringComparison.Ordinal);
        Assert.Contains("X-CSRF-TOKEN", page, StringComparison.Ordinal);
        Assert.Contains("CustomerInternalRemark", page, StringComparison.Ordinal);
        Assert.Contains("InternalRemarkPrivateDescription", english, StringComparison.Ordinal);
        Assert.Contains("InternalRemarkPrivateDescription", thai, StringComparison.Ordinal);
        Assert.Contains("เห็นเฉพาะพนักงาน", thai, StringComparison.Ordinal);
    }

    private static bool HasRemarkName(PropertyInfo property) =>
        property.Name.Contains("Remark", StringComparison.OrdinalIgnoreCase);

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
