using System.Text.Json;
using Microsoft.Playwright;

namespace SupplierCatalogPersistence.Acceptance;

/// <summary>Observes a clone of the actual browser fetch; never replaces its promise, response or wire request.</summary>
internal sealed class SupplierAddressBrowserObserver(IPage page, string url, bool heldInstallationControl = false)
{
    private readonly string id = Guid.NewGuid().ToString("N");
    private bool closed;
    private bool realmClosed;
    private Task? installing;
    private Task<string>? closing;
    private JsonElement? joinedReceipt;
    internal bool InstallationEvaluationSettled => installing?.IsCompleted == true;
    internal bool ClosingWasDispatched => closing is not null;
    internal bool CanRetryAfterRealmClosure => !closed && page.IsClosed && installing is { IsCompleted: true } && (closing is null || closing.IsCompleted);
    internal bool RequiresRealmRecovery => !closed && page.IsClosed && installing is not null;

    internal async Task AwaitExactEvaluationAfterRealmClosureAsync()
    {
        if (!page.IsClosed || installing is null) throw new InvalidOperationException("Owned realm must close before evaluation recovery.");
        var exact = closing is null ? installing : Task.WhenAll(installing, closing);
        try { await exact.WaitAsync(TimeSpan.FromSeconds(2)); }
        catch (Exception) when (exact.IsCompleted) { _ = exact.Exception; }
    }

    internal Task InstallAsync(CancellationToken token)
    {
        var expression = heldInstallationControl
            ? "async options => { await window.__supplierObserverInstallationControlGate; return (" + InstallScript + ")(options); }"
            : InstallScript;
        installing ??= page.EvaluateAsync(expression, new { url, id, maxBytes = 65536, deadlineMs = 5000 });
        return installing.WaitAsync(token);
    }

    internal async Task<JsonElement> ReadAndCloseAsync(CancellationToken token)
    {
        var value = await CloseAsync(token);
        var receipt = value.GetProperty("receipt");
        Assert.Equal(1, receipt.GetProperty("matchingRequests").GetInt32());
        Assert.Equal(1, receipt.GetProperty("capturedResponses").GetInt32());
        Assert.Equal(200, receipt.GetProperty("status").GetInt32());
        Assert.True(receipt.GetProperty("captureSucceeded").GetBoolean());
        Assert.Equal(JsonValueKind.Null, value.GetProperty("error").ValueKind);
        Assert.InRange(receipt.GetProperty("capturedBytes").GetInt32(), 1, 65536);
        return value;
    }

    internal async Task DisposeAsync(CancellationToken token)
    {
        if (installing is null) closed = true; // No installation RPC was admitted or dispatched.
        if (CanRetryAfterRealmClosure)
        {
            _ = installing!.Exception;
            _ = closing?.Exception; // Observe exact RPC failures; never call realm destruction a joined reader.
            realmClosed = closed = true;
        }
        if (!closed) await CloseAsync(token);
    }

    internal object ReleaseReceipt() => new
    {
        schema = 1,
        state = installing is null ? "observer-never-dispatched" : realmClosed ? "owned-realm-destroyed-after-settled-evaluation" : "reader-joined-fetch-restored",
        observerId = id,
        installationDispatched = installing is not null,
        installationEvaluationSettled = installing?.IsCompleted == true,
        retainedEvaluationSettled = (installing is null || installing.IsCompleted) && (closing is null || closing.IsCompleted),
        ownedPageClosed = realmClosed && page.IsClosed,
        joinedReaderReceipt = realmClosed ? (JsonElement?)null : joinedReceipt,
        runId = Environment.GetEnvironmentVariable("GITHUB_RUN_ID"),
        runAttempt = Environment.GetEnvironmentVariable("GITHUB_RUN_ATTEMPT"),
    };

