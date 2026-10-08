using System.Collections.Concurrent;
using System.Text.Json;

namespace SupplierCatalogPersistence.Acceptance;

// A failed attempt never discards the only owner or repeats an in-flight release.
internal sealed class SupplierResourceScope : IAsyncDisposable
{
    private static readonly ConcurrentDictionary<Guid, SupplierResourceScope> Retained = new();
    private readonly object gate = new();
    private readonly SemaphoreSlim cleanup = new(1, 1);
    private readonly SemaphoreSlim receipts = new(1, 1);
    private readonly List<BackendBinding> bindings = [];
    private long receiptSequence;
    private readonly List<Entry> clients = [];
    private readonly List<Entry> backends = [];
    private readonly CancellationTokenSource lifetime = new();
    private readonly TimeSpan window;
    private readonly Action<Action> dispatch;
    private readonly DateTimeOffset expires;
    private bool terminal;
    internal Guid Id { get; } = Guid.NewGuid();
    internal bool Released { get; private set; }
    internal static IReadOnlyCollection<SupplierResourceScope> Unresolved => Retained.Values.ToArray();
    internal CancellationToken Token => lifetime.Token;

    internal SupplierResourceScope(TimeSpan lease, TimeSpan? window = null, Action<Action>? dispatch = null)
    {
        this.window = window ?? TimeSpan.FromSeconds(30);
        this.dispatch = dispatch ?? (work => { _ = Task.Run(work); });
        expires = DateTimeOffset.UtcNow + lease;
        lifetime.CancelAfter(lease);
        Retained[Id] = this;
    }

    internal Entry Register(string name, Func<CancellationToken, Task> release, int phase = 0) => Add(name, release, clients, phase);
    internal Entry RegisterBackend(string name, Func<CancellationToken, Task> release) => Add(name, release, backends, 0);

    internal T Acquire<T>(string name, Func<T> allocate, Func<T, CancellationToken, Task> release, int phase = 0) where T : class
        => AcquireOwned(name, allocate, release, clients, phase);

    internal T AcquireBackend<T>(string name, Func<T> allocate, Func<T, CancellationToken, Task> release) where T : class
        => AcquireOwned(name, allocate, release, backends, 0);

    private T AcquireOwned<T>(string name, Func<T> allocate, Func<T, CancellationToken, Task> release,
        List<Entry> entries, int phase) where T : class
    {
        T? resource = null;
        Entry entry;
        TaskCompletionSource completion;
        CancellationToken token;
        lock (gate)
        {
            entry = Add(name, releaseToken => resource is null ? Task.CompletedTask : release(resource, releaseToken), entries, phase);
            completion = ReserveStartup(entry);
            token = lifetime.Token;
        }
        DispatchStartup(completion, _ => { resource = allocate(); return Task.CompletedTask; });
        // Preserve the synchronous API required by WebApplicationFactory.CreateHost.
        // The tracked worker, including a late allocation, remains owned after timeout.
        completion.Task.WaitAsync(window, token).GetAwaiter().GetResult();
        lock (gate)
        {
            // Completion alone is not authority to hand back a resource after cleanup closed admission.
            RequireAdmission();
            return resource!;
        }
    }

    private Entry Add(string name, Func<CancellationToken, Task> release, List<Entry> entries, int phase)
    {
        lock (gate)
        {
            RequireAdmission();
            var entry = new Entry(name, release, phase, dispatch);
            entries.Add(entry);
            return entry;
        }
    }

    internal async Task StartAsync(Entry entry, Func<CancellationToken, Task> start)
    {
        TaskCompletionSource completion;
        CancellationToken token;
        lock (gate)
        {
            RequireAdmission();
            if (!clients.Contains(entry) && !backends.Contains(entry)) throw new InvalidOperationException("Unregistered startup.");
            completion = ReserveStartup(entry);
            token = lifetime.Token;
        }
        DispatchStartup(completion, start);
        await completion.Task.WaitAsync(window, token);
    }

    private void RequireAdmission()
    {
        if (terminal || DateTimeOffset.UtcNow >= expires || lifetime.IsCancellationRequested)
            throw new InvalidOperationException("Supplier resource owner is terminal.");
    }

    // Called under gate: ownership and completion exist before any scheduler can run.
    private static TaskCompletionSource ReserveStartup(Entry entry)
    {
        if (entry.Startup is { IsCompleted: false }) throw new InvalidOperationException("Owned startup is already in flight.");
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        entry.Startup = completion.Task;
        return completion;
    }

    private void DispatchStartup(TaskCompletionSource completion, Func<CancellationToken, Task> callback)
    {
        try { dispatch(() => { _ = RunAsync(); }); }
        catch (Exception failure) { completion.TrySetException(failure); }

        async Task RunAsync()
        {
            try
            {
                // Admission is serialized with terminal cleanup; callbacks never run under gate.
                // Once admitted, cleanup must await this completion even if cancellation races it.
                lock (gate) { RequireAdmission(); }
                await callback(lifetime.Token).ConfigureAwait(false);
                completion.TrySetResult();
            }
            catch (Exception failure) { completion.TrySetException(failure); }
        }
    }

