namespace Legacy.Maliev.Intranet.Tests;

public sealed class ShadcnPackageIntegrationContractTests
{
    [Fact]
    public void ClientReferencesAndRegistersTheReusablePackageOnce()
    {
        var project = Read("Legacy.Maliev.Intranet.Client", "Legacy.Maliev.Intranet.Client.csproj");
        var program = Read("Legacy.Maliev.Intranet.Client", "Program.cs");

        Assert.Contains("<PackageReference Include=\"Maliev.ShadcnBlazor\" Version=\"1.2.2\"", project, StringComparison.Ordinal);
        Assert.DoesNotContain("Maliev.ShadcnBlazor.csproj", project, StringComparison.Ordinal);
        Assert.Contains("AddMalievShadcn", program, StringComparison.Ordinal);
        Assert.DoesNotContain("AddMudServices", program, StringComparison.Ordinal);
        Assert.Contains("IBM Plex Sans Thai", program, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Legacy.Maliev.Intranet.Client", "Legacy.Maliev.Intranet.Client.csproj")]
    [InlineData("Legacy.Maliev.Intranet.Contracts", "Legacy.Maliev.Intranet.Contracts.csproj")]
    public void ReleaseWasmProjectsDoNotPublishDebugSymbols(string directory, string projectFile)
    {
        var project = Read(directory, projectFile);

        var expectedCondition = directory == "Legacy.Maliev.Intranet.Contracts"
            ? "Condition=\"'$(Configuration)' == 'Release' And '$(EnableCoverageSymbols)' != 'true'\""
            : "Condition=\"'$(Configuration)' == 'Release'\"";
        Assert.Contains(expectedCondition, project, StringComparison.Ordinal);
        Assert.Contains("<DebugSymbols>false</DebugSymbols>", project, StringComparison.Ordinal);
        Assert.Contains("<DebugType>None</DebugType>", project, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("MainLayout.razor")]
    [InlineData("EmptyLayout.razor")]
    public void LayoutUsesOneShadcnProvider(string file)
    {
        var layout = Read("Legacy.Maliev.Intranet.Client", "Layout", file);

        Assert.Equal(1, Count(layout, "<ShadcnThemeProvider"));
        Assert.Contains("IsDarkMode=\"@ThemeService.IsDarkMode\"", layout, StringComparison.Ordinal);
        Assert.Contains("Direction=\"ShadcnDirection.LeftToRight\"", layout, StringComparison.Ordinal);
    }

    [Fact]
    public void BootstrapAndStylesLoadNativePackageAssetsInExactOrder()
    {
        var index = Read("Legacy.Maliev.Intranet.Client", "wwwroot", "index.html");

        Assert.Contains("root.dataset.shadcnTheme = theme", index, StringComparison.Ordinal);
        AssertOrder(index,
            "css/ibm-plex-sans-thai.css",
            "_content/Maliev.ShadcnBlazor/css/shadcn-base.css",
            "_content/Maliev.ShadcnBlazor/css/shadcn-semantic-foundations.css",
            "_content/Maliev.ShadcnBlazor/css/shadcn-layout.css",
            "_content/Maliev.ShadcnBlazor/css/shadcn-actions.css",
            "_content/Maliev.ShadcnBlazor/css/shadcn-data-display.css",
            "_content/Maliev.ShadcnBlazor/css/shadcn-disclosure-navigation.css",
            "_content/Maliev.ShadcnBlazor/css/shadcn-forms.css",
            "_content/Maliev.ShadcnBlazor/css/shadcn-feedback-content.css",
            "_content/Maliev.ShadcnBlazor/css/shadcn-overlays-menus.css",
            "_content/Maliev.ShadcnBlazor/css/shadcn-conversation.css",
            "css/design-tokens.css",
            "css/app.css",
            "css/module-pages.css",
            "css/utilities.css",
            "css/operations-pages.css",
            "css/shadcn.css",
            "Legacy.Maliev.Intranet.Client.styles.css",
            "css/loading-shell.css");
    }

    [Fact]
    public void LoginUsesLegacyThemeServiceAndDoesNotCallThemeInteropDirectly()
    {
        var login = Read("Legacy.Maliev.Intranet.Client", "Pages", "Login.razor");

        Assert.Contains("@inject LegacyThemeService ThemeService", login, StringComparison.Ordinal);
        Assert.Contains("ThemeService.ToggleAsync", login, StringComparison.Ordinal);
        Assert.DoesNotContain("malievTheme.isDark", login, StringComparison.Ordinal);
        Assert.DoesNotContain("malievTheme.toggle", login, StringComparison.Ordinal);
    }

    private static string Read(params string[] segments) =>
        File.ReadAllText(Path.Combine([FindRoot(), .. segments]));

    private static int Count(string value, string token)
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

    private static void AssertOrder(string value, params string[] tokens)
    {
        var offset = -1;
        foreach (var token in tokens)
        {
            var next = value.IndexOf(token, StringComparison.Ordinal);
            Assert.True(next > offset, $"Expected '{token}' after the previous stylesheet.");
            offset = next;
        }
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