    private async Task<JsonElement> CloseAsync(CancellationToken token)
    {
        if (installing is null) throw new InvalidOperationException("Observer installation was never dispatched.");
        await installing.WaitAsync(token); // The cancellation wrapper is not original RPC settlement.
        closing ??= page.EvaluateAsync<string>("async id => { const state = window.__supplierAddressObserver; if (!state || state.id !== id) throw new Error('Owned observer identity missing'); return JSON.stringify(await state.close()); }", id);
        using var actualWire = JsonDocument.Parse(await closing.WaitAsync(token));
        var value = actualWire.RootElement.Clone();
        RequireExactKeys(value, "body", "error", "receipt");
        var receipt = value.GetProperty("receipt");
        RequireExactKeys(receipt, "schema", "source", "observerId", "maxBytes", "deadlineMs", "matchingRequests", "capturedResponses", "capturedBytes", "status",
            "captureSucceeded", "exactRequestAndResponseUrl", "method", "closed", "fetchIdentityRestored", "activeTasks", "activeTimers", "readSettled", "cancelSettled");
        Assert.True(receipt.GetProperty("closed").GetBoolean());
        Assert.True(receipt.GetProperty("fetchIdentityRestored").GetBoolean());
        Assert.Equal(0, receipt.GetProperty("activeTasks").GetInt32());
        Assert.Equal(0, receipt.GetProperty("activeTimers").GetInt32());
        Assert.True(receipt.GetProperty("readSettled").GetBoolean());
        Assert.True(receipt.GetProperty("cancelSettled").GetBoolean());
        joinedReceipt = receipt;
        closed = true;
        return value;
    }

    internal static void RequireExactKeys(JsonElement value, params string[] expected)
        => Assert.Equal(expected.Order(), value.EnumerateObject().Select(property => property.Name).Order());

    internal const string InstallScript = """
        options => {
          const target = options.target || window;
          if (target.__supplierAddressObserver) throw new Error('Observer already installed');
          const expected = new URL(options.url);
          const original = target.fetch;
          if (typeof original !== 'function' || !Number.isInteger(options.maxBytes) || options.maxBytes < 1 || options.maxBytes > 65536 ||
              !Number.isInteger(options.deadlineMs) || options.deadlineMs < 1 || options.deadlineMs > 5000) throw new Error('Observer bounds invalid');
          const state = { id: options.id, count: 0, captures: 0, active: 0, timers: 0, bytes: 0, status: 0,
            readSettled: true, cancelSettled: true, error: null, body: null, closed: false, tasks: [] };
          function matches(value, method) {
            try {
            const u = new URL(value, expected.origin);
            const pairs = [...u.searchParams.entries()];
            return method === 'POST' && u.origin === expected.origin && u.pathname === expected.pathname && !u.hash && !u.username && !u.password &&
              expected.search === '' && pairs.length === 0;
            } catch { return false; }
          }
          async function capture(response) {
            state.active++;
            let reader, reading, cancel, alarm;
            try {
              state.status = response.status;
              if (response.status !== 200 || !matches(response.url, 'POST')) throw new Error('Response contract differs');
              const clone = response.clone();
              if (!clone.body) throw new Error('Response stream missing');
              reader = clone.body.getReader();
              state.readSettled = false; state.cancelSettled = false;
              const chunks = [];
              const deadline = new Promise((_, reject) => {
                state.timers++;
                alarm = setTimeout(() => reject(new Error('Clone deadline exceeded')), options.deadlineMs);
              });
              for (;;) {
                reading = reader.read();
                const part = await Promise.race([reading, deadline]);
                if (part.done) break;
                state.bytes += part.value.byteLength;
                if (state.bytes > options.maxBytes) throw new Error('Clone byte cap exceeded');
                chunks.push(part.value);
              }
              const bytes = new Uint8Array(state.bytes);
              let offset = 0;
              for (const chunk of chunks) { bytes.set(chunk, offset); offset += chunk.byteLength; }
              state.body = new TextDecoder('utf-8', { fatal: true }).decode(bytes);
              JSON.parse(state.body);
              state.captures++;
            } catch (error) { state.error = String(error.message || error); }
            finally {
              if (alarm !== undefined) { clearTimeout(alarm); state.timers--; }
              if (reader) {
                try { cancel = reader.cancel(); await cancel; }
                catch (error) { state.error = String(error.message || error); }
                finally { state.cancelSettled = true; }
                if (reading) {
                  try { await reading; }
                  catch (error) { state.error = state.error || String(error.message || error); }
                }
                state.readSettled = true;
                try { reader.releaseLock(); }
                catch (error) { state.error = state.error || String(error.message || error); }
              }
              state.active--;
            }
          }
          function wrapper(...args) {
            const promise = original.apply(this, args);
            const input = args[0], init = args[1];
            const requestUrl = input instanceof Request ? input.url : String(input);
            const method = String(init?.method || (input instanceof Request ? input.method : 'GET')).toUpperCase();
            if (!state.closed && matches(requestUrl, method)) {
              state.count++;
              if (state.count > 1) state.error = 'Duplicate exact request';
              else state.tasks.push(promise.then(capture, error => { state.error = String(error.message || error); }));
            }
            return promise;
          }
          state.close = async () => {
            state.closed = true;
            const identity = target.fetch === wrapper || target.fetch === original;
            if (target.fetch === wrapper) target.fetch = original;
            await Promise.all(state.tasks);
            return { body: state.body, error: state.error, receipt: { schema: 1, source: 'actual-browser-fetch-clone',
              observerId: state.id, maxBytes: options.maxBytes, deadlineMs: options.deadlineMs,
              matchingRequests: state.count, capturedResponses: state.captures, capturedBytes: state.bytes, status: state.status,
              captureSucceeded: state.error === null && state.captures === 1, exactRequestAndResponseUrl: state.captures === 1,
              method: 'POST', closed: state.closed, fetchIdentityRestored: identity && target.fetch === original,
              activeTasks: state.active, activeTimers: state.timers, readSettled: state.readSettled, cancelSettled: state.cancelSettled } };
          };
          target.__supplierAddressObserver = state;
          target.fetch = wrapper;
        }
        """;

