extern alias Bff;

using System.Diagnostics;
using System.Net;
using System.Runtime.CompilerServices;
using Legacy.Maliev.Intranet.Auth;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http;
using Microsoft.Extensions.Options;
using Xunit.Abstractions;
using BffProgram = Bff::Program;

namespace Legacy.Maliev.Intranet.Tests;

// Characterizes ordinary host shutdown, including the real factory's pool.
// Records collection within the observed cleanup window, not permanent leakage.
[Collection("Test host lifecycle")]
public sealed class PooledTestHostReleaseContractTests(ITestOutputHelper output)
{
    [Fact]
    public async Task DisposedOrdinaryBffHost_ReleasesProviderAndTransportAfterObservedPoolExpiry()
    {
        // Separate process-wide first-host initialization from per-host ownership,
        // as the existing lifecycle characterization does.
        _ = await ExerciseAndDisposeAsync();
        var roots = await ExerciseAndDisposeAsync();
        var elapsed = Stopwatch.StartNew();
        var deadline = roots.HandlerLifetime + TimeSpan.FromSeconds(30);
        Assert.InRange(roots.HandlerLifetime, TimeSpan.FromSeconds(1), TimeSpan.FromMinutes(2));
        while (elapsed.Elapsed < deadline)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            if (!roots.Factory.IsAlive && !roots.Provider.IsAlive && !roots.Transport.IsAlive) break;
            await Task.Delay(TimeSpan.FromSeconds(1));
        }
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        output.WriteLine("Observed handler lifetime: {0}; release elapsed: {1}; factory/provider/transport alive: {2}/{3}/{4}; transport disposals: {5}; managed bytes: {6}",
            roots.HandlerLifetime, elapsed.Elapsed, roots.Factory.IsAlive, roots.Provider.IsAlive,
            roots.Transport.IsAlive, Volatile.Read(ref roots.Disposal.Count), GC.GetTotalMemory(false));
        Assert.False(roots.Factory.IsAlive, "Disposed ordinary BFF factory remained rooted beyond observed handler expiry and cleanup.");
        Assert.False(roots.Provider.IsAlive, "Disposed host service provider remained rooted beyond observed handler expiry and cleanup.");
        Assert.False(roots.Transport.IsAlive, "Expired ordinary factory transport remained rooted beyond its cleanup window.");
        Assert.Equal(1, Volatile.Read(ref roots.Disposal.Count));
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static async Task<RetainedHost> ExerciseAndDisposeAsync()
    {
        var disposal = new DisposalCount();
        await using var factory = new WebApplicationFactory<BffProgram>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            TestJwtConfiguration.Configure(builder);
            builder.UseSetting("Services:Auth", "http://pool-auth.invalid/");
            builder.ConfigureServices(services => services.PostConfigure<HttpClientFactoryOptions>(nameof(ILegacyAuthClient), options =>
                options.HttpMessageHandlerBuilderActions.Add(handlerBuilder =>
                {
                    var previous = handlerBuilder.PrimaryHandler;
                    // The retained options action must not keep a disposed transport
                    // alive itself: retain only its weak/disposal witness here.
                    var transport = new ProbeTransport(disposal);
                    disposal.Transport = new WeakReference(transport);
                    handlerBuilder.PrimaryHandler = transport;
                    previous.Dispose();
                })));
        });
        using var browser = factory.CreateClient(new() { BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false });
        using var session = await browser.GetAsync("/bff/session");
        Assert.Equal(HttpStatusCode.OK, session.StatusCode);
        var provider = factory.Services;
        var lifetime = provider.GetRequiredService<IOptionsMonitor<HttpClientFactoryOptions>>().Get(nameof(ILegacyAuthClient)).HandlerLifetime;
        using var downstream = provider.GetRequiredService<IHttpClientFactory>().CreateClient(nameof(ILegacyAuthClient));
        using var response = await downstream.GetAsync("/pool-release-probe");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        var transportWitness = disposal.Transport
            ?? throw new InvalidOperationException("The actual named client did not activate its primary transport.");
        return new(new(factory), new(provider), transportWitness, lifetime, disposal);
    }

    private sealed record RetainedHost(WeakReference Factory, WeakReference Provider, WeakReference Transport, TimeSpan HandlerLifetime, DisposalCount Disposal);
    private sealed class DisposalCount
    {
        public int Count;
        public WeakReference? Transport;
    }
    private sealed class ProbeTransport(DisposalCount disposal) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Assert.Equal(HttpMethod.Get, request.Method);
            Assert.Equal("/pool-release-probe", request.RequestUri!.AbsolutePath);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Unauthorized));
        }
        protected override void Dispose(bool disposing)
        {
            if (disposing) Interlocked.Increment(ref disposal.Count);
            base.Dispose(disposing);
        }
    }
}
