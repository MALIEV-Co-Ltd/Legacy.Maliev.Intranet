using System.Text.Json;

namespace Maliev.ShadcnBlazor.Tests.Contracts;

public sealed class ReferenceManifestTests
{
    private const string ExpectedRepository = "https://github.com/shadcn-ui/ui";
    private const string ExpectedCommit = "6261bd89f72d794aea491482cc2acfd8dc3d63e2";
    private const string ExpectedRegistryRoot = "apps/v4/registry/bases/base/ui";
    private const string ExpectedStylePath = "apps/v4/registry/styles/style-vega.css";
    private const string ExpectedStyleBlobSha = "5621c5d5d76c015cec864f60e0d2e49c2765d938";

    // This approved catalog is intentionally independent of both JSON files. A manifest or
    // ledger edit therefore requires an explicit, reviewable contract change here as well.
    private static readonly ExpectedComponent[] ApprovedCatalog =
    [
        new("Accordion", "accordion", "registry-file", "d0080d19c888c68cf1bb933b5a9dda0476ed19e3", null, 6, "adapter"),
        new("Alert", "alert", "registry-file", "f1b66301005fd52dc5b428bd5549ebcee0cb4aff", null, 5, "adapter"),
        new("Alert Dialog", "alert-dialog", "registry-file", "74b7390d74f99f32bdb60327d7ce9e99005099f6", null, 7, "composition"),
        new("Aspect Ratio", "aspect-ratio", "registry-file", "d005931ea4968369a5c02afa038f4b4826fcc2c7", null, 2, "custom"),
        new("Attachment", "attachment", "registry-file", "bf86fcd201f0d395dd2574e2bd715dc04269dcba", null, 9, "custom"),
        new("Avatar", "avatar", "registry-file", "39c33e3ff1f035291378089eef864b5e89735d87", null, 5, "adapter"),
        new("Badge", "badge", "registry-file", "c67b787699e8d96dec72fbdff3404257a8ab01e0", null, 5, "adapter"),
        new("Breadcrumb", "breadcrumb", "registry-file", "e2e8da335a39156944fc3887bb84c0f62ceabbc2", null, 6, "composition"),
        new("Bubble", "bubble", "registry-file", "237d23381766f739422142769cc3314dd474b208", null, 9, "custom"),
        new("Button", "button", "registry-file", "fa343173a022bb2dd18247daa2c34e6e7b37c38c", null, 3, "adapter"),
        new("Button Group", "button-group", "registry-file", "c1f1aeaa23c9458d55c280e2d78d4fd79291878a", null, 3, "composition"),
        new("Calendar", "calendar", "registry-file", "bae95f6b61aa0e26c894a0b83114c984be8fc5bf", null, 4, "adapter"),
        new("Card", "card", "registry-file", "bed029b599af855dd76e1ec9f83d5d7d01be700a", null, 5, "composition"),
        new("Carousel", "carousel", "registry-file", "cbdbc3c1ed36ef7fd28d8173b3f95b420aee993f", null, 5, "adapter"),
        new("Chart", "chart", "registry-file", "86905c59c704f6f482aca9732cddcd589f691e0f", null, 8, "composition"),
        new("Checkbox", "checkbox", "registry-file", "3bcf55ab5ef6db4053f23129cd2445a6d9d3332d", null, 3, "adapter"),
        new("Collapsible", "collapsible", "registry-file", "488fb33af5107a94791ef3235ee58861e3ed6855", null, 6, "adapter"),
        new("Combobox", "combobox", "registry-file", "75cd8118eb8a6cc37dfdf3aadb61dde464908be6", null, 4, "composition"),
        new("Command", "command", "registry-file", "8ce76c45ad1333eec8e90af7bebc251c8ddf31f2", null, 7, "custom"),
        new("Context Menu", "context-menu", "registry-file", "71260e78b242ec696d8dcb734a173eeae508681e", null, 7, "composition"),
        new("Data Table", "data-table", "composition", null, ["table", "pagination"], 8, "composition"),
        new("Date Picker", "date-picker", "composition", null, ["calendar", "popover", "button"], 4, "composition"),
        new("Dialog", "dialog", "registry-file", "6c88a072af3f4b7f3ee217fa95648606d8e13a5f", null, 7, "adapter"),
        new("Direction", "direction", "registry-file", "d8cf134614ac38b4fbc6c902e6e2cf770ba389c1", null, 2, "adapter"),
        new("Drawer", "drawer", "registry-file", "505f7326fe93c13311db634aec3d2262e6be9b23", null, 7, "adapter"),
        new("Dropdown Menu", "dropdown-menu", "registry-file", "92f0ad66503447d5fb654b3e7ece3cdc3eb0e100", null, 7, "composition"),
        new("Empty", "empty", "registry-file", "38ff021d6b1f5edd3cceadd6812aa27edbdc2885", null, 2, "custom"),
        new("Field", "field", "registry-file", "8ab9ef35e41a06c7fc1c29df7417155d60024562", null, 2, "custom"),
        new("Hover Card", "hover-card", "registry-file", "0b6fb3cc4c611623b12cebc14f164fcfac9a3848", null, 7, "composition"),
        new("Input", "input", "registry-file", "d45a0b058ba9f59a11334ca6fcbf02a81506ad66", null, 4, "adapter"),
        new("Input Group", "input-group", "registry-file", "3aea74ebd07b8e0cbc44a4b30f8db26e77912446", null, 4, "custom"),
        new("Input OTP", "input-otp", "registry-file", "334527068ea34a08f877718def8bc69f0147bb3b", null, 4, "custom"),
        new("Item", "item", "registry-file", "6230a4915831eabd49f5586ed35233b4d28a135a", null, 2, "custom"),
        new("Kbd", "kbd", "registry-file", "2eef65d8ef06a27e3456d6808b342a94a1650457", null, 2, "custom"),
        new("Label", "label", "registry-file", "a439e097d65313152827715dd37c2f2c8d7b3900", null, 2, "custom"),
        new("Marker", "marker", "registry-file", "fa507ddf2c885e1f182caef9e9bbc0042918f5e0", null, 9, "custom"),
        new("Menubar", "menubar", "registry-file", "d2a26f09c402e7ec401a16628676d72fbef6db1b", null, 7, "composition"),
        new("Message", "message", "registry-file", "86ca73af5e8432c82d77d97dc65123e6794892e1", null, 9, "custom"),
        new("Message Scroller", "message-scroller", "registry-file", "c8518b0cd078518bf58d32033dac1c87df70ce42", null, 9, "custom"),
        new("Native Select", "native-select", "registry-file", "47f8ce63266f7bfaf6e17bfe04fcb234acec2664", null, 4, "custom"),
        new("Navigation Menu", "navigation-menu", "registry-file", "e3d12d0d44106fb3295ff4873bb53f5f488925a6", null, 6, "composition"),
        new("Pagination", "pagination", "registry-file", "016dec358462d61e5fbee7ef82d59ca8dcb2f211", null, 6, "composition"),
        new("Popover", "popover", "registry-file", "7ce182012730deae1094684396a33f4959a57cd2", null, 7, "adapter"),
        new("Progress", "progress", "registry-file", "3df1ca586c6000b963b1b6b315bae81b666d5367", null, 5, "adapter"),
        new("Questionnaire", "questionnaire", "registry-file", "5b36d35336ba6a3f205e7ef4c94cfe4896ea2267", null, 9, "custom"),
        new("Radio Group", "radio-group", "registry-file", "dc7acc81b5ffdc1d0e6e2f256972218ce3ad9ce5", null, 3, "adapter"),
        new("Resizable", "resizable", "registry-file", "0e6a967d5680207cabce35579498bef20a61a477", null, 6, "custom"),
        new("Scroll Area", "scroll-area", "registry-file", "7d251e056d6ed274f687d680065c16e94e55b864", null, 6, "custom"),
        new("Select", "select", "registry-file", "35ca37f35bd66ef830374e90eb1e447895ebf99c", null, 4, "adapter"),
        new("Separator", "separator", "registry-file", "cf212eb4ded8ed527c9491737db44c4d2dc7b2e5", null, 2, "adapter"),
        new("Sheet", "sheet", "registry-file", "c8850e23ad47f1b46ed8fd94df9febac3d870a88", null, 7, "composition"),
        new("Sidebar", "sidebar", "registry-file", "cfeb87df4245f3f7aac9f0b90da0c4c97d537d48", null, 6, "composition"),
        new("Skeleton", "skeleton", "registry-file", "0f76bfb60fc4d7d7f7cd950ae4c1d9d523984208", null, 5, "adapter"),
        new("Slider", "slider", "registry-file", "42018e6fd69a7d03827b18d6a72be6439aacb046", null, 3, "adapter"),
        new("Spinner", "spinner", "registry-file", "e2b6067051e443125d8e83552c53b2667e56c276", null, 5, "adapter"),
        new("Switch", "switch", "registry-file", "6afc57f76e848724e5b72babebc5da8e16ce0504", null, 3, "adapter"),
        new("Table", "table", "registry-file", "8167a7bc1a3eecbd0b649a01232694d7b9f1b851", null, 8, "composition"),
        new("Tabs", "tabs", "registry-file", "a9f20615cc4fbd8fafc64ae6494b79a4cf59074a", null, 6, "adapter"),
        new("Textarea", "textarea", "registry-file", "e703bf57c63904f2f9c5f0354f8c1277150f03af", null, 4, "adapter"),
        new("Toast", "toast", "registry-file", "bee9db04bf52bec472c4d0febfb959cc39a1e7b9", null, 5, "composition"),
        new("Toggle", "toggle", "registry-file", "dc7c644e9e869c2e787097eda3ea04568ba1691e", null, 3, "adapter"),
        new("Toggle Group", "toggle-group", "registry-file", "7735cbdf018af9028ddf1ede01d681a20f00febe", null, 3, "adapter"),
        new("Tooltip", "tooltip", "registry-file", "f145a091aca9815cca73b4f0f68cae316390fb3a", null, 7, "adapter"),
        new("Typography", "typography", "composition", null, ["https://ui.shadcn.com/docs/typeset"], 2, "custom")
    ];

