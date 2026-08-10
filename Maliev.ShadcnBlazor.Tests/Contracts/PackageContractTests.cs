using System.Diagnostics;
using System.IO.Compression;

namespace Maliev.ShadcnBlazor.Tests.Contracts;

public sealed class PackageContractTests
{
    [Fact]
    public async Task NupkgContainsReadmeLicensesTokensAndReferenceManifest()
    {
        var output = Path.Combine(Path.GetTempPath(), $"maliev-shadcn-package-{Guid.NewGuid():N}");
        Directory.CreateDirectory(output);
        try
        {
            var root = FindRoot();
            var project = Path.Combine(root, "Maliev.ShadcnBlazor", "Maliev.ShadcnBlazor.csproj");
            var startInfo = new ProcessStartInfo("dotnet")
            {
                WorkingDirectory = root,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            foreach (var argument in new[] { "pack", project, "-c", "Release", "--no-restore", "-o", output })
                startInfo.ArgumentList.Add(argument);

            using var process = Process.Start(startInfo)
                ?? throw new InvalidOperationException("Could not start dotnet pack.");
            var stdoutTask = process.StandardOutput.ReadToEndAsync();
            var stderrTask = process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();
            var stdout = await stdoutTask;
            var stderr = await stderrTask;
            Assert.True(
                process.ExitCode == 0,
                $"dotnet pack exited with code {process.ExitCode}.{Environment.NewLine}stdout:{Environment.NewLine}{stdout}{Environment.NewLine}stderr:{Environment.NewLine}{stderr}");

            var package = Assert.Single(
                Directory.GetFiles(output, "Maliev.ShadcnBlazor.*.nupkg", SearchOption.TopDirectoryOnly),
                path => !path.EndsWith(".symbols.nupkg", StringComparison.OrdinalIgnoreCase));
            using var archive = ZipFile.OpenRead(package);
            var names = archive.Entries.Select(x => x.FullName).ToArray();

            Assert.Contains("README.md", names);
            Assert.Contains("licenses/shadcn-ui-LICENSE.md", names);
            Assert.Contains("licenses/MudBlazor-LICENSE", names);
            Assert.Contains("reference/shadcn-reference.json", names);
            Assert.Contains("staticwebassets/css/shadcn-base.css", names);
            Assert.Contains(names, x => x.EndsWith("lib/net10.0/Maliev.ShadcnBlazor.dll", StringComparison.Ordinal));
        }
        finally
        {
            Directory.Delete(output, recursive: true);
        }
    }

    private static string FindRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Legacy.Maliev.Intranet.slnx")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new DirectoryNotFoundException();
    }
}
