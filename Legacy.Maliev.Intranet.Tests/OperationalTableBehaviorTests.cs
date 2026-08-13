using Legacy.Maliev.Intranet.Client.Shared.Components;

namespace Legacy.Maliev.Intranet.Tests;

public sealed class OperationalTableBehaviorTests
{
    [Fact]
    public void Toggle_keeps_only_one_expanded_record()
    {
        var state = new OperationalTableState<int>();
        state.Toggle(41);
        Assert.True(state.IsExpanded(41));
        state.Toggle(84);
        Assert.False(state.IsExpanded(41));
        Assert.True(state.IsExpanded(84));
    }

    [Fact]
    public void Toggle_current_record_collapses_and_clear_resets()
    {
        var state = new OperationalTableState<int>();
        state.Toggle(41);
        state.Toggle(41);
        Assert.False(state.HasExpandedKey);
        state.Toggle(84);
        state.Clear();
        Assert.False(state.HasExpandedKey);
    }

    [Fact]
    public void Markup_preserves_the_shared_semantic_table_and_action_contract()
    {
        var root = FindRepositoryRoot();
        var markup = File.ReadAllText(Path.Combine(root, "Legacy.Maliev.Intranet.Client.Shared", "Components", "OperationalTable.razor"));
        var styles = File.ReadAllText(Path.Combine(root, "Legacy.Maliev.Intranet.Client.Shared", "Components", "OperationalTable.razor.css"));
        var source = string.Concat(markup, styles);

        Assert.Contains("<table", markup, StringComparison.Ordinal);
        Assert.Contains("operational-table__scroll", source, StringComparison.Ordinal);
        Assert.Contains("operational-table__row", source, StringComparison.Ordinal);
        Assert.Contains("operational-table__identity", source, StringComparison.Ordinal);
        Assert.Contains("operational-table__actions", source, StringComparison.Ordinal);
        Assert.Contains("operational-table__detail", source, StringComparison.Ordinal);
        Assert.Contains("operational-table__toggle", source, StringComparison.Ordinal);
        Assert.Contains("operational-table__quick-view", source, StringComparison.Ordinal);
        Assert.Contains("colspan=\"@ColumnCount\"", markup, StringComparison.Ordinal);
        Assert.Contains("aria-expanded", markup, StringComparison.Ordinal);
        Assert.Contains("aria-controls", markup, StringComparison.Ordinal);
        Assert.Contains("DetailAriaLabel(item)", markup, StringComparison.Ordinal);
        Assert.Contains("ExpandAriaLabel(item)", markup, StringComparison.Ordinal);
        Assert.Contains("CollapseAriaLabel(item)", markup, StringComparison.Ordinal);
        Assert.Contains("ColumnCount < 1", markup, StringComparison.Ordinal);
        Assert.Contains("overscroll-behavior-inline: contain", styles, StringComparison.Ordinal);
        Assert.Contains("@media (prefers-reduced-motion: reduce)", styles, StringComparison.Ordinal);
        Assert.Contains("@media (forced-colors: active)", styles, StringComparison.Ordinal);
        Assert.DoesNotContain("display: block", styles, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(".mud-", styles, StringComparison.OrdinalIgnoreCase);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Legacy.Maliev.Intranet.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new DirectoryNotFoundException("Repository root was not found.");
    }
}
