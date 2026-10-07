using Legacy.Maliev.Intranet.Server.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace Legacy.Maliev.Intranet.Tests;

public sealed class HostOwnedTimeProviderTests
{
    [Fact]
    public void ForwardsClockAndOriginalTimerArgumentsAndChanges()
    {
        var clock = new RecordingClock();
        using var owner = new HostOwnedTimeProvider(clock);
        Assert.Equal(clock.GetUtcNow(), owner.GetUtcNow());
        Assert.Same(clock.LocalTimeZone, owner.LocalTimeZone);
        Assert.Equal(clock.GetTimestamp(), owner.GetTimestamp());
        Assert.Equal(clock.TimestampFrequency, owner.TimestampFrequency);
        var state = new object();
        object? observed = null;
        TimerCallback callback = value => observed = value;
        using var timer = owner.CreateTimer(callback, state, TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(3));
        var native = Assert.Single(clock.Timers);
        Assert.Same(callback, native.Callback);
        Assert.Same(state, native.State);
        Assert.Equal((TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(3)), native.Schedule);
        native.Callback(native.State);
        Assert.Same(state, observed);
        Assert.True(timer.Change(TimeSpan.FromSeconds(4), TimeSpan.FromSeconds(5)));
        Assert.Equal((TimeSpan.FromSeconds(4), TimeSpan.FromSeconds(5)), native.Schedule);
    }

