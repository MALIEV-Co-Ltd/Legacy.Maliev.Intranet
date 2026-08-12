namespace Legacy.Maliev.Intranet.Tests;

public sealed class OperationsPageVisualSystemContractTests
{
    [Fact]
    public void OperationsStylesFollowThePackageAdapterAndRemainGeometryOnly()
    {
        var index = Read("Legacy.Maliev.Intranet.Client", "wwwroot", "index.html");
        var operations = Read("Legacy.Maliev.Intranet.Client", "wwwroot", "css", "operations-pages.css");

        Assert.True(index.IndexOf("_content/Maliev.ShadcnBlazor/css/shadcn-mudblazor.css", StringComparison.Ordinal) <
                    index.IndexOf("css/operations-pages.css", StringComparison.Ordinal));
        Assert.Contains("overflow-wrap: anywhere", operations, StringComparison.Ordinal);
        Assert.Contains("@media (max-width: 900px)", operations, StringComparison.Ordinal);
        Assert.Contains("@media (max-width: 720px)", operations, StringComparison.Ordinal);
        Assert.Contains("@media (pointer: coarse)", operations, StringComparison.Ordinal);
        Assert.DoesNotContain("background:", operations, StringComparison.Ordinal);
        Assert.DoesNotContain("border:", operations, StringComparison.Ordinal);
        Assert.DoesNotContain("font-family:", operations, StringComparison.Ordinal);
    }

    [Fact]
    public void OperationsGeometryPreservesListFormAndResponsiveRecordLayout()
    {
        var operations = Read("Legacy.Maliev.Intranet.Client", "wwwroot", "css", "operations-pages.css");

        Assert.Contains(".legacy-page-container .mud-table-container", operations, StringComparison.Ordinal);
        Assert.Contains(".legacy-page-container .mud-table-body .mud-table-row", operations, StringComparison.Ordinal);
        Assert.Contains(".legacy-page-container .mud-form", operations, StringComparison.Ordinal);
        Assert.Contains(".legacy-page-container .mud-grid", operations, StringComparison.Ordinal);
        Assert.Contains(".legacy-page-container .mud-tabs-toolbar", operations, StringComparison.Ordinal);
        Assert.DoesNotContain(".mud-button-root {\n        display: none", operations, StringComparison.Ordinal);
        Assert.DoesNotContain(".mud-table-body {\n        display: none", operations, StringComparison.Ordinal);
    }

    private static string Read(params string[] segments) => File.ReadAllText(Path.Combine([FindRoot(), .. segments]));

    private static string FindRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Legacy.Maliev.Intranet.slnx"))) directory = directory.Parent;
        return directory?.FullName ?? throw new DirectoryNotFoundException("Could not find repository root.");
    }
}
