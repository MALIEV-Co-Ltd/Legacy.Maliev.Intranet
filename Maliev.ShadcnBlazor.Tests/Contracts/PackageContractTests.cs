using System.IO.Compression;

namespace Maliev.ShadcnBlazor.Tests.Contracts;

public sealed class PackageContractTests
{
    [Fact]
    public void NupkgContainsReadmeLicensesTokensAndReferenceManifest()
    {
        var output = Path.Combine(FindRoot(), ".artifacts", "packages");
        var package = Directory.GetFiles(output, "Maliev.ShadcnBlazor.*.nupkg", SearchOption.TopDirectoryOnly)
            .Where(path => !path.EndsWith(".symbols.nupkg", StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .First();
        using var archive = ZipFile.OpenRead(package);
        var names = archive.Entries.Select(x => x.FullName).ToArray();

        Assert.Contains("README.md", names);
        Assert.Contains("licenses/shadcn-ui-LICENSE.md", names);
        Assert.Contains("licenses/MudBlazor-LICENSE", names);
        Assert.Contains("reference/shadcn-reference.json", names);
        Assert.Contains("staticwebassets/css/shadcn-base.css", names);
        Assert.Contains(names, x => x.EndsWith("lib/net10.0/Maliev.ShadcnBlazor.dll", StringComparison.Ordinal));
    }

    private static string FindRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Legacy.Maliev.Intranet.slnx")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new DirectoryNotFoundException();
    }
}
