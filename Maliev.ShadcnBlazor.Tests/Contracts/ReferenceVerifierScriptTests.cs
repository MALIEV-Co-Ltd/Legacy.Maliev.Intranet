using System.Diagnostics;
using System.Text.Json.Nodes;

namespace Maliev.ShadcnBlazor.Tests.Contracts;

public sealed class ReferenceVerifierScriptTests
{
    [Fact]
    public async Task CheckedInManifestMatchesEveryPinnedUpstreamSource()
    {
        var root = FindRoot();

        var result = await RunVerifierAsync(root);

        Assert.Equal(0, result.ExitCode);
        Assert.Contains(
            "Verified 61 Base registry files and Vega style at 6261bd89f72d794aea491482cc2acfd8dc3d63e2.",
            result.Output,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task ChangedManifestShaFailsAgainstPinnedUpstreamWithDiagnostic()
    {
        using var fixture = IsolatedRoot.CreateWithChangedAccordionSha();

        var result = await RunVerifierAsync(fixture.Path);

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("Pinned Shadcn reference mismatch", result.Output, StringComparison.Ordinal);
        Assert.Contains("Accordion: expected 0000000000000000000000000000000000000000", result.Output, StringComparison.Ordinal);
        Assert.Contains("received d0080d19c888c68cf1bb933b5a9dda0476ed19e3", result.Output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task OfflineManifestOverrideIsNotAccepted()
    {
        var root = FindRoot();
        var manifest = Path.Combine(root, "Maliev.ShadcnBlazor", "Reference", "shadcn-reference.json");

        var result = await RunVerifierAsync(root, "-ManifestPath", manifest);

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("parameter", result.Output, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ManifestPath", result.Output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ShortCommitPinFailsBeforeContactingUpstream()
    {
        using var fixture = IsolatedRoot.CreateWithShortCommit();

        var result = await RunVerifierAsync(fixture.Path);

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains(
            "The Shadcn reference commit must be a full lowercase Git commit SHA: 6261bd89f72d794aea491482cc2acfd8dc3d63e",
            result.Output,
            StringComparison.Ordinal);
        Assert.DoesNotContain("Invoke-RestMethod", result.Output, StringComparison.Ordinal);
    }

    private static async Task<ProcessResult> RunVerifierAsync(string root, params string[] arguments)
    {
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
        foreach (var argument in arguments)
            startInfo.ArgumentList.Add(argument);

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

    private sealed class IsolatedRoot : IDisposable
    {
        private IsolatedRoot(string path) => Path = path;

        public string Path { get; }

        public static IsolatedRoot CreateWithChangedAccordionSha()
        {
            return Create(manifest =>
            {
                var accordion = manifest["components"]!.AsArray()
                    .Select(component => component!.AsObject())
                    .Single(component => component["name"]!.GetValue<string>() == "Accordion");
                accordion["blobSha"] = new string('0', 40);
            });
        }

        public static IsolatedRoot CreateWithShortCommit()
        {
            return Create(manifest =>
                manifest["commit"] = "6261bd89f72d794aea491482cc2acfd8dc3d63e");
        }

        private static IsolatedRoot Create(Action<JsonObject> mutateManifest)
        {
            var sourceRoot = FindRoot();
            var isolatedRoot = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                $"shadcn-reference-{Guid.NewGuid():N}");
            var scriptDirectory = System.IO.Path.Combine(isolatedRoot, "scripts");
            var manifestDirectory = System.IO.Path.Combine(isolatedRoot, "Maliev.ShadcnBlazor", "Reference");
            Directory.CreateDirectory(scriptDirectory);
            Directory.CreateDirectory(manifestDirectory);

            File.Copy(
                System.IO.Path.Combine(sourceRoot, "scripts", "verify-shadcn-reference.ps1"),
                System.IO.Path.Combine(scriptDirectory, "verify-shadcn-reference.ps1"));

            var sourceManifest = System.IO.Path.Combine(
                sourceRoot,
                "Maliev.ShadcnBlazor",
                "Reference",
                "shadcn-reference.json");
            var manifest = JsonNode.Parse(File.ReadAllText(sourceManifest))!.AsObject();
            mutateManifest(manifest);
            File.WriteAllText(
                System.IO.Path.Combine(manifestDirectory, "shadcn-reference.json"),
                manifest.ToJsonString());
            return new IsolatedRoot(isolatedRoot);
        }

        public void Dispose() => Directory.Delete(Path, recursive: true);
    }

    private sealed record ProcessResult(int ExitCode, string Output);
}
