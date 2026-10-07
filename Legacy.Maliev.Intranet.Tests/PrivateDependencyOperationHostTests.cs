extern alias Bff;

using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Legacy.Maliev.Intranet.Auth;
using Legacy.Maliev.Intranet.Contracts;
using Legacy.Maliev.Intranet.Materials;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Http;
using Microsoft.Extensions.Logging;
using Xunit.Abstractions;
using BffProgram = Bff::Program;
using OrdersProxy = Bff::Legacy.Maliev.Intranet.Bff.Orders.OrdersProxy;

namespace Legacy.Maliev.Intranet.Tests;

// These tests must fail if either Program loses its explicit selection or puts it inside retries.
// Only token issuance and the external transport are substituted; factory/auth/resilience stay real.
[Collection("Private diagnostic native console")]
public sealed class PrivateDependencyOperationHostTests(ITestOutputHelper output)
{
    private const string Canary = "operation-private-canary@example.com";
    private const string EmployeeToken = "employee-token-canary";
    private const string ServiceToken = "service-token-canary";
    private const string BodyCanary = "private-response-body-canary";
    private const string ExceptionCanary = "private-exception-detail-canary";

    [Theory]
    [InlineData(true, 3, "OrderService", "Orders.Get")]
    [InlineData(false, 0, "CatalogService", "Materials.Get")]
    public async Task Final503_RegisteredClient_EmitsOneTerminalSafeOperation(bool bff, int attempts, string dependency, string operation)
    {
        await using var host = new Host(bff, (_, _) => Task.FromResult(Response(HttpStatusCode.ServiceUnavailable, BodyCanary)));
        await host.ReadAsync(CancellationToken.None);
        output.WriteLine("Registered {0} terminal503 transport attempts observed: {1}", dependency, host.Transport.Requests.Count);

        if (bff) Assert.Equal(attempts, host.Transport.Requests.Count);
        else Assert.True(host.Transport.Requests.Count > 1, "The registered Catalog safe-read policy must retry before its single terminal event.");
        var record = Assert.Single(host.Logs.Failures);
        Assert.Equal(LogLevel.Error, record.Level);
        Assert.Null(record.Exception);
        Assert.Equal("DependencyRequestFailure", record.Fields["EventName"]);
        Assert.Equal(dependency, record.Fields["Dependency"]);
        Assert.Equal(operation, record.Fields["Operation"]);
        Assert.Equal(503, record.Fields["StatusCode"]);
        AssertSafeFailure(record);
        AssertHandlerOrder(host);
        AssertRequestBoundary(host, bff);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task RecoveredRetry_RegisteredClient_PreservesResultWithoutFailureEvent(bool bff)
    {
        await using var host = new Host(bff, (attempt, _) => Task.FromResult(attempt == 1
            ? Response(HttpStatusCode.ServiceUnavailable, Canary)
            : Response(HttpStatusCode.OK, "{\"Items\":[],\"PageIndex\":2,\"TotalPages\":0,\"TotalRecords\":0,\"HasNextPage\":false,\"HasPreviousPage\":true}")));
        await host.ReadAsync(CancellationToken.None, HttpStatusCode.OK);
        output.WriteLine("Registered {0} recovered-retry transport attempts observed: {1}", bff ? "OrdersProxy" : "ILegacyCatalogClient", host.Transport.Requests.Count);
        Assert.Equal(2, host.Transport.Requests.Count);
        Assert.Empty(host.Logs.Failures);
        AssertHandlerOrder(host);
        AssertRequestBoundary(host, bff);
    }

    [Theory]
    [InlineData(true, 401)]
    [InlineData(true, 403)]
    [InlineData(false, 401)]
    [InlineData(false, 403)]
    public async Task AuthorizationFailure_RegisteredClient_PreservesStatusAndInvalidationWithoutRetry(bool bff, int status)
    {
        await using var host = new Host(bff, (_, _) => Task.FromResult(Response((HttpStatusCode)status, Canary)));
        await host.ReadAsync(CancellationToken.None, (HttpStatusCode)status);
        Assert.Single(host.Transport.Requests);
        Assert.Equal("service-token-canary", Assert.Single(host.Tokens.Invalidated));
        Assert.Empty(host.Logs.Failures);
        AssertRequestBoundary(host, bff);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task CallerCancellation_RegisteredClient_PropagatesWithoutDependencyEvent(bool bff)
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var host = new Host(bff, async (_, token) =>
        {
            entered.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
            return Response(HttpStatusCode.OK, "{}");
        });
        using var cancellation = new CancellationTokenSource();
        var pending = host.ReadAsync(cancellation.Token);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
        Assert.Single(host.Transport.Requests);
        Assert.Empty(host.Logs.Failures);
    }

    [Theory]
    [InlineData(true, 3)]
    public async Task TerminalTransportFailure_RegisteredClient_PreservesExceptionIdentityAndSafeEvent(bool bff, int attempts)
    {
        var expected = new HttpRequestException($"{ExceptionCanary} {Canary} {EmployeeToken} {ServiceToken} {BodyCanary}");
        await using var host = new Host(bff, (_, _) => Task.FromException<HttpResponseMessage>(expected));
        var actual = await Assert.ThrowsAsync<HttpRequestException>(() => host.ReadAsync(CancellationToken.None));
        output.WriteLine("Registered OrdersProxy terminal transport attempts observed: {0}", host.Transport.Requests.Count);
        Assert.Same(expected, actual);
        Assert.Equal(attempts, host.Transport.Requests.Count);
        var record = Assert.Single(host.Logs.Failures);
        Assert.Null(record.Exception);
        AssertSafeFailure(record);
        AssertHandlerOrder(host);
        Assert.Equal(bff ? "Orders.Get" : "Materials.Get", record.Fields["Operation"]);
    }

    [Theory]
    [InlineData(true, "OrderService", "Orders.Get")]
    [InlineData(false, "CatalogService", "Materials.Get")]
    public async Task NonCallerTransportTimeout_RegisteredClient_PreservesExceptionAndOneSafeEvent(
        bool bff, string dependency, string operation)
    {
        var expected = new TimeoutException($"{ExceptionCanary} {Canary} {EmployeeToken} {ServiceToken} {BodyCanary}");
        await using var host = new Host(bff, (_, token) =>
        {
            Assert.False(token.IsCancellationRequested);
            return Task.FromException<HttpResponseMessage>(expected);
        });
        var actual = await Assert.ThrowsAsync<TimeoutException>(() => host.ReadAsync(CancellationToken.None));
        Assert.Same(expected, actual);
        Assert.Single(host.Transport.Requests);
        Assert.Empty(host.Tokens.Invalidated);
        var record = Assert.Single(host.Logs.Failures);
        Assert.Equal(LogLevel.Error, record.Level);
        Assert.Equal(dependency, record.Fields["Dependency"]);
        Assert.Equal(operation, record.Fields["Operation"]);
        Assert.False(record.Fields.ContainsKey("StatusCode"));
        AssertSafeFailure(record);
        AssertHandlerOrder(host);
        AssertRequestBoundary(host, bff);
    }

    [Fact]
    public async Task HttpClientDeadline_RegisteredOrdersClient_EmitsOneSafeEventWithoutCallerCancellation()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        CancellationToken transportToken = default;
        await using var host = new Host(true, async (_, token) =>
        {
            transportToken = token;
            entered.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
            return Response(HttpStatusCode.OK, "{}");
        });
        var pending = host.ReadAsync(CancellationToken.None);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        var actual = await Assert.ThrowsAsync<TaskCanceledException>(() => pending.WaitAsync(TimeSpan.FromSeconds(30)));
        Assert.IsType<TimeoutException>(actual.InnerException);
        Assert.False(CancellationToken.None.IsCancellationRequested);
        Assert.True(transportToken.IsCancellationRequested);
        Assert.Single(host.Transport.Requests);
        Assert.Empty(host.Tokens.Invalidated);
        AssertHandlerOrder(host);
        AssertRequestBoundary(host, true);
        output.WriteLine("Actual registered Orders HttpClient deadline propagated as {0} with inner {1}; safe dependency events: {2}",
            actual.GetType().Name, actual.InnerException!.GetType().Name, host.Logs.Failures.Count());
        var record = Assert.Single(host.Logs.Failures);
        Assert.Equal(LogLevel.Error, record.Level);
        Assert.Equal("OrderService", record.Fields["Dependency"]);
        Assert.Equal("Orders.Get", record.Fields["Operation"]);
        Assert.False(record.Fields.ContainsKey("StatusCode"));
        AssertSafeFailure(record);
    }

