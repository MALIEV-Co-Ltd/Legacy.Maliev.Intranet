using System.Xml.Linq;

namespace Legacy.Maliev.Intranet.Tests;

public sealed class IntranetWasmRuntimeContractTests
{
    [Fact]
    public void ClientProject_IncludesAllGlobalizationDataForSupportedRuntimeCultures()
    {
        var root = FindRoot();
        var projectPath = Path.Combine(
            root,
            "Legacy.Maliev.Intranet.Client",
            "Legacy.Maliev.Intranet.Client.csproj");

        var project = XDocument.Load(projectPath);
        var properties = project.Root?.Elements().FirstOrDefault(element => element.Name.LocalName == "PropertyGroup");

        Assert.Equal(
            "false",
            properties?.Elements().FirstOrDefault(element => element.Name.LocalName == "InvariantGlobalization")?.Value,
            ignoreCase: true);
        Assert.Equal(
            "true",
            properties?.Elements().FirstOrDefault(element => element.Name.LocalName == "BlazorWebAssemblyLoadAllGlobalizationData")?.Value,
            ignoreCase: true);
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