    [Fact]
    public void ManifestExactlyMatchesTheApprovedPinnedCatalog()
    {
        using var manifest = JsonDocument.Parse(File.ReadAllText(FindFile(
            "Maliev.ShadcnBlazor", "Reference", "shadcn-reference.json")));
        var root = manifest.RootElement;
        Assert.Equal("shadcn-reference/v1", root.GetProperty("schema").GetString());
        Assert.Equal(ExpectedRepository, root.GetProperty("repository").GetString());
        Assert.Equal(ExpectedCommit, root.GetProperty("commit").GetString());
        Assert.Equal("base", root.GetProperty("primitive").GetString());
        Assert.Equal("vega", root.GetProperty("style").GetString());
        Assert.Equal("neutral", root.GetProperty("theme").GetString());
        Assert.Equal(ExpectedRegistryRoot, root.GetProperty("registryRoot").GetString());
        Assert.Equal(ExpectedStylePath, root.GetProperty("styleSource").GetProperty("path").GetString());
        Assert.Equal(ExpectedStyleBlobSha, root.GetProperty("styleSource").GetProperty("blobSha").GetString());

        var actual = root.GetProperty("components").EnumerateArray().ToArray();
        Assert.Equal(ApprovedCatalog.Length, actual.Length);
        for (var index = 0; index < ApprovedCatalog.Length; index++)
            AssertManifestComponent(ApprovedCatalog[index], actual[index]);
    }

