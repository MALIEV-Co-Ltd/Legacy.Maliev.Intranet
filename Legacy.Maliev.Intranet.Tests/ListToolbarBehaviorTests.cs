using Legacy.Maliev.Intranet.Client.Shared.Components;
using Legacy.Maliev.Intranet.Client.Shared.Infrastructure;
using Microsoft.Extensions.Time.Testing;

namespace Legacy.Maliev.Intranet.Tests;

public sealed class ListToolbarBehaviorTests
{
    private static readonly ListToolbarState<TestSort> Defaults = new(null, TestSort.Newest, 25);

    [Fact]
    public void SharedToolbarUsesNativeShadcnControls()
    {
        var root = FindRoot();
        var source = File.ReadAllText(Path.Combine(
            root,
            "Legacy.Maliev.Intranet.Client.Shared",
            "Components",
            "ListToolbar.razor"));

        Assert.Contains("<ShadcnInput", source, StringComparison.Ordinal);
        Assert.Contains("<ShadcnNativeSelect", source, StringComparison.Ordinal);
        Assert.Contains("<ShadcnButton", source, StringComparison.Ordinal);
        Assert.Contains("<ShadcnIcon", source, StringComparison.Ordinal);
        Assert.DoesNotContain("<Mud", source, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Search_waits_for_debounce_and_emits_only_the_latest_value()
    {
        var time = new FakeTimeProvider();
        await using var controller = new DebouncedListToolbarController<TestSort>(Defaults, Defaults, time, TimeSpan.FromMilliseconds(350));
        var requests = new List<ListToolbarRequest<TestSort>>();

        var first = controller.ChangeSearchAsync("s", Capture);
        time.Advance(TimeSpan.FromMilliseconds(200));
        var second = controller.ChangeSearchAsync("steel", Capture);
        time.Advance(TimeSpan.FromMilliseconds(349));
        Assert.Empty(requests);

        time.Advance(TimeSpan.FromMilliseconds(1));
        await Task.WhenAll(first, second);

        var request = Assert.Single(requests);
        Assert.Equal("steel", request.State.Search);
        Assert.Equal(ListToolbarChangeReason.SearchDebounced, request.Reason);
        return;

        Task Capture(ListToolbarRequest<TestSort> request, CancellationToken _)
        {
            requests.Add(request);
            return Task.CompletedTask;
        }
    }

    [Fact]
    public async Task Immediate_changes_cancel_pending_search_and_include_latest_visible_state()
    {
        var time = new FakeTimeProvider();
        await using var controller = new DebouncedListToolbarController<TestSort>(Defaults, Defaults, time, TimeSpan.FromMilliseconds(350));
        var requests = new List<ListToolbarRequest<TestSort>>();

        var pending = controller.ChangeSearchAsync("aluminium", Capture);
        await controller.ChangeSortAsync(TestSort.Oldest, Capture);
        time.Advance(TimeSpan.FromSeconds(1));
        await pending;

        var request = Assert.Single(requests);
        Assert.Equal(new ListToolbarState<TestSort>("aluminium", TestSort.Oldest, 25), request.State);
        Assert.Equal(ListToolbarChangeReason.SortChanged, request.Reason);

        await controller.ChangePageSizeAsync(50, Capture);
        Assert.Equal(ListToolbarChangeReason.PageSizeChanged, requests[^1].Reason);
        Assert.Equal(50, requests[^1].State.PageSize);
        return;

        Task Capture(ListToolbarRequest<TestSort> request, CancellationToken _)
        {
            requests.Add(request);
            return Task.CompletedTask;
        }
    }

    [Fact]
    public async Task Identical_state_is_a_no_op_but_refresh_always_emits()
    {
        await using var controller = new DebouncedListToolbarController<TestSort>(Defaults, Defaults);
        var requests = new List<ListToolbarRequest<TestSort>>();

        await controller.ChangeSortAsync(TestSort.Newest, Capture);
        await controller.ChangePageSizeAsync(25, Capture);
        Assert.Empty(requests);

        await controller.RefreshAsync(Capture);
        Assert.Equal(ListToolbarChangeReason.Refreshed, Assert.Single(requests).Reason);
        return;

        Task Capture(ListToolbarRequest<TestSort> request, CancellationToken _)
        {
            requests.Add(request);
            return Task.CompletedTask;
        }
    }

    [Fact]
    public async Task Clear_restores_defaults_once()
    {
        var initial = new ListToolbarState<TestSort>("active", TestSort.Oldest, 100);
        await using var controller = new DebouncedListToolbarController<TestSort>(initial, Defaults);
        var requests = new List<ListToolbarRequest<TestSort>>();

        await controller.ClearAsync((request, _) =>
        {
            requests.Add(request);
            return Task.CompletedTask;
        });

        var request = Assert.Single(requests);
        Assert.Equal(Defaults, request.State);
        Assert.Equal(ListToolbarChangeReason.Cleared, request.Reason);
    }

    [Fact]
    public async Task Disposal_cancels_pending_search_without_emitting()
    {
        var time = new FakeTimeProvider();
        var controller = new DebouncedListToolbarController<TestSort>(Defaults, Defaults, time, TimeSpan.FromMilliseconds(350));
        var emitted = false;
        var pending = controller.ChangeSearchAsync("pending", (_, _) =>
        {
            emitted = true;
            return Task.CompletedTask;
        });

        await controller.DisposeAsync();
        time.Advance(TimeSpan.FromSeconds(1));
        await pending;

        Assert.False(emitted);
    }

    [Fact]
    public void Latest_request_gate_cancels_and_rejects_superseded_leases()
    {
        using var gate = new LatestRequestGate();
        using var first = gate.Begin();
        using var second = gate.Begin();

        Assert.True(first.CancellationToken.IsCancellationRequested);
        Assert.False(first.IsCurrent);
        Assert.True(second.IsCurrent);
        Assert.False(second.CancellationToken.IsCancellationRequested);
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

    private enum TestSort
    {
        Newest,
        Oldest,
    }
}