    private static void AssertSafeFailure(Failure record)
    {
        Assert.Null(record.Exception);
        var fields = JsonSerializer.Serialize(record.Fields);
        foreach (var forbidden in new[] { Canary, Uri.EscapeDataString(Canary), EmployeeToken, ServiceToken, BodyCanary, ExceptionCanary })
        {
            Assert.DoesNotContain(forbidden, fields);
            Assert.DoesNotContain(forbidden, record.Message);
        }
    }

    [Theory]
    [InlineData(true, "Orders.Get")]
    [InlineData(false, "Materials.Get")]
    public async Task SelectedFactoryRequest_BodyCanariesNeverEnterDependencyFieldsOrMessage(bool bff, string operation)
    {
        await using var host = new Host(bff, (_, _) => Task.FromResult(Response(HttpStatusCode.ServiceUnavailable, BodyCanary)));
        await host.SendBodyCanaryAsync();
        var record = Assert.Single(host.Logs.Failures);
        Assert.Equal(503, record.Fields["StatusCode"]);
        Assert.Equal(operation, record.Fields["Operation"]);
        AssertSafeFailure(record);
        AssertHandlerOrder(host);
        Assert.All(host.Transport.Requests, request =>
        {
            Assert.Equal(BodyCanary, request.Body);
            Assert.Equal("Bearer service-token-canary", request.Authorization);
            Assert.Contains("operation-private-canary%40example.com", request.PathAndQuery);
        });
    }

