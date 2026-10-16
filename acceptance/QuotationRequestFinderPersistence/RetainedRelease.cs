using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Hosting;

namespace QuotationRequestFinderPersistence.Acceptance;

// Keep an actual operation AND its owner rooted across an observation timeout. A timeout
// never marks disposal complete, cancels ownership, or starts a second disposal operation.
internal sealed class RetainedRelease(string kind, object owner, Func<Task> release)
{
    private static readonly ConcurrentDictionary<string, RetainedRelease> Pending = new();
    private readonly object gate = new();
    private readonly string id = kind + "-" + Guid.NewGuid().ToString("N");
    private readonly DateTimeOffset expiry = DateTimeOffset.UtcNow.AddMinutes(25);
    private readonly object retainedOwner = owner;
    private Task? actual;
    private int reportingErrors;
    private int observations;
    private bool terminalRecorded;

    public Task Actual()
    {
        lock (gate)
        {
            if (actual is not null) return actual;
            Pending[id] = this;
            Record("pending", false);
            actual = RunAsync();
            return actual;
        }
    }

    private async Task RunAsync()
    {
        var completed = false;
        try
        {
            await release().ConfigureAwait(false);
            Record("completed", true);
            if (reportingErrors != 0) throw new IOException("Owned release receipt could not be recorded.");
            completed = true;
        }
        catch
        {
            Record("failed", false);
            throw;
        }
        finally
        {
            GC.KeepAlive(retainedOwner);
            if (completed) Pending.TryRemove(id, out _);
        }
    }

    public async Task ObserveAsync()
    {
        var operation = Actual();
        while (true)
        {
            if (operation.IsCompleted) { await operation.ConfigureAwait(false); return; }
            var attempt = Interlocked.Increment(ref observations);
            if (attempt > 2)
            {
                Record("unresolved", false);
                throw new TimeoutException("Owned disposal remains unresolved; actual task and owner are retained until settlement.");
            }
            try
            {
                await operation.WaitAsync(TimeSpan.FromSeconds(attempt == 1 ? 30 : 10)).ConfigureAwait(false);
                return;
            }
            catch (TimeoutException) when (!operation.IsCompleted)
            {
                Record(attempt == 1 ? "pending-after-first-timeout" : "unresolved", false);
                if (attempt == 2) throw new TimeoutException("Owned disposal remains unresolved; actual task and owner are retained until settlement.");
            }
        }
    }

    private void Record(string state, bool success)
    {
        var folder = Environment.GetEnvironmentVariable("FINDER_JOIN_RESOURCES");
        if (string.IsNullOrWhiteSpace(folder)) return;
        try
        {
            lock (gate)
            {
                if (terminalRecorded && state is not ("completed" or "failed")) return;
                if (state is "completed" or "failed") terminalRecorded = true;
                Directory.CreateDirectory(folder);
                File.WriteAllText(Path.Combine(folder, "release-" + id + ".json"), JsonSerializer.Serialize(new
                {
                    id, kind, state, success, expiryUtc = expiry, observedUtc = DateTimeOffset.UtcNow,
                    reportingErrors, actualOperationCompleted = state is "completed" or "failed",
                }));
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            Interlocked.Increment(ref reportingErrors);
        }
    }
}

internal abstract class OwnedFactory<TProgram> : WebApplicationFactory<TProgram> where TProgram : class
{
    private IHost? builtHost;
    private bool startFailed;
    private readonly object releaseGate = new();
    private RetainedRelease? release;

    protected override IHost CreateHost(IHostBuilder builder)
    {
        var host = builder.Build();
        builtHost = host; // Retained before Start: WAF does not own its _host yet if Start throws.
        try { host.Start(); return host; }
        catch
        {
            startFailed = true;
            _ = ReleaseActualAsync(); // Registered owner retains and observes this actual release task.
            throw; // Preserve the actual startup failure; InitializeAsync joins cleanup separately.
        }
    }

    public Task ReleaseActualAsync()
    {
        lock (releaseGate)
        {
            release ??= new RetainedRelease(typeof(TProgram).Assembly.GetName().Name!, this, ReleaseCoreAsync);
            return release.Actual();
        }
    }

    private async Task ReleaseCoreAsync()
    {
        var failures = new List<Exception>();
        try { await base.DisposeAsync().ConfigureAwait(false); }
        catch (Exception exception) { failures.Add(exception); }
        if (startFailed && builtHost is not null)
        {
            try { await builtHost.StopAsync(CancellationToken.None).ConfigureAwait(false); }
            catch (Exception exception) { failures.Add(exception); }
            try
            {
                if (builtHost is IAsyncDisposable asyncHost) await asyncHost.DisposeAsync().ConfigureAwait(false);
                else builtHost.Dispose();
            }
            catch (Exception exception) { failures.Add(exception); }
        }
        if (failures.Count != 0) throw new AggregateException("Host release failed.", failures);
    }
}
