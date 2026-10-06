extern alias Bff;

using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Legacy.Maliev.Intranet.Auth;
using Legacy.Maliev.Intranet.Contracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using BffProgram = Bff::Program;
using CustomersProxy = Bff::Legacy.Maliev.Intranet.Bff.Customers.CustomersProxy;

namespace Legacy.Maliev.Intranet.Tests;

// SOURCE-ONLY DRAFT: no execution/acceptance is implied. The real BFF Program,
// cookie/CSRF admission, service authentication handler and custom list retry remain.
// Only the established ILegacyAuthClient/token adapter and primary transport are controlled.
public sealed class BffCustomersPrivateFailureObservationTests
{
    private const string ServiceToken = "controlled-private-service-token";
    private const string QueryProbe = "controlled-private-query";
    private const string HeaderProbe = "controlled-private-header";
    private const string BodyProbe = "controlled-private-upstream-body";
    private const string ExceptionProbe = "controlled-private-transport-exception";
    private const string FailureCategory = "Maliev.Aspire.ServiceDefaults.Diagnostics.PrivateDependencyFailureHandler";
    private const string Page = """
        {"Items":[],"PageIndex":1,"TotalPages":0,"TotalRecords":0,"HasNextPage":false,"HasPreviousPage":false}
        """;

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task NormalCustomerPrivateFailure_RetryChainRecordsOnlyUnrecoveredTerminalResult(bool recovered)
    {
        using var transport = new ControlledTransport(recovered ? Mode.Recovered : Mode.Unavailable);
        await using var factory = new Factory(transport);
        using var client = Client(factory);
        await SignInAsync(client);
        client.DefaultRequestHeaders.Add("X-Controlled-Secret", HeaderProbe);

        using var response = await client.GetAsync("/bff/customers?search=" + QueryProbe);
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(recovered ? HttpStatusCode.OK : HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal(3, transport.Requests.Count);
        AssertAuthenticatedTransport(transport);
        Assert.Equal(1, factory.Tokens.Calls);
        Assert.Empty(factory.Tokens.Invalidated);
        if (recovered)
        {
            using var json = JsonDocument.Parse(body);
            Assert.Empty(json.RootElement.GetProperty("items").EnumerateArray());
            Assert.Equal(0, json.RootElement.GetProperty("totalRecords").GetInt32());
            Assert.Empty(factory.Logs.Entries);
        }
        else
        {
            Assert.Contains("CustomerService unavailable", body, StringComparison.Ordinal);
            AssertFailure(factory.Logs, 503);
        }
        AssertPublicBodySafe(body);
    }

    [Fact]
    public async Task NormalCustomerPrivateFailure_UnknownTransportPreservesGeneric503AndOpaqueIncident()
    {
        using var transport = new ControlledTransport(Mode.UnknownTransport);
        await using var factory = new Factory(transport);
        using var client = Client(factory);
        await SignInAsync(client);
        client.DefaultRequestHeaders.Add("X-Controlled-Secret", HeaderProbe);

        using var response = await client.GetAsync("/bff/customers?search=" + QueryProbe);
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal(3, transport.Requests.Count);
        AssertAuthenticatedTransport(transport);
        AssertFailure(factory.Logs, null);
        AssertPublicBodySafe(body);
        Assert.Empty(factory.Tokens.Invalidated);
    }

    [Fact]
    public async Task NormalCustomerPrivateFailure_CallerCancellationRemainsQuietAfterRealTransportAdmission()
    {
        using var transport = new ControlledTransport(Mode.WaitForCancellation);
        await using var factory = new Factory(transport);
        using var client = Client(factory);
        await SignInAsync(client);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        var pending = client.GetAsync("/bff/customers?search=" + QueryProbe, cancellation.Token);
        await transport.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            using var unexpected = await pending;
        });
        await transport.Cancelled.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await factory.RequestCompleted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Single(transport.Requests);
        AssertAuthenticatedTransport(transport);
        Assert.Empty(factory.Logs.Entries);
        Assert.Empty(factory.Tokens.Invalidated);
    }

    [Theory]
    [InlineData(401)]
    [InlineData(403)]
    public async Task NormalCustomerPrivateFailure_UpstreamAuthStatusPreservesInvalidationWithout5xxIncident(int status)
    {
        using var transport = new ControlledTransport(status == 401 ? Mode.Unauthorized : Mode.Forbidden);
        await using var factory = new Factory(transport);
        using var client = Client(factory);
        await SignInAsync(client);

        using var response = await client.GetAsync("/bff/customers?search=" + QueryProbe);

        Assert.Equal((HttpStatusCode)status, response.StatusCode);
        Assert.Single(transport.Requests);
        AssertAuthenticatedTransport(transport);
        Assert.Equal(ServiceToken, Assert.Single(factory.Tokens.Invalidated));
        Assert.Empty(factory.Logs.Entries);
        AssertPublicBodySafe(await response.Content.ReadAsStringAsync());
    }

    [Theory]
    [InlineData(false, 401)]
    [InlineData(true, 403)]
    public async Task NormalCustomerPrivateFailure_ExistingEmployeeAdmissionRejectsBeforeSelectedClient(bool signedIn, int status)
    {
        using var transport = new ControlledTransport(Mode.Unavailable);
        await using var factory = new Factory(transport, hasPermission: false);
        using var client = Client(factory);
        if (signedIn) await SignInAsync(client);

        using var response = await client.GetAsync("/bff/customers");

        Assert.Equal((HttpStatusCode)status, response.StatusCode);
        Assert.Empty(transport.Requests);
        Assert.Equal(0, factory.Tokens.Calls);
        Assert.Empty(factory.Logs.Entries);
    }

    private static void AssertFailure(CaptureProvider logs, int? status)
    {
        var entry = Assert.Single(logs.Entries);
        Assert.Equal(FailureCategory, entry.Category);
        Assert.Equal(LogLevel.Error, entry.Level);
        Assert.Equal(5101, entry.EventId.Id);
        Assert.Equal("DependencyRequestFailure", entry.EventId.Name);
        Assert.Null(entry.Exception);
        var keys = status.HasValue
            ? new[] { "Dependency", "EventName", "Operation", "StatusCode" }
            : new[] { "Dependency", "EventName", "Operation" };
        Assert.Equal(keys, entry.Values.Keys.Order(StringComparer.Ordinal).ToArray());
        Assert.Equal("DependencyRequestFailure", entry.Values["EventName"]);
        Assert.Equal("CustomerService", entry.Values["Dependency"]);
        Assert.Equal("Customers.Get", entry.Values["Operation"]);
        if (status.HasValue) Assert.Equal(status.Value, entry.Values["StatusCode"]);
        var captured = entry.Message + JsonSerializer.Serialize(entry.Values);
        foreach (var probe in new[] { ServiceToken, QueryProbe, HeaderProbe, BodyProbe, ExceptionProbe, "http://customer/", "server-only-access-token", "server-only-refresh-token" })
            Assert.DoesNotContain(probe, captured, StringComparison.Ordinal);
    }

    private static void AssertAuthenticatedTransport(ControlledTransport transport)
    {
        Assert.NotEmpty(transport.Requests);
        foreach (var request in transport.Requests)
        {
            Assert.Equal(HttpMethod.Get, request.Method);
            Assert.Equal("/customers?sort=CustomerCreatedDate_Descending&search=" + QueryProbe + "&index=1&size=25", request.PathAndQuery);
            Assert.Equal("Bearer " + ServiceToken, request.Authorization);
            Assert.Null(request.Body);
            Assert.False(request.HasBrowserCookie);
            Assert.False(request.HasProbeHeader);
        }
    }

    private static void AssertPublicBodySafe(string body)
    {
        foreach (var probe in new[] { ServiceToken, QueryProbe, HeaderProbe, BodyProbe, ExceptionProbe, "server-only-access-token", "server-only-refresh-token" })
            Assert.DoesNotContain(probe, body, StringComparison.Ordinal);
    }

    private static HttpClient Client(WebApplicationFactory<BffProgram> factory) => factory.CreateClient(new()
    {
        AllowAutoRedirect = false,
        BaseAddress = new Uri("https://localhost"),
        HandleCookies = true,
    });

    private static async Task SignInAsync(HttpClient client)
    {
        using var sessionResponse = await client.GetAsync("/bff/session");
        sessionResponse.EnsureSuccessStatusCode();
        var session = await sessionResponse.Content.ReadFromJsonAsync<JsonElement>();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/bff/login")
        {
            Content = JsonContent.Create(new
            {
                email = "employee@maliev.com",
                password = "controlled-password",
                returnUrl = "/Customers/Index",
            }),
        };
        request.Headers.Add("X-CSRF-TOKEN", session.GetProperty("csrfToken").GetString());
        using var response = await client.SendAsync(request);
        response.EnsureSuccessStatusCode();
    }

    private sealed class Factory(ControlledTransport transport, bool hasPermission = true) : WebApplicationFactory<BffProgram>
    {
        public TokenProvider Tokens { get; } = new();
        public CaptureProvider Logs { get; } = new();
        public TaskCompletionSource RequestCompleted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing");
            TestJwtConfiguration.Configure(builder);
            builder.UseSetting("Services:Auth", "http://auth/");
            builder.UseSetting("Services:Catalog", "http://catalog/");
            builder.UseSetting("Services:Customer", "http://customer/");
            builder.UseSetting("OTEL_EXPORTER_OTLP_ENDPOINT", "");
            builder.ConfigureLogging(logging => logging.AddProvider(Logs));
            builder.ConfigureServices(services =>
            {
                services.AddSingleton<IStartupFilter>(new RequestCompletionFilter(RequestCompleted));
                services.RemoveAll<ILegacyAuthClient>();
                services.AddSingleton<ILegacyAuthClient>(new AuthClient(hasPermission));
                services.RemoveAll<IServiceAccessTokenProvider>();
                services.AddSingleton<IServiceAccessTokenProvider>(Tokens);
                // Primary transport only: do not select the observer in the fixture,
                // replace the proxy, remove real auth/retry handlers or change timing.
                services.AddHttpClient<CustomersProxy>().ConfigurePrimaryHttpMessageHandler(() => transport);
            });
        }
    }

    // Test-only outer request rendezvous. Transport cancellation alone occurs before
    // selected observation/auth/retry handlers unwind, so it cannot prove quietness.
    // This filter neither selects the observer nor changes the HttpClient handler chain.
    private sealed class RequestCompletionFilter(TaskCompletionSource completed) : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => application =>
        {
            application.Use(async (HttpContext context, RequestDelegate requestNext) =>
            {
                if (context.Request.Path != new PathString("/bff/customers"))
                {
                    await requestNext(context);
                    return;
                }

                try
                {
                    await requestNext(context);
                }
                finally
                {
                    completed.TrySetResult();
                }
            });
            next(application);
        };
    }

    private sealed class TokenProvider : IServiceAccessTokenProvider
    {
        public int Calls { get; private set; }
        public List<string> Invalidated { get; } = [];
        public ValueTask<string?> GetAccessTokenAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Calls++;
            return ValueTask.FromResult<string?>(ServiceToken);
        }
        public void Invalidate(string token) => Invalidated.Add(token);
    }

    private sealed class AuthClient(bool hasPermission) : ILegacyAuthClient
    {
        public Task<EmployeeLoginResult> LoginAsync(string email, string password, CancellationToken cancellationToken) => Task.FromResult(new EmployeeLoginResult(
            true,
            new AuthTokenResponse("server-only-access-token", "server-only-refresh-token", "Bearer", 900, DateTimeOffset.UtcNow.AddDays(1)),
            new EmployeeIdentity(email, email, email, hasPermission ? ["legacy-customer.customers.list"] : [])));
        public Task<EmployeeRefreshResult?> RefreshAsync(string refreshToken, CancellationToken cancellationToken) => Task.FromResult<EmployeeRefreshResult?>(null);
        public Task RevokeAsync(string refreshToken, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<CustomerIdentityResponse?> CreateCustomerIdentityAsync(int databaseId, CreateCustomerIdentityRequest request, string accessToken, CancellationToken cancellationToken) => Task.FromResult<CustomerIdentityResponse?>(null);
        public Task<EmployeeIdentityResponse?> CreateEmployeeIdentityAsync(int databaseId, CreateEmployeeIdentityRequest request, string accessToken, CancellationToken cancellationToken) => Task.FromResult<EmployeeIdentityResponse?>(null);
    }

    private enum Mode
    {
        Unavailable,
        Recovered,
        UnknownTransport,
        WaitForCancellation,
        Unauthorized,
        Forbidden,
    }

    private sealed class ControlledTransport(Mode mode) : HttpMessageHandler
    {
        public ConcurrentQueue<RequestEvidence> Requests { get; } = new();
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Cancelled { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Enqueue(new RequestEvidence(request.Method, request.RequestUri?.PathAndQuery,
                request.Headers.Authorization?.ToString(), request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken),
                request.Headers.Contains("Cookie"), request.Headers.Contains("X-Controlled-Secret")));
            Entered.TrySetResult();
            if (mode == Mode.WaitForCancellation)
            {
                try
                {
                    await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                }
                finally
                {
                    if (cancellationToken.IsCancellationRequested) Cancelled.TrySetResult();
                }
                throw new InvalidOperationException("Cancellation did not stop controlled transport.");
            }
            if (mode == Mode.UnknownTransport) throw new HttpRequestException(ExceptionProbe);
            var status = mode switch
            {
                Mode.Recovered when Requests.Count >= 3 => HttpStatusCode.OK,
                Mode.Unauthorized => HttpStatusCode.Unauthorized,
                Mode.Forbidden => HttpStatusCode.Forbidden,
                _ => HttpStatusCode.ServiceUnavailable,
            };
            return new HttpResponseMessage(status)
            {
                RequestMessage = request,
                Content = new StringContent(status == HttpStatusCode.OK ? Page : BodyProbe, Encoding.UTF8, "application/json"),
            };
        }
    }

    private sealed record RequestEvidence(HttpMethod Method, string? PathAndQuery, string? Authorization, string? Body, bool HasBrowserCookie, bool HasProbeHeader);

    private sealed class CaptureProvider : ILoggerProvider
    {
        public ConcurrentQueue<Entry> Entries { get; } = new();
        public ILogger CreateLogger(string categoryName) => new CaptureLogger(categoryName, Entries);
        public void Dispose() { }
    }

    private sealed class CaptureLogger(string category, ConcurrentQueue<Entry> entries) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => category == FailureCategory;
        public void Log<TState>(LogLevel level, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(level)) return;
            entries.Enqueue(new Entry(category, level, eventId, exception, formatter(state, exception),
                ((IEnumerable<KeyValuePair<string, object?>>)state!).Where(pair => pair.Key != "{OriginalFormat}").ToDictionary()));
        }
    }

    private sealed record Entry(string Category, LogLevel Level, EventId EventId, Exception? Exception, string Message, Dictionary<string, object?> Values);
}
