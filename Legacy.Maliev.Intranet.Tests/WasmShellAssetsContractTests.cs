namespace Legacy.Maliev.Intranet.Tests;

public sealed class WasmShellAssetsContractTests
{
    [Fact]
    public void ClientIndexProvidesTheCurrentLoadingErrorAndBrandingShell()
    {
        var root = FindRoot();
        var indexPath = Path.Combine(root, "Legacy.Maliev.Intranet.Client", "wwwroot", "index.html");
        var cssPath = Path.Combine(root, "Legacy.Maliev.Intranet.Client", "wwwroot", "css", "loading-shell.css");
        var faviconPath = Path.Combine(root, "Legacy.Maliev.Intranet.Client", "wwwroot", "images", "favicon.svg");

        Assert.True(File.Exists(indexPath));
        Assert.True(File.Exists(cssPath));
        Assert.True(File.Exists(faviconPath));

        var index = File.ReadAllText(indexPath);
        var css = File.ReadAllText(cssPath);
        var favicon = File.ReadAllText(faviconPath);

        Assert.Contains("rel=\"preload\" id=\"webassembly\"", index, StringComparison.Ordinal);
        Assert.Contains("css/loading-shell.css", index, StringComparison.Ordinal);
        Assert.Contains("rel=\"icon\" type=\"image/svg+xml\" href=\"images/favicon.svg\"", index, StringComparison.Ordinal);
        Assert.Contains("class=\"legacy-wasm-loading\"", index, StringComparison.Ordinal);
        Assert.Contains("role=\"status\" aria-live=\"polite\"", index, StringComparison.Ordinal);
        Assert.Equal(2, CountOccurrences(index, "<circle "));
        Assert.Contains("id=\"blazor-error-ui\" role=\"alert\"", index, StringComparison.Ordinal);
        Assert.Contains("class=\"reload\"", index, StringComparison.Ordinal);
        Assert.Contains("class=\"dismiss\"", index, StringComparison.Ordinal);
        Assert.Contains("_framework/blazor.webassembly.js", index, StringComparison.Ordinal);
        Assert.Contains("--blazor-load-percentage", css, StringComparison.Ordinal);
        Assert.Contains("@media (prefers-reduced-motion: reduce)", css, StringComparison.Ordinal);
        Assert.Contains("@media (forced-colors: active)", css, StringComparison.Ordinal);
        Assert.Contains("#blazor-error-ui", css, StringComparison.Ordinal);
        Assert.Contains("style=\"fill:white;\"", favicon, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("AccessToken", index, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("RefreshToken", index, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("apiKey", index, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void LoadingShellKeepsProgressAndErrorControlsVisibleAtNarrowWidths()
    {
        var root = FindRoot();
        var css = File.ReadAllText(Path.Combine(
            root,
            "Legacy.Maliev.Intranet.Client",
            "wwwroot",
            "css",
            "loading-shell.css"));

        Assert.Contains("@media (max-width: 640px)", css, StringComparison.Ordinal);
        Assert.Contains("padding-top: 15vh", css, StringComparison.Ordinal);
        Assert.Contains("padding-inline: 0.75rem 2.75rem", css, StringComparison.Ordinal);
        Assert.Contains("min-height: 1.5rem", css, StringComparison.Ordinal);
        Assert.Contains("focus-visible", css, StringComparison.Ordinal);
    }

    private static int CountOccurrences(string value, string token)
    {
        var count = 0;
        var offset = 0;
        while ((offset = value.IndexOf(token, offset, StringComparison.Ordinal)) >= 0)
        {
            count++;
            offset += token.Length;
        }

        return count;
    }

    private static string FindRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Legacy.Maliev.Intranet.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new DirectoryNotFoundException();
    }
}
