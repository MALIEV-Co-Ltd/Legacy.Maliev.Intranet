using System.Reflection;
using System.Net;
using Legacy.Maliev.Intranet.Client.Pages;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace Legacy.Maliev.Intranet.Tests;

public sealed class DashboardRefreshLifetimeTests
{
    [Fact]
    public async Task QueuedRefreshRenderAfterDisposalDoesNotRender()
    {
        var dashboard = CreateDashboard(new DeferredRefreshRuntime().Runtime);
        var render = typeof(Dashboard).GetMethod("RenderRefreshIfActive", BindingFlags.Instance | BindingFlags.NonPublic)!;

        await dashboard.DisposeAsync();
        var exception = Record.Exception(() => render.Invoke(dashboard, null));

        Assert.Null(exception);
    }

    [Fact]
    public async Task RefreshResponseAfterDisposalDoesNotNavigateOrRender()
    {
        var handler = new DeferredDashboardHandler();
        using var client = new HttpClient(handler) { BaseAddress = new Uri("https://localhost/") };
        var navigation = new RecordingNavigationManager();
        var dashboard = CreateDashboard(new DeferredRefreshRuntime().Runtime);
        typeof(Dashboard).GetProperty("Http", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(dashboard, client);
        typeof(Dashboard).GetProperty("Navigation", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(dashboard, navigation);
        var refresh = dashboard.RefreshFromBrowserAsync();

        await dashboard.DisposeAsync();
        handler.Complete(new HttpResponseMessage(HttpStatusCode.Unauthorized));
        await refresh;

        Assert.Equal(0, navigation.Navigations);
    }

    [Fact]
    public async Task DisposeBeforeStartCompletes_StopsTheLateTimer()
    {
        var js = new DeferredRefreshRuntime();
        var dashboard = CreateDashboard(js.Runtime);
        var start = StartRefresh(dashboard);

        await dashboard.DisposeAsync();
        js.CompleteStart(41);
        await start;

        Assert.Equal([41], js.StoppedHandles);
    }

    [Fact]
    public async Task DisposeAfterStartCompletes_StopsTheOwnedTimerOnce()
    {
        var js = new DeferredRefreshRuntime();
        var dashboard = CreateDashboard(js.Runtime);
        var start = StartRefresh(dashboard);
        js.CompleteStart(42);
        await start;

        await dashboard.DisposeAsync();
        await dashboard.DisposeAsync();

        Assert.Equal([42], js.StoppedHandles);
    }

    private static Dashboard CreateDashboard(IJSRuntime runtime)
    {
        var dashboard = new Dashboard();
        typeof(Dashboard).GetProperty("JS", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(dashboard, runtime);
        return dashboard;
    }

    private static Task StartRefresh(Dashboard dashboard) =>
        (Task)typeof(Dashboard).GetMethod("OnAfterRenderAsync", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(dashboard, [true])!;

    private sealed class DeferredRefreshRuntime
    {
        private readonly TaskCompletionSource<int> start = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public IJSRuntime Runtime => new Adapter(this);
        public List<int> StoppedHandles { get; } = [];
        public void CompleteStart(int handle) => start.SetResult(handle);

        private ValueTask<TValue> InvokeCore<TValue>(string identifier, object?[]? args)
        {
            if (identifier == "malievDashboardRefresh.start") return new(WaitForStart<TValue>());
            Assert.Equal("malievDashboardRefresh.stop", identifier);
            StoppedHandles.Add(Assert.IsType<int>(args![0]));
            return ValueTask.FromResult(default(TValue)!);
        }

        private async Task<TValue> WaitForStart<TValue>() => (TValue)(object)await start.Task;

        private sealed class Adapter(DeferredRefreshRuntime owner) : IJSRuntime
        {
            public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) => owner.InvokeCore<TValue>(identifier, args);
            public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args) => owner.InvokeCore<TValue>(identifier, args);
        }
    }

    private sealed class DeferredDashboardHandler : HttpMessageHandler
    {
        private readonly TaskCompletionSource<HttpResponseMessage> response = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public void Complete(HttpResponseMessage value) => response.SetResult(value);
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => response.Task;
    }

    private sealed class RecordingNavigationManager : NavigationManager
    {
        public int Navigations { get; private set; }
        public RecordingNavigationManager() => Initialize("https://localhost/", "https://localhost/");
        protected override void NavigateToCore(string uri, bool forceLoad) => Navigations++;
    }
}