    public async ValueTask DisposeAsync()
    {
        await cleanup.WaitAsync();
        try
        {
            if (Released) return;
            Entry[] currentClients;
            Entry[] currentBackends;
            lock (gate) { terminal = true; currentClients = clients.AsEnumerable().Reverse().ToArray(); currentBackends = backends.AsEnumerable().Reverse().ToArray(); }
            var failures = new List<Exception>();
            try { await lifetime.CancelAsync(); } catch (Exception error) { failures.Add(error); }
            foreach (var entry in currentClients.Concat(currentBackends))
            {
                try
                {
                    if (entry.Startup is { } startup)
                    {
                        try { await startup.WaitAsync(window); }
                        catch when (startup.IsCompleted) { _ = startup.Exception; }
                    }
                }
                catch (Exception error) { failures.Add(error); }
            }
            if (currentClients.Concat(currentBackends).Any(entry => entry.Startup is { IsCompleted: false }))
                throw new AggregateException("Startup unsettled; preserve all owned resources.", failures);
            foreach (var phase in currentClients.GroupBy(entry => entry.Phase).OrderBy(group => group.Key))
            {
                foreach (var entry in phase)
                    try { await entry.ReleaseAsync(window); } catch (Exception error) { failures.Add(error); }
                if (phase.Any(entry => !entry.Released)) break;
            }
            // A faulted/unfinished client shutdown is not proof of quiescence.
            if (currentClients.All(entry => entry.Released))
            {
                await ReceiptAsync("clients-quiescent", currentClients, currentBackends);
                foreach (var entry in currentBackends)
                    try { await entry.ReleaseAsync(window); } catch (Exception error) { failures.Add(error); }
            }
            if (currentClients.Concat(currentBackends).All(entry => entry.Released))
            {
                await ReceiptAsync("released", currentClients, currentBackends);
                Released = true;
                Retained.TryRemove(Id, out _);
                lifetime.Dispose();
            }
            if (failures.Count != 0) throw new AggregateException("Exact supplier owner retained for cleanup retry.", failures);
        }
        catch (Exception original)
        {
            Entry[] currentClients;
            Entry[] currentBackends;
            lock (gate) { currentClients = clients.ToArray(); currentBackends = backends.ToArray(); }
            try { await ReceiptAsync("retained", currentClients, currentBackends); }
            catch (Exception receipt) { throw new AggregateException("Cleanup and retained-owner receipt failed.", original, receipt); }
            throw;
        }
        finally { cleanup.Release(); }
    }

    private async Task ReceiptAsync(string state, Entry[] currentClients, Entry[] currentBackends)
    {
        var directory = Environment.GetEnvironmentVariable("SUPPLIER_RESOURCE_EVIDENCE");
        if (directory is null) return; // Pure fault controls do not create a native-resource ledger.
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await WriteReceiptAsync("scope.jsonl", new
        {
            state,
            expires,
            clients = currentClients.Select(entry => new { entry.Name, entry.Phase, entry.Released, startupSettled = entry.Startup?.IsCompleted ?? true }).ToArray(),
            backends = currentBackends.Select(entry => new { entry.Name, entry.Released, startupSettled = entry.Startup?.IsCompleted ?? true }).ToArray(),
            backendBindings = bindings.Select(binding => new { binding.Name, binding.Run, ids = binding.CapturedIds() }).ToArray()
        }, deadline.Token);
    }

    internal void BindBackend(string name, string run, Func<string[]> capturedIds)
    {
        lock (gate)
        {
            if (terminal || DateTimeOffset.UtcNow >= expires || lifetime.IsCancellationRequested ||
                !backends.Any(entry => entry.Name == name) || bindings.Any(binding => binding.Name == name))
                throw new InvalidOperationException("Backend binding requires a live preregistered owner.");
            bindings.Add(new BackendBinding(name, run, capturedIds));
        }
    }

    // Both journals share one owner and serialized sequence; no wall-clock ordering inference.
    internal async Task WriteReceiptAsync<T>(string file, T receipt, CancellationToken token)
    {
        var directory = Environment.GetEnvironmentVariable("SUPPLIER_RESOURCE_EVIDENCE")
            ?? throw new InvalidOperationException("Resource evidence destination required.");
        await receipts.WaitAsync(token);
        try
        {
            Directory.CreateDirectory(directory);
            var row = JsonSerializer.SerializeToNode(receipt)!.AsObject();
            row["owner"] = Id.ToString("N");
            row["sequence"] = ++receiptSequence;
            await File.AppendAllTextAsync(Path.Combine(directory, file), row.ToJsonString() + Environment.NewLine, token);
        }
        finally { receipts.Release(); }
    }

    private sealed record BackendBinding(string Name, string Run, Func<string[]> CapturedIds);

    internal sealed class Entry(string name, Func<CancellationToken, Task> release, int phase, Action<Action> dispatch)
    {
        internal string Name { get; } = name;
        internal int Phase { get; } = phase;
        internal Task? Startup { get; set; }
        internal bool Released { get; private set; }
        private Task? attempt;
        internal Task? ReleaseSettlement => attempt;
        private CancellationTokenSource? deadline;
        internal async Task ReleaseAsync(TimeSpan window)
        {
            if (Released) return;
            // A successful late settlement is exit proof; never repeat its release callback.
            if (attempt is { IsCompletedSuccessfully: true })
            {
                Released = true;
                deadline!.Dispose();
                return;
            }
            if (attempt is null || attempt.IsCompleted)
            {
                deadline?.Dispose();
                deadline = new CancellationTokenSource(window);
                var token = deadline.Token;
                var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                attempt = completion.Task;
                try { dispatch(() => { _ = RunAsync(); }); }
                catch (Exception error) { completion.TrySetException(error); }

                async Task RunAsync()
                {
                    try
                    {
                        token.ThrowIfCancellationRequested();
                        await release(token).ConfigureAwait(false);
                        completion.TrySetResult();
                    }
                    catch (Exception error) { completion.TrySetException(error); }
                }
            }
            await attempt.WaitAsync(window);
            Released = true;
            deadline!.Dispose();
        }
    }
}
