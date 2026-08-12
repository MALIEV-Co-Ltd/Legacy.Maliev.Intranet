using Maliev.ShadcnBlazor.Showcase;
using Maliev.ShadcnBlazor.Theming;
using Maliev.ShadcnBlazor.Tests.Contracts;
using System.Text.RegularExpressions;

namespace Maliev.ShadcnBlazor.Tests.Showcase;

public sealed class ShowcaseStateTests
{
    [Fact]
    public void MudInventoryDeclaresEveryProductionAdapterFixtureExactlyOnce()
    {
        var inventory = ReadSource("Pages", "MudInventory.razor");
        var provider = ReadSource("..", "Maliev.ShadcnBlazor", "Components", "ShadcnThemeProvider.razor");
        var fixtures = Regex.Matches(inventory, "data-mud-type=\\\"(?<type>Mud[A-Za-z]+)\\\"")
            .Select(match => match.Groups["type"].Value)
            .Concat(Regex.Matches(provider, "<(?<type>Mud(?:ThemeProvider|DialogProvider|PopoverProvider|SnackbarProvider))\\b")
                .Select(match => match.Groups["type"].Value))
            .ToArray();

        Assert.Equal(MudAdapterContractTests.ProductionTypes.Order(), fixtures.Distinct().Order());
        Assert.Equal(fixtures.Length, fixtures.Distinct(StringComparer.Ordinal).Count());

        var testIds = Regex.Matches(inventory, "data-testid=\\\"(?<id>[A-Za-z0-9-]+)\\\"")
            .Select(match => match.Groups["id"].Value)
            .ToArray();
        Assert.Equal(testIds.Length, testIds.Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(
            ["mud-actions", "mud-typography", "mud-forms", "mud-surfaces-overlays", "mud-data-feedback"],
            Regex.Matches(inventory, "<section id=\\\"(?<id>mud-[a-z-]+)\\\"")
                .Select(match => match.Groups["id"].Value)
                .ToArray());

        Assert.Matches("<MudLayout[^>]*data-mud-type=\"MudLayout\"", inventory);
        Assert.Matches("<MudMainContent[^>]*data-mud-type=\"MudMainContent\"", inventory);
        Assert.DoesNotContain("mud-inventory__structural-fixture", inventory, StringComparison.Ordinal);
        Assert.Contains("<MudThemeProvider", provider, StringComparison.Ordinal);
        Assert.Contains("<MudDialogProvider", provider, StringComparison.Ordinal);
        Assert.Contains("CloseOnEscapeKey=\"true\"", provider, StringComparison.Ordinal);
        Assert.Contains("<MudPopoverProvider", provider, StringComparison.Ordinal);
        Assert.Contains("<MudSnackbarProvider", provider, StringComparison.Ordinal);
    }

    [Fact]
    public void ToggleTheme_TransitionsBetweenLightAndDarkAndRaisesChanged()
    {
        var state = new ShowcaseState();
        var changedCount = 0;
        state.Changed += (_, _) => changedCount++;

        Assert.False(state.IsDarkMode);

        state.ToggleTheme();

        Assert.True(state.IsDarkMode);
        Assert.Equal(1, changedCount);

        state.ToggleTheme();

        Assert.False(state.IsDarkMode);
        Assert.Equal(2, changedCount);
    }

    [Fact]
    public void ToggleDirection_TransitionsBetweenLtrAndRtlAndRaisesChanged()
    {
        var state = new ShowcaseState();
        var changedCount = 0;
        state.Changed += (_, _) => changedCount++;

        Assert.Equal(ShadcnDirection.LeftToRight, state.Direction);

        state.ToggleDirection();

        Assert.Equal(ShadcnDirection.RightToLeft, state.Direction);
        Assert.Equal(1, changedCount);

        state.ToggleDirection();

        Assert.Equal(ShadcnDirection.LeftToRight, state.Direction);
        Assert.Equal(2, changedCount);
    }

    private static string FindRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Legacy.Maliev.Intranet.slnx")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new DirectoryNotFoundException("Could not find repository root.");
    }

    private static string ReadSource(params string[] segments) => File.ReadAllText(Path.Combine([FindRoot(), "Maliev.ShadcnBlazor.Showcase", .. segments]));
}
