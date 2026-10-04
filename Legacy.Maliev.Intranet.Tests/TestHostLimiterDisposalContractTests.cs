extern alias Bff;

using System.Reflection;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit.Abstractions;
using BffProgram = Bff::Program;

namespace Legacy.Maliev.Intranet.Tests;

// Catches missing test-host shutdown ownership, not a changed rate-limit policy.
[Collection("Test host lifecycle")]
public sealed class TestHostLimiterDisposalContractTests(ITestOutputHelper output)
{
    [Fact]
    public Task Dispose_DisposesActualRegisteredEndpointLimiter() => VerifyAsync(false);

    [Fact]
    public Task DisposeAsync_DisposesActualRegisteredEndpointLimiter() => VerifyAsync(true);

    private async Task VerifyAsync(bool asynchronous)
    {
        var capture = new CaptureStartupFilter();
        var factory = new ProbeFactory(capture);
        int ownerCount = 0;
        int capturedBefore = 0;
        bool matchesBefore = false;
        ITestHostLimiterDisposalDiagnostics? diagnostics = null;
        CancellationToken stopped = default;
        try
        {
            using var client = factory.CreateClient(new()
            {
                BaseAddress = new Uri("https://localhost"),
                AllowAutoRedirect = false,
            });
            capture.ClearVisited();
            using var response = await client.GetAsync("/bff/session");
            Assert.True(response.IsSuccessStatusCode);
            var limiter = Assert.Single(capture.Limiters);

            // Observe only this known test-owned filter's limiter list, before disposal.
            // No arbitrary provider/object traversal or post-disposal list reads.
            var owners = factory.Services.GetServices<IStartupFilter>()
                .Where(filter => filter.GetType().DeclaringType == typeof(TestHostLifecycle)
                    && filter.GetType().Name == "LimiterDisposalStartupFilter").ToArray();
            ownerCount = owners.Length;
            Assert.Single(owners);
            diagnostics = Assert.IsAssignableFrom<ITestHostLimiterDisposalDiagnostics>(owners[0]);
            stopped = factory.Services.GetRequiredService<IHostApplicationLifetime>().ApplicationStopped;
            var field = owners[0].GetType().GetField("limiters", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(field);
            Assert.Equal(typeof(List<PartitionedRateLimiter<HttpContext>>), field.FieldType);
            var owned = Assert.IsType<List<PartitionedRateLimiter<HttpContext>>>(field.GetValue(owners[0]));
            capturedBefore = owned.Count;
            matchesBefore = owned.Any(candidate => ReferenceEquals(candidate, limiter));
            Assert.Equal(1, capturedBefore);
            Assert.True(matchesBefore, "Shutdown owner must capture the actual registered endpoint limiter.");
            using (var lease = limiter.AttemptAcquire(new DefaultHttpContext()))
            {
                Assert.True(lease.IsAcquired);
            }

            WriteSnapshot(0, ownerCount, capturedBefore, matchesBefore, diagnostics, stopped);
            if (asynchronous) await factory.DisposeAsync();
            else factory.Dispose();
            WriteSnapshot(1, ownerCount, capturedBefore, matchesBefore, diagnostics, stopped);

            Assert.Throws<ObjectDisposedException>(() =>
            {
                using var lease = limiter.AttemptAcquire(new DefaultHttpContext());
            });
            Assert.Equal(1, diagnostics.Snapshot().CleanupStarted);
            Assert.Equal(1, diagnostics.Snapshot().CleanupCompleted);

            // Reuse the actual pre-disposal owner, not a provider lookup or fake.
            Assert.IsAssignableFrom<IDisposable>(diagnostics).Dispose();
            await Assert.IsAssignableFrom<IAsyncDisposable>(diagnostics).DisposeAsync();
            Assert.Throws<ObjectDisposedException>(() =>
            {
                using var lease = limiter.AttemptAcquire(new DefaultHttpContext());
            });
            Assert.Equal(1, diagnostics.Snapshot().CleanupStarted);
            Assert.Equal(1, diagnostics.Snapshot().CleanupCompleted);
        }
        finally
        {
            try { await factory.DisposeAsync(); }
            finally
            {
                try { WriteSnapshot(2, ownerCount, capturedBefore, matchesBefore, diagnostics, stopped); }
                finally { capture.Cleanup(); }
            }
        }
    }

    private void WriteSnapshot(int phase, int ownerCount, int capturedBefore, bool matchesBefore,
        ITestHostLimiterDisposalDiagnostics? diagnostics, CancellationToken stopped)
    {
        var snapshot = diagnostics?.Snapshot() ?? default;
        output.WriteLine("phase={0} owner_count={1} captured_before={2} matches_before={3} valid={4} application_stopped={5} dispose_started={6} dispose_completed={7} async_dispose_started={8} async_dispose_completed={9} cleanup_started={10} cleanup_completed={11}",
            phase, ownerCount, capturedBefore, matchesBefore ? 1 : 0, diagnostics is null ? 0 : 1,
            stopped.IsCancellationRequested ? 1 : 0, snapshot.DisposeStarted, snapshot.DisposeCompleted,
            snapshot.AsyncDisposeStarted, snapshot.AsyncDisposeCompleted, snapshot.CleanupStarted, snapshot.CleanupCompleted);
    }

    private sealed class ProbeFactory(CaptureStartupFilter capture) : WebApplicationFactory<BffProgram>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing");
            TestJwtConfiguration.Configure(builder); // Registers the unchanged ownership helper.
            builder.ConfigureServices(services => services.AddSingleton<IStartupFilter>(capture));
        }
    }

    // Passive and not DI-disposable: cleanup happens only after the actual assertion.
    private sealed class CaptureStartupFilter : IStartupFilter
    {
        private readonly HashSet<object> visited = new(ReferenceEqualityComparer.Instance);
        public List<PartitionedRateLimiter<HttpContext>> Limiters { get; } = [];

        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) =>
            application => next(new TrackingApplicationBuilder(application, Capture));

        private void Capture(RequestDelegate middleware)
        {
            if (middleware.Target is { } target) CaptureTarget(target);
        }

        private void CaptureTarget(object target)
        {
            if (!visited.Add(target)) return;
            var type = target.GetType();
            if (type.DeclaringType?.FullName == "Microsoft.AspNetCore.Builder.UseMiddlewareExtensions+ReflectionMiddlewareBinder"
                && type.Name.StartsWith("<>c__DisplayClass", StringComparison.Ordinal))
            {
                RequireReviewedVersion(type);
                var instanceField = type.GetField("instance", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                    ?? throw new InvalidOperationException("Limiter diagnostic requires the reviewed binder instance field.");
                if (instanceField.FieldType != typeof(object) || instanceField.GetValue(target) is not { } instance)
                    throw new InvalidOperationException("Limiter diagnostic cannot capture the actual binder middleware instance.");
                CaptureTarget(instance);
            }

            foreach (var nested in type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                .Where(field => field.FieldType == typeof(RequestDelegate))
                .Select(field => field.GetValue(target)).OfType<RequestDelegate>()) Capture(nested);

            if (type.FullName != "Microsoft.AspNetCore.RateLimiting.RateLimitingMiddleware") return;
            RequireReviewedVersion(type);
            var field = type.GetField("_endpointLimiter", BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new InvalidOperationException("Limiter diagnostic requires the reviewed middleware field.");
            if (field.GetValue(target) is not PartitionedRateLimiter<HttpContext> limiter)
                throw new InvalidOperationException("Limiter diagnostic cannot capture the actual endpoint limiter.");
            if (!Limiters.Any(existing => ReferenceEquals(existing, limiter))) Limiters.Add(limiter);
        }

        private static void RequireReviewedVersion(Type type)
        {
            var version = type.Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
            if (version != "10.0.12" && version?.StartsWith("10.0.12+", StringComparison.Ordinal) != true)
                throw new InvalidOperationException("Limiter diagnostic requires the reviewed ASP.NET Core version.");
        }

        public void ClearVisited() => visited.Clear();

        public void Cleanup()
        {
            try
            {
                foreach (var limiter in Limiters) limiter.Dispose();
            }
            finally
            {
                Limiters.Clear();
                visited.Clear();
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
