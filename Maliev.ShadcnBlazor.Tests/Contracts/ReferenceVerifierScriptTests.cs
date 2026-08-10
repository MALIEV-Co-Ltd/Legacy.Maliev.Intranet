using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Maliev.ShadcnBlazor.Tests.Contracts;

public sealed class ReferenceVerifierScriptTests
{
    [Fact]
    public async Task MatchingFixtureExitsZeroAndReportsEveryPinnedSource()
    {
        using var fixture = ReferenceFixture.Create();

        var result = await RunVerifierAsync(fixture);

        Assert.Equal(0, result.ExitCode);
        Assert.Contains(
            $"Verified 61 Base registry files and Vega style at {fixture.Commit}.",
            result.Output,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task ShaMismatchExitsNonzeroWithExpectedAndActualDiagnostic()
    {
        using var fixture = ReferenceFixture.Create(mismatchFirstRegistrySha: true);

        var result = await RunVerifierAsync(fixture);

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("Pinned Shadcn reference mismatch", result.Output, StringComparison.Ordinal);
        Assert.Contains("Accordion: expected", result.Output, StringComparison.Ordinal);
        Assert.Contains("received 0000000000000000000000000000000000000000", result.Output, StringComparison.Ordinal);
    }

    private static async Task<ProcessResult> RunVerifierAsync(ReferenceFixture fixture)
    {
        var root = FindRoot();
        var script = Path.Combine(root, "scripts", "verify-shadcn-reference.ps1");
        var startInfo = new ProcessStartInfo("pwsh")
        {
            WorkingDirectory = root,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        startInfo.ArgumentList.Add("-NoProfile");
        startInfo.ArgumentList.Add("-File");
        startInfo.ArgumentList.Add(script);
        startInfo.ArgumentList.Add("-ManifestPath");
        startInfo.ArgumentList.Add(fixture.ManifestPath);
        startInfo.ArgumentList.Add("-RegistryResponsePath");
        startInfo.ArgumentList.Add(fixture.RegistryResponsePath);
        startInfo.ArgumentList.Add("-StyleResponsePath");
        startInfo.ArgumentList.Add(fixture.StyleResponsePath);

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Could not start PowerShell.");
        var standardOutput = process.StandardOutput.ReadToEndAsync();
        var standardError = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        var output = $"{await standardOutput}{Environment.NewLine}{await standardError}";
        return new ProcessResult(process.ExitCode, output);
    }

    private static string FindRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Legacy.Maliev.Intranet.slnx")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new DirectoryNotFoundException();
    }

    private sealed class ReferenceFixture : IDisposable
    {
        private readonly string _directory;

        private ReferenceFixture(string directory, string commit)
        {
            _directory = directory;
            Commit = commit;
            ManifestPath = Path.Combine(directory, "manifest.json");
            RegistryResponsePath = Path.Combine(directory, "registry.json");
            StyleResponsePath = Path.Combine(directory, "style.json");
        }

        public string Commit { get; }
        public string ManifestPath { get; }
        public string RegistryResponsePath { get; }
        public string StyleResponsePath { get; }

        public static ReferenceFixture Create(bool mismatchFirstRegistrySha = false)
        {
            var directory = Path.Combine(Path.GetTempPath(), $"shadcn-reference-{Guid.NewGuid():N}");
            Directory.CreateDirectory(directory);
            var sourceManifestPath = Path.Combine(FindRoot(), "Maliev.ShadcnBlazor", "Reference", "shadcn-reference.json");
            var manifest = JsonNode.Parse(File.ReadAllText(sourceManifestPath))!.AsObject();
            var fixture = new ReferenceFixture(directory, manifest["commit"]!.GetValue<string>());
            File.Copy(sourceManifestPath, fixture.ManifestPath);

            var registry = new JsonArray();
            var registryIndex = 0;
            foreach (var componentNode in manifest["components"]!.AsArray())
            {
                var component = componentNode!.AsObject();
                if (component["sourceKind"]!.GetValue<string>() != "registry-file")
                    continue;
                var sha = component["blobSha"]!.GetValue<string>();
                if (mismatchFirstRegistrySha && registryIndex == 0)
                    sha = new string('0', 40);
                registry.Add(new JsonObject
                {
                    ["name"] = $"{component["slug"]!.GetValue<string>()}.tsx",
                    ["sha"] = sha
                });
                registryIndex++;
            }

            File.WriteAllText(fixture.RegistryResponsePath, registry.ToJsonString());
            File.WriteAllText(fixture.StyleResponsePath, new JsonObject
            {
                ["sha"] = manifest["styleSource"]!["blobSha"]!.GetValue<string>()
            }.ToJsonString());
            return fixture;
        }

        public void Dispose() => Directory.Delete(_directory, recursive: true);
    }

    private sealed record ProcessResult(int ExitCode, string Output);
}
