extern alias Bff;

using System.Diagnostics;
using System.Net;
using System.Reflection;
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
        var factoryAlive = roots.Factory.IsAlive;
        var providerAlive = roots.Provider.IsAlive;
        var transportAlive = roots.Transport.IsAlive;
        var transportDisposals = Volatile.Read(ref roots.Disposal.Count);
        output.WriteLine("Observed handler lifetime: {0}; release elapsed: {1}; factory/provider/transport alive: {2}/{3}/{4}; transport disposals: {5}; managed bytes: {6}",
            roots.HandlerLifetime, elapsed.Elapsed, factoryAlive, providerAlive,
            transportAlive, transportDisposals, GC.GetTotalMemory(false));
        // Capture only after the original observation. Extra diagnostic time must never
        // turn that observation into a pass, and the hook must not retain weak targets.
        if (factoryAlive || providerAlive || transportAlive) await SignalRootCaptureAsync(roots);
        GC.KeepAlive(roots); // Preserve the weak-witness record for offline identification only.
        Assert.False(factoryAlive, "Disposed ordinary BFF factory remained rooted beyond observed handler expiry and cleanup.");
        Assert.False(providerAlive, "Disposed host service provider remained rooted beyond observed handler expiry and cleanup.");
        Assert.False(transportAlive, "Expired ordinary factory transport remained rooted beyond its cleanup window.");
        Assert.Equal(1, transportDisposals);
    }

    [Fact]
    public async Task LiveParentOwner_TracksDisposedDerivedFactoryAndProvider()
    {
        await using var parent = new WebApplicationFactory<BffProgram>();
        var roots = await ExerciseAndDisposeDerivedAsync(parent);
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        // Pinned WebApplicationFactory stores derived factories in the parent owner.
        // This control keeps that owner deliberately alive, unlike the release Fact.
        // The helper returns only weak witnesses, so no caller local owns the child.
        Assert.True(roots.Factory.IsAlive);
        Assert.True(roots.Provider.IsAlive);
        GC.KeepAlive(parent);
    }

    [Theory]
    [InlineData("10.0.13")]
    [InlineData("10.0.12-unreviewed")]
    [InlineData("")]
    public void DiagnosticWeakHandle_RejectsUnreviewedRuntime(string version)
    {
        Assert.Null(ReadDiagnosticWeakHandle(new WeakReference(new object()), version));
    }

    private static string? ReadDiagnosticWeakHandle(WeakReference witness, string? version)
    {
        // Exact runtime source v10.0.12 WeakReference.cs:40,84-86. Read the
        // test-owned weak handle itself, never Target or arbitrary host fields.
        if (version != "10.0.12" && version?.StartsWith("10.0.12+", StringComparison.Ordinal) != true) return null;
        if (witness.GetType() != typeof(WeakReference) || witness.TrackResurrection) return null;
        var field = typeof(WeakReference).GetField("_taggedHandle", BindingFlags.Instance | BindingFlags.NonPublic);
        if (field is null || field.FieldType != typeof(nint) || field.GetValue(witness) is not nint handle || handle == 0
            || (handle & 3) != 0) return null;
        return unchecked((ulong)handle).ToString("x16", System.Globalization.CultureInfo.InvariantCulture);
    }

    private async Task SignalRootCaptureAsync(RetainedHost roots)
    {
        if (Environment.GetEnvironmentVariable("MALIEV_POOLED_HOST_ROOT_DIAGNOSTICS") != "synthetic-isolated-v1") return;
        var head = Environment.GetEnvironmentVariable("MALIEV_POOLED_HOST_ROOT_HEAD");
        if (Environment.GetEnvironmentVariable("RUNNER_OS") != "Linux"
            || Environment.GetEnvironmentVariable("GITHUB_ACTIONS") != "false"
            || !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("GITHUB_TOKEN"))
            || Environment.GetEnvironmentVariable("DOTNET_PROCESSOR_COUNT") != "1"
            || head is null || head.Length != 40 || !head.All(char.IsAsciiHexDigit))
        {
            output.WriteLine("Synthetic root capture unavailable: hosted diagnostic prerequisites were not satisfied.");
            return;
        }

        var pid = Environment.ProcessId;
        var signal = Path.Combine(Path.GetTempPath(), $"maliev-intranet-pooled-root-{pid}.ready");
        var version = typeof(WeakReference).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        var factoryHandle = ReadDiagnosticWeakHandle(roots.Factory, version);
        var providerHandle = ReadDiagnosticWeakHandle(roots.Provider, version);
        if (factoryHandle is null || providerHandle is null || factoryHandle == providerHandle)
        {
            output.WriteLine("Synthetic root capture unavailable: exact weak-handle ownership metadata was not available.");
            return;
        }
        try
        {
            File.WriteAllLines(signal,
            [
                $"pid={pid}",
                $"head={head}",
                "factory=Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory`1+DelegatedWebApplicationFactory",
                "provider=Microsoft.Extensions.DependencyInjection.ServiceProvider",
                "witness=Legacy.Maliev.Intranet.Tests.PooledTestHostReleaseContractTests+RetainedHost",
                $"factory-handle={factoryHandle}",
                $"provider-handle={providerHandle}"
            ]);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            output.WriteLine("Synthetic root capture unavailable: signal file could not be written.");
            return;
        }
        output.WriteLine("Synthetic root capture signalled for PID {0}, head {1}; post-observation wait: 45 seconds.", pid, head);
        await Task.Delay(TimeSpan.FromSeconds(45)).ConfigureAwait(false);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static async Task<RetainedHost> ExerciseAndDisposeAsync()
    {
        await using var parent = new WebApplicationFactory<BffProgram>();
        return await ExerciseAndDisposeDerivedAsync(parent);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static async Task<RetainedHost> ExerciseAndDisposeDerivedAsync(WebApplicationFactory<BffProgram> parent)
    {
        var disposal = new DisposalCount();
        await using var factory = parent.WithWebHostBuilder(builder =>
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
