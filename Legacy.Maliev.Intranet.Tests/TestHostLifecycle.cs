using System.Reflection;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Legacy.Maliev.Intranet.Tests;

internal static class TestHostLifecycle
{
    internal static void Configure(IWebHostBuilder builder) => builder.ConfigureServices(services =>
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IStartupFilter, LimiterDisposalStartupFilter>()));

    // ASP.NET Core 10.0.12 creates its endpoint limiter outside DI and never disposes it.
    // Its heartbeat captures the test-host startup context, rooting each disposed pipeline.
    // https://github.com/dotnet/aspnetcore/issues/66434
    // Keep the real middleware/policies intact and supply only the missing shutdown ownership.
    private sealed class LimiterDisposalStartupFilter : IStartupFilter, IDisposable, IAsyncDisposable
    {
        private readonly List<PartitionedRateLimiter<HttpContext>> limiters = [];
        private readonly HashSet<object> visited = new(ReferenceEqualityComparer.Instance);

        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) =>
            application => next(new TrackingApplicationBuilder(application, Capture));

        private void Capture(RequestDelegate middleware)
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
            foreach (var limiter in limiters) limiter.Dispose();
            limiters.Clear();
            visited.Clear();
        }

        public async ValueTask DisposeAsync()
        {
            foreach (var limiter in limiters) await limiter.DisposeAsync();
            limiters.Clear();
            visited.Clear();
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
