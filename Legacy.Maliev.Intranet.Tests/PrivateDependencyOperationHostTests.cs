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
using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Options;
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
        var record = Assert.Single(host.Logs.Failures);
        Assert.Equal(status, record.Fields["StatusCode"]);
        Assert.Equal("GET", record.Fields["Method"]);
        Assert.Null(record.Fields["ExceptionType"]);
        AssertSafeFailure(record);
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
        const bool useBff = true;
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        CancellationToken transportToken = default;
        OperationCanceledException? terminalCancellation = null;
        await using var host = new Host(useBff, async (_, token) =>
        {
            transportToken = token;
            entered.TrySetResult();
            try { await Task.Delay(Timeout.InfiniteTimeSpan, token); }
            catch (OperationCanceledException failure) { terminalCancellation = failure; throw; }
            return Response(HttpStatusCode.OK, "{}");
        });
        using var caller = new CancellationTokenSource();
        var pending = host.ReadAsync(caller.Token);
        try
        {
            Assert.Equal(TimeSpan.FromSeconds(10), host.SelectedClientTimeout());
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            var actual = await Assert.ThrowsAsync<TaskCanceledException>(() => pending.WaitAsync(TimeSpan.FromSeconds(30)));
            var timeout = Assert.IsType<TimeoutException>(actual.InnerException);
            Assert.Same(terminalCancellation, timeout.InnerException);
            Assert.False(caller.IsCancellationRequested);
            Assert.True(transportToken.IsCancellationRequested);
            Assert.Single(host.Transport.Requests);
            Assert.Empty(host.Tokens.Invalidated);
            AssertHandlerOrder(host);
            AssertRequestBoundary(host, useBff);
            output.WriteLine("Actual registered Orders HttpClient deadline propagated as {0} with inner {1}; safe dependency events: {2}",
                actual.GetType().Name, actual.InnerException!.GetType().Name, host.Logs.Failures.Count());
            var record = Assert.Single(host.Logs.Failures);
            Assert.Equal(LogLevel.Error, record.Level);
            Assert.Equal(useBff ? "OrderService" : "CatalogService", record.Fields["Dependency"]);
            Assert.Equal(useBff ? "Orders.Get" : "Materials.Get", record.Fields["Operation"]);
            Assert.Equal("TaskCanceledException", record.Fields["ExceptionType"]);
            Assert.False(record.Fields.ContainsKey("StatusCode"));
            AssertSafeFailure(record);
        }
        finally
        {
            caller.Cancel();
            try { await pending.WaitAsync(TimeSpan.FromSeconds(5)); } catch (OperationCanceledException) { }
        }
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
        if (status is HttpStatusCode.ServiceUnavailable or HttpStatusCode.TooManyRequests)
            response.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.Zero);
        return response;
    }

    [Theory]
    [InlineData(true, 302)]
    [InlineData(false, 302)]
    [InlineData(true, 304)]
    [InlineData(false, 304)]
    [InlineData(true, 400)]
    [InlineData(false, 400)]
    [InlineData(true, 404)]
    [InlineData(false, 404)]
    [InlineData(true, 409)]
    [InlineData(false, 409)]
    [InlineData(true, 429)]
    [InlineData(false, 429)]
    public async Task TerminalNonSuccess_SelectedRegisteredClient_ObservesWithoutChangingCallerOutcome(bool bff, int status)
    {
        await using var host = new Host(bff, (_, _) => Task.FromResult(Response((HttpStatusCode)status, BodyCanary)));
        await host.ReadAsync(CancellationToken.None, (HttpStatusCode)status);
        Assert.NotEmpty(host.Transport.Requests);
        if (status != 429) Assert.Single(host.Transport.Requests);
        Assert.Empty(host.Tokens.Invalidated);
        var record = Assert.Single(host.Logs.Failures);
        Assert.Equal(status, record.Fields["StatusCode"]);
        Assert.Equal("GET", record.Fields["Method"]);
        Assert.Null(record.Fields["ExceptionType"]);
        AssertSafeFailure(record);
        AssertHandlerOrder(host);
        AssertRequestBoundary(host, bff);
    }

    [Theory]
    [InlineData(true, 0)]
    [InlineData(false, 0)]
    [InlineData(true, 1)]
    [InlineData(false, 1)]
    [InlineData(true, 2)]
    [InlineData(false, 2)]
    public async Task UnexpectedTransport_SelectedRegisteredClient_PreservesIdentityAndSafeType(bool bff, int kind)
    {
        var expected = UnexpectedFailure(kind);
        await using var host = new Host(bff, (_, _) => Task.FromException<HttpResponseMessage>(expected));
        Assert.Same(expected, await Record.ExceptionAsync(() => host.ReadAsync(CancellationToken.None)));
        Assert.Single(host.Transport.Requests);
        var record = Assert.Single(host.Logs.Failures);
        Assert.Equal(expected.GetType().Name, record.Fields["ExceptionType"]);
        Assert.Equal("GET", record.Fields["Method"]);
        Assert.False(record.Fields.ContainsKey("StatusCode"));
        AssertSafeFailure(record);
        AssertHandlerOrder(host);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task TokenProviderFailure_SelectedRegisteredClient_ObservesBeforeTransport(bool bff)
    {
        var expected = UnexpectedFailure(0);
        await using var host = new Host(bff, (_, _) => Task.FromResult(Response(HttpStatusCode.OK, "{}")));
        host.Tokens.Failure = expected;
        Assert.Same(expected, await Record.ExceptionAsync(() => host.ReadAsync(CancellationToken.None)));
        Assert.Empty(host.Transport.Requests);
        Assert.Empty(host.Tokens.Invalidated);
        var record = Assert.Single(host.Logs.Failures);
        Assert.Equal("InvalidOperationException", record.Fields["ExceptionType"]);
        Assert.Equal("GET", record.Fields["Method"]);
        AssertSafeFailure(record);
        AssertHandlerOrder(host);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task UnselectedFactoryClient_UnexpectedFailure_RemainsQuiet(bool bff)
    {
        await using var host = new Host(bff, (_, _) => Task.FromResult(Response(HttpStatusCode.OK, "{}")));
        await host.SendUnselectedAsync();
        Assert.Empty(host.Transport.Requests);
        Assert.Empty(host.Logs.Failures);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task NormalConsole_SelectedGenericFailure_EmitsSafeSourceMetadata(bool bff)
    {
        var original = Console.Out;
        using var native = new StringWriter();
        Console.SetOut(TextWriter.Synchronized(native));
        try
        {
            await using (var host = new Host(bff, (_, _) => Task.FromException<HttpResponseMessage>(UnexpectedFailure(1))))
            {
                await Assert.ThrowsAsync<IOException>(() => host.ReadAsync(CancellationToken.None));
            }
            var lines = native.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries);
            var failures = new List<JsonElement>();
            foreach (var line in lines)
            {
                using var document = JsonDocument.Parse(line);
                if (document.RootElement.TryGetProperty("Category", out var category)
                    && category.GetString() == "Maliev.Aspire.ServiceDefaults.Diagnostics.PrivateDependencyFailureHandler")
                    failures.Add(document.RootElement.Clone());
            }
            var failure = Assert.Single(failures);
            Assert.Equal("ERROR", failure.GetProperty("severity").GetString());
            Assert.Equal(5101, failure.GetProperty("EventId").GetInt32());
            Assert.Equal(JsonValueKind.Null, failure.GetProperty("Exception").ValueKind);
            var state = failure.GetProperty("State");
            Assert.Equal("GET", state.GetProperty("Method").GetString());
            Assert.Equal("IOException", state.GetProperty("ExceptionType").GetString());
            Assert.Equal(bff ? "OrderService" : "CatalogService", state.GetProperty("Dependency").GetString());
            foreach (var secret in new[] { Canary, EmployeeToken, ServiceToken, BodyCanary, ExceptionCanary })
                Assert.DoesNotContain(secret, failure.GetRawText());
        }
        finally { Console.SetOut(original); }
    }

    [Theory]
    [InlineData(0, 200, false)]
    [InlineData(1, 200, false)]
    [InlineData(2, 200, false)]
    [InlineData(3, 200, false)]
    [InlineData(0, 400, false)]
    [InlineData(1, 400, false)]
    [InlineData(2, 400, false)]
    [InlineData(3, 400, false)]
    [InlineData(0, 200, true)]
    [InlineData(1, 400, true)]
    [InlineData(2, 200, true)]
    [InlineData(3, 400, true)]
    public async Task CatalogTypedCall_NativeBufferFault_RecordsActualOuterFailureOnce(int site, int status, bool io)
    {
        Exception expected = io ? new IOException(ExceptionCanary) : new InvalidOperationException(ExceptionCanary);
        var contents = new List<ControlledCatalogContent>();
        await using var host = new Host(false, (attempt, _) =>
        {
            if (site == 3 && attempt == 1) return Task.FromResult(Response(HttpStatusCode.OK, "[]"));
            var content = new ControlledCatalogContent(expected, false);
            contents.Add(content);
            return Task.FromResult(new HttpResponseMessage((HttpStatusCode)status) { Content = content });
        });
        var actual = await Record.ExceptionAsync(() => host.CallCatalogSiteAsync(site, CancellationToken.None));
        if (io) Assert.Same(expected, Assert.IsType<HttpRequestException>(actual).InnerException);
        else Assert.Same(expected, actual);
        Assert.Equal(1, Assert.Single(contents).Serializations);
        var record = Assert.Single(host.Logs.Failures);
        Assert.Equal(io ? "HttpRequestException" : "InvalidOperationException", record.Fields["ExceptionType"]);
        Assert.Equal("CatalogService", record.Fields["Dependency"]);
        Assert.Equal(site == 0 ? "GET" : site == 2 ? "PUT" : "POST", record.Fields["Method"]);
        Assert.False(record.Fields.ContainsKey("StatusCode"));
        AssertSafeFailure(record);
        AssertHandlerOrder(host);
        Assert.All(host.Transport.Requests, request => Assert.Equal("Bearer service-token-canary", request.Authorization));
    }

    [Fact]
    public async Task CatalogTypedCall_NonSuccessStatus_WaitsForRealNativeBufferCompletion()
    {
        using var caller = new CancellationTokenSource();
        var content = new ControlledCatalogContent(null, true);
        await using var host = new Host(false, (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.BadRequest) { Content = content }));
        var pending = host.ReadAsync(caller.Token, HttpStatusCode.BadRequest);
        try
        {
            await content.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.False(pending.IsCompleted);
            Assert.Empty(host.Logs.Failures);
            content.Release.TrySetResult();
            await pending.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(1, content.Serializations);
            var record = Assert.Single(host.Logs.Failures);
            Assert.Equal(400, record.Fields["StatusCode"]);
            Assert.Null(record.Fields["ExceptionType"]);
            AssertSafeFailure(record);
        }
        finally
        {
            caller.Cancel();
            content.Release.TrySetResult();
            try { await pending.WaitAsync(TimeSpan.FromSeconds(5)); } catch (OperationCanceledException) { }
        }
    }

    [Fact]
    public async Task CatalogTypedCall_BufferCallerCancellation_DoesNotForgeHandlerDeadline()
    {
        using var caller = new CancellationTokenSource();
        var content = new ControlledCatalogContent(null, true);
        await using var host = new Host(false, (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.BadRequest) { Content = content }));
        var pending = host.ReadAsync(caller.Token, HttpStatusCode.BadRequest);
        try
        {
            await content.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(Timeout.InfiniteTimeSpan, host.SelectedClientTimeout());
            Assert.False(pending.IsCompleted);
            Assert.Empty(host.Logs.Failures);
            caller.Cancel();
            var actual = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending.WaitAsync(TimeSpan.FromSeconds(5)));
            Assert.NotNull(content.TerminalCancellation);
            Assert.True(caller.IsCancellationRequested);
            Assert.True(ReferenceEquals(actual, content.TerminalCancellation)
                || ReferenceEquals(actual.InnerException, content.TerminalCancellation));
            Assert.Empty(host.Logs.Failures);
            Assert.Single(host.Transport.Requests);
            Assert.Equal(1, content.Serializations);
        }
        finally
        {
            caller.Cancel();
            content.Release.TrySetResult();
            try { await pending.WaitAsync(TimeSpan.FromSeconds(5)); } catch (OperationCanceledException) { }
        }
    }

    [Fact]
    public async Task CatalogActualFactory_RetainsInfiniteNativeTimeoutAndDistinctStandardBudgets()
    {
        await using var host = new Host(false, (_, _) => Task.FromResult(Response(HttpStatusCode.OK, "{}")));
        Assert.Equal(Timeout.InfiniteTimeSpan, host.SelectedClientTimeout());
        var typedOptions = host.CatalogStandardOptions();
        Assert.Equal(TimeSpan.FromSeconds(10), typedOptions.AttemptTimeout.Timeout);
        Assert.Equal(TimeSpan.FromSeconds(30), typedOptions.TotalRequestTimeout.Timeout);
        Assert.Equal(TimeSpan.FromSeconds(30), typedOptions.CircuitBreaker.SamplingDuration);
        var options = host.SharedStandardOptions();
        Assert.Equal(TimeSpan.FromSeconds(30), options.AttemptTimeout.Timeout);
        Assert.Equal(TimeSpan.FromSeconds(60), options.TotalRequestTimeout.Timeout);
        Assert.Equal(TimeSpan.FromSeconds(65), options.CircuitBreaker.SamplingDuration);
        Assert.Empty(host.Transport.Requests);
        Assert.Empty(host.Logs.Failures);
    }

    private sealed class ControlledCatalogContent(Exception? failure, bool block) : HttpContent
    {
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int Serializations { get; private set; }
        public OperationCanceledException? TerminalCancellation { get; private set; }
        protected override bool TryComputeLength(out long length) { length = 0; return false; }
        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) =>
            SerializeToStreamAsync(stream, context, CancellationToken.None);
        protected override async Task SerializeToStreamAsync(Stream stream, TransportContext? context, CancellationToken token)
        {
            Serializations++;
            Entered.TrySetResult();
            try
            {
                if (block) await Release.Task.WaitAsync(token);
                if (failure is not null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
                await stream.WriteAsync(Encoding.UTF8.GetBytes(BodyCanary), token);
            }
            catch (OperationCanceledException exception) { TerminalCancellation = exception; throw; }
        }
    }

    private static Exception UnexpectedFailure(int kind) => kind switch
    {
        0 => new InvalidOperationException($"{ExceptionCanary} {Canary} {EmployeeToken} {ServiceToken} {BodyCanary}"),
        1 => new IOException($"{ExceptionCanary} {Canary} {EmployeeToken} {ServiceToken} {BodyCanary}"),
        _ => new ArgumentException($"{ExceptionCanary} {Canary} {EmployeeToken} {ServiceToken} {BodyCanary}")
    };

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
                    if (expectedStatus == HttpStatusCode.NotFound)
                    {
                        Assert.Null(page);
                        return;
                    }
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

        public TimeSpan SelectedClientTimeout()
        {
            using var client = Services.GetRequiredService<IHttpClientFactory>()
                .CreateClient(useBff ? nameof(OrdersProxy) : nameof(ILegacyCatalogClient));
            return client.Timeout;
        }

        public HttpStandardResilienceOptions CatalogStandardOptions() =>
            Services.GetRequiredService<IOptionsMonitor<HttpStandardResilienceOptions>>()
                .Get(nameof(ILegacyCatalogClient) + "-standard");

        public HttpStandardResilienceOptions SharedStandardOptions() =>
            Services.GetRequiredService<IOptionsMonitor<HttpStandardResilienceOptions>>()
                .Get("-standard");

        public async Task CallCatalogSiteAsync(int site, CancellationToken token)
        {
            using var scope = Services.CreateScope();
            var catalog = scope.ServiceProvider.GetRequiredService<ILegacyCatalogClient>();
            switch (site)
            {
                case 0: await catalog.GetMaterialsAsync(MaterialSortType.MaterialName_Ascending, Canary, 2, 25, EmployeeToken, token); break;
                case 1: await catalog.CreateMaterialAsync(MaterialFixtures.Request, EmployeeToken, token); break;
                case 2: await catalog.UpdateMaterialAsync(42, MaterialFixtures.Request, EmployeeToken, token); break;
                default: await catalog.SyncMaterialColorsAsync(42, [1], EmployeeToken, token); break;
            }
        }

        public async Task SendUnselectedAsync()
        {
            using var client = Services.GetRequiredService<IHttpClientFactory>().CreateClient("UnselectedSourceObserverProof");
            using var request = new HttpRequestMessage(HttpMethod.Get, "http://unselected.invalid/orders");
            await Assert.ThrowsAsync<InvalidOperationException>(() => client.SendAsync(request));
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
        public Exception? Failure { get; set; }
        public ValueTask<string?> GetAccessTokenAsync(CancellationToken cancellationToken) => Failure is { } failure
            ? ValueTask.FromException<string?>(failure) : ValueTask.FromResult<string?>(ServiceToken);
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
