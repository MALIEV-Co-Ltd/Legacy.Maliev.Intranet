using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using System.Security.Claims;
using Bunit;
using Legacy.Maliev.Intranet.Client;
using Legacy.Maliev.Intranet.Client.Pages;
using Legacy.Maliev.Intranet.Contracts;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.JSInterop;

namespace Legacy.Maliev.Intranet.Tests;

public sealed class LoginAuthenticationChangeTests
{
    [Fact]
    public async Task InitialAuthenticationCompletingAfterDisposalDoesNotNavigate()
    {
        var login = new Login();
        var authentication = new PendingAuthenticationStateProvider();
        var navigation = new RecordingNavigationManager();
        SetProperty(login, "AuthenticationStateProvider", authentication);
        SetProperty(login, "Navigation", navigation);
        SetProperty(login, "Logger", NullLogger<Login>.Instance);
        SetProperty(login, "ThemeService", new LegacyThemeService(new UnusedJsRuntime(), NullLogger<LegacyThemeService>.Instance));
        var initialize = typeof(Login).GetMethod("OnInitializedAsync", BindingFlags.Instance | BindingFlags.NonPublic)!;

        var pending = (Task)initialize.Invoke(login, null)!;
        Assert.False(pending.IsCompleted);
        await login.DisposeAsync();
        authentication.CompleteAuthenticated();
        await pending;

        Assert.Equal(0, navigation.Navigations);
    }

    [Fact]
    public async Task SuccessfulSignInCompletingAfterDisposalDoesNotNavigate()
    {
        using var context = new BunitContext();
        var auth = new NotifyingAuthenticationStateProvider();
        var handler = new DelayedLoginHandler();
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://localhost") };
        context.Services.AddLogging();
        context.Services.AddLocalization();
        context.Services.AddSingleton<AuthenticationStateProvider>(auth);
        context.Services.AddSingleton<LegacyThemeService>();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        var clientType = typeof(Login).Assembly.GetType("Legacy.Maliev.Intranet.Client.EmployeeAuthenticationClient")!;
        var sessionClient = new EmployeeSessionClient(http, NullLogger<EmployeeSessionClient>.Instance);
        context.Services.AddSingleton(clientType, services => Activator.CreateInstance(clientType,
            http, sessionClient, services.GetRequiredService(typeof(ILogger<>).MakeGenericType(clientType)))!);
        var login = context.Render<Login>();
        SetField(login.Instance, "_emailInput", "employee@maliev.com");
        SetField(login.Instance, "_password", "synthetic-password");
        var method = typeof(Login).GetMethod("HandleLogin", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var navigation = context.Services.GetRequiredService<NavigationManager>();
        var originalUri = navigation.Uri;

        var pending = login.InvokeAsync(() => (Task)method.Invoke(login.Instance, null)!);
        await handler.LoginStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await login.Instance.DisposeAsync();
        handler.CompleteSuccessfulLogin();
        await pending;

        Assert.Equal(originalUri, navigation.Uri);
    }

    [Fact]
    public async Task AuthenticatedNotificationDuringFailedSignInNavigatesAfterSignInSettles()
    {
        using var context = new BunitContext();
        var auth = new NotifyingAuthenticationStateProvider();
        var handler = new DelayedLoginHandler();
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://localhost") };
        context.Services.AddLogging();
        context.Services.AddLocalization();
        context.Services.AddSingleton<AuthenticationStateProvider>(auth);
        context.Services.AddSingleton<LegacyThemeService>();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        var clientType = typeof(Login).Assembly.GetType("Legacy.Maliev.Intranet.Client.EmployeeAuthenticationClient")!;
        var sessionClient = new EmployeeSessionClient(http, NullLogger<EmployeeSessionClient>.Instance);
        context.Services.AddSingleton(clientType, services => Activator.CreateInstance(clientType,
            http, sessionClient, services.GetRequiredService(typeof(ILogger<>).MakeGenericType(clientType)))!);
        var login = context.Render<Login>();
        SetField(login.Instance, "_emailInput", "employee@maliev.com");
        SetField(login.Instance, "_password", "synthetic-password");
        var method = typeof(Login).GetMethod("HandleLogin", BindingFlags.Instance | BindingFlags.NonPublic)!;

        var pending = login.InvokeAsync(() => (Task)method.Invoke(login.Instance, null)!);
        await handler.LoginStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await login.InvokeAsync(auth.NotifyAuthenticated);
        login.WaitForAssertion(() => Assert.True((bool)GetField(login.Instance, "_authenticationChangedDuringProcessing")!));
        handler.CompleteLogin();
        await pending;

        Assert.EndsWith("/Dashboard", context.Services.GetRequiredService<NavigationManager>().Uri, StringComparison.Ordinal);
    }

    private static object? GetField(Login login, string name) =>
        typeof(Login).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(login);

    private static void SetField(Login login, string name, object value) =>
        typeof(Login).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(login, value);

    private static void SetProperty(Login login, string name, object value) =>
        typeof(Login).GetProperty(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(login, value);

    private sealed class PendingAuthenticationStateProvider : AuthenticationStateProvider
    {
        private readonly TaskCompletionSource<AuthenticationState> state = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override Task<AuthenticationState> GetAuthenticationStateAsync() => state.Task;
        public void CompleteAuthenticated() => state.SetResult(new AuthenticationState(new ClaimsPrincipal(
            new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "employee")], "test"))));
    }

    private sealed class RecordingNavigationManager : NavigationManager
    {
        public int Navigations { get; private set; }
        public RecordingNavigationManager() => Initialize("https://localhost/", "https://localhost/Login");
        protected override void NavigateToCore(string uri, bool forceLoad) => Navigations++;
    }

    private sealed class UnusedJsRuntime : IJSRuntime
    {
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) => throw new InvalidOperationException();
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args) => throw new InvalidOperationException();
    }

    private sealed class NotifyingAuthenticationStateProvider : AuthenticationStateProvider
    {
        public override Task<AuthenticationState> GetAuthenticationStateAsync() =>
            Task.FromResult(new AuthenticationState(new ClaimsPrincipal(new ClaimsIdentity())));

        public void NotifyAuthenticated() => NotifyAuthenticationStateChanged(Task.FromResult(
            new AuthenticationState(new ClaimsPrincipal(new ClaimsIdentity(
                [new Claim(ClaimTypes.NameIdentifier, "employee")], "test")))));
    }

    private sealed class DelayedLoginHandler : HttpMessageHandler
    {
        private readonly TaskCompletionSource<HttpResponseMessage> login = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource LoginStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public void CompleteLogin() => login.SetResult(new HttpResponseMessage(HttpStatusCode.Unauthorized));
        public void CompleteSuccessfulLogin() => login.SetResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(new EmployeeSignInResponse("/Dashboard")),
        });

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.RequestUri!.AbsolutePath == "/bff/session")
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = JsonContent.Create(new EmployeeSessionSummary(false, null, null, [], "synthetic-csrf")),
                });
            Assert.Equal("/bff/login", request.RequestUri.AbsolutePath);
            LoginStarted.TrySetResult();
            return login.Task;
        }
    }
}
