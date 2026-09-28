using System.Xml.Linq;

namespace Legacy.Maliev.Intranet.Tests;

public sealed class UploadEdgeLimitContractTests
{
    [Fact]
    public void OrderAndFinanceUploadConsumers_EnforceShared100MiBAggregate()
    {
        var root = FindRoot();
        foreach (var relativePath in new[]
        {
            "Orders/OrderCreateEndpointMapper.cs",
            "Orders/OrderDetailEndpointMapper.cs",
            "Accounting/FinanceCreateEndpointMapper.cs",
            "Accounting/FinanceDetailEndpointMapper.cs",
        })
        {
            var source = File.ReadAllText(Path.Combine(root, "Legacy.Maliev.Intranet.Bff", relativePath));
            Assert.Contains("100L * 1024 * 1024", source, StringComparison.Ordinal);
            Assert.DoesNotContain("200L * 1024 * 1024", source, StringComparison.Ordinal);
        }

        var routes = File.ReadAllText(Path.Combine(root, "Legacy.Maliev.Intranet.Bff", "Program.cs"));
        Assert.Equal(2, Count(routes, "RequestSizeLimitAttribute(101L * 1024 * 1024)"));
        Assert.Equal(2, Count(routes, "MultipartBodyLengthLimit = 101L * 1024 * 1024"));
        var legacyHost = File.ReadAllText(Path.Combine(root, "Legacy.Maliev.Intranet", "Program.cs"));
        Assert.Contains("maximumRequestBytes = 101L * 1024L * 1024L", legacyHost, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Legacy.Maliev.Intranet.Client.Features.Orders", "Pages/OrderCreate", "FilesTooLarge")]
    [InlineData("Legacy.Maliev.Intranet.Client.Features.Accounting", "Pages/FinanceCreate", "SelectedFiles")]
    [InlineData("Legacy.Maliev.Intranet.Client.Features.Accounting", "Pages/FinanceCreate", "FilesTooLarge")]
    [InlineData("Legacy.Maliev.Intranet.Client.Features.Orders", "Pages/OrderDetail", "FilesTooLarge")]
    [InlineData("Legacy.Maliev.Intranet.Client.Features.Accounting", "Pages/FinanceView", "FilesTooLarge")]
    public void EmployeeForms_Advertise100MBInEnglishAndThai(string project, string page, string resourceKey)
    {
        var root = FindRoot();
        foreach (var suffix in new[] { ".resx", ".th.resx" })
        {
            var resource = XDocument.Load(Path.Combine(root, project, page + suffix));
            var value = resource.Descendants("data")
                .Single(item => (string?)item.Attribute("name") == resourceKey)
                .Element("value")?.Value;
            Assert.Contains("100 MB", value, StringComparison.Ordinal);
            Assert.DoesNotContain("200 MB", value, StringComparison.Ordinal);
        }
    }

    private static int Count(string source, string value) =>
        source.Split(value, StringSplitOptions.None).Length - 1;

    private static string FindRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Legacy.Maliev.Intranet.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new DirectoryNotFoundException("Intranet solution root was not found.");
    }
}
