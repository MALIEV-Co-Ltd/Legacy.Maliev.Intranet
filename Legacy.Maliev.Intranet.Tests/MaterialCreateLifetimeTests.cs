using System.Net;
using System.Reflection;
using System.Text;
using Bunit;
using Legacy.Maliev.Intranet.Client.Features.Catalog.Pages;
using Legacy.Maliev.Intranet.Contracts;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging.Abstractions;

namespace Legacy.Maliev.Intranet.Tests;

public sealed class MaterialCreateLifetimeTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DisposedDuringPreflightDoesNotCreateOrRedirect(bool unauthorized)
    {
        using var context = new BunitContext();
        context.Services.AddLocalization();
        using var handler = new CreateHandler(holdSession: true);
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://localhost") };
        var page = CreatePage(context, http);
        var navigation = context.Services.GetRequiredService<NavigationManager>();
        var uri = navigation.Uri;
        var pending = Submit(page);
        try
        {
            await handler.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            page.Dispose();
            handler.Release.TrySetResult(unauthorized ? new HttpResponseMessage(HttpStatusCode.Unauthorized) : Session());
            await pending.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(0, handler.Writes);
            Assert.Equal(uri, navigation.Uri);
        }
        finally { handler.Release.TrySetResult(Session()); await pending.WaitAsync(TimeSpan.FromSeconds(5)); }
    }

    [Fact]
    public async Task DisposedAfterDispatchDoesNotNavigateOrRepeat()
    {
        using var context = new BunitContext();
        context.Services.AddLocalization();
        using var handler = new CreateHandler(holdSession: false);
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://localhost") };
        var page = CreatePage(context, http);
        var navigation = context.Services.GetRequiredService<NavigationManager>();
        var uri = navigation.Uri;
        var pending = Submit(page);
        try
        {
            await handler.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            page.Dispose();
            handler.Release.TrySetResult(Json("{\"id\":42}"));
            await pending.WaitAsync(TimeSpan.FromSeconds(5));
            await Submit(page);
            Assert.Equal(1, handler.Writes);
            Assert.Equal(uri, navigation.Uri);
        }
        finally { handler.Release.TrySetResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)); await pending.WaitAsync(TimeSpan.FromSeconds(5)); }
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.Conflict)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.BadGateway)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    public async Task AmbiguousResponseFencesAnotherCreate(HttpStatusCode status)
    {
        using var context = new BunitContext();
        context.Services.AddLocalization();
        using var handler = new CreateHandler(holdSession: false);
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://localhost") };
        var page = CreatePage(context, http);
        var pending = Submit(page);
        try
        {
            await handler.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            handler.Release.TrySetResult(new HttpResponseMessage(status));
            await pending.WaitAsync(TimeSpan.FromSeconds(5));
            await Submit(page);
            Assert.Equal(1, handler.Writes);
            Assert.True((bool)Field(page, "createOutcomeUnknown")!);
        }
        finally { handler.Release.TrySetResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)); await pending.WaitAsync(TimeSpan.FromSeconds(5)); page.Dispose(); }
    }

    [Fact]
    public async Task UnexpectedPostDispatchExceptionFencesRetry()
    {
        using var context = new BunitContext();
        context.Services.AddLocalization();
        using var handler = new CreateHandler(holdSession: false);
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://localhost") };
        var page = CreatePage(context, http);
        var pending = Submit(page);
        try
        {
            await handler.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            handler.Release.TrySetException(new InvalidOperationException("synthetic failure"));
            await pending.WaitAsync(TimeSpan.FromSeconds(5));
            await Submit(page);
            Assert.Equal(1, handler.Writes);
            Assert.True((bool)Field(page, "createOutcomeUnknown")!);
        }
        finally { page.Dispose(); }
    }

    [Fact]
    public async Task DisposedPostDispatchExceptionIsObservedWithoutLateStateOrReplay()
    {
        using var context = new BunitContext();
        context.Services.AddLocalization();
        using var handler = new CreateHandler(holdSession: false);
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://localhost") };
        var page = CreatePage(context, http);
        var pending = Submit(page);
        try
        {
            await handler.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            page.Dispose();
            handler.Release.TrySetException(new InvalidOperationException("synthetic failure"));
            await pending.WaitAsync(TimeSpan.FromSeconds(5));
            await Submit(page);
            Assert.Equal(1, handler.Writes);
            Assert.Null(Field(page, "error"));
        }
        finally { page.Dispose(); }
    }

    private static MaterialCreate CreatePage(BunitContext context, HttpClient http)
    {
        var page = new MaterialCreate();
        Set(page, "Http", http);
        Set(page, "Text", context.Services.GetRequiredService<IStringLocalizer<MaterialCreate>>());
        Set(page, "Navigation", context.Services.GetRequiredService<NavigationManager>());
        Set(page, "Logger", NullLogger<MaterialCreate>.Instance);
        typeof(MaterialCreate).GetField("editContext", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(page, new EditContext(Field(page, "model")!));
        return page;
    }
    private static object? Field(MaterialCreate page, string name) => typeof(MaterialCreate).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(page);
    private static void Set(MaterialCreate page, string name, object value) => typeof(MaterialCreate).GetProperty(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(page, value);
    private static Task Submit(MaterialCreate page) => (Task)typeof(MaterialCreate).GetMethod("SubmitAsync", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(page, null)!;
    private static HttpResponseMessage Json(string body) => new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
    private static HttpResponseMessage Session() => Json("{\"isAuthenticated\":true,\"roles\":[],\"csrfToken\":\"synthetic\"}");

    private sealed class CreateHandler(bool holdSession) : HttpMessageHandler
    {
        public int Writes { get; private set; }
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<HttpResponseMessage> Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.Method == HttpMethod.Get)
            {
                if (!holdSession) return Task.FromResult(Session());
                Started.TrySetResult();
                return Release.Task;
            }
            Writes++;
            Started.TrySetResult();
            return Release.Task;
        }
    }
}
