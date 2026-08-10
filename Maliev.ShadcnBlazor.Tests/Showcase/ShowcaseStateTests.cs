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
        var source = File.ReadAllText(Path.Combine(FindRoot(), "Maliev.ShadcnBlazor.Showcase", "Pages", "MudInventory.razor"));
        var fixtures = Regex.Matches(source, "data-mud-type=\\\"(?<type>Mud[A-Za-z]+)\\\"")
            .Select(match => match.Groups["type"].Value)
            .ToArray();

        Assert.Equal(MudAdapterContractTests.ProductionTypes.Order(), fixtures.Distinct().Order());
        Assert.Equal(fixtures.Length, fixtures.Distinct(StringComparer.Ordinal).Count());

        var testIds = Regex.Matches(source, "data-testid=\\\"(?<id>[A-Za-z0-9-]+)\\\"")
            .Select(match => match.Groups["id"].Value)
            .ToArray();
        Assert.Equal(testIds.Length, testIds.Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(
            ["mud-actions", "mud-typography", "mud-forms", "mud-surfaces-overlays", "mud-data-feedback"],
            Regex.Matches(source, "<section id=\\\"(?<id>mud-[a-z-]+)\\\"")
                .Select(match => match.Groups["id"].Value)
                .ToArray());
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
}
