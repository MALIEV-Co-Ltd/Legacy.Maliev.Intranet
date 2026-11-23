using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using System.Security.Claims;
using Bunit;
using Legacy.Maliev.Intranet.Client.Features.Employees.Pages;
using Legacy.Maliev.Intranet.Contracts;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;

namespace Legacy.Maliev.Intranet.Tests;

public sealed class EmployeeProfileValidationRaceTests
{
    [Fact]
    public async Task RefreshWaitsForNotifiedAuthenticationWhenProviderGetterLags()
    {
        using var context = new BunitContext();
        var provider = new LaggingAuthenticationProvider();
        var handler = new SessionHandler("employee-a");
        context.Services.AddLocalization();
        context.Services.AddSingleton<AuthenticationStateProvider>(provider);
        context.Services.AddSingleton(new HttpClient(handler) { BaseAddress = new Uri("https://localhost") });
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        var cut = context.Render<EmployeeProfile>();
        cut.WaitForAssertion(() => Assert.Equal(1, handler.ProfileReads));

        var next = new TaskCompletionSource<AuthenticationState>(TaskCreationOptions.RunContinuationsAsynchronously);
        await cut.InvokeAsync(() => provider.Notify(next.Task));
        Task? pending = null;
        await cut.InvokeAsync(() =>
        {
            pending = Assert.IsAssignableFrom<Task>(
                typeof(EmployeeProfile).GetMethod("LoadAsync", BindingFlags.Instance | BindingFlags.NonPublic,
                    binder: null, types: Type.EmptyTypes, modifiers: null)!.Invoke(cut.Instance, null));
        });
        Assert.NotNull(pending);
        Assert.False(pending.IsCompleted);
        Assert.Equal(1, handler.ProfileReads);

        handler.EmployeeId = "employee-b";
        next.SetResult(LaggingAuthenticationProvider.State("employee-b"));
        await pending;
        Assert.Equal(2, handler.ProfileReads);
        Assert.Equal("employee-b", Field(cut.Instance, "profileEmployeeIdentity").GetValue(cut.Instance));
    }