    [Fact]
    public void LedgerExactlyMapsTheApprovedCatalogToItsPlanAndClassification()
    {
        using var ledger = JsonDocument.Parse(File.ReadAllText(FindFile("docs", "shadcn-component-ledger.json")));
        var root = ledger.RootElement;
        Assert.Equal("shadcn-component-ledger/v1", root.GetProperty("schema").GetString());
        Assert.Equal(ExpectedCommit, root.GetProperty("referenceCommit").GetString());

        var actual = root.GetProperty("components").EnumerateArray().ToArray();
        Assert.Equal(ApprovedCatalog.Length, actual.Length);
        for (var index = 0; index < ApprovedCatalog.Length; index++)
        {
            var expected = ApprovedCatalog[index];
            var entry = actual[index];
            Assert.Equal(expected.Name, entry.GetProperty("name").GetString());
            Assert.Equal(expected.Slug, entry.GetProperty("slug").GetString());
            Assert.Equal(expected.Plan, entry.GetProperty("plan").GetInt32());
            Assert.Equal(expected.Classification, entry.GetProperty("classification").GetString());
            Assert.Equal("planned", entry.GetProperty("status").GetString());
        }
    }

    private static void AssertManifestComponent(ExpectedComponent expected, JsonElement actual)
    {
        Assert.Equal(expected.Name, actual.GetProperty("name").GetString());
        Assert.Equal(expected.Slug, actual.GetProperty("slug").GetString());
        Assert.Equal(expected.SourceKind, actual.GetProperty("sourceKind").GetString());
        if (expected.BlobSha is not null)
        {
            Assert.Equal(expected.BlobSha, actual.GetProperty("blobSha").GetString());
            Assert.False(actual.TryGetProperty("sources", out _));
        }
        else
        {
            Assert.False(actual.TryGetProperty("blobSha", out _));
            Assert.Equal(expected.Sources, actual.GetProperty("sources").EnumerateArray()
                .Select(source => source.GetString()).ToArray());
        }
    }

    private static string FindFile(params string[] segments)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Legacy.Maliev.Intranet.slnx")))
            directory = directory.Parent;
        return Path.Combine(directory?.FullName ?? throw new DirectoryNotFoundException(), Path.Combine(segments));
    }

    private sealed record ExpectedComponent(
        string Name,
        string Slug,
        string SourceKind,
        string? BlobSha,
        string[]? Sources,
        int Plan,
        string Classification);
}