    private void AssertHandlerOrder(Host host)
    {
        var chain = Assert.Single(host.HandlerChains);
        output.WriteLine("Actual selected factory AdditionalHandlers: {0}", string.Join(" -> ", chain));
        var observers = chain.Select((name, index) => (name, index))
            .Where(item => item.name == "Maliev.Aspire.ServiceDefaults.Diagnostics.PrivateDependencyFailureHandler").ToArray();
        var observer = Assert.Single(observers).index;
        var authentication = Assert.Single(chain.Select((name, index) => (name, index)),
            item => item.name == typeof(LegacyServiceAuthenticationHandler).FullName).index;
        var resilience = chain.Select((name, index) => (name, index))
            .Where(item => item.name == "Microsoft.Extensions.Http.Resilience.ResilienceHandler").ToArray();
        Assert.NotEmpty(resilience);
        Assert.True(observer < authentication, "Observation must wrap authentication, including token-acquisition failures.");
        Assert.All(resilience, handler => Assert.True(observer < handler.index, "Observation must wrap every actual resilience layer."));
        // Factory logging may wrap all handlers, but no functional handler may wrap the selected observer.
        Assert.All(chain.Take(observer), name => Assert.StartsWith("Microsoft.Extensions.Http.Logging.", name));
    }

    private static void AssertRequestBoundary(Host host, bool bff)
    {
        Assert.All(host.Transport.Requests, request =>
        {
            Assert.Equal("GET", request.Method);
            Assert.Equal(bff
                ? "/Orders?sort=OrderCreatedDate_Descending&search=operation-private-canary%40example.com&index=2&size=25"
                : "/Materials?sort=MaterialName_Ascending&search=operation-private-canary%40example.com&index=2&size=25", request.PathAndQuery);
            Assert.Equal("Bearer service-token-canary", request.Authorization);
            Assert.Null(request.Body);
            Assert.False(request.OwnershipHeader);
        });
    }

