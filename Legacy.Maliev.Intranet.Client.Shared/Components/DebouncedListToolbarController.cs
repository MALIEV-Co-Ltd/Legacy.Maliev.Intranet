namespace Legacy.Maliev.Intranet.Client.Shared.Components;

public sealed class DebouncedListToolbarController<TSort> : IAsyncDisposable
    where TSort : struct, Enum
{
    private readonly TimeProvider timeProvider;
    private readonly TimeSpan debounceDelay;
    private readonly object sync = new();
    private CancellationTokenSource? pendingSearch;
    private bool disposed;
    private ListToolbarState<TSort> current;
    private ListToolbarState<TSort> lastEmitted;

    public DebouncedListToolbarController(
        ListToolbarState<TSort> value,
        ListToolbarState<TSort> defaults,
        TimeProvider? timeProvider = null,
        TimeSpan? debounceDelay = null)
    {
        current = value.Normalize();
        lastEmitted = current;
        Defaults = defaults.Normalize();
        this.timeProvider = timeProvider ?? TimeProvider.System;
        this.debounceDelay = debounceDelay ?? TimeSpan.FromMilliseconds(350);
    }

    public ListToolbarState<TSort> Current => current;
    public ListToolbarState<TSort> Defaults { get; private set; }
    public bool CanClear => current != Defaults;

    public void Hydrate(ListToolbarState<TSort> value, ListToolbarState<TSort> defaults)
    {
        ThrowIfDisposed();
        CancelPendingSearch();
        current = value.Normalize();
        lastEmitted = current;
        Defaults = defaults.Normalize();
    }

    public async Task ChangeSearchAsync(
        string? search,
        Func<ListToolbarRequest<TSort>, CancellationToken, Task> emit,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(emit);
        current = (current with { Search = search }).Normalize();

        var source = ReplacePendingSearch(cancellationToken);
        try
        {
            await Task.Delay(debounceDelay, timeProvider, source.Token).ConfigureAwait(false);
            await EmitIfChangedAsync(ListToolbarChangeReason.SearchDebounced, emit, source.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (source.IsCancellationRequested)
        {
            // Superseded input and disposal are expected terminal states.
        }
        finally
        {
            lock (sync)
            {
                if (ReferenceEquals(pendingSearch, source))
                {
                    pendingSearch = null;
                }
            }

            source.Dispose();
        }
    }

    public Task ChangeSortAsync(
        TSort sort,
        Func<ListToolbarRequest<TSort>, CancellationToken, Task> emit,
        CancellationToken cancellationToken = default)
    {
        current = current with { Sort = sort };
        return ApplyImmediateAsync(ListToolbarChangeReason.SortChanged, emit, cancellationToken);
    }

    public Task ChangePageSizeAsync(
        int pageSize,
        Func<ListToolbarRequest<TSort>, CancellationToken, Task> emit,
        CancellationToken cancellationToken = default)
    {
        if (pageSize <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(pageSize));
        }

        current = current with { PageSize = pageSize };
        return ApplyImmediateAsync(ListToolbarChangeReason.PageSizeChanged, emit, cancellationToken);
    }

    public Task ClearAsync(
        Func<ListToolbarRequest<TSort>, CancellationToken, Task> emit,
        CancellationToken cancellationToken = default)
    {
        current = Defaults;
        return ApplyImmediateAsync(ListToolbarChangeReason.Cleared, emit, cancellationToken);
    }

    public async Task RefreshAsync(
        Func<ListToolbarRequest<TSort>, CancellationToken, Task> emit,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(emit);
        CancelPendingSearch();
        current = current.Normalize();
        lastEmitted = current;
        await emit(new(current, ListToolbarChangeReason.Refreshed), cancellationToken).ConfigureAwait(false);
    }

    private async Task ApplyImmediateAsync(
        ListToolbarChangeReason reason,
        Func<ListToolbarRequest<TSort>, CancellationToken, Task> emit,
        CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(emit);
        CancelPendingSearch();
        await EmitIfChangedAsync(reason, emit, cancellationToken).ConfigureAwait(false);
    }

    private async Task EmitIfChangedAsync(
        ListToolbarChangeReason reason,
        Func<ListToolbarRequest<TSort>, CancellationToken, Task> emit,
        CancellationToken cancellationToken)
    {
        current = current.Normalize();
        if (current == lastEmitted)
        {
            return;
        }

        var request = new ListToolbarRequest<TSort>(current, reason);
        await emit(request, cancellationToken).ConfigureAwait(false);
        lastEmitted = current;
    }

    private CancellationTokenSource ReplacePendingSearch(CancellationToken cancellationToken)
    {
        lock (sync)
        {
            pendingSearch?.Cancel();
            pendingSearch = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            return pendingSearch;
        }
    }

    private void CancelPendingSearch()
    {
        lock (sync)
        {
            pendingSearch?.Cancel();
            pendingSearch = null;
        }
    }

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(disposed, this);

    public ValueTask DisposeAsync()
    {
        if (!disposed)
        {
            disposed = true;
            CancelPendingSearch();
        }

        return ValueTask.CompletedTask;
    }
}