    internal static string ControlsScript => "async () => { const install = " + InstallScript + "; " + ControlsBody + " }";
    // A primitive JSON string avoids Playwright's JsonElement reference-preservation metadata.
    internal static string ControlsWireScript => "async () => JSON.stringify(await (" + ControlsScript + ")())";
    private const string ControlsBody = """
        const url = 'https://owned.synthetic.test/bff/lookups/thai-addresses/resolve';
        const body = JSON.stringify({ marker: 'owned-synthetic-observer-control' });
        const passed = [];
        function check(condition, name) { if (!condition) throw new Error(name); }
        function response(text = body, status = 200, responseUrl = url) {
          const value = new Response(text, { status });
          Object.defineProperty(value, 'url', { value: responseUrl });
          return value;
        }
        function fixture(factory = () => response(), maxBytes = 65536, deadlineMs = 80) {
          let calls = 0, originalPromise, receiver, args;
          const target = { fetch: function(...values) {
            calls++; receiver = this; args = values; originalPromise = factory();
            if (!(originalPromise instanceof Promise)) originalPromise = Promise.resolve(originalPromise);
            return originalPromise;
          } };
          const original = target.fetch;
          install({ target, url, id: 'a'.repeat(32), maxBytes, deadlineMs });
          return { target, original, get calls() { return calls; }, get promise() { return originalPromise; },
            get receiver() { return receiver; }, get args() { return args; }, state: target.__supplierAddressObserver };
        }
        function settled(result) {
          const r = result.receipt;
          return r.closed && r.fetchIdentityRestored && r.activeTasks === 0 && r.activeTimers === 0 && r.readSettled && r.cancelSettled;
        }
        {
          const expectedResponse = response();
          const f = fixture(() => expectedResponse), options = { method: 'POST', body: 'unchanged-original-post-body' };
          const promise = f.target.fetch(url, options);
          check(promise === f.promise && f.calls === 1 && f.receiver === f.target && f.args[0] === url && f.args[1] === options, 'identity');
          const originalResponse = await promise;
          check(originalResponse === expectedResponse, 'same original response');
          check(await originalResponse.text() === body, 'original body preserved');
          const result = await f.state.close();
          check(settled(result) && result.body === body && result.receipt.captureSucceeded && f.target.fetch === f.original, 'clone joins');
          passed.push('same-promise-response-args-receiver');
          await f.target.fetch(url, { method: 'POST' });
          check(f.calls === 2 && f.state.count === 1 && f.state.tasks.length === 1, 'restored pass through');
          passed.push('restored-pass-through-no-new-task');
        }
        for (const [name, changed, method] of [
          ['wrong-origin', url.replace('owned.synthetic.test', 'other.synthetic.test'), 'POST'],
          ['wrong-path', url.replace('/thai-addresses/resolve', '/thai-addresses/other'), 'POST'],
          ['wrong-query', url + '?unexpected=1', 'POST'],
          ['wrong-method', url, 'GET'],
          ['url-fragment', url + '#fragment', 'POST'],
          ['duplicate-query-key', url + '?q=first&q=second', 'POST']]) {
          const f = fixture(); const p = f.target.fetch(changed, { method });
          check(p === f.promise, name + ' promise'); await p;
          const result = await f.state.close();
          check(settled(result) && f.calls === 1 && result.receipt.matchingRequests === 0 && result.receipt.capturedResponses === 0, name);
          passed.push(name);
        }
        for (const [name, factory, cap] of [
          ['wrong-response-url', () => response(body, 200, url + '?unexpected=1'), 65536],
          ['wrong-response-status', () => response(body, 503), 65536],
          ['byte-cap', () => response(body), 8],
          ['invalid-json', () => response('{'), 65536],
          ['clone-read-error', () => { const s = new ReadableStream({ start(c) { c.error(new Error('synthetic stream failure')); } }); return response(s); }, 65536]]) {
          const f = fixture(factory, cap); const value = await f.target.fetch(url, { method: 'POST' });
          try { await value.text(); } catch { /* Expected errored control stream; clone error is independently asserted. */ }
          const result = await f.state.close();
          check(settled(result) && result.error !== null && !result.receipt.captureSucceeded && f.calls === 1, name);
          passed.push(name);
        }
        {
          const f = fixture(); const values = await Promise.all([f.target.fetch(url, { method: 'POST' }), f.target.fetch(url, { method: 'POST' })]);
          await Promise.all(values.map(value => value.text()));
          const result = await f.state.close();
          check(settled(result) && result.error !== null && result.receipt.matchingRequests === 2 && !result.receipt.captureSucceeded && f.state.tasks.length === 1, 'cardinality');
          passed.push('duplicate-exact-request');
        }
        {
          const f = fixture(() => Promise.reject(new Error('synthetic original rejection')));
          const p = f.target.fetch(url, { method: 'POST' }); check(p === f.promise, 'rejected promise preserved');
          let rejected = false; try { await p; } catch { rejected = true; }
          const result = await f.state.close();
          check(rejected && settled(result) && result.error !== null && !result.receipt.captureSucceeded, 'original rejection surfaced');
          passed.push('original-fetch-rejection');
        }
        {
          const stream = new ReadableStream({ start(c) { c.enqueue(new TextEncoder().encode(body)); } });
          const originalResponse = response(stream);
          const f = fixture(() => originalResponse, 65536, 20);
          await f.target.fetch(url, { method: 'POST' });
          let joined = false;
          const closing = f.state.close().then(r => { joined = true; return r; });
          await new Promise(resolve => setTimeout(resolve, 60));
          check(!joined && f.state.active === 1 && !f.state.cancelSettled && f.target.fetch === f.original, 'tee cancellation honestly pending');
          await originalResponse.body.cancel();
          const result = await closing;
          check(settled(result) && result.error !== null && !result.receipt.captureSucceeded, 'tee cancellation joined after original close');
          passed.push('deadline-pending-tee-until-original-close');
        }
        {
          const f = fixture(); await (await f.target.fetch(url, { method: 'POST' })).text();
          const other = function() { throw new Error('other actor'); }; f.target.fetch = other;
          const result = await f.state.close();
          check(f.target.fetch === other && !result.receipt.fetchIdentityRestored && result.receipt.activeTasks === 0 && result.receipt.activeTimers === 0, 'detach does not clobber');
          passed.push('detach-identity-interference');
        }
        check(passed.length === 17 && new Set(passed).size === 17, 'exact controls');
        return { schema: 1, nativeBrowserObserverControls: true, cases: passed, realNetworkAllocated: false };
        """;
}

