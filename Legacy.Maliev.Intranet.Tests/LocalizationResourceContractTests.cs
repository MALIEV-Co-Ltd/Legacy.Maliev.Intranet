using System.Xml.Linq;

namespace Legacy.Maliev.Intranet.Tests;

public sealed class LocalizationResourceContractTests
{
    [Fact]
    public void SharedShellAndLogin_UseLocalizersForVisibleText()
    {
        var root = FindRepositoryRoot();
        var topBar = File.ReadAllText(Path.Combine(root, "Legacy.Maliev.Intranet.Client", "Layout", "LegacyTopBar.razor"));
        var mainLayout = File.ReadAllText(Path.Combine(root, "Legacy.Maliev.Intranet.Client", "Layout", "MainLayout.razor"));
        var login = File.ReadAllText(Path.Combine(root, "Legacy.Maliev.Intranet.Client", "Pages", "Login.razor"));

        Assert.Contains("@inject IStringLocalizer<LegacyTopBar> Text", topBar, StringComparison.Ordinal);
        Assert.Contains("@inject IStringLocalizer<MainLayout> Text", mainLayout, StringComparison.Ordinal);
        Assert.Contains("@inject IStringLocalizer<Login> Text", login, StringComparison.Ordinal);
        Assert.DoesNotContain(">Employee gateway<", login, StringComparison.Ordinal);
        Assert.DoesNotContain(">Sign in with email<", login, StringComparison.Ordinal);
        Assert.DoesNotContain(">Workspace navigation<", topBar, StringComparison.Ordinal);
        Assert.DoesNotContain(">Skip to content<", mainLayout, StringComparison.Ordinal);
    }

    [Fact]
    public void SharedShellAndLogin_ThaiResourcesMatchEnglishKeysAndContainThaiText()
    {
        var root = FindRepositoryRoot();
        var pairs = new[]
        {
            Path.Combine(root, "Legacy.Maliev.Intranet.Client", "Layout", "LegacyTopBar"),
            Path.Combine(root, "Legacy.Maliev.Intranet.Client", "Layout", "MainLayout"),
            Path.Combine(root, "Legacy.Maliev.Intranet.Client", "Pages", "Login"),
            Path.Combine(root, "Legacy.Maliev.Intranet.Client", "Pages", "Home"),
            Path.Combine(root, "Legacy.Maliev.Intranet.Client", "Pages", "AccessDenied")
        };

        foreach (var path in pairs)
        {
            var english = ReadValues($"{path}.resx");
            var thai = ReadValues($"{path}.th.resx");

            Assert.Equal(english.Keys.Order(), thai.Keys.Order());
            Assert.All(thai.Values, value => Assert.False(string.IsNullOrWhiteSpace(value)));
            Assert.Contains(thai.Values, value => value.Any(IsThaiCharacter));
        }
    }

    private static Dictionary<string, string> ReadValues(string path) =>
        XDocument.Load(path)
            .Root!
            .Elements("data")
            .ToDictionary(
                element => (string)element.Attribute("name")!,
                element => element.Element("value")?.Value ?? string.Empty,
                StringComparer.Ordinal);

    private static bool IsThaiCharacter(char value) => value is >= '\u0E00' and <= '\u0E7F';

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Legacy.Maliev.Intranet.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new InvalidOperationException("Could not locate the Legacy.Maliev.Intranet repository root.");
    }
}
