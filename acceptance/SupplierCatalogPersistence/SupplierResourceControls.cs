namespace SupplierCatalogPersistence.Acceptance;

/// <summary>Exercises actual ownership transitions without starting native resources.</summary>
public sealed class SupplierResourceControls
{
    /// <summary>A synchronous allocator cannot hold admission or defeat retained cleanup.</summary>
    [Fact]
    public Task SynchronouslyBlockedClientAllocation_IsBoundedAndLateResourceRemainsOwned()
        => BlockedAllocationAsync(backend: false);

    /// <summary>A synchronous backend allocator also retains its late resource until cleanup settles.</summary>
    [Fact]
    public Task SynchronouslyBlockedBackendAllocation_IsBoundedAndLateResourceRemainsOwned()
        => BlockedAllocationAsync(backend: true);

    private static async Task BlockedAllocationAsync(bool backend)
    {
        var entered = Signal();
        var unblock = Signal();
        var returned = Signal();
        var childReleased = false;
        var backendRemoved = false;
        var owner = new SupplierResourceScope(TimeSpan.FromMinutes(1), TimeSpan.FromMilliseconds(250));
        owner.RegisterBackend("dependency", _ => { backendRemoved = true; return Task.CompletedTask; });
        object Allocate()
        {
            entered.TrySetResult();
            unblock.Task.GetAwaiter().GetResult(); // Synchronous, token-ignoring callback.
            returned.TrySetResult();
            return new object();
        }
        Task Release(object _, CancellationToken token) { childReleased = true; return Task.CompletedTask; }
        var acquisition = Task.Run(() => backend
            ? owner.AcquireBackend("blocked", Allocate, Release)
            : owner.Acquire("blocked", Allocate, Release));
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await Assert.ThrowsAsync<TimeoutException>(async () => await acquisition.WaitAsync(TimeSpan.FromSeconds(5)));
            await Assert.ThrowsAsync<AggregateException>(async () => await owner.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5)));
            Assert.False(childReleased);
            Assert.False(backendRemoved);
            Assert.Contains(owner, SupplierResourceScope.Unresolved);
            unblock.TrySetResult();
            await returned.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await owner.DisposeAsync();
            Assert.True(childReleased);
            Assert.True(backendRemoved);
            Assert.True(owner.Released);
        }
        finally { unblock.TrySetResult(); await owner.DisposeAsync(); }
    }

    /// <summary>A startup callback that blocks before returning its Task remains visible to cleanup.</summary>
    [Fact]
    public async Task SynchronouslyBlockedStartup_IsBoundedAndFencesDependencies()
    {
        var entered = Signal();
        var unblock = Signal();
        var removed = false;
        var owner = new SupplierResourceScope(TimeSpan.FromMinutes(1), TimeSpan.FromMilliseconds(250));
        owner.RegisterBackend("database", _ => { removed = true; return Task.CompletedTask; });
        var host = owner.Register("host", _ => Task.CompletedTask);
        var startup = owner.StartAsync(host, _ =>
        {
            entered.TrySetResult();
            unblock.Task.GetAwaiter().GetResult();
            return Task.CompletedTask;
        });
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await Assert.ThrowsAsync<TimeoutException>(() => startup.WaitAsync(TimeSpan.FromSeconds(5)));
            await Assert.ThrowsAsync<AggregateException>(async () => await owner.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5)));
            Assert.False(removed);
            Assert.Contains(owner, SupplierResourceScope.Unresolved);
            unblock.TrySetResult();
            await host.Startup!.WaitAsync(TimeSpan.FromSeconds(5));
            await owner.DisposeAsync();
            Assert.True(removed);
            Assert.True(owner.Released);
        }
        finally { unblock.TrySetResult(); await owner.DisposeAsync(); }
    }

    /// <summary>Synchronous shutdown is tracked before dispatch and is never repeated in flight.</summary>
    [Fact]
    public async Task SynchronouslyBlockedRelease_IsBoundedAndRetryAwaitsSameAttempt()
    {
        var entered = Signal();
        var unblock = Signal();
        var releases = 0;
        var removed = false;
        var owner = new SupplierResourceScope(TimeSpan.FromMinutes(1), TimeSpan.FromMilliseconds(250));
        owner.RegisterBackend("database", _ => { removed = true; return Task.CompletedTask; });
        var host = owner.Register("host", _ =>
        {
            Interlocked.Increment(ref releases);
            entered.TrySetResult();
            unblock.Task.GetAwaiter().GetResult();
            return Task.CompletedTask;
        });
        try
        {
            var cleanup = owner.DisposeAsync().AsTask();
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await Assert.ThrowsAsync<AggregateException>(() => cleanup.WaitAsync(TimeSpan.FromSeconds(5)));
            await Assert.ThrowsAsync<AggregateException>(async () => await owner.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5)));
            Assert.Equal(1, releases);
            Assert.False(removed);
            Assert.Contains(owner, SupplierResourceScope.Unresolved);
            unblock.TrySetResult();
            await host.ReleaseSettlement!.WaitAsync(TimeSpan.FromSeconds(5));
            await owner.DisposeAsync();
            Assert.Equal(1, releases);
            Assert.True(removed);
            Assert.True(owner.Released);
        }
        finally { unblock.TrySetResult(); await owner.DisposeAsync(); }
    }

    /// <summary>Preregistered work queued before cleanup must recheck admission at actual dispatch.</summary>
    [Fact]
    public Task TerminalBeforeQueuedAllocation_NeverInvokesCallback()
        => TerminalBeforeDispatchAsync(allocation: true);

    /// <summary>Queued startup callbacks recheck admission after terminal cancellation.</summary>
    [Fact]
    public Task TerminalBeforeQueuedStartup_NeverInvokesCallback()
        => TerminalBeforeDispatchAsync(allocation: false);

    private static async Task TerminalBeforeDispatchAsync(bool allocation)
    {
        var queued = new System.Collections.Concurrent.ConcurrentQueue<Action>();
        var enqueued = Signal();
        var hold = true;
        var invoked = false;
        Task? queuedCleanup = null;
        SupplierResourceScope owner = null!;
        owner = new SupplierResourceScope(TimeSpan.FromMinutes(1), TimeSpan.FromMilliseconds(250), queuedWork =>
        {
            if (Volatile.Read(ref hold))
            {
                queued.Enqueue(queuedWork);
                // Cancel before returning to the bounded acquisition wait, rather than racing a scheduled continuation.
                queuedCleanup = owner.DisposeAsync().AsTask();
                enqueued.TrySetResult();
            }
            else { _ = Task.Run(queuedWork); }
        });
        var host = allocation ? null : owner.Register("host", _ => Task.CompletedTask);
        Task work = allocation
            ? Task.Run(() => owner.Acquire("host", () => { invoked = true; return new object(); }, (_, _) => Task.CompletedTask))
            : owner.StartAsync(host!, _ => { invoked = true; return Task.CompletedTask; });
        try
        {
            await enqueued.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.NotNull(queuedCleanup);
            await Assert.ThrowsAsync<AggregateException>(() => queuedCleanup!.WaitAsync(TimeSpan.FromSeconds(5)));
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => work.WaitAsync(TimeSpan.FromSeconds(5)));
            Volatile.Write(ref hold, false);
            Assert.True(queued.TryDequeue(out var dispatch));
            dispatch!();
            await owner.DisposeAsync();
            Assert.False(invoked);
            Assert.True(owner.Released);
        }
        finally
        {
            Volatile.Write(ref hold, false);
            while (queued.TryDequeue(out var dispatch)) dispatch();
            await owner.DisposeAsync();
        }
    }

    private static TaskCompletionSource Signal() => new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>Successful allocation completion cannot reopen handoff after cleanup closes admission.</summary>
    [Fact]
    public async Task TerminalBeforeAllocationReturns_RejectsResourceHandoff()
    {
        // Inline dispatch makes the completion/canceled-token ordering deterministic without a return hook.
        var owner = new SupplierResourceScope(TimeSpan.FromMinutes(1), TimeSpan.FromSeconds(5), work => work());
        Task? cleanup = null;
        var releaseEntered = Signal();
        var releaseBarrier = Signal();
        var childReleases = 0;
        var removed = false;
        owner.RegisterBackend("database", _ => { removed = true; return Task.CompletedTask; });
        try
        {
            Assert.Throws<InvalidOperationException>(() => owner.Acquire("host", () =>
            {
                cleanup = owner.DisposeAsync().AsTask();
                Assert.True(owner.Token.IsCancellationRequested);
                Assert.False(removed); // The preregistered allocation is still unsettled here.
                return new object();
            }, (_, _) =>
            {
                Interlocked.Increment(ref childReleases);
                releaseEntered.TrySetResult();
                return releaseBarrier.Task;
            }));
            Assert.NotNull(cleanup);
            await releaseEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.False(owner.Released);
            Assert.False(removed);
            releaseBarrier.TrySetResult();
            await cleanup!.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(1, childReleases);
            Assert.True(removed);
            Assert.True(owner.Released);
        }
        finally
        {
            releaseBarrier.TrySetResult();
            if (cleanup is not null) await cleanup.WaitAsync(TimeSpan.FromSeconds(5));
            await owner.DisposeAsync();
        }
    }

    /// <summary>Allocated ownership survives a startup exception until cleanup settles.</summary>
    [Fact]
    public async Task PartialStartup_StillReleasesAllocatedHostBeforeBackend()
    {
        var calls = new List<string>();
        var owner = new SupplierResourceScope(TimeSpan.FromMinutes(1));
        owner.RegisterBackend("database", _ => { calls.Add("backend"); return Task.CompletedTask; });
        var host = owner.Register("host", _ => { calls.Add("host"); return Task.CompletedTask; });
        await Assert.ThrowsAsync<IOException>(() => owner.StartAsync(host, _ => throw new IOException("synthetic startup")));
        await owner.DisposeAsync();
        Assert.Equal(new[] { "host", "backend" }, calls);
        Assert.True(owner.Released);
    }

    /// <summary>Failed host shutdown preserves dependencies and supports real retry.</summary>
    [Fact]
    public async Task FailedShutdown_PreservesBackendUntilSuccessfulRetry()
    {
        var backendRemoved = false;
        var fail = true;
        var owner = new SupplierResourceScope(TimeSpan.FromMinutes(1));
        owner.RegisterBackend("database", _ => { backendRemoved = true; return Task.CompletedTask; });
        owner.Register("host", _ => fail ? Task.FromException(new IOException("synthetic shutdown")) : Task.CompletedTask);
        await Assert.ThrowsAsync<AggregateException>(async () => await owner.DisposeAsync());
        Assert.False(backendRemoved);
        Assert.False(owner.Released);
        Assert.Contains(owner, SupplierResourceScope.Unresolved);
        fail = false;
        await owner.DisposeAsync();
        Assert.True(backendRemoved);
        Assert.True(owner.Released);
        Assert.DoesNotContain(owner, SupplierResourceScope.Unresolved);
    }

    /// <summary>Cancellation cannot remove a backend while startup is still running.</summary>
    [Fact]
    public async Task UnsettledStartup_FencesBackendRemoval()
    {
        var startup = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var removed = false;
        var owner = new SupplierResourceScope(TimeSpan.FromMinutes(1), TimeSpan.FromMilliseconds(20));
        owner.RegisterBackend("database", _ => { removed = true; return Task.CompletedTask; });
        var host = owner.Register("host", _ => Task.CompletedTask);
        await Assert.ThrowsAsync<TimeoutException>(() => owner.StartAsync(host, _ => startup.Task));
        await Assert.ThrowsAsync<AggregateException>(async () => await owner.DisposeAsync());
        Assert.False(removed);
        startup.SetResult();
        await owner.DisposeAsync();
        Assert.True(removed);
    }

    /// <summary>Expiry blocks acquisition before any new resource is allocated.</summary>
    [Fact]
    public async Task ExpiredOwner_RejectsNewAcquisition()
    {
        var owner = new SupplierResourceScope(TimeSpan.Zero);
        Assert.Throws<InvalidOperationException>(() => owner.Register("late", _ => Task.CompletedTask));
        await owner.DisposeAsync();
    }

    /// <summary>Preregistration is not authority to dispatch a token-ignoring callback after expiry.</summary>
    [Fact]
    public async Task ExpiredPreregisteredStartup_NeverDispatchesCallback()
    {
        var owner = new SupplierResourceScope(TimeSpan.FromMilliseconds(100));
        var dispatched = false;
        try
        {
            var entry = owner.Register("browser", _ => Task.CompletedTask);
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Task.Delay(Timeout.InfiniteTimeSpan, owner.Token));
            await Assert.ThrowsAsync<InvalidOperationException>(() => owner.StartAsync(entry, _ =>
            {
                dispatched = true; // Deliberately ignores the caller token.
                return Task.CompletedTask;
            }));
            Assert.False(dispatched);
            Assert.Null(entry.Startup);
        }
        finally { await owner.DisposeAsync(); }
        Assert.True(owner.Released);
    }

    /// <summary>An allocation fault leaves a reachable owner without inventing a child.</summary>
    [Fact]
    public async Task AllocationFailure_RemainsReachableAndSettlesWithoutReleaseOfUnknownChild()
    {
        var owner = new SupplierResourceScope(TimeSpan.FromMinutes(1));
        var releasedUnknown = false;
        Assert.Throws<IOException>(() => owner.Acquire<object>("allocation", () => throw new IOException("synthetic allocation"),
            (_, _) => { releasedUnknown = true; return Task.CompletedTask; }));
        Assert.Contains(owner, SupplierResourceScope.Unresolved);
        await owner.DisposeAsync();
        Assert.False(releasedUnknown);
        Assert.True(owner.Released);
    }
}
