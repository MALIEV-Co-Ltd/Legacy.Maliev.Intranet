extern alias Bff;

using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Runtime.ExceptionServices;
using Legacy.Maliev.Intranet.Contracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;
using BffProgram = Bff::Program;
using DiagnosticEventStore = Bff::Legacy.Maliev.Intranet.Bff.Diagnostics.DiagnosticEventStore;

namespace Legacy.Maliev.Intranet.Tests;

// Captures the registered native console provider, not a replacement formatter.
internal sealed class PrivateDiagnosticHostFixture : IAsyncDisposable
{
    private readonly TextWriter originalConsole = Console.Out;
    private readonly StringWriter console = new();
    private readonly WebApplicationFactory<Program>? compatibility;
    private readonly WebApplicationFactory<BffProgram>? bff;
    private bool disposed;
    private int attemptedExternalCalls;
    private readonly bool observeFrameworkFailure;
    private readonly AsyncLocal<bool> frameworkRequest = new();
    private readonly ConcurrentQueue<FrameworkExceptionIdentity> frameworkFailures = new();
    private int observedFrameworkFailures;

    public PrivateDiagnosticHostFixture(bool useBff, bool observeFrameworkFailure = false)
    {
        UseBff = useBff;
        this.observeFrameworkFailure = observeFrameworkFailure;
        Console.SetOut(TextWriter.Synchronized(console));
        if (useBff) bff = new BffFactory(this);
        else compatibility = new CompatibilityFactory(this);
        if (observeFrameworkFailure) AppDomain.CurrentDomain.FirstChanceException += ObserveFrameworkException;
    }

    public bool UseBff { get; }
    public string Prefix => UseBff ? "intranet-bff" : "intranet";
    public FakeTimeProvider Clock { get; } = new(new DateTimeOffset(2026, 10, 3, 0, 0, 0, TimeSpan.Zero));
    public DiagnosticRecords Records { get; } = new();
    public string NativeOutput => console.ToString();
    public int AttemptedExternalCalls => Volatile.Read(ref attemptedExternalCalls);
    public FrameworkExceptionIdentity[] FrameworkFailures => frameworkFailures.ToArray();
    public IServiceProvider Services => UseBff ? bff!.Services : compatibility!.Services;

    public HttpClient CreateClient() => UseBff
        ? bff!.CreateClient(ClientOptions())
        : compatibility!.CreateClient(ClientOptions());

    public int FeedCount => UseBff
        ? Services.GetRequiredService<DiagnosticEventStore>()
            .Query(DiagnosticEventSort.LogId_Ascending, null, 1, 100).TotalRecords
        : 0;

    public static HttpRequestMessage Probe(string? nonce, string peer = "loopback", string method = "GET")
    {
        var request = new HttpRequestMessage(new HttpMethod(method), "/internal/diagnostics/observability");
        request.Headers.Add("X-Fixture-Direct-Peer", peer);
        if (nonce is not null) request.Headers.TryAddWithoutValidation("X-Maliev-Diagnostic-Id", nonce);
        return request;
    }

    private static WebApplicationFactoryClientOptions ClientOptions() => new()
    {
        BaseAddress = new Uri("https://localhost"),
        AllowAutoRedirect = false,
    };

    private void Configure(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        TestJwtConfiguration.Configure(builder);
        foreach (var service in new[] { "Auth", "Customer", "Employee", "Catalog", "Procurement", "Order", "Notification", "Accounting", "Quotation", "Delivery" })
            builder.UseSetting($"Services:{service}", $"http://{service.ToLowerInvariant()}.invalid/");
        builder.UseSetting("ForwardedHeaders:KnownProxies:0", "203.0.113.7");
        builder.ConfigureServices(services =>
        {
            services.AddSingleton<TimeProvider>(Clock);
            services.AddSingleton<IStartupFilter>(new DirectPeerStartupFilter(this));
            services.AddSingleton<ILoggerProvider>(Records);
            services.PostConfigureAll<HttpClientFactoryOptions>(options =>
                options.HttpMessageHandlerBuilderActions.Add(handlerBuilder =>
                {
                    var previous = handlerBuilder.PrimaryHandler;
                    handlerBuilder.PrimaryHandler = new RejectExternalTransport(this);
                    previous.Dispose();
                }));
        });
    }