    [Fact]
    public void ShutdownDisposesOnlyItsLiveTimersExactlyOnce()
    {
        var clock = new RecordingClock();
        using var unrelated = clock.CreateTimer(_ => { }, null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        var owner = new HostOwnedTimeProvider(clock);
        var first = owner.CreateTimer(_ => { }, null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        var second = owner.CreateTimer(_ => { }, null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        first.Dispose();
        owner.Dispose();
        owner.Dispose();
        second.Dispose();
        Assert.Equal(0, clock.Timers[0].Disposals);
        Assert.Equal(1, clock.Timers[1].Disposals);
        Assert.Equal(1, clock.Timers[2].Disposals);
        Assert.Throws<ObjectDisposedException>(() => owner.CreateTimer(_ => { }, null, TimeSpan.Zero, Timeout.InfiniteTimeSpan));
        Assert.False(second.Change(TimeSpan.Zero, Timeout.InfiniteTimeSpan));
        Assert.Equal(3, clock.Timers.Count);
    }

    [Fact]
    public async Task AsyncShutdownJoinsAnAlreadyRunningNativeCallback()
    {
        using var release = new ManualResetEventSlim();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var owner = new HostOwnedTimeProvider(TimeProvider.System);
        _ = owner.CreateTimer(_ =>
        {
            entered.TrySetResult();
            release.Wait(TimeSpan.FromSeconds(5));
        }, null, TimeSpan.Zero, Timeout.InfiniteTimeSpan);
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            var disposal = owner.DisposeAsync().AsTask();
            Assert.False(disposal.IsCompleted);
            release.Set();
            await disposal.WaitAsync(TimeSpan.FromSeconds(5));
            await owner.DisposeAsync();
        }
        finally
        {
            release.Set();
            await owner.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CreationRacingShutdownRejectsAndDisposesTheLateChild(bool asynchronous)
    {
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var clock = new RecordingClock { Entered = entered, Release = release };
        var owner = new HostOwnedTimeProvider(clock);
        var creation = Task.Run(() => owner.CreateTimer(_ => { }, null, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1)));
        try
        {
            Assert.True(entered.Wait(TimeSpan.FromSeconds(5)));
            if (!asynchronous) owner.Dispose();
            var shutdown = owner.DisposeAsync().AsTask();
            Assert.False(shutdown.IsCompleted);
            release.Set();
            await Assert.ThrowsAsync<ObjectDisposedException>(async () =>
            {
                _ = await creation.WaitAsync(TimeSpan.FromSeconds(5));
            });
            await shutdown.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(1, Assert.Single(clock.Timers).Disposals);
        }
        finally
        {
            release.Set();
            try { await creation.WaitAsync(TimeSpan.FromSeconds(5)); }
            catch (ObjectDisposedException) { }
            await owner.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
        }
    }

    [Fact]
    public void FactoryRegistrationGivesTheRealProviderShutdownOwnership()
    {
        var clock = new RecordingClock();
        var services = new ServiceCollection();
        services.AddSingleton<TimeProvider>(_ => new HostOwnedTimeProvider(clock));
        var provider = services.BuildServiceProvider();
        _ = provider.GetRequiredService<TimeProvider>().CreateTimer(_ => { }, null, TimeSpan.Zero, TimeSpan.FromSeconds(10));
        provider.Dispose();
        Assert.Equal(1, Assert.Single(clock.Timers).Disposals);
    }

    [Fact]
    public void CleanupFailureStillStopsEveryOtherOwnedTimer()
    {
        var clock = new RecordingClock();
        var owner = new HostOwnedTimeProvider(clock);
        _ = owner.CreateTimer(_ => { }, null, TimeSpan.Zero, TimeSpan.FromSeconds(1));
        _ = owner.CreateTimer(_ => { }, null, TimeSpan.Zero, TimeSpan.FromSeconds(1));
        clock.Timers[0].FailDisposal = true;
        Assert.Throws<AggregateException>(owner.Dispose);
        Assert.All(clock.Timers, timer => Assert.Equal(1, timer.Disposals));
        Assert.Throws<AggregateException>(owner.Dispose);
    }

    [Fact]
    public async Task DisposedChangePreservesNativeResultAndArgumentValidation()
    {
        await using var owner = new HostOwnedTimeProvider(TimeProvider.System);
        var timer = owner.CreateTimer(_ => { }, null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        timer.Dispose();
        await timer.DisposeAsync();
        Assert.False(timer.Change(TimeSpan.Zero, Timeout.InfiniteTimeSpan));
        Assert.Throws<ArgumentOutOfRangeException>(() => timer.Change(TimeSpan.FromMilliseconds(-2), Timeout.InfiniteTimeSpan));
        Assert.Throws<ArgumentOutOfRangeException>(() => timer.Change(Timeout.InfiniteTimeSpan, TimeSpan.FromMilliseconds(-2)));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SynchronousDisposalRetainsTheNativeCallbackDrain(bool disposeOwner)
    {
        using var release = new ManualResetEventSlim();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var owner = new HostOwnedTimeProvider(TimeProvider.System);
        var timer = owner.CreateTimer(_ =>
        {
            entered.TrySetResult();
            release.Wait(TimeSpan.FromSeconds(5));
        }, null, TimeSpan.Zero, Timeout.InfiniteTimeSpan);
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            if (disposeOwner) owner.Dispose();
            else timer.Dispose();
            var timerDrain = timer.DisposeAsync().AsTask();
            var ownerDrain = owner.DisposeAsync().AsTask();
            Assert.False(timerDrain.IsCompleted);
            Assert.False(ownerDrain.IsCompleted);
            release.Set();
            await Task.WhenAll(timerDrain, ownerDrain).WaitAsync(TimeSpan.FromSeconds(5));
        }
        finally
        {
            release.Set();
            await owner.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
        }
    }

    [Fact]
    public async Task ShutdownJoinsAnAdmittedCreationAndItsAlreadyRunningCallback()
    {
        using var creationEntered = new ManualResetEventSlim();
        using var creationRelease = new ManualResetEventSlim();
        using var callbackRelease = new ManualResetEventSlim();
        var callbackEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var clock = new BlockedNativeCreationClock(creationEntered, creationRelease);
        var owner = new HostOwnedTimeProvider(clock);
        var creation = Task.Run(() => owner.CreateTimer(_ =>
        {
            callbackEntered.TrySetResult();
            callbackRelease.Wait(TimeSpan.FromSeconds(5));
        }, null, TimeSpan.Zero, Timeout.InfiniteTimeSpan));
        try
        {
            Assert.True(creationEntered.Wait(TimeSpan.FromSeconds(5)));
            await callbackEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            var shutdown = owner.DisposeAsync().AsTask();
            Assert.False(shutdown.IsCompleted);
            creationRelease.Set();
            await Assert.ThrowsAsync<ObjectDisposedException>(async () =>
            {
                _ = await creation.WaitAsync(TimeSpan.FromSeconds(5));
            });
            Assert.False(shutdown.IsCompleted);
            callbackRelease.Set();
            await shutdown.WaitAsync(TimeSpan.FromSeconds(5));
        }
        finally
        {
            creationRelease.Set();
            callbackRelease.Set();
            try { await creation.WaitAsync(TimeSpan.FromSeconds(5)); }
            catch (ObjectDisposedException) { }
            await owner.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
        }
    }

    private sealed class BlockedNativeCreationClock(ManualResetEventSlim entered, ManualResetEventSlim release) : TimeProvider
    {
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            var timer = TimeProvider.System.CreateTimer(callback, state, dueTime, period);
            entered.Set();
            if (release.Wait(TimeSpan.FromSeconds(5))) return timer;
            timer.Dispose();
            throw new TimeoutException("Native timer creation was not released.");
        }
    }

    private sealed class RecordingClock : TimeProvider
    {
        public List<RecordingTimer> Timers { get; } = [];
        public ManualResetEventSlim? Entered { get; init; }
        public ManualResetEventSlim? Release { get; init; }
        public override DateTimeOffset GetUtcNow() => new(2026, 10, 6, 0, 0, 0, TimeSpan.Zero);
        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
        public override long GetTimestamp() => 12345;
        public override long TimestampFrequency => 1000;
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            Entered?.Set();
            if (Release is not null && !Release.Wait(TimeSpan.FromSeconds(5)))
                throw new TimeoutException("Synthetic timer creation was not released.");
            var timer = new RecordingTimer(callback, state, dueTime, period);
            Timers.Add(timer);
            return timer;
        }
    }

    private sealed class RecordingTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period) : ITimer
    {
        public TimerCallback Callback { get; } = callback;
        public object? State { get; } = state;
        public (TimeSpan DueTime, TimeSpan Period) Schedule { get; private set; } = (dueTime, period);
        public int Disposals { get; private set; }
        public bool FailDisposal { get; set; }
        public bool Change(TimeSpan due, TimeSpan repeat)
        {
            if (Disposals != 0) return false;
            Schedule = (due, repeat);
            return true;
        }
        public void Dispose()
        {
            Disposals++;
            if (FailDisposal) throw new InvalidOperationException("Synthetic timer disposal failure.");
        }
        public ValueTask DisposeAsync()
        {
            Dispose();
            return ValueTask.CompletedTask;
        }
    }
}
