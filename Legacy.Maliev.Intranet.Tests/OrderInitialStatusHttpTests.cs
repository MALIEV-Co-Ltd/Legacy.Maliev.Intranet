extern alias Bff;

using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Bunit;
using Legacy.Maliev.Intranet.Auth;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Routing;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit.Abstractions;
using BffProgram = Bff::Program;
using OrderCatalogReferenceProxy = Bff::Legacy.Maliev.Intranet.Bff.Orders.OrderCatalogReferenceProxy;
using OrderDetailProxy = Bff::Legacy.Maliev.Intranet.Bff.Orders.OrderDetailProxy;
using OrderEmployeeReferenceProxy = Bff::Legacy.Maliev.Intranet.Bff.Orders.OrderEmployeeReferenceProxy;
using OrderFileProxy = Bff::Legacy.Maliev.Intranet.Bff.Orders.OrderFileProxy;
using OrdersProxy = Bff::Legacy.Maliev.Intranet.Bff.Orders.OrdersProxy;
using OrderView = Legacy.Maliev.Intranet.Client.Features.Orders.Pages.OrderDetail;

namespace Legacy.Maliev.Intranet.Tests;

/// <summary>Actual BFF cookies/aggregator and actual routed component with controlled external transports.</summary>
public sealed class OrderInitialStatusHttpTests(ITestOutputHelper output)
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Latest404_ProjectsInitialStatusAndReadsOnlyProducerAllowedTransitions(bool missingCreatedDate)
    {
        var upstream = new Boundary { LatestStatus = HttpStatusCode.NotFound, MissingCreatedDate = missingCreatedDate };
        await using var factory = new Factory(upstream);
        using var client = Client(factory);
        await LoginAsync(client);
        using var response = await client.GetAsync("/bff/orders/84");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        var current = json.GetProperty("currentStatus");
        Assert.Equal(JsonValueKind.Object, current.ValueKind);
        Assert.Equal(1, current.GetProperty("id").GetInt32());
        Assert.Equal("New", current.GetProperty("name").GetString());
        Assert.Equal("New order", current.GetProperty("description").GetString());
        Assert.Equal(missingCreatedDate ? null : "2030-07-15T00:00:00Z", current.GetProperty("createdDate").GetString());
        Assert.Equal(2, Assert.Single(json.GetProperty("availableStatuses").EnumerateArray()).GetProperty("id").GetInt32());
        Assert.Contains(upstream.Requests, item => item == "GET /orderstatuses/1/available");
        Assert.All(upstream.Requests, item => Assert.StartsWith("GET ", item, StringComparison.Ordinal));
    }

    [Fact]
    public async Task Latest404_ReadsStatusOneTransitionsButNeverWritesHistory()
    {
        var upstream = new Boundary { LatestStatus = HttpStatusCode.NotFound };
        await using var factory = new Factory(upstream);
        using var client = Client(factory);
        await LoginAsync(client);
        using var response = await client.GetAsync("/bff/orders/84");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("GET /orderstatuses/1/available", upstream.Requests);
        Assert.All(upstream.Requests, item => Assert.StartsWith("GET ", item, StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(true, false, false)]
    [InlineData(true, true, false)]
    [InlineData(false, false, false)]
    [InlineData(false, false, true)]
    [InlineData(false, true, false)]
    public async Task InitialHistoryProjection_ExistsOnlyForHistory404(bool missingHistory, bool nullLatest, bool emptyHistory)
    {
        var upstream = new Boundary
        {
            HistoryStatus = missingHistory ? HttpStatusCode.NotFound : HttpStatusCode.OK,
            LatestBody = nullLatest ? "null" : """{"Id":2,"Name":"Reviewed"}""",
            HistoryBody = emptyHistory ? "[]" : """[{"Id":91,"OrderId":84,"OrderStatusId":2,"Name":"Reviewed","CreatedDate":"2030-07-16T00:00:00Z"}]""",
        };
        await using var factory = new Factory(upstream);
        using var client = Client(factory);
        await LoginAsync(client);
        using var response = await client.GetAsync("/bff/orders/84");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(json.TryGetProperty("initialHistoryEntry", out var initial));
        if (missingHistory)
        {
            Assert.Equal("New", initial.GetProperty("name").GetString());
            Assert.Equal("New order", initial.GetProperty("description").GetString());
            Assert.Equal("2030-07-15T00:00:00Z", initial.GetProperty("createdDate").GetString());
            Assert.False(initial.TryGetProperty("orderId", out _));
            Assert.False(initial.TryGetProperty("orderStatusId", out _));
            Assert.Empty(json.GetProperty("history").EnumerateArray());
        }
        else
        {
            Assert.Equal(JsonValueKind.Null, initial.ValueKind);
            Assert.Equal(emptyHistory ? 0 : 1, json.GetProperty("history").GetArrayLength());
        }
        if (nullLatest) Assert.Equal(JsonValueKind.Null, json.GetProperty("currentStatus").ValueKind);
        Assert.All(upstream.Requests, item => Assert.StartsWith("GET ", item, StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("en", "New", false)]
    [InlineData("th", "งานใหม่", false)]
    [InlineData("en", "New", true)]
    [InlineData("th", "งานใหม่", true)]
    public async Task History404_RendersInitialEntryWithoutInventingPersistedHistory(string culture, string label, bool missingCreatedDate)
    {
        using var cultureScope = new CultureScope(culture);
        var upstream = new Boundary { HistoryStatus = HttpStatusCode.NotFound, MissingCreatedDate = missingCreatedDate };
        await using var factory = new Factory(upstream);
        using var client = Client(factory);
        await LoginAsync(client);
        using var response = await client.GetAsync("/bff/orders/84");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Empty(json.GetProperty("history").EnumerateArray());
        var stages = new PresentationStages();
        using var context = RenderContext(client, stages);
        var page = Render(context).FindComponent<OrderView>();
        try
        {
            await page.WaitForAssertionAsync(() => Assert.NotEmpty(page.FindAll(".order-workflow-panel")));
            var history = page.Find(".order-history");
            Assert.Contains(label, history.TextContent, StringComparison.Ordinal);
            Assert.Contains(missingCreatedDate ? "-" : culture == "en" ? "15 Jul 2030, 07:00" : "15 ก.ค. 2573, 07:00", history.TextContent, StringComparison.Ordinal);
            Assert.DoesNotContain("Reviewed", history.TextContent, StringComparison.Ordinal);
            Assert.All(upstream.Requests, item => Assert.StartsWith("GET ", item, StringComparison.Ordinal));
        }
        finally
        {
            // Test-owned synthetic stages only: never emit request paths, bodies or identity.
            var routeComponents = -1;
            var workflowPanels = -1;
            try
            {
                routeComponents = 1;
                workflowPanels = page.FindAll(".order-workflow-panel").Count;
            }
            catch (Exception)
            {
                // Observation is secondary and must not replace the original assertion failure.
                routeComponents = -1;
                workflowPanels = -1;
            }
            output.WriteLine("Synthetic presentation stages: route components={0}; workflow panels={1}; session started/completed={2}/{3}; detail started/completed={4}/{5}; other started/completed={6}/{7}",
                routeComponents, workflowPanels,
                stages.Started(0), stages.Completed(0), stages.Started(1), stages.Completed(1), stages.Started(2), stages.Completed(2));
        }
    }

    [Fact]
    public async Task History404_DeferredDetailNotifiesChildBeforeUpdatingRouterMarkup()
    {
        using var cultureScope = new CultureScope("th");
        var upstream = new Boundary { HistoryStatus = HttpStatusCode.NotFound, MissingCreatedDate = true };
        await using var factory = new Factory(upstream);
        using var client = Client(factory);
        await LoginAsync(client);
        var stages = new PresentationStages { HoldDetail = true };
        using var context = RenderContext(client, stages);
        var router = Render(context);
        var detail = router.FindComponent<OrderView>();
        var parentMarkupAtChildNotification = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        void ObserveChildRender(object? sender, EventArgs args)
        {
            if (detail.FindAll(".order-workflow-panel").Count > 0)
                parentMarkupAtChildNotification.TrySetResult(router.FindAll(".order-workflow-panel").Count > 0);
        }
        detail.OnAfterRender += ObserveChildRender;
        try
        {
            await stages.DetailEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Empty(detail.FindAll(".order-workflow-panel"));
            // The default deadline still rejects a response that remains held.
            // Awaiting the same helper lets this test release the pending HTTP work.
            var heldAssertion = detail.WaitForAssertionAsync(() => Assert.NotEmpty(detail.FindAll(".order-workflow-panel")));
            Assert.False(heldAssertion.IsCompleted);
            await Assert.ThrowsAsync<Bunit.Extensions.WaitForHelpers.WaitForFailedException>(() => heldAssertion);
            Assert.Empty(detail.FindAll(".order-workflow-panel"));
            var readyAssertion = detail.WaitForAssertionAsync(() => Assert.NotEmpty(detail.FindAll(".order-workflow-panel")));
            Assert.False(readyAssertion.IsCompleted);
            stages.DetailReleased.TrySetResult();
            await readyAssertion;
            Assert.Contains("งานใหม่", detail.Find(".order-history").TextContent, StringComparison.Ordinal);
            Assert.Contains("-", detail.Find(".order-history").TextContent, StringComparison.Ordinal);
            // bUnit updates/notifies the child before refreshing its parents' DOM.
            // A wait on the child must therefore query the child's own snapshot.
            Assert.False(await parentMarkupAtChildNotification.Task.WaitAsync(TimeSpan.FromSeconds(5)));
            Assert.All(upstream.Requests, item => Assert.StartsWith("GET ", item, StringComparison.Ordinal));
        }
        finally
        {
            detail.OnAfterRender -= ObserveChildRender;
            stages.DetailReleased.TrySetResult();
        }
    }

    [Theory]
    [InlineData("en", "New")]
    [InlineData("th", "งานใหม่")]
    public async Task Latest404_WithRealHistory_RendersLocalizedCurrentButPreservesRealHistory(string culture, string label)
    {
        using var cultureScope = new CultureScope(culture);
        var upstream = new Boundary { LatestStatus = HttpStatusCode.NotFound };
        await using var factory = new Factory(upstream);
        using var client = Client(factory);
        await LoginAsync(client);
        using var context = RenderContext(client);
        var page = Render(context).FindComponent<OrderView>();
        await page.WaitForAssertionAsync(() => Assert.NotEmpty(page.FindAll(".operations-status-pill")));
        Assert.Equal(label, page.Find(".operations-status-pill").TextContent);
        Assert.DoesNotContain(label, page.Find(".order-history").TextContent, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("en", "Reviewed")]
    [InlineData("th", "ตรวจสอบแล้ว")]
    public async Task RealStatusAndHistory_RemainUnchangedAndSuccessfulEmptyHistoryIsNot404(string culture, string label)
    {
        using var cultureScope = new CultureScope(culture);
        var upstream = new Boundary();
        await using var factory = new Factory(upstream);
        using var client = Client(factory);
        await LoginAsync(client);
        using var response = await client.GetAsync("/bff/orders/84");
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        var entry = Assert.Single(json.GetProperty("history").EnumerateArray());
        Assert.Equal(91, entry.GetProperty("id").GetInt32());
        Assert.Equal(2, entry.GetProperty("orderStatusId").GetInt32());
        Assert.Equal("2030-07-16T00:00:00Z", entry.GetProperty("createdDate").GetString());
        var realStages = new PresentationStages();
        using (var context = RenderContext(client, realStages))
        {
            var page = Render(context).FindComponent<OrderView>();
            try
            {
                await page.WaitForAssertionAsync(() => Assert.Equal(label, page.Find(".operations-status-pill").TextContent));
                Assert.Contains(label, page.Find(".order-history").TextContent, StringComparison.Ordinal);
            }
            finally
            {
                EmitPresentationWitness(page, realStages, "real-history");
            }
        }
        upstream.HistoryBody = "[]";
        var emptyStages = new PresentationStages();
        using var emptyContext = RenderContext(client, emptyStages);
        var emptyPage = Render(emptyContext).FindComponent<OrderView>();
        try
        {
            await emptyPage.WaitForAssertionAsync(() => Assert.NotEmpty(emptyPage.FindAll(".order-history")));
            Assert.True(string.IsNullOrWhiteSpace(emptyPage.Find(".order-history").TextContent));
        }
        finally
        {
            EmitPresentationWitness(emptyPage, emptyStages, "empty-history");
        }

        void EmitPresentationWitness(IRenderedComponent<OrderView> page, PresentationStages stages, string phase)
        {
            // Synthetic counters/presence only; never emit markup, paths, bodies or identity.
            var routeCount = -1;
            var routeRenderCounts = "unavailable";
            var loading = -1;
            var error = -1;
            var workflow = -1;
            try
            {
                routeCount = 1;
                routeRenderCounts = page.RenderCount.ToString(CultureInfo.InvariantCulture);
                loading = page.FindAll(".order-detail__loading").Count;
                error = page.FindAll("[role='alert']").Count;
                workflow = page.FindAll(".order-workflow-panel").Count;
            }
            catch (Exception)
            {
                // Observation must not replace an original assertion failure.
            }
            output.WriteLine("Synthetic order presentation: phase={0}; route components={1}; route component render counts=[{2}]; loading/error/workflow={3}/{4}/{5}; session started/completed={6}/{7}; detail started/completed={8}/{9}; other started/completed={10}/{11}",
                phase, routeCount, routeRenderCounts, loading, error, workflow,
                stages.Started(0), stages.Completed(0), stages.Started(1), stages.Completed(1), stages.Started(2), stages.Completed(2));
        }
    }

    [Theory]
    [InlineData(401)]
    [InlineData(403)]
    [InlineData(500)]
    public async Task Non404LatestFailure_IsGenericUnavailableNotInitialSuccess(int status)
    {
        var upstream = new Boundary { LatestStatus = (HttpStatusCode)status };
        await using var factory = new Factory(upstream);
        using var client = Client(factory);
        await LoginAsync(client);
        using var response = await client.GetAsync("/bff/orders/84");
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.DoesNotContain("New order", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        Assert.DoesNotContain(upstream.Requests, item => item.Contains("/available", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(401)]
    [InlineData(403)]
    [InlineData(500)]
    public async Task Non404HistoryFailure_IsUnavailableNotInitialSuccess(int status)
    {
        var upstream = new Boundary { HistoryStatus = (HttpStatusCode)status };
        await using var factory = new Factory(upstream);
        using var client = Client(factory);
        await LoginAsync(client);
        using var response = await client.GetAsync("/bff/orders/84");
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.DoesNotContain("New order", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        Assert.All(upstream.Requests, item => Assert.StartsWith("GET ", item, StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(401)]
    [InlineData(403)]
    [InlineData(500)]
    public async Task InitialAvailableFailure_IsGenericUnavailableNotUsablePage(int status)
    {
        var upstream = new Boundary { LatestStatus = HttpStatusCode.NotFound, AvailableStatus = (HttpStatusCode)status };
        await using var factory = new Factory(upstream);
        using var client = Client(factory);
        await LoginAsync(client);
        using var response = await client.GetAsync("/bff/orders/84");
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.DoesNotContain("New order", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        Assert.All(upstream.Requests, item => Assert.StartsWith("GET ", item, StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("not-json")]
    [InlineData("{}")]
    public async Task MalformedHistory_IsUnavailableNotInitialHistory(string body)
    {
        var upstream = new Boundary { HistoryBody = body };
        await using var factory = new Factory(upstream);
        using var client = Client(factory);
        await LoginAsync(client);
        using var response = await client.GetAsync("/bff/orders/84");
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.DoesNotContain("New order", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("not-json")]
    [InlineData("[]")]
    public async Task MalformedSuccessfulLatest_IsUnavailableNotInitialSuccess(string body)
    {
        var upstream = new Boundary { LatestBody = body };
        await using var factory = new Factory(upstream);
        using var client = Client(factory);
        await LoginAsync(client);
        using var response = await client.GetAsync("/bff/orders/84");
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.DoesNotContain(upstream.Requests, item => item.Contains("/available", StringComparison.Ordinal));
    }

    [Fact]
    public async Task SuccessfulNullLatest_IsNotEvidenceOfNew()
    {
        using var cultureScope = new CultureScope("en");
        var upstream = new Boundary { LatestBody = "null" };
        await using var factory = new Factory(upstream);
        using var client = Client(factory);
        await LoginAsync(client);
        using var response = await client.GetAsync("/bff/orders/84");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(JsonValueKind.Null, json.GetProperty("currentStatus").ValueKind);
        Assert.DoesNotContain(upstream.Requests, item => item.Contains("/available", StringComparison.Ordinal));
        using var context = RenderContext(client);
        var page = Render(context).FindComponent<OrderView>();
        await page.WaitForAssertionAsync(() => Assert.Equal("-", page.Find(".operations-status-pill").TextContent));
    }

    [Fact]
    public async Task MissingCanonicalOrder_Remains404DespiteMissingStatusReads()
    {
        var upstream = new Boundary { OrderStatus = HttpStatusCode.NotFound, LatestStatus = HttpStatusCode.NotFound, HistoryStatus = HttpStatusCode.NotFound };
        await using var factory = new Factory(upstream);
        using var client = Client(factory);
        await LoginAsync(client);
        using var response = await client.GetAsync("/bff/orders/84");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.DoesNotContain(upstream.Requests, item => item.Contains("/available", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AnonymousOrMissingReadPermission_DeniesBeforeDownstream(bool authenticated)
    {
        var upstream = new Boundary();
        await using var factory = new Factory(upstream, authenticated ? [] : Permissions);
        using var client = Client(factory);
        if (authenticated) await LoginAsync(client);
        using var response = await client.GetAsync("/bff/orders/84");
        Assert.Equal(authenticated ? HttpStatusCode.Forbidden : HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Empty(upstream.Requests);
    }

    [Fact]
    public async Task CallerAbortDuringLatestRead_PropagatesWithoutAvailableOrMutationCalls()
    {
        var upstream = new Boundary { BlockLatest = true };
        await using var factory = new Factory(upstream);
        using var client = Client(factory);
        await LoginAsync(client);
        using var cancellation = new CancellationTokenSource();
        var pending = client.GetAsync("/bff/orders/84", cancellation.Token);
        await upstream.LatestEntered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
        Assert.DoesNotContain(upstream.Requests, item => item.Contains("/available", StringComparison.Ordinal));
        Assert.All(upstream.Requests, item => Assert.StartsWith("GET ", item, StringComparison.Ordinal));
    }

    private static BunitContext RenderContext(HttpClient client, PresentationStages? stages = null)
    {
        var context = new BunitContext();
        context.Services.AddLocalization();
        context.Services.AddSingleton(new HttpClient(new BrowserBoundary(client, stages)) { BaseAddress = new("https://localhost/") });
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        context.Services.GetRequiredService<NavigationManager>().NavigateTo("/Orders/View?id=84");
        return context;
    }

    private static IRenderedComponent<Router> Render(BunitContext context) => context.Render<Router>(parameters => parameters
        .Add(router => router.AppAssembly, typeof(OrderView).Assembly)
        .Add(router => router.Found, (RenderFragment<RouteData>)(route => builder =>
        {
            builder.OpenComponent<RouteView>(0);
            builder.AddAttribute(1, nameof(RouteView.RouteData), route);
            builder.CloseComponent();
        })));

    private sealed class CultureScope : IDisposable
    {
        private readonly CultureInfo previous = CultureInfo.CurrentCulture;
        private readonly CultureInfo previousUi = CultureInfo.CurrentUICulture;
        public CultureScope(string culture)
        {
            var selectedCulture = CultureInfo.GetCultureInfo(culture);
            CultureInfo.CurrentCulture = selectedCulture;
            CultureInfo.CurrentUICulture = selectedCulture;
        }
        public void Dispose() { CultureInfo.CurrentCulture = previous; CultureInfo.CurrentUICulture = previousUi; }
    }

    private sealed class PresentationStages
    {
        public bool HoldDetail { get; init; }
        public TaskCompletionSource DetailEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource DetailReleased { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly int[] started = new int[3];
        private readonly int[] completed = new int[3];
        public void Start(int stage) => Interlocked.Increment(ref started[stage]);
        public void Complete(int stage) => Interlocked.Increment(ref completed[stage]);
        public int Started(int stage) => Volatile.Read(ref started[stage]);
        public int Completed(int stage) => Volatile.Read(ref completed[stage]);
    }

    private sealed class BrowserBoundary(HttpClient client, PresentationStages? stages = null) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            stages is null ? client.GetAsync(request.RequestUri!.PathAndQuery, cancellationToken) : SendObservedAsync(request, cancellationToken);

        private async Task<HttpResponseMessage> SendObservedAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var stage = request.RequestUri!.AbsolutePath switch { "/bff/session" => 0, "/bff/orders/84" => 1, _ => 2 };
            stages?.Start(stage);
            if (stage == 1 && stages?.HoldDetail == true)
            {
                stages.DetailEntered.TrySetResult();
                await stages.DetailReleased.Task.WaitAsync(cancellationToken);
            }
            var response = await client.GetAsync(request.RequestUri.PathAndQuery, cancellationToken);
            stages?.Complete(stage);
            return response;
        }
    }

    private static HttpClient Client(Factory factory) => factory.CreateClient(new() { BaseAddress = new("https://localhost/"), AllowAutoRedirect = false });
    private static async Task LoginAsync(HttpClient client)
    {
        using var session = await client.GetAsync("/bff/session");
        var json = await session.Content.ReadFromJsonAsync<JsonElement>();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/bff/login") { Content = JsonContent.Create(new { email = "employee@maliev.com", password = "fixture", returnUrl = "/Orders/View?id=84" }) };
        request.Headers.Add("X-CSRF-TOKEN", json.GetProperty("csrfToken").GetString());
        using var response = await client.SendAsync(request);
        response.EnsureSuccessStatusCode();
    }

    private static readonly string[] Permissions = [LegacyEmployeePermissions.OrdersRead, LegacyEmployeePermissions.OrderCatalogRead, LegacyEmployeePermissions.EmployeesList, LegacyEmployeePermissions.CatalogMaterialsRead, LegacyEmployeePermissions.OrderStatusRead, LegacyEmployeePermissions.OrderFilesRead, LegacyEmployeePermissions.FileUploadsRead];

    private sealed class Factory(Boundary boundary, IReadOnlyList<string>? permissions = null) : WebApplicationFactory<BffProgram>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing");
            TestJwtConfiguration.Configure(builder);
            foreach (var service in new[] { "Auth", "Catalog", "Customer", "Employee", "Order", "File", "Document" }) builder.UseSetting($"Services:{service}", $"http://{service.ToLowerInvariant()}/");
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<ILegacyAuthClient>();
                services.AddSingleton<ILegacyAuthClient>(new AuthBoundary(permissions ?? Permissions));
                services.RemoveAll<IServiceAccessTokenProvider>();
                services.AddSingleton<IServiceAccessTokenProvider>(new Tokens());
                Replace(services, new OrdersProxy(Transport("order")));
                Replace(services, new OrderDetailProxy(Transport("order")));
                Replace(services, new OrderCatalogReferenceProxy(Transport("catalog")));
                Replace(services, new OrderEmployeeReferenceProxy(Transport("employee")));
                Replace(services, new OrderFileProxy(Transport("file")));
            });
        }
        private HttpClient Transport(string service) => new(new LegacyServiceAuthenticationHandler(new Tokens()) { InnerHandler = boundary }) { BaseAddress = new($"http://{service}/"), Timeout = TimeSpan.FromSeconds(10) };
        private static void Replace<T>(IServiceCollection services, T instance) where T : class { services.RemoveAll<T>(); services.AddSingleton(instance); }
    }

    private sealed class Tokens : IServiceAccessTokenProvider
    {
        public ValueTask<string?> GetAccessTokenAsync(CancellationToken cancellationToken) => ValueTask.FromResult<string?>("fixture-service-token");
        public void Invalidate(string token) { }
    }
    private sealed class AuthBoundary(IReadOnlyList<string> permissions) : ILegacyAuthClient
    {
        public Task<EmployeeLoginResult> LoginAsync(string email, string password, CancellationToken cancellationToken) => Task.FromResult(new EmployeeLoginResult(true, new("fixture-access", "fixture-refresh", "Bearer", 900, DateTimeOffset.UtcNow.AddDays(1)), new("fixture-employee", email, email, permissions, 7)));
        public Task<EmployeeRefreshResult?> RefreshAsync(string refreshToken, CancellationToken cancellationToken) => Task.FromResult<EmployeeRefreshResult?>(null);
        public Task RevokeAsync(string refreshToken, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<CustomerIdentityResponse?> CreateCustomerIdentityAsync(int databaseId, CreateCustomerIdentityRequest request, string accessToken, CancellationToken cancellationToken) => Task.FromResult<CustomerIdentityResponse?>(null);
        public Task<EmployeeIdentityResponse?> CreateEmployeeIdentityAsync(int databaseId, CreateEmployeeIdentityRequest request, string accessToken, CancellationToken cancellationToken) => Task.FromResult<EmployeeIdentityResponse?>(null);
    }

    private sealed class Boundary : HttpMessageHandler
    {
        public HttpStatusCode OrderStatus { get; init; } = HttpStatusCode.OK;
        public HttpStatusCode LatestStatus { get; init; } = HttpStatusCode.OK;
        public HttpStatusCode HistoryStatus { get; init; } = HttpStatusCode.OK;
        public HttpStatusCode AvailableStatus { get; init; } = HttpStatusCode.OK;
        public string LatestBody { get; init; } = """{"Id":2,"Name":"Reviewed","Description":"Checked","CreatedDate":"2030-07-16T00:00:00Z"}""";
        public string HistoryBody { get; set; } = """[{"Id":91,"OrderId":84,"OrderStatusId":2,"Name":"Reviewed","Description":"Checked","CreatedDate":"2030-07-16T00:00:00Z"}]""";
        public bool MissingCreatedDate { get; init; }
        public bool BlockLatest { get; init; }
        public TaskCompletionSource LatestEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public ConcurrentBag<string> Requests { get; } = [];
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.AbsolutePath;
            Requests.Add($"{request.Method} {path}");
            if (path.EndsWith("/latest", StringComparison.Ordinal) && BlockLatest)
            {
                LatestEntered.TrySetResult();
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            }
            return path switch
            {
                "/Orders/84" => Json(MissingCreatedDate ? Order.Replace("\"CreatedDate\":\"2030-07-15T00:00:00Z\"", "\"CreatedDate\":null", StringComparison.Ordinal) : Order, OrderStatus),
                "/orderstatuses/Histories/84/latest" => Json(LatestBody, LatestStatus),
                "/orderstatuses/Histories/84" => Json(HistoryBody, HistoryStatus),
                "/orderstatuses/1/available" => Json("""[{"Id":1,"Name":"New"},{"Id":2,"Name":"Reviewed"}]""", AvailableStatus),
                "/orderstatuses/2/available" => Json("""[{"Id":2,"Name":"Reviewed"},{"Id":3,"Name":"Quoted"}]""", AvailableStatus),
                "/orders/processes" => Json("""[{"Id":3,"Name":"FDM"}]"""),
                "/Materials" or "/employees" => Json("""{"Items":[]}"""),
                "/materials/Colors" or "/materials/SurfaceFinishes" or "/Currencies" or "/orders/84/files" => Json("[]"),
                _ => throw new InvalidOperationException("Unexpected fixture route."),
            };
        }
        private static HttpResponseMessage Json(string value, HttpStatusCode status = HttpStatusCode.OK) => new(status) { Content = new StringContent(value, Encoding.UTF8, "application/json") };
    }

    private const string Order = """{"Id":84,"CustomerId":42,"Name":"Order fixture","ProcessId":3,"Quantity":2,"Manufactured":0,"Remaining":2,"AllowCancellation":true,"CreatedDate":"2030-07-15T00:00:00Z","ModifiedDate":"2030-07-15T08:30:00Z"}""";
}
