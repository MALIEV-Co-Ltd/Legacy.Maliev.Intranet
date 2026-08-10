using System.Text.Json;

namespace Maliev.ShadcnBlazor.Tests.Contracts;

public sealed class ReferenceManifestTests
{
    private const string ExpectedCommit = "6261bd89f72d794aea491482cc2acfd8dc3d63e2";

    [Fact]
    public void ManifestPinsTheApprovedSourceAndAllRequestedComponents()
    {
        using var manifest = JsonDocument.Parse(File.ReadAllText(FindFile(
            "Maliev.ShadcnBlazor", "Reference", "shadcn-reference.json")));
        var root = manifest.RootElement;
        Assert.Equal("shadcn-reference/v1", root.GetProperty("schema").GetString());
        Assert.Equal(ExpectedCommit, root.GetProperty("commit").GetString());
        Assert.Equal("base", root.GetProperty("primitive").GetString());
        Assert.Equal("vega", root.GetProperty("style").GetString());
        Assert.Equal("neutral", root.GetProperty("theme").GetString());

        var components = root.GetProperty("components").EnumerateArray().ToArray();
        Assert.Equal(64, components.Length);
        Assert.Equal(64, components.Select(x => x.GetProperty("name").GetString()).Distinct().Count());
        Assert.Equal(61, components.Count(x => x.GetProperty("sourceKind").GetString() == "registry-file"));
        Assert.Equal(3, components.Count(x => x.GetProperty("sourceKind").GetString() == "composition"));
        Assert.All(components.Where(x => x.GetProperty("sourceKind").GetString() == "registry-file"), component =>
            Assert.Matches("^[0-9a-f]{40}$", component.GetProperty("blobSha").GetString()!));
    }

    [Fact]
    public void LedgerHasOnePlannedEntryForEveryManifestComponent()
    {
        using var manifest = JsonDocument.Parse(File.ReadAllText(FindFile(
            "Maliev.ShadcnBlazor", "Reference", "shadcn-reference.json")));
        using var ledger = JsonDocument.Parse(File.ReadAllText(FindFile("docs", "shadcn-component-ledger.json")));

        var expected = manifest.RootElement.GetProperty("components").EnumerateArray()
            .Select(x => x.GetProperty("name").GetString()).Order().ToArray();
        var actual = ledger.RootElement.GetProperty("components").EnumerateArray()
            .Select(x => x.GetProperty("name").GetString()).Order().ToArray();

        Assert.Equal("shadcn-component-ledger/v1", ledger.RootElement.GetProperty("schema").GetString());
        Assert.Equal(expected, actual);
        Assert.All(ledger.RootElement.GetProperty("components").EnumerateArray(), entry =>
            Assert.Equal("planned", entry.GetProperty("status").GetString()));
    }

    private static string FindFile(params string[] segments)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Legacy.Maliev.Intranet.slnx")))
            directory = directory.Parent;
        return Path.Combine(directory?.FullName ?? throw new DirectoryNotFoundException(), Path.Combine(segments));
    }
}
