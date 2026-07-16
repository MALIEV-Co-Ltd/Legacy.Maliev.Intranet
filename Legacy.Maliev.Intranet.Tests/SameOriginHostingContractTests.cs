extern alias Bff;

using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using BffProgram = Bff::Program;

namespace Legacy.Maliev.Intranet.Tests;

public sealed class SameOriginHostingContractTests
{
    [Fact]
    public void Bff_HostsTheStandaloneClientWithoutChangingTheCompatibilityHost()
    {
        var root = FindRoot();
        var bffProject = File.ReadAllText(Path.Combine(
            root,
            "Legacy.Maliev.Intranet.Bff",
            "Legacy.Maliev.Intranet.Bff.csproj"));
        var bffProgram = File.ReadAllText(Path.Combine(root, "Legacy.Maliev.Intranet.Bff", "Program.cs"));
        var compatibilityProgram = File.ReadAllText(Path.Combine(root, "Legacy.Maliev.Intranet", "Program.cs"));

        Assert.Contains("Microsoft.AspNetCore.Components.WebAssembly.Server", bffProject, StringComparison.Ordinal);
        Assert.Contains("..\\Legacy.Maliev.Intranet.Client\\Legacy.Maliev.Intranet.Client.csproj", bffProject, StringComparison.Ordinal);
        Assert.Contains("UseBlazorFrameworkFiles", bffProgram, StringComparison.Ordinal);
        Assert.Contains("MapStaticAssets().AllowAnonymous()", bffProgram, StringComparison.Ordinal);
        Assert.Contains("MapFallbackToFile(\"index.html\").AllowAnonymous()", bffProgram, StringComparison.Ordinal);
        Assert.DoesNotContain("UseBlazorFrameworkFiles", compatibilityProgram, StringComparison.Ordinal);
        Assert.Contains("app.MapRazorPages()", compatibilityProgram, StringComparison.Ordinal);
    }

    [Fact]
    public void Client_UsesOnlyTheRelativeSameOriginSessionEndpoint()
    {
        var root = FindRoot();
        var clientRoot = Path.Combine(root, "Legacy.Maliev.Intranet.Client");
        var source = string.Join(
            Environment.NewLine,
            Directory.EnumerateFiles(clientRoot, "*.*", SearchOption.AllDirectories)
                .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
                .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
                .Where(path => Path.GetExtension(path) is ".cs" or ".razor")
                .Select(File.ReadAllText));

        Assert.Contains("GetAsync(\"/bff/session\"", source, StringComparison.Ordinal);
        Assert.Contains("ReadFromJsonAsync<EmployeeSessionSummary>", source, StringComparison.Ordinal);
        Assert.Contains("EmployeeSessionClient", source, StringComparison.Ordinal);
        Assert.Contains("response.IsSuccessStatusCode", source, StringComparison.Ordinal);
        Assert.Contains("Session unavailable", source, StringComparison.Ordinal);
        Assert.DoesNotContain("http://", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("https://", source, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task BffFallback_ReturnsTheWasmShellForClientRoutes()
    {
        await using var factory = new SameOriginBffFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            BaseAddress = new Uri("https://localhost"),
        });

        using var response = await client.GetAsync("/migration-foundation");
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("<div id=\"app\">", html, StringComparison.Ordinal);
        Assert.Contains("_framework/blazor.webassembly.js", html, StringComparison.Ordinal);
        Assert.Contains("noindex,nofollow,noarchive", html, StringComparison.Ordinal);

        foreach (var asset in new[]
        {
            "/_framework/blazor.webassembly.js",
            "/_content/MudBlazor/MudBlazor.min.css",
            "/_content/MudBlazor/MudBlazor.min.js",
        })
        {
            using var assetResponse = await client.GetAsync(asset);
            var assetBody = await assetResponse.Content.ReadAsStringAsync();
            Assert.True(
                assetResponse.StatusCode == HttpStatusCode.OK,
                $"{asset} returned {(int)assetResponse.StatusCode}: {assetBody}");
            Assert.Null(assetResponse.Headers.Location);
        }
    }

    private sealed class SameOriginBffFactory : WebApplicationFactory<BffProgram>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder) => builder.UseEnvironment("Testing");
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
