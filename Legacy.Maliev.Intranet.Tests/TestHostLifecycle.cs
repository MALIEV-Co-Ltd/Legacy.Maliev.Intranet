using System.Reflection;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace Legacy.Maliev.Intranet.Tests;

internal readonly record struct TestHostLimiterDisposalSnapshot(
    int DisposeStarted, int DisposeCompleted, int AsyncDisposeStarted, int AsyncDisposeCompleted,
    int CleanupStarted, int CleanupCompleted);

internal interface ITestHostLimiterDisposalDiagnostics
{
    TestHostLimiterDisposalSnapshot Snapshot();
}

internal static class TestHostLifecycle
{
    internal static void Configure(IWebHostBuilder builder) => builder.ConfigureServices(services =>
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IStartupFilter, LimiterDisposalStartupFilter>()));

    // ASP.NET Core 10.0.12 creates its endpoint limiter outside DI and never disposes it.
    // Its heartbeat captures the test-host startup context, rooting each disposed pipeline.
    // https://github.com/dotnet/aspnetcore/issues/66434
    // Keep the real middleware/policies intact and supply only the missing shutdown ownership.
    private sealed class LimiterDisposalStartupFilter : IStartupFilter, IDisposable, IAsyncDisposable, ITestHostLimiterDisposalDiagnostics
    {
        private readonly List<PartitionedRateLimiter<HttpContext>> limiters = [];
        private readonly HashSet<object> visited = new(ReferenceEqualityComparer.Instance);
        private readonly object cleanupGate = new();
        private readonly CancellationTokenRegistration stoppedCleanup;
        private TaskCompletionSource? cleanupCompletion;
        private int disposeStarted;
        private int disposeCompleted;
        private int asyncDisposeStarted;
        private int asyncDisposeCompleted;
        private int cleanupStarted;
        private int cleanupCompleted;

        public LimiterDisposalStartupFilter(IHostApplicationLifetime lifetime)
        {
            // ApplicationStopped is a shutdown boundary, not a DI-disposal completion
            // guarantee. Join actual cleanup before this callback returns.
            stoppedCleanup = lifetime.ApplicationStopped.Register(static state =>
                ((LimiterDisposalStartupFilter)state!).JoinCleanup(), this);
        }

        public TestHostLimiterDisposalSnapshot Snapshot() => new(
            Volatile.Read(ref disposeStarted), Volatile.Read(ref disposeCompleted),
            Volatile.Read(ref asyncDisposeStarted), Volatile.Read(ref asyncDisposeCompleted),
            Volatile.Read(ref cleanupStarted), Volatile.Read(ref cleanupCompleted));

        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) =>
            application => next(new TrackingApplicationBuilder(application, Capture));

        private void Capture(RequestDelegate middleware)
        {
            lock (cleanupGate)
            {
                if (cleanupCompletion is not null)
                    throw new InvalidOperationException("Test-host limiter capture cannot continue after shutdown ownership begins.");
                CaptureCore(middleware);
            }
        }

        private void CaptureCore(RequestDelegate middleware)
        {
            if (middleware.Target is not { } target || !visited.Add(target)) return;
            // Minimal-host pipelines are already composed before the startup filter runs.
            // Follow request delegates only, including Map/MapWhen branches, not arbitrary DI graphs.
            foreach (var nested in target.GetType().GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                .Select(field => field.GetValue(target)).OfType<RequestDelegate>()) Capture(nested);
            if (target.GetType().FullName != "Microsoft.AspNetCore.RateLimiting.RateLimitingMiddleware") return;
            var field = middleware.Target.GetType().GetField("_endpointLimiter", BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new InvalidOperationException("Rate-limiter lifecycle workaround needs review for this ASP.NET Core version.");
            if (field.GetValue(middleware.Target) is not PartitionedRateLimiter<HttpContext> limiter)
                throw new InvalidOperationException("Cannot capture the real endpoint limiter for test-host disposal.");
            limiters.Add(limiter);
        }

        public void Dispose()
        {
            Interlocked.Increment(ref disposeStarted);
            try
            {
                JoinCleanup();
                Interlocked.Increment(ref disposeCompleted);
            }
            finally { stoppedCleanup.Dispose(); }
        }

        public async ValueTask DisposeAsync()
        {
            Interlocked.Increment(ref asyncDisposeStarted);
            try
            {
                var (completion, owned) = BeginCleanup();
                if (owned is not null) await CompleteCleanupAsync(completion, owned).ConfigureAwait(false);
                await completion.Task.ConfigureAwait(false);
                Interlocked.Increment(ref asyncDisposeCompleted);
            }
            finally { stoppedCleanup.Dispose(); }
        }

        // Callback and regular DI disposal join the same completed operation.
        // The callback never unregisters itself; no gate is held during disposal,
        // waits, or cancellation-registration disposal.
        private void JoinCleanup()
        {
            var (completion, owned) = BeginCleanup();
            if (owned is not null) CompleteCleanup(completion, owned);
            completion.Task.ConfigureAwait(false).GetAwaiter().GetResult();
        }

        private (TaskCompletionSource Completion, PartitionedRateLimiter<HttpContext>[]? Owned) BeginCleanup()
        {
            lock (cleanupGate)
            {
                if (cleanupCompletion is not null) return (cleanupCompletion, null);
                var owned = limiters.ToArray();
                cleanupCompletion = new(TaskCreationOptions.RunContinuationsAsynchronously);
                limiters.Clear();
                visited.Clear();
                Interlocked.Increment(ref cleanupStarted);
                return (cleanupCompletion, owned);
            }
        }

        private void CompleteCleanup(TaskCompletionSource completion, PartitionedRateLimiter<HttpContext>[] owned)
        {
            List<Exception>? failures = null;
            foreach (var limiter in owned)
            {
                try { limiter.Dispose(); }
                catch (Exception exception) { (failures ??= []).Add(exception); }
            }
            FinishCleanup(completion, owned, failures);
        }

        private async Task CompleteCleanupAsync(TaskCompletionSource completion, PartitionedRateLimiter<HttpContext>[] owned)
        {
            List<Exception>? failures = null;
            foreach (var limiter in owned)
            {
                try { await limiter.DisposeAsync().ConfigureAwait(false); }
                catch (Exception exception) { (failures ??= []).Add(exception); }
            }
            FinishCleanup(completion, owned, failures);
        }

        private void FinishCleanup(TaskCompletionSource completion,
            PartitionedRateLimiter<HttpContext>[] owned, List<Exception>? failures)
        {
            Array.Clear(owned);
            if (failures is not null) completion.TrySetException(failures);
            else
            {
                Interlocked.Increment(ref cleanupCompleted);
                completion.TrySetResult();
            }
        }
    }

    private sealed class TrackingApplicationBuilder(IApplicationBuilder inner, Action<RequestDelegate> capture) : IApplicationBuilder
    {
        public IServiceProvider ApplicationServices { get => inner.ApplicationServices; set => inner.ApplicationServices = value; }
        public IFeatureCollection ServerFeatures => inner.ServerFeatures;
        public IDictionary<string, object?> Properties => inner.Properties;
        public IApplicationBuilder New() => new TrackingApplicationBuilder(inner.New(), capture);
        public RequestDelegate Build() => inner.Build();

        public IApplicationBuilder Use(Func<RequestDelegate, RequestDelegate> middleware)
        {
            inner.Use(next =>
            {
                var built = middleware(next);
                capture(built);
                return built;
            });
            return this;
        }
    }
}
