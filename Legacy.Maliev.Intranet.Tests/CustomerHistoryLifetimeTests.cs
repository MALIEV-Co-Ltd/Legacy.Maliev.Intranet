using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using System.Security.Claims;
using Bunit;
using Legacy.Maliev.Intranet.Client.Features.Customers.Pages;
using Legacy.Maliev.Intranet.Contracts;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;

namespace Legacy.Maliev.Intranet.Tests;

public sealed class CustomerHistoryLifetimeTests
{
    [Theory]
    [InlineData("LoadActivityAsync", null)]
    [InlineData("LoadOrdersPageAsync", 1)]
    [InlineData("LoadQuotationsPageAsync", 1)]
    [InlineData("LoadInvoicesPageAsync", 1)]
    public async Task RetiredCustomerHistoryCannotRedirectOnLateUnauthorized(string method, int? pageIndex)
    {
        using var context = new BunitContext();
        context.Services.AddLocalization();
        using var handler = new DelayedHandler();
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://localhost") };
        var page = new CustomerView { Id = 42 };
        typeof(CustomerView).GetField("appliedAuthenticationRevision", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(page, 0L);
        SetProperty(page, "Http", http);
        SetProperty(page, "Text", context.Services.GetRequiredService<IStringLocalizer<CustomerView>>());
        var navigation = context.Services.GetRequiredService<NavigationManager>();
        SetProperty(page, "Navigation", navigation);
        foreach (var field in new[] { "canReadOrders", "canReadQuotations", "canReadInvoices" })
            typeof(CustomerView).GetField(field, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(page, true);
        var original = navigation.Uri;
        var pending = (Task)typeof(CustomerView).GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(page, pageIndex is null ? null : [pageIndex.Value])!;
        try
        {
            await handler.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            typeof(CustomerView).GetMethod("ResetHistoryState", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(page, null);
            page.Id = 43;
            handler.Release.TrySetResult(new HttpResponseMessage(HttpStatusCode.Unauthorized));
            await pending.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(original, navigation.Uri);
        }
        finally
        {
            handler.Release.TrySetResult(new HttpResponseMessage(HttpStatusCode.Unauthorized));
            await pending.WaitAsync(TimeSpan.FromSeconds(5));
        }
    }

    [Fact]
    public async Task PreviousAuthenticationHistoryCannotRedirectOnLateUnauthorized()
    {
        using var context = new BunitContext();
        context.Services.AddLocalization();
        using var handler = new DelayedHandler();
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://localhost") };
        var page = new CustomerView { Id = 42 };
        typeof(CustomerView).GetField("appliedAuthenticationRevision", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(page, 0L);
        SetProperty(page, "Http", http);
        SetProperty(page, "Text", context.Services.GetRequiredService<IStringLocalizer<CustomerView>>());
        var navigation = context.Services.GetRequiredService<NavigationManager>();
        SetProperty(page, "Navigation", navigation);
        var original = navigation.Uri;
        var pending = (Task)typeof(CustomerView).GetMethod("LoadActivityAsync", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(page, null)!;
        try
        {
            await handler.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            typeof(CustomerView).GetField("authenticationRevision", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(page, 1L);
            typeof(CustomerView).GetField("appliedAuthenticationRevision", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(page, 1L);
            handler.Release.TrySetResult(new HttpResponseMessage(HttpStatusCode.Unauthorized));
            await pending.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(original, navigation.Uri);
        }
        finally
        {
            handler.Release.TrySetResult(new HttpResponseMessage(HttpStatusCode.Unauthorized));
            await pending.WaitAsync(TimeSpan.FromSeconds(5));
        }
    }

    [Fact]
    public async Task SupersededAuthenticationCannotApplyPermissionsOrStartHistory()
    {
        using var context = new BunitContext();
        context.Services.AddLocalization();
        using var handler = new DelayedHandler();
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://localhost") };
        var page = new CustomerView { Id = 42 };
        SetProperty(page, "Http", http);
        SetProperty(page, "Text", context.Services.GetRequiredService<IStringLocalizer<CustomerView>>());
        typeof(CustomerView).GetField("selectedTab", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(page, "orders");
        var principal = new ClaimsPrincipal(new ClaimsIdentity([new Claim("permissions", "legacy.orders.read")], "test"));
        var heldState = new TaskCompletionSource<AuthenticationState>(TaskCreationOptions.RunContinuationsAsynchronously);
        typeof(CustomerView).GetField("latestAuthenticationState", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(page, heldState.Task);
        var resolution = (Task<(AuthenticationState State, long Revision)>)typeof(CustomerView)
            .GetMethod("GetLatestAuthenticationStateAsync", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(page, null)!;
        Assert.False(resolution.IsCompleted);
        heldState.TrySetResult(new AuthenticationState(principal));
        var prior = await resolution.WaitAsync(TimeSpan.FromSeconds(5));
        typeof(CustomerView).GetField("authenticationRevision", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(page, 1L);
        var pending = (Task)typeof(CustomerView).GetMethod("ApplyHistoryAuthenticationAsync", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(page, [prior.State, prior.Revision])!;
        await pending.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.False(handler.Started.Task.IsCompleted);
        Assert.False((bool)typeof(CustomerView).GetField("canReadOrders", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(page)!);
    }

    [Fact]
    public async Task InitialLoadWaitsForNewerAuthenticationBeforeDispatch()
    {
        using var context = new BunitContext();
        context.Services.AddLocalization();
        using var handler = new DelayedHandler();
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://localhost") };
        var page = new CustomerView { Id = 42 };
        SetProperty(page, "Http", http);
        SetProperty(page, "Text", context.Services.GetRequiredService<IStringLocalizer<CustomerView>>());
        SetProperty(page, "Navigation", context.Services.GetRequiredService<NavigationManager>());
        var oldState = new TaskCompletionSource<AuthenticationState>(TaskCreationOptions.RunContinuationsAsynchronously);
        var newState = new TaskCompletionSource<AuthenticationState>(TaskCreationOptions.RunContinuationsAsynchronously);
        var stateField = typeof(CustomerView).GetField("latestAuthenticationState", BindingFlags.Instance | BindingFlags.NonPublic)!;
        stateField.SetValue(page, oldState.Task);
        var pending = (Task)typeof(CustomerView).GetMethod("LoadAsync", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(page, null)!;
        try
        {
            stateField.SetValue(page, newState.Task);
            typeof(CustomerView).GetField("authenticationRevision", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(page, 1L);
            oldState.TrySetResult(new AuthenticationState(new ClaimsPrincipal(new ClaimsIdentity([new Claim("permissions", "legacy.orders.read")], "old"))));
            await Task.WhenAny(handler.Started.Task, pending, Task.Delay(100));
            Assert.False(handler.Started.Task.IsCompleted);
            Assert.False(pending.IsCompleted);
            newState.TrySetResult(new AuthenticationState(new ClaimsPrincipal(new ClaimsIdentity([], "new"))));
            await handler.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.False((bool)typeof(CustomerView).GetField("canReadOrders", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(page)!);
            handler.Release.TrySetResult(new HttpResponseMessage(HttpStatusCode.NotFound));
            await pending.WaitAsync(TimeSpan.FromSeconds(5));
        }
        finally
        {
            oldState.TrySetResult(new AuthenticationState(new ClaimsPrincipal()));
            newState.TrySetResult(new AuthenticationState(new ClaimsPrincipal()));
            handler.Release.TrySetResult(new HttpResponseMessage(HttpStatusCode.NotFound));
            await pending.WaitAsync(TimeSpan.FromSeconds(5));
        }
    }

    [Theory]
    [InlineData("LoadActivityAsync", null)]
    [InlineData("LoadOrdersPageAsync", 2)]
    [InlineData("LoadQuotationsPageAsync", 2)]
    [InlineData("LoadInvoicesPageAsync", 2)]
    public async Task DirectHistoryDispatchRejectsSupersededAuthentication(string method, int? pageIndex)
    {
        using var context = new BunitContext();
        context.Services.AddLocalization();
        using var handler = new DelayedHandler();
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://localhost") };
        var page = new CustomerView { Id = 42 };
        SetProperty(page, "Http", http);
        SetProperty(page, "Text", context.Services.GetRequiredService<IStringLocalizer<CustomerView>>());
        foreach (var field in new[] { "canReadOrders", "canReadQuotations", "canReadInvoices" })
            typeof(CustomerView).GetField(field, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(page, true);
        typeof(CustomerView).GetField("appliedAuthenticationRevision", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(page, 0L);
        typeof(CustomerView).GetField("authenticationRevision", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(page, 1L);
        var pending = (Task)typeof(CustomerView).GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(page, pageIndex is null ? null : [pageIndex.Value])!;
        await pending.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.False(handler.Started.Task.IsCompleted);
    }

    [Fact]
    public async Task TabNavigationRejectsSupersededAuthentication()
    {
        using var context = new BunitContext();
        var page = new CustomerView { Id = 42 };
        var navigation = context.Services.GetRequiredService<NavigationManager>();
        SetProperty(page, "Navigation", navigation);
        typeof(CustomerView).GetField("canReadOrders", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(page, true);
        typeof(CustomerView).GetField("appliedAuthenticationRevision", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(page, 0L);
        typeof(CustomerView).GetField("authenticationRevision", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(page, 1L);
        var original = navigation.Uri;
        var pending = (Task)typeof(CustomerView).GetMethod("OnActiveTabChangedAsync", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(page, ["orders"])!;
        await pending.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(original, navigation.Uri);
    }

    [Fact]
    public async Task RoutedPermissionRevocationReturnsDisallowedTabToOverview()
    {
        using var context = new BunitContext();
        context.Services.AddLocalization();
        context.Services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        var provider = new ChangingAuthenticationStateProvider();
        context.Services.AddSingleton<AuthenticationStateProvider>(provider);
        using var handler = new CustomerRouteHandler();
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://localhost") };
        context.Services.AddSingleton(http);
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        var navigation = context.Services.GetRequiredService<NavigationManager>();
        navigation.NavigateTo("/Customers/View?id=42&tab=orders");
        var router = context.Render<Router>(parameters => parameters
            .Add(router => router.AppAssembly, typeof(CustomerView).Assembly)
            .Add(router => router.Found, (RenderFragment<RouteData>)(route => builder =>
            {
                builder.OpenComponent<RouteView>(0);
                builder.AddAttribute(1, nameof(RouteView.RouteData), route);
                builder.CloseComponent();
            })));
        router.WaitForAssertion(() => Assert.Contains("tab=orders", navigation.Uri, StringComparison.OrdinalIgnoreCase));
        router.WaitForAssertion(() => Assert.True(handler.OrdersStarted));
        await router.InvokeAsync(provider.RevokeOrders);
        router.WaitForAssertion(() => Assert.Contains("tab=overview", navigation.Uri, StringComparison.OrdinalIgnoreCase));
        router.WaitForAssertion(() => Assert.DoesNotContain("tab=orders", navigation.Uri, StringComparison.OrdinalIgnoreCase));
        router.WaitForAssertion(() =>
        {
            var panel = router.Find(".customer-overview").Closest("[role='tabpanel']");
            Assert.NotNull(panel);
            Assert.Equal("active", panel.GetAttribute("data-state"));
            Assert.False(panel.HasAttribute("hidden"));
            Assert.False(panel.HasAttribute("inert"));
        });
    }

    private static void SetProperty(CustomerView page, string name, object value) =>
        typeof(CustomerView).GetProperty(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(page, value);

    private sealed class DelayedHandler : HttpMessageHandler
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<HttpResponseMessage> Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Started.TrySetResult();
            return Release.Task;
        }
    }

    private sealed class ChangingAuthenticationStateProvider : AuthenticationStateProvider
    {
        private AuthenticationState current = State(true);
        public override Task<AuthenticationState> GetAuthenticationStateAsync() => Task.FromResult(current);
        public void RevokeOrders()
        {
            current = State(false);
            NotifyAuthenticationStateChanged(Task.FromResult(current));
        }
        private static AuthenticationState State(bool orders) => new(new ClaimsPrincipal(new ClaimsIdentity(
            orders ? [new Claim("permissions", "legacy-customer.customers.read"), new Claim("permissions", "legacy.orders.read")]
                   : [new Claim("permissions", "legacy-customer.customers.read")], "test")));
    }

    private sealed class CustomerRouteHandler : HttpMessageHandler
    {
        public bool OrdersStarted { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.RequestUri!.AbsolutePath.EndsWith("/orders", StringComparison.Ordinal))
            {
                OrdersStarted = true;
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
            }
            if (request.RequestUri.AbsolutePath.EndsWith("/internal-remark", StringComparison.Ordinal))
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(new CustomerInternalRemarkResponse(42, null)) });
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new CustomerDetail(42, "Synthetic", "Customer", "Synthetic Customer", null, null, null,
                    "synthetic@example.invalid", null, null, null, null, DateTime.UtcNow, null, null, null, null)),
            };
            response.Headers.ETag = new("\"00000001\"");
            return Task.FromResult(response);
        }
    }
}
