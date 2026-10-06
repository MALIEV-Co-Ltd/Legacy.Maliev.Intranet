namespace Legacy.Maliev.Intranet.Server.Infrastructure;

/// <summary>Owns timers created by the host's clock until their disposal or host shutdown.</summary>
/// <param name="clock">The clock whose time and timer behavior is forwarded.</param>
/// <remarks>
/// ServiceDiscovery 10.10.0 creates its HTTP resolver outside DI and does not dispose
/// its recurring timer with the handler. Owning the public timer boundary stops that
/// proven shutdown root without changing discovery, clock, or handler-pool behavior.
/// This does not replace the resolver's own asynchronous watcher cleanup.
/// </remarks>
public sealed class HostOwnedTimeProvider(TimeProvider clock) : TimeProvider, IDisposable, IAsyncDisposable
{
    private readonly object gate = new();
    private readonly HashSet<OwnedTimer> timers = [];
    private readonly List<Exception> creationFailures = [];
    private int creating;
    private TaskCompletionSource? shutdown;
    private TaskCompletionSource? creationDrain;

    /// <inheritdoc />
    public override DateTimeOffset GetUtcNow() => clock.GetUtcNow();
    /// <inheritdoc />
    public override TimeZoneInfo LocalTimeZone => clock.LocalTimeZone;
    /// <inheritdoc />
    public override long GetTimestamp() => clock.GetTimestamp();
    /// <inheritdoc />
    public override long TimestampFrequency => clock.TimestampFrequency;

    /// <inheritdoc />
    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(shutdown is not null, this);
            creating++;
        }
        var reserved = true;
        try
        {
            // Forward the original callback/state directly. No owner gate is held
            // across clock code, and shutdown joins this admitted creation.
            var timer = new OwnedTimer(this, clock.CreateTimer(callback, state, dueTime, period));
            lock (gate)
            {
                if (shutdown is null)
                {
                    timers.Add(timer);
                    FinishCreation(null);
                    reserved = false;
                    return timer;
                }
            }
            reserved = false;
            _ = RejectCreationAsync(timer);
            throw new ObjectDisposedException(nameof(HostOwnedTimeProvider));
        }
        finally
        {
            if (reserved)
            {
                lock (gate) FinishCreation(null);
            }
        }
    }

    private async Task RejectCreationAsync(OwnedTimer timer)
    {
        Exception? failure = null;
        try { await timer.DisposeAsync().ConfigureAwait(false); }
        catch (Exception error) { failure = error; }
        finally
        {
            lock (gate) FinishCreation(failure);
        }
    }

    // Called only under gate; the reservation lasts through rejected-child drain.
    private void FinishCreation(Exception? failure)
    {
        if (failure is not null) creationFailures.Add(failure);
        creating--;
        if (creating != 0 || creationDrain is null) return;
        if (creationFailures.Count == 0) creationDrain.TrySetResult();
        else creationDrain.TrySetException(new AggregateException(creationFailures));
        creationFailures.Clear();
    }

    private void Untrack(OwnedTimer timer)
    {
        lock (gate) timers.Remove(timer);
    }

    private Task BeginShutdown()
    {
        TaskCompletionSource completion;
        Task drain;
        OwnedTimer[] owned;
        lock (gate)
        {
            if (shutdown is not null) return shutdown.Task;
            shutdown = completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
            creationDrain = new(TaskCreationOptions.RunContinuationsAsynchronously);
            if (creating == 0) creationDrain.SetResult();
            drain = creationDrain.Task;
            owned = timers.ToArray();
        }
        _ = CompleteShutdownAsync(completion, owned, drain);
        return completion.Task;
    }

    private static async Task CompleteShutdownAsync(TaskCompletionSource completion, OwnedTimer[] owned, Task drain)
    {
        // Start every native disposal before joining callbacks or creations.
        var pending = owned.Select(timer => timer.DisposeAsync().AsTask()).Append(drain).ToArray();
        try
        {
            await Task.WhenAll(pending).ConfigureAwait(false);
            completion.SetResult();
        }
        catch
        {
            completion.SetException(new AggregateException(pending.Where(task => task.IsFaulted)
                .SelectMany(task => task.Exception!.InnerExceptions)));
            _ = completion.Task.Exception;
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        var drain = BeginShutdown();
        // Like native synchronous timer disposal, stop future ticks immediately.
        // Keep the shared callback drain for a later asynchronous owner disposal.
        if (drain.IsCompleted) drain.GetAwaiter().GetResult();
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync() => new(BeginShutdown());

    private sealed class OwnedTimer(HostOwnedTimeProvider owner, ITimer timer) : ITimer
    {
        private readonly object cleanupGate = new();
        private TaskCompletionSource? cleanup;

        public bool Change(TimeSpan dueTime, TimeSpan period) => timer.Change(dueTime, period);

        private Task BeginCleanup()
        {
            TaskCompletionSource completion;
            lock (cleanupGate)
            {
                if (cleanup is not null) return cleanup.Task;
                cleanup = completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
            }
            _ = CompleteCleanupAsync(completion);
            return completion.Task;
        }

        private async Task CompleteCleanupAsync(TaskCompletionSource completion)
        {
            try
            {
                await timer.DisposeAsync().ConfigureAwait(false);
                completion.SetResult();
            }
            catch (Exception error)
            {
                completion.SetException(error);
                _ = completion.Task.Exception;
            }
            finally { owner.Untrack(this); }
        }

        public void Dispose()
        {
            var drain = BeginCleanup();
            if (drain.IsCompleted) drain.GetAwaiter().GetResult();
        }

        public ValueTask DisposeAsync() => new(BeginCleanup());
    }
}