    private static HttpResponseMessage Response(HttpStatusCode status, string body)
    {
        var response = new HttpResponseMessage(status)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };
        // A real server hint keeps the standard Catalog retry characterization bounded;
        // the BFF's existing ShouldRetryAfterHeader=false still exercises its actual delays.
        if (status == HttpStatusCode.ServiceUnavailable)
            response.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.Zero);
        return response;
    }

    private sealed class Host : IAsyncDisposable
    {
        private readonly bool useBff;
        private readonly WebApplicationFactory<Program>? compatibility;
        private readonly WebApplicationFactory<BffProgram>? bff;
        public Host(bool useBff, Func<int, CancellationToken, Task<HttpResponseMessage>> send)
        {
            this.useBff = useBff;
            Transport = new ScriptedTransport(send);
            if (useBff) bff = new BffFactory(this);
            else compatibility = new CompatibilityFactory(this);
        }

        public ScriptedTransport Transport { get; }
        public Tokens Tokens { get; } = new();
        public FailureLogs Logs { get; } = new();
        public ConcurrentQueue<string[]> HandlerChains { get; } = new();
        private IServiceProvider Services => useBff ? bff!.Services : compatibility!.Services;

        public async Task SendBodyCanaryAsync()
        {
            using var client = Services.GetRequiredService<IHttpClientFactory>()
                .CreateClient(useBff ? nameof(OrdersProxy) : nameof(ILegacyCatalogClient));
            using var request = new HttpRequestMessage(HttpMethod.Get, useBff
                ? "/Orders?search=operation-private-canary%40example.com"
                : "/Materials?search=operation-private-canary%40example.com")
            {
                Content = new StringContent(BodyCanary),
            };
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", EmployeeToken);
            using var response = await client.SendAsync(request);
            Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
            Assert.Same(Transport.LastResponse, response);
        }

        public async Task ReadAsync(CancellationToken token, HttpStatusCode expectedStatus = HttpStatusCode.ServiceUnavailable)
        {
            using var scope = Services.CreateScope();
            if (useBff)
            {
                using var response = await scope.ServiceProvider.GetRequiredService<OrdersProxy>().GetAsync(
                    OrderListSort.OrderCreatedDate_Descending, Canary, 2, 25, token);
                Assert.Equal(expectedStatus, response.StatusCode);
                Assert.Same(Transport.LastResponse, response);
            }
            else
            {
                try
                {
                    var page = await scope.ServiceProvider.GetRequiredService<ILegacyCatalogClient>().GetMaterialsAsync(
                        MaterialSortType.MaterialName_Ascending, Canary, 2, 25, EmployeeToken, token);
                    Assert.Equal(HttpStatusCode.OK, expectedStatus);
                    Assert.NotNull(page);
                    Assert.Equal(2, page.PageIndex);
                }
                catch (HttpRequestException exception) when (exception.StatusCode is not null)
                {
                    Assert.NotEqual(HttpStatusCode.OK, expectedStatus);
                    Assert.Equal(expectedStatus, exception.StatusCode);
                }
            }
        }

        private void Configure(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing");
            TestJwtConfiguration.Configure(builder);
            foreach (var service in new[] { "Auth", "Customer", "Employee", "Catalog", "Procurement", "Order", "Notification", "Accounting", "Quotation", "Delivery" })
                builder.UseSetting($"Services:{service}", $"http://{service.ToLowerInvariant()}.invalid/");
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IServiceAccessTokenProvider>();
                services.AddSingleton<IServiceAccessTokenProvider>(Tokens);
                services.AddSingleton<ILoggerProvider>(Logs);
                // First filter wraps all existing filters; its readback happens after their changes.
                // It does not insert, remove or reorder any AdditionalHandlers.
                services.Insert(0, ServiceDescriptor.Singleton<IHttpMessageHandlerBuilderFilter>(new HandlerChainCapture(this)));
                // Leave typed client registrations and every additional handler untouched.
                services.PostConfigureAll<HttpClientFactoryOptions>(options =>
                    options.HttpMessageHandlerBuilderActions.Add(handlerBuilder =>
                    {
                        var previous = handlerBuilder.PrimaryHandler;
                        handlerBuilder.PrimaryHandler = handlerBuilder.Name == (useBff
                            ? nameof(OrdersProxy) : nameof(ILegacyCatalogClient))
                            ? Transport : new RejectTransport();
                        previous.Dispose();
                    }));
            });
        }

        private sealed class HandlerChainCapture(Host owner) : IHttpMessageHandlerBuilderFilter
        {
            public Action<HttpMessageHandlerBuilder> Configure(Action<HttpMessageHandlerBuilder> next) => builder =>
            {
                next(builder);
                if (builder.Name == (owner.useBff ? nameof(OrdersProxy) : nameof(ILegacyCatalogClient)))
                    owner.HandlerChains.Enqueue(builder.AdditionalHandlers.Select(handler => handler.GetType().FullName!).ToArray());
            };
        }

        public async ValueTask DisposeAsync()
        {
            if (bff is not null) await bff.DisposeAsync();
            if (compatibility is not null) await compatibility.DisposeAsync();
        }

        private sealed class BffFactory(Host owner) : WebApplicationFactory<BffProgram>
        {
            protected override void ConfigureWebHost(IWebHostBuilder builder) => owner.Configure(builder);
        }
        private sealed class CompatibilityFactory(Host owner) : WebApplicationFactory<Program>
        {
            protected override void ConfigureWebHost(IWebHostBuilder builder) => owner.Configure(builder);
        }
    }

    private sealed class Tokens : IServiceAccessTokenProvider
    {
        public ConcurrentQueue<string> Invalidated { get; } = new();
        public ValueTask<string?> GetAccessTokenAsync(CancellationToken cancellationToken) => ValueTask.FromResult<string?>(ServiceToken);
        public void Invalidate(string token) => Invalidated.Enqueue(token);
    }

    private sealed record Request(string Method, string PathAndQuery, string? Authorization, string? Body, bool OwnershipHeader);
    private sealed class ScriptedTransport(Func<int, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        private int attempts;
        public ConcurrentQueue<Request> Requests { get; } = new();
        public HttpResponseMessage? LastResponse { get; private set; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            Requests.Enqueue(new(request.Method.Method, request.RequestUri!.PathAndQuery,
                request.Headers.Authorization?.ToString(), request.Content is null ? null : await request.Content.ReadAsStringAsync(token),
                request.Headers.Contains("Maliev.DependencyFailureOwnedByCaller")));
            LastResponse = await send(Interlocked.Increment(ref attempts), token);
            return LastResponse;
        }
    }

    private sealed class RejectTransport : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) =>
            throw new InvalidOperationException("Only the explicitly selected fixture client may call transport.");
    }

    private sealed record Failure(LogLevel Level, Dictionary<string, object?> Fields, string Message, Exception? Exception);
    private sealed class FailureLogs : ILoggerProvider
    {
        private readonly ConcurrentQueue<Failure> failures = new();
        public Failure[] Failures => failures.ToArray();
        public ILogger CreateLogger(string categoryName) => new Recorder(categoryName, failures);
        public void Dispose() { }
        private sealed class Recorder(string category, ConcurrentQueue<Failure> failures) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
            public bool IsEnabled(LogLevel level) => level != LogLevel.None;
            public void Log<TState>(LogLevel level, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            {
                if (category != "Maliev.Aspire.ServiceDefaults.Diagnostics.PrivateDependencyFailureHandler" || eventId.Id != 5101) return;
                var fields = ((IEnumerable<KeyValuePair<string, object?>>)(object)state!)
                    .Where(pair => pair.Key != "{OriginalFormat}").ToDictionary(pair => pair.Key, pair => pair.Value);
                failures.Enqueue(new(level, fields, formatter(state, exception), exception));
            }
        }
    }
}
