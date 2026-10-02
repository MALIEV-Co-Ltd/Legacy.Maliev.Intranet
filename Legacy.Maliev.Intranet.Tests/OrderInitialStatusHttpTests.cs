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
using BffProgram = Bff::Program;
using OrderCatalogReferenceProxy = Bff::Legacy.Maliev.Intranet.Bff.Orders.OrderCatalogReferenceProxy;
using OrderDetailProxy = Bff::Legacy.Maliev.Intranet.Bff.Orders.OrderDetailProxy;
using OrderEmployeeReferenceProxy = Bff::Legacy.Maliev.Intranet.Bff.Orders.OrderEmployeeReferenceProxy;
using OrderFileProxy = Bff::Legacy.Maliev.Intranet.Bff.Orders.OrderFileProxy;
using OrdersProxy = Bff::Legacy.Maliev.Intranet.Bff.Orders.OrdersProxy;
using OrderView = Legacy.Maliev.Intranet.Client.Features.Orders.Pages.OrderDetail;

namespace Legacy.Maliev.Intranet.Tests;

/// <summary>Actual BFF cookies/aggregator and actual routed component with controlled external transports.</summary>
public sealed class OrderInitialStatusHttpTests
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
        using var context = RenderContext(client);
        var page = Render(context);
        page.WaitForAssertion(() => Assert.NotEmpty(page.FindAll(".order-workflow-panel")));
        var history = page.Find(".order-history");
        Assert.Contains(label, history.TextContent, StringComparison.Ordinal);
        Assert.Contains(missingCreatedDate ? "-" : culture == "en" ? "15 Jul 2030, 07:00" : "15 ก.ค. 2573, 07:00", history.TextContent, StringComparison.Ordinal);
        Assert.DoesNotContain("Reviewed", history.TextContent, StringComparison.Ordinal);
        Assert.All(upstream.Requests, item => Assert.StartsWith("GET ", item, StringComparison.Ordinal));
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
        var page = Render(context);
        page.WaitForAssertion(() => Assert.NotEmpty(page.FindAll(".operations-status-pill")));
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
        using (var context = RenderContext(client))
        {
            var page = Render(context);
            page.WaitForAssertion(() => Assert.Equal(label, page.Find(".operations-status-pill").TextContent));
            Assert.Contains(label, page.Find(".order-history").TextContent, StringComparison.Ordinal);
        }
        upstream.HistoryBody = "[]";
        using var emptyContext = RenderContext(client);
        var emptyPage = Render(emptyContext);
        emptyPage.WaitForAssertion(() => Assert.NotEmpty(emptyPage.FindAll(".order-history")));
        Assert.True(string.IsNullOrWhiteSpace(emptyPage.Find(".order-history").TextContent));
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
        var page = Render(context);
        page.WaitForAssertion(() => Assert.Equal("-", page.Find(".operations-status-pill").TextContent));
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

    private static BunitContext RenderContext(HttpClient client)
    {
        var context = new BunitContext();
        context.Services.AddLocalization();
        context.Services.AddSingleton(new HttpClient(new BrowserBoundary(client)) { BaseAddress = new("https://localhost/") });
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

    private sealed class BrowserBoundary(HttpClient client) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => client.GetAsync(request.RequestUri!.PathAndQuery, cancellationToken);
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
