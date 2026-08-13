using Bunit;
using Legacy.Maliev.Intranet.Client.Shared.Components;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;

namespace Legacy.Maliev.Intranet.Tests;

public sealed class OperationalTableBehaviorTests : BunitContext
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

    [Fact]
    public void Rendered_table_uses_opaque_unique_stable_aria_ids_for_distinct_keys()
    {
        var first = new CollidingKey("first");
        var second = new CollidingKey("second");
        var cut = RenderTable(
            [new Row<CollidingKey>(first, "first row"), new Row<CollidingKey>(second, "second row")],
            row => row.Key);

        Assert.Equal("A", cut.Find("a.operational-table__detail").TagName);
        Assert.Equal("BUTTON", cut.Find("button.operational-table__toggle").TagName);
        var firstToggle = cut.FindAll(".operational-table__toggle")[0];
        firstToggle.Click();
        var firstId = cut.Find(".operational-table__quick-view").Id;

        cut.FindAll(".operational-table__toggle")[1].Click();
        var secondId = cut.Find(".operational-table__quick-view").Id;

        cut.FindAll(".operational-table__toggle")[0].Click();
        var firstIdAfterRefresh = cut.Find(".operational-table__quick-view").Id;

        Assert.NotEqual(firstId, secondId);
        Assert.Equal(firstId, firstIdAfterRefresh);
        Assert.Matches("^[A-Za-z][A-Za-z0-9_-]*$", firstId);
        Assert.Equal(firstId, cut.FindAll(".operational-table__toggle")[0].GetAttribute("aria-controls"));
    }

    [Fact]
    public void Rendered_table_does_not_disclose_punctuation_or_whitespace_key_in_aria_id()
    {
        const string key = " customer / 41 ! ";
        var cut = RenderTable([new Row<string>(key, "customer row")], row => row.Key);

        cut.Find(".operational-table__toggle").Click();
        var quickViewId = cut.Find(".operational-table__quick-view").Id;

        Assert.Matches("^[A-Za-z][A-Za-z0-9_-]*$", quickViewId);
        Assert.DoesNotContain("customer", quickViewId, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(quickViewId, cut.Find(".operational-table__toggle").GetAttribute("aria-controls"));
    }

    [Fact]
    public void Rendered_document_keeps_quick_view_ids_unique_across_table_instances()
    {
        var cut = Render<TwoOperationalTables>();
        var quickViews = cut.FindAll(".operational-table__quick-view");
        var ids = quickViews.Select(quickView => quickView.Id).ToArray();

        Assert.Equal(2, ids.Distinct(StringComparer.Ordinal).Count());
        foreach (var toggle in cut.FindAll("button.operational-table__toggle"))
        {
            var controlledId = toggle.GetAttribute("aria-controls");
            Assert.NotNull(controlledId);
            Assert.Single(quickViews, quickView => quickView.Id == controlledId);
        }
    }

    [Fact]
    public void Tables_sharing_state_rerender_when_another_table_changes_the_expanded_record()
    {
        var cut = Render<SharedStateOperationalTables>();
        var toggles = cut.FindAll("button.operational-table__toggle");

        toggles[0].Click();
        Assert.Equal("Quick view first", cut.Find(".operational-table__quick-view").TextContent);

        cut.FindAll("button.operational-table__toggle")[1].Click();
        Assert.Single(cut.FindAll(".operational-table__quick-view"));
        Assert.Equal("Quick view second", cut.Find(".operational-table__quick-view").TextContent);

        cut.FindAll("button.operational-table__toggle")[1].Click();
        Assert.Empty(cut.FindAll(".operational-table__quick-view"));
    }

    [Fact]
    public void Scoped_styles_cross_the_render_fragment_boundary_and_target_native_action_roots()
    {
        var root = FindRepositoryRoot();
        var styles = File.ReadAllText(Path.Combine(root, "Legacy.Maliev.Intranet.Client.Shared", "Components", "OperationalTable.razor.css"));

        Assert.Contains(".operational-table__scroll ::deep .operational-table__identity", styles, StringComparison.Ordinal);
        Assert.Contains(".operational-table__scroll ::deep [data-priority=\"supporting\"]", styles, StringComparison.Ordinal);
        Assert.Contains("button.operational-table__toggle", styles, StringComparison.Ordinal);
        Assert.Contains("a.operational-table__detail", styles, StringComparison.Ordinal);
    }

    private IRenderedComponent<OperationalTable<Row<TKey>, TKey>> RenderTable<TKey>(
        IReadOnlyList<Row<TKey>> rows,
        Func<Row<TKey>, TKey> keySelector)
        where TKey : notnull =>
        Render<OperationalTable<Row<TKey>, TKey>>(parameters => parameters
            .Add(component => component.Items, rows)
            .Add(component => component.KeySelector, keySelector)
            .Add(component => component.HeaderContent, HeaderContent())
            .Add(component => component.RowContent, RowContent<TKey>())
            .Add(component => component.QuickViewContent, QuickViewContent<TKey>())
            .Add(component => component.DetailHref, _ => "/detail")
            .Add(component => component.DetailAriaLabel, row => $"Open {row.Name}")
            .Add(component => component.ExpandAriaLabel, row => $"Expand {row.Name}")
            .Add(component => component.CollapseAriaLabel, row => $"Collapse {row.Name}")
            .Add(component => component.TableLabel, "Operational records")
            .Add(component => component.ColumnCount, 2)
            .Add(component => component.State, new OperationalTableState<TKey>()));

    private static RenderFragment HeaderContent() => builder =>
    {
        builder.OpenElement(0, "tr");
        builder.OpenElement(1, "th");
        builder.AddContent(2, "Record");
        builder.CloseElement();
        builder.OpenElement(3, "th");
        builder.AddContent(4, "Actions");
        builder.CloseElement();
        builder.CloseElement();
    };

    private static RenderFragment<Row<TKey>> RowContent<TKey>() => row => builder =>
    {
        builder.OpenElement(0, "td");
        builder.AddAttribute(1, "class", "operational-table__identity");
        builder.AddAttribute(2, "data-priority", "supporting");
        builder.AddContent(3, row.Name);
        builder.CloseElement();
    };

    private static RenderFragment<Row<TKey>> QuickViewContent<TKey>() => row => builder =>
    {
        builder.OpenElement(0, "span");
        builder.AddContent(1, $"Quick view {row.Name}");
        builder.CloseElement();
    };

    private sealed record Row<TKey>(TKey Key, string Name);

    private sealed class CollidingKey(string value)
    {
        public override string ToString() => "same display";
        public string Value { get; } = value;
    }

    private sealed class TwoOperationalTables : ComponentBase
    {
        private static readonly IReadOnlyList<Row<int>> Rows = [new(1, "record")];
        private readonly OperationalTableState<int> firstState = ExpandedState();
        private readonly OperationalTableState<int> secondState = ExpandedState();

        protected override void BuildRenderTree(RenderTreeBuilder builder)
        {
            builder.OpenElement(0, "section");
            AddTable(builder, firstState);
            AddAlternateTable(builder, secondState);
            builder.CloseElement();
        }

        private static OperationalTableState<int> ExpandedState()
        {
            var state = new OperationalTableState<int>();
            state.Toggle(1);
            return state;
        }

        private static void AddTable(RenderTreeBuilder builder, OperationalTableState<int> state)
        {
            builder.OpenComponent<OperationalTable<Row<int>, int>>(0);
            builder.AddAttribute(1, nameof(OperationalTable<Row<int>, int>.Items), Rows);
            builder.AddAttribute(2, nameof(OperationalTable<Row<int>, int>.KeySelector), (Func<Row<int>, int>)(row => row.Key));
            builder.AddAttribute(3, nameof(OperationalTable<Row<int>, int>.HeaderContent), HeaderContent());
            builder.AddAttribute(4, nameof(OperationalTable<Row<int>, int>.RowContent), RowContent<int>());
            builder.AddAttribute(5, nameof(OperationalTable<Row<int>, int>.QuickViewContent), QuickViewContent<int>());
            builder.AddAttribute(6, nameof(OperationalTable<Row<int>, int>.DetailHref), (Func<Row<int>, string?>)(_ => "/detail"));
            builder.AddAttribute(7, nameof(OperationalTable<Row<int>, int>.DetailAriaLabel), (Func<Row<int>, string>)(row => $"Open {row.Name}"));
            builder.AddAttribute(8, nameof(OperationalTable<Row<int>, int>.ExpandAriaLabel), (Func<Row<int>, string>)(row => $"Expand {row.Name}"));
            builder.AddAttribute(9, nameof(OperationalTable<Row<int>, int>.CollapseAriaLabel), (Func<Row<int>, string>)(row => $"Collapse {row.Name}"));
            builder.AddAttribute(10, nameof(OperationalTable<Row<int>, int>.TableLabel), "Operational records");
            builder.AddAttribute(11, nameof(OperationalTable<Row<int>, int>.ColumnCount), 2);
            builder.AddAttribute(12, nameof(OperationalTable<Row<int>, int>.State), state);
            builder.CloseComponent();
        }

        private static void AddAlternateTable(RenderTreeBuilder builder, OperationalTableState<int> state)
        {
            builder.OpenComponent<OperationalTable<AlternateRow, int>>(20);
            builder.AddAttribute(21, nameof(OperationalTable<AlternateRow, int>.Items), AlternateRows);
            builder.AddAttribute(22, nameof(OperationalTable<AlternateRow, int>.KeySelector), (Func<AlternateRow, int>)(row => row.Key));
            builder.AddAttribute(23, nameof(OperationalTable<AlternateRow, int>.HeaderContent), HeaderContent());
            builder.AddAttribute(24, nameof(OperationalTable<AlternateRow, int>.RowContent), AlternateRowContent());
            builder.AddAttribute(25, nameof(OperationalTable<AlternateRow, int>.QuickViewContent), AlternateQuickViewContent());
            builder.AddAttribute(26, nameof(OperationalTable<AlternateRow, int>.DetailHref), (Func<AlternateRow, string?>)(_ => "/detail"));
            builder.AddAttribute(27, nameof(OperationalTable<AlternateRow, int>.DetailAriaLabel), (Func<AlternateRow, string>)(row => $"Open {row.Name}"));
            builder.AddAttribute(28, nameof(OperationalTable<AlternateRow, int>.ExpandAriaLabel), (Func<AlternateRow, string>)(row => $"Expand {row.Name}"));
            builder.AddAttribute(29, nameof(OperationalTable<AlternateRow, int>.CollapseAriaLabel), (Func<AlternateRow, string>)(row => $"Collapse {row.Name}"));
            builder.AddAttribute(30, nameof(OperationalTable<AlternateRow, int>.TableLabel), "Alternate operational records");
            builder.AddAttribute(31, nameof(OperationalTable<AlternateRow, int>.ColumnCount), 2);
            builder.AddAttribute(32, nameof(OperationalTable<AlternateRow, int>.State), state);
            builder.CloseComponent();
        }

        private static readonly IReadOnlyList<AlternateRow> AlternateRows = [new(1, "alternate record")];

        private static RenderFragment<AlternateRow> AlternateRowContent() => row => builder =>
        {
            builder.OpenElement(0, "td");
            builder.AddAttribute(1, "class", "operational-table__identity");
            builder.AddContent(2, row.Name);
            builder.CloseElement();
        };

        private static RenderFragment<AlternateRow> AlternateQuickViewContent() => row => builder =>
        {
            builder.OpenElement(0, "span");
            builder.AddContent(1, $"Quick view {row.Name}");
            builder.CloseElement();
        };

        private sealed record AlternateRow(int Key, string Name);
    }

    private sealed class SharedStateOperationalTables : ComponentBase
    {
        private readonly OperationalTableState<int> state = new();

        protected override void BuildRenderTree(RenderTreeBuilder builder)
        {
            AddTable(builder, new Row<int>(1, "first"));
            AddTable(builder, new Row<int>(2, "second"));
        }

        private void AddTable(RenderTreeBuilder builder, Row<int> row)
        {
            builder.OpenComponent<OperationalTable<Row<int>, int>>(0);
            builder.AddAttribute(1, nameof(OperationalTable<Row<int>, int>.Items), new[] { row });
            builder.AddAttribute(2, nameof(OperationalTable<Row<int>, int>.KeySelector), (Func<Row<int>, int>)(item => item.Key));
            builder.AddAttribute(3, nameof(OperationalTable<Row<int>, int>.HeaderContent), HeaderContent());
            builder.AddAttribute(4, nameof(OperationalTable<Row<int>, int>.RowContent), RowContent<int>());
            builder.AddAttribute(5, nameof(OperationalTable<Row<int>, int>.QuickViewContent), QuickViewContent<int>());
            builder.AddAttribute(6, nameof(OperationalTable<Row<int>, int>.DetailHref), (Func<Row<int>, string?>)(_ => "/detail"));
            builder.AddAttribute(7, nameof(OperationalTable<Row<int>, int>.DetailAriaLabel), (Func<Row<int>, string>)(item => $"Open {item.Name}"));
            builder.AddAttribute(8, nameof(OperationalTable<Row<int>, int>.ExpandAriaLabel), (Func<Row<int>, string>)(item => $"Expand {item.Name}"));
            builder.AddAttribute(9, nameof(OperationalTable<Row<int>, int>.CollapseAriaLabel), (Func<Row<int>, string>)(item => $"Collapse {item.Name}"));
            builder.AddAttribute(10, nameof(OperationalTable<Row<int>, int>.TableLabel), $"{row.Name} records");
            builder.AddAttribute(11, nameof(OperationalTable<Row<int>, int>.ColumnCount), 2);
            builder.AddAttribute(12, nameof(OperationalTable<Row<int>, int>.State), state);
            builder.CloseComponent();
        }
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
