using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using Bunit;
using Legacy.Maliev.Intranet.Client.Features.Customers.Pages;
using Legacy.Maliev.Intranet.Contracts;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;

namespace Legacy.Maliev.Intranet.Tests;

public sealed class CustomerInternalRemarkLifetimeTests
{
    [Fact]
    public async Task SupersededUnauthorizedRemarkReadCannotRedirect()
    {
        using var context = new BunitContext();
        context.Services.AddLocalization();
        using var handler = new HeldHandler(holdSession: true);
        using var client = new HttpClient(handler) { BaseAddress = new Uri("https://localhost") };
        var view = Create(context, client);
        var navigation = context.Services.GetRequiredService<NavigationManager>();
        var original = navigation.Uri;
        var pending = (Task)typeof(CustomerView).GetMethod("LoadInternalRemarkAsync", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(view, null)!;
        try
        {
            await handler.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            view.Id = 43;
            handler.Release.TrySetResult();
            await pending.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(original, navigation.Uri);
        }
        finally { handler.Release.TrySetResult(); await pending.WaitAsync(TimeSpan.FromSeconds(5)); view.Dispose(); }
    }

    [Fact]
    public async Task SupersededUnauthorizedPreflightCannotRedirectOrWrite()
    {
        using var context = new BunitContext();
        context.Services.AddLocalization();
        using var handler = new HeldHandler(holdSession: true);
        using var client = new HttpClient(handler) { BaseAddress = new Uri("https://localhost") };
        var view = Create(context, client);
        var navigation = context.Services.GetRequiredService<NavigationManager>();
        var original = navigation.Uri;
        var pending = Save(view);
        try
        {
            await handler.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            view.Id = 43;
            handler.Release.TrySetResult();
            await pending.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(0, handler.Writes);
            Assert.Equal(original, navigation.Uri);
        }
        finally { handler.Release.TrySetResult(); await pending.WaitAsync(TimeSpan.FromSeconds(5)); view.Dispose(); }
    }

    [Fact]
    public async Task ChangedInputCannotAlterDispatchedBodyOrPermitRetryAfterUnknownOutcome()
    {
        using var context = new BunitContext();
        context.Services.AddLocalization();
        using var handler = new HeldHandler(holdSession: false);
        using var client = new HttpClient(handler) { BaseAddress = new Uri("https://localhost") };
        var view = Create(context, client);
        var model = (CustomerInternalRemarkUpdateRequest)Get(view, "internalRemarkEditModel")!;
        model.InternalRemark = "original";
        var pending = Save(view);
        try
        {
            await handler.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            model.InternalRemark = "changed";
            handler.Release.TrySetResult();
            await pending.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Contains("original", handler.Body, StringComparison.Ordinal);
            Assert.DoesNotContain("changed", handler.Body, StringComparison.Ordinal);
            await Save(view);
            Assert.Equal(1, handler.Writes);
        }
        finally { handler.Release.TrySetResult(); await pending.WaitAsync(TimeSpan.FromSeconds(5)); view.Dispose(); }
    }

    [Fact]
    public async Task UncertainWriteRemainsFencedWhenAReadPrecedesItsLateCommit()
    {
        using var context = new BunitContext();
        context.Services.AddLocalization();
        using var handler = new LateCommitHandler();
        using var client = new HttpClient(handler) { BaseAddress = new Uri("https://localhost") };
        var view = Create(context, client);
        var model = (CustomerInternalRemarkUpdateRequest)Get(view, "internalRemarkEditModel")!;
        model.InternalRemark = "new value";
        try
        {
            await Save(view);
            Assert.Equal(1, handler.Writes);
            await LoadRemark(view);
            Assert.Equal("before", handler.LastRead);
            Set(view, "internalRemarkEditing", true);
            await Save(view);
            Assert.Equal(1, handler.Writes);
            handler.Commit();
            await LoadRemark(view);
            Assert.Equal("new value", handler.LastRead);
            Set(view, "internalRemarkEditing", true);
            await Save(view);
            Assert.Equal(1, handler.Writes);
        }
        finally { view.Dispose(); }
    }

    private static CustomerView Create(BunitContext context, HttpClient client)
    {
        var view = new CustomerView { Id = 42 };
        SetProperty(view, "Http", client);
        SetProperty(view, "Navigation", context.Services.GetRequiredService<NavigationManager>());
        SetProperty(view, "Text", context.Services.GetRequiredService<IStringLocalizer<CustomerView>>());
        SetProperty(view, "AuthenticationStateProvider", new EmptyAuthenticationStateProvider());
        var model = (CustomerInternalRemarkUpdateRequest)Get(view, "internalRemarkEditModel")!;
        Set(view, "internalRemarkEditContext", new EditContext(model));
        Set(view, "internalRemarkEditing", true);
        Set(view, "canEdit", true);
        Set(view, "appliedAuthenticationRevision", 0L);
        Set(view, "customerRevision", "revision");
        return view;
    }

    private static object? Get(CustomerView view, string field) => typeof(CustomerView).GetField(field, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(view);
    private static void Set(CustomerView view, string field, object value) => typeof(CustomerView).GetField(field, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(view, value);
    private static void SetProperty(CustomerView view, string name, object value) => typeof(CustomerView).GetProperty(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(view, value);
    private static Task Save(CustomerView view) => (Task)typeof(CustomerView).GetMethod("SaveInternalRemarkAsync", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(view, null)!;
    private static Task LoadRemark(CustomerView view) => (Task)typeof(CustomerView).GetMethod("LoadInternalRemarkAsync", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(view, null)!;

    private sealed class EmptyAuthenticationStateProvider : AuthenticationStateProvider
    {
        public override Task<AuthenticationState> GetAuthenticationStateAsync() =>
            Task.FromResult(new AuthenticationState(new System.Security.Claims.ClaimsPrincipal()));
    }

    private sealed class HeldHandler(bool holdSession) : HttpMessageHandler
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int Writes { get; private set; }
        public string Body { get; private set; } = string.Empty;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.Method == HttpMethod.Get)
            {
                if (holdSession) { Started.TrySetResult(); await Release.Task; return new(HttpStatusCode.Unauthorized); }
                return new(HttpStatusCode.OK) { Content = JsonContent.Create(new EmployeeSessionSummary(true, "employee", "Synthetic", ["Employee"], "synthetic-csrf", 7, [])) };
            }
            Writes++;
            Started.TrySetResult();
            await Release.Task;
            Body = await request.Content!.ReadAsStringAsync(cancellationToken);
            return new(HttpStatusCode.ServiceUnavailable);
        }
    }

    private sealed class LateCommitHandler : HttpMessageHandler
    {
        private string value = "before";
        private string? pending;
        public string? LastRead { get; private set; }
        public int Writes { get; private set; }
        public void Commit() => value = pending!;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.RequestUri!.AbsolutePath == "/bff/session")
                return new(HttpStatusCode.OK) { Content = JsonContent.Create(new EmployeeSessionSummary(true, "employee", "Synthetic", ["Employee"], "synthetic-csrf", 7, [])) };
            if (request.Method == HttpMethod.Get)
            {
                LastRead = value;
                return new(HttpStatusCode.OK) { Content = JsonContent.Create(new CustomerInternalRemarkResponse(42, value)) };
            }
            Writes++;
            pending = (await request.Content!.ReadFromJsonAsync<CustomerInternalRemarkUpdateRequest>(cancellationToken: cancellationToken))!.InternalRemark;
            return new(HttpStatusCode.ServiceUnavailable);
        }
    }
}