    public async ValueTask DisposeAsync()
    {
        if (disposed) return;
        disposed = true;
        try
        {
            if (bff is not null) await bff.DisposeAsync();
            if (compatibility is not null) await compatibility.DisposeAsync();
        }
        finally
        {
            if (observeFrameworkFailure) AppDomain.CurrentDomain.FirstChanceException -= ObserveFrameworkException;
            Console.SetOut(originalConsole);
        }
    }

    private void ObserveFrameworkException(object? sender, FirstChanceExceptionEventArgs args)
    {
        if (!frameworkRequest.Value || args.Exception is not InvalidOperationException) return;
        if (Interlocked.Increment(ref observedFrameworkFailures) > 4) return;
        var frames = (new StackTrace(args.Exception, false).GetFrames() ?? [])
            .Select(frame => frame.GetMethod())
            .Where(method => method?.DeclaringType?.FullName is { } name
                && (name.StartsWith("Microsoft.AspNetCore.", StringComparison.Ordinal)
                    || name.StartsWith("System.", StringComparison.Ordinal)
                    || name.StartsWith("Maliev.Aspire.ServiceDefaults.", StringComparison.Ordinal)
                    || name.StartsWith("Legacy.Maliev.Intranet", StringComparison.Ordinal)))
            .Take(8)
            .Select(method => new FrameworkMethodIdentity(
                Bound(method!.DeclaringType!.FullName!, 192), Bound(method.Name, 96)))
            .ToArray();
        frameworkFailures.Enqueue(new(Bound(args.Exception.GetType().FullName!, 192), frames));
    }

    private static string Bound(string value, int maximum) => value[..Math.Min(value.Length, maximum)];

    private sealed class CompatibilityFactory(PrivateDiagnosticHostFixture owner) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder) => owner.Configure(builder);
    }

    private sealed class BffFactory(PrivateDiagnosticHostFixture owner) : WebApplicationFactory<BffProgram>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder) => owner.Configure(builder);
    }

    // A passive server transport input, outside the actual Program pipeline. No route/response/log implementation.
    private sealed class DirectPeerStartupFilter(PrivateDiagnosticHostFixture owner) : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            app.Use(async (context, continuation) =>
            {
                context.Connection.RemoteIpAddress = context.Request.Headers["X-Fixture-Direct-Peer"].ToString() switch
                {
                    "public" => IPAddress.Parse("203.0.113.7"),
                    "absent" => null,
                    _ => IPAddress.Loopback,
                };
                bool previous = owner.frameworkRequest.Value;
                owner.frameworkRequest.Value = owner.observeFrameworkFailure
                    && HttpMethods.IsGet(context.Request.Method)
                    && context.Request.Path == "/_framework/blazor.webassembly.js";
                try
                {
                    await continuation(context);
                }
                finally
                {
                    owner.frameworkRequest.Value = previous;
                }
            });
            next(app);
        };
    }

    private sealed class RejectExternalTransport(PrivateDiagnosticHostFixture owner) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref owner.attemptedExternalCalls);
            throw new InvalidOperationException("The diagnostic fixture must not call an external transport.");
        }
    }
}

internal sealed record DiagnosticRecord(LogLevel Level, string Category, int EventId, string? EventName, string? ExceptionType);
internal sealed record FrameworkMethodIdentity(string DeclaringType, string Method);
internal sealed record FrameworkExceptionIdentity(string ExceptionType, FrameworkMethodIdentity[] Methods);

internal sealed class DiagnosticRecords : ILoggerProvider
{
    private readonly ConcurrentQueue<DiagnosticRecord> records = new();
    public DiagnosticRecord[] Snapshot => records.ToArray();
    public ILogger CreateLogger(string categoryName) => new Recorder(categoryName, records);
    public void Dispose() { }

    private sealed class Recorder(string category, ConcurrentQueue<DiagnosticRecord> records) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None;
        public void Log<TState>(LogLevel level, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(level)) return;
            string? eventName = (state as IEnumerable<KeyValuePair<string, object?>>)?
                .FirstOrDefault(pair => pair.Key == "EventName").Value as string;
            records.Enqueue(new(level, category, eventId.Id, eventName, exception?.GetType().Name));
        }
    }
}