    [Fact]
    public void ProfileReadForDifferentSessionIsNotDisplayed()
    {
        using var context = new BunitContext();
        var handler = new SessionHandler("employee-a") { ProfileId = 8 };
        context.Services.AddLocalization();
        context.Services.AddSingleton<AuthenticationStateProvider>(new LaggingAuthenticationProvider());
        context.Services.AddSingleton(new HttpClient(handler) { BaseAddress = new Uri("https://localhost") });
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        var cut = context.Render<EmployeeProfile>();

        var expected = context.Services.GetRequiredService<IStringLocalizer<EmployeeProfile>>()["SessionUnavailable"].Value;
        cut.WaitForAssertion(() => Assert.Equal(expected, Field(cut.Instance, "error").GetValue(cut.Instance)));
        Assert.Null(Field(cut.Instance, "profile").GetValue(cut.Instance));
        Assert.Equal(0, handler.Writes);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ChangedModelOrAuthenticationDuringValidationDoesNotWrite(bool changeAuthentication)
    {
        var handler = new SessionHandler("employee-a");
        var page = CreatePage(handler);
        var model = Field(page, "model").GetValue(page)!;
        var context = new EditContext(model);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        context.OnValidationRequested += (_, args) => args.AddAsyncValidator(_ => release.Task);
        Field(page, "editContext").SetValue(page, context);

        var pending = InvokeSave(page);
        Assert.False(pending.IsCompleted);
        if (changeAuthentication) Field(page, "authenticationRevision").SetValue(page, 1L);
        else model.GetType().GetProperty("FirstName")!.SetValue(model, "Changed");
        release.SetResult();
        await pending;

        Assert.Equal(0, handler.Requests);
        Assert.Equal(0, handler.Writes);
    }

    [Fact]
    public async Task SessionForDifferentEmployeeDoesNotWriteProfile()
    {
        var handler = new SessionHandler("employee-b");
        var page = CreatePage(handler);
        var model = Field(page, "model").GetValue(page)!;
        Field(page, "editContext").SetValue(page, new EditContext(model));

        await InvokeSave(page);

        Assert.Equal(1, handler.Requests);
        Assert.Equal(0, handler.Writes);
        Assert.Equal("SessionUnavailable", Field(page, "error").GetValue(page));
    }

    [Theory]
    [InlineData(502)]
    [InlineData(503)]
    [InlineData(200)]
    [InlineData(0)]
    public async Task UnconfirmedWriteRequiresReloadBeforeAnotherSave(int status)
    {
        var handler = new SessionHandler("employee-a") { WriteStatus = status };
        var page = CreatePage(handler);
        var model = Field(page, "model").GetValue(page)!;
        Field(page, "editContext").SetValue(page, new EditContext(model));

        await InvokeSave(page);
        await InvokeSave(page);

        Assert.Equal(1, handler.Writes);
        Assert.Null(Field(page, "profile").GetValue(page));
        Assert.Equal("SaveOutcomeUnknown", Field(page, "error").GetValue(page));
    }

    private static EmployeeProfile CreatePage(SessionHandler handler)
    {
        var page = new EmployeeProfile();
        typeof(EmployeeProfile).GetProperty("Http", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(page, new HttpClient(handler) { BaseAddress = new Uri("https://localhost") });
        typeof(EmployeeProfile).GetProperty("Text", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(page, new KeyLocalizer<EmployeeProfile>());
        Field(page, "profile").SetValue(page, new EmployeeDetail(7, null, "First", "Last", "First Last", null,
            "employee@example.invalid", null, null, null, null, null, null));
        Field(page, "profileAuthenticationRevision").SetValue(page, 0L);
        Field(page, "profileEmployeeIdentity").SetValue(page, "employee-a");
        var model = Field(page, "model").GetValue(page)!;
        model.GetType().GetProperty("FirstName")!.SetValue(model, "First");
        model.GetType().GetProperty("LastName")!.SetValue(model, "Last");
        return page;
    }

    private static FieldInfo Field(EmployeeProfile page, string name) =>
        page.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!;

    private static Task InvokeSave(EmployeeProfile page) => Assert.IsAssignableFrom<Task>(
        typeof(EmployeeProfile).GetMethod("SaveAsync", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(page, null));

    private sealed class SessionHandler(string employeeId) : HttpMessageHandler
    {
        public string EmployeeId { get; set; } = employeeId;
        public int ProfileId { get; set; } = 7;
        public int WriteStatus { get; set; } = 204;
        public int Requests { get; private set; }
        public int Writes { get; private set; }
        public int ProfileReads { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests++;
            if (request.Method != HttpMethod.Get)
            {
                Writes++;
                if (WriteStatus == 0) throw new HttpRequestException("synthetic transport failure");
                return Task.FromResult(new HttpResponseMessage((HttpStatusCode)WriteStatus));
            }
            if (request.RequestUri?.AbsolutePath == "/bff/profile")
            {
                ProfileReads++;
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = JsonContent.Create(new EmployeeDetail(ProfileId, null, "First", "Last", "First Last", null,
                        "employee@example.invalid", null, null, null, null, null, null)),
                });
            }
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new EmployeeSessionSummary(true, EmployeeId, null, [], "csrf", 7)),
            });
        }
    }

    private sealed class LaggingAuthenticationProvider : AuthenticationStateProvider
    {
        public override Task<AuthenticationState> GetAuthenticationStateAsync() => Task.FromResult(State("employee-a"));
        public void Notify(Task<AuthenticationState> state) => NotifyAuthenticationStateChanged(state);
        public static AuthenticationState State(string identity) => new(new ClaimsPrincipal(
            new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, identity)], "test")));
    }

    private sealed class KeyLocalizer<T> : IStringLocalizer<T>
    {
        public LocalizedString this[string name] => new(name, name);
        public LocalizedString this[string name, params object[] arguments] => new(name, name);
        public IEnumerable<LocalizedString> GetAllStrings(bool includeParentCultures) => [];
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
    }
}