public sealed class SupplierAddressBrowserObserverControls
{
    [Fact]
    public Task PendingCloneRetainsExactEvaluationUntilOwnedRealmClosesAndSameOwnerRetries()
        => RunPendingControlAsync(false, "browser-observer-realm-control.json");

    [Fact]
    public Task CanceledInstallationRetainsOriginalRpcBeforeCloseAndOwnedRealmRetry()
        => RunPendingControlAsync(true, "browser-observer-installation-control.json");

    private static async Task RunPendingControlAsync(bool heldInstallation, string evidenceName)
    {
        var owner = new SupplierResourceScope(TimeSpan.FromMinutes(1), TimeSpan.FromSeconds(10));
        SupplierAddressBrowserObserver? observer = null;
        try
        {
            IPlaywright? playwright = null;
            var driver = owner.Register("pending-control-driver", _ => { playwright?.Dispose(); return Task.CompletedTask; }, phase: 1);
            await owner.StartAsync(driver, async _ => playwright = await Playwright.CreateAsync());
            IBrowser? browser = null;
            var browserLease = owner.Register("pending-control-browser", _ => browser?.DisposeAsync().AsTask() ?? Task.CompletedTask);
            await owner.StartAsync(browserLease, async _ => browser = await playwright!.Chromium.LaunchAsync(new() { Headless = true, Timeout = 30_000 }));
            IBrowserContext? context = null;
            var contextLease = owner.Register("pending-control-context", _ => context?.DisposeAsync().AsTask() ?? Task.CompletedTask);
            await owner.StartAsync(contextLease, async _ => context = await browser!.NewContextAsync());
            var page = await context!.NewPageAsync();
            const string url = "https://owned.synthetic.test/bff/lookups/thai-addresses/resolve";
            // Controlled original fetch only in this negative unit control, never in the business journey.
            await page.EvaluateAsync("url => { const response = new Response(new ReadableStream({ start(c) { c.enqueue(new TextEncoder().encode('{\"synthetic\":true}')); } })); Object.defineProperty(response, 'url', { value: url }); window.fetch = function() { return Promise.resolve(response); }; }", url);
            if (heldInstallation)
                await page.EvaluateAsync("() => { window.__supplierObserverInstallationControlGate = new Promise(() => {}); }");
            observer = new SupplierAddressBrowserObserver(page, url, heldInstallation);
            var lease = owner.Register("pending-control-observer", observer.DisposeAsync);
            using var readWindow = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));
            if (heldInstallation)
            {
                await Assert.ThrowsAnyAsync<OperationCanceledException>(() => owner.StartAsync(lease, _ => observer.InstallAsync(readWindow.Token)));
                Assert.False(observer.InstallationEvaluationSettled);
                Assert.False(observer.ClosingWasDispatched);
            }
            else
            {
                await owner.StartAsync(lease, observer.InstallAsync);
                await page.EvaluateAsync("url => { void window.fetch(url, { method: 'POST' }); }", url);
                await page.WaitForFunctionAsync("window.__supplierAddressObserver.active === 1");
                using var bodyWindow = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));
                await Assert.ThrowsAnyAsync<OperationCanceledException>(() => observer.ReadAndCloseAsync(bodyWindow.Token));
            }
            Assert.False(observer.CanRetryAfterRealmClosure);
            await Assert.ThrowsAsync<AggregateException>(() => owner.DisposeAsync().AsTask());
            Assert.False(owner.Released);
            Assert.True(page.IsClosed);
            await observer.AwaitExactEvaluationAfterRealmClosureAsync();
            Assert.True(observer.InstallationEvaluationSettled);
            if (heldInstallation) Assert.False(observer.ClosingWasDispatched);
            Assert.True(observer.CanRetryAfterRealmClosure);
            await owner.DisposeAsync();
            Assert.True(owner.Released);
            var receipt = JsonSerializer.SerializeToElement(observer.ReleaseReceipt());
            Assert.Equal("owned-realm-destroyed-after-settled-evaluation", receipt.GetProperty("state").GetString());
            Assert.True(receipt.GetProperty("retainedEvaluationSettled").GetBoolean());
            Assert.True(receipt.GetProperty("ownedPageClosed").GetBoolean());
            Assert.Equal(JsonValueKind.Null, receipt.GetProperty("joinedReaderReceipt").ValueKind);
            var directory = Environment.GetEnvironmentVariable("PROOF_RESULTS") ?? throw new InvalidOperationException("Explicit native control results required.");
            Directory.CreateDirectory(directory);
            await File.WriteAllTextAsync(Path.Combine(directory, evidenceName), receipt.GetRawText());
        }
        finally { await owner.DisposeAsync(); }
    }

    [Fact]
    public async Task ActualPassiveCloneContractsAndFailureSettlement()
    {
        var owner = new SupplierResourceScope(TimeSpan.FromMinutes(1));
        JsonElement result = default;
        Task<string>? evaluation = null;
        Exception? executionFailure = null;
        try
        {
            IPlaywright? playwright = null;
            var driver = owner.Register("observer-control-driver", _ => { playwright?.Dispose(); return Task.CompletedTask; }, phase: 1);
            await owner.StartAsync(driver, async _ => playwright = await Playwright.CreateAsync());
            IBrowser? browser = null;
            var browserLease = owner.Register("observer-control-browser", _ => browser?.DisposeAsync().AsTask() ?? Task.CompletedTask);
            await owner.StartAsync(browserLease, async _ => browser = await playwright!.Chromium.LaunchAsync(new() { Headless = true, Timeout = 30_000 }));
            IBrowserContext? context = null;
            var contextLease = owner.Register("observer-control-context", _ => context?.DisposeAsync().AsTask() ?? Task.CompletedTask);
            await owner.StartAsync(contextLease, async _ => context = await browser!.NewContextAsync());
            var page = await context!.NewPageAsync();
            evaluation = page.EvaluateAsync<string>(SupplierAddressBrowserObserver.ControlsWireScript);
            using var actualWire = JsonDocument.Parse(await evaluation.WaitAsync(TimeSpan.FromSeconds(10)));
            result = actualWire.RootElement.Clone();
            SupplierAddressBrowserObserver.RequireExactKeys(result, "schema", "nativeBrowserObserverControls", "cases", "realNetworkAllocated");
        }
        catch (Exception error) { executionFailure = error; }
        finally
        {
            var failures = new List<Exception>();
            if (executionFailure is not null) failures.Add(executionFailure);
            try { await owner.DisposeAsync(); }
            catch (Exception cleanup) { failures.Add(cleanup); }
            if (evaluation is not null)
            {
                try { await evaluation.WaitAsync(TimeSpan.FromSeconds(2)); }
                catch (Exception settlement)
                {
                    // Parent realm termination is separate from completion of the waiting wrapper.
                    if (evaluation.IsCompleted) _ = evaluation.Exception;
                    if (!evaluation.IsCompleted || executionFailure is null) failures.Add(settlement);
                }
            }
            if (failures.Count > 1) throw new AggregateException("Native observer execution and exact evaluation cleanup failed.", failures);
            if (failures.Count == 1) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failures[0]).Throw();
        }
        Assert.True(owner.Released);
        Assert.Equal(17, result.GetProperty("cases").GetArrayLength());
        var directory = Environment.GetEnvironmentVariable("PROOF_RESULTS") ?? throw new InvalidOperationException("Explicit native control results required.");
        Directory.CreateDirectory(directory);
        await File.WriteAllTextAsync(Path.Combine(directory, "browser-observer-controls.json"), result.GetRawText());
    }
}
