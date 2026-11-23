using System.Net;
using System.Reflection;
using System.Text;
using Bunit;
using Legacy.Maliev.Intranet.Client.Features.Employees.Pages;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;

namespace Legacy.Maliev.Intranet.Tests;

public sealed class EmployeeEmailConfirmationLifetimeTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ChangedLinkOrDisposedDuringPreflightDoesNotSubmit(bool dispose)
    {
        using var context = new BunitContext();
        context.Services.AddLocalization();
        using var handler = new DeferredRecoveryHandler();
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://localhost") };
        var page = CreatePage(context, http);
        var pending = Submit(page);
        try
        {
            await handler.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            if (dispose) page.Dispose();
            else { page.Email = "other@example.invalid"; page.Token = "other-token"; Parameters(page); }
            handler.Release.TrySetResult(Session());
            await pending.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(0, handler.Posts);
        }
        finally { handler.Release.TrySetResult(Session()); await pending.WaitAsync(TimeSpan.FromSeconds(5)); page.Dispose(); }
    }

    [Fact]
    public async Task ChangedLinkAfterDispatchDoesNotCompleteNewLink()
    {
        using var context = new BunitContext();
        context.Services.AddLocalization();
        using var handler = new DeferredRecoveryHandler(holdPost: true);
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://localhost") };
        var page = CreatePage(context, http);
        var pending = Submit(page);
        try
        {
            await handler.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            page.Email = "other@example.invalid"; page.Token = "other-token"; Parameters(page);
            handler.Release.TrySetResult(new HttpResponseMessage(HttpStatusCode.NoContent));
            await pending.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(1, handler.Posts);
            Assert.False((bool)typeof(EmployeeEmailConfirmation).GetField("completed", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(page)!);
        }
        finally { handler.Release.TrySetResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)); await pending.WaitAsync(TimeSpan.FromSeconds(5)); page.Dispose(); }
    }

    private static EmployeeEmailConfirmation CreatePage(BunitContext context, HttpClient http)
    {
        var page = new EmployeeEmailConfirmation { Email = "first@example.invalid", Token = "first-token" };
        typeof(EmployeeEmailConfirmation).GetProperty("Http", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(page, http);
        typeof(EmployeeEmailConfirmation).GetProperty("Text", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(page, context.Services.GetRequiredService<IStringLocalizer<EmployeeEmailConfirmation>>());
        Parameters(page);
        return page;
    }
    private static void Parameters(EmployeeEmailConfirmation page) => typeof(EmployeeEmailConfirmation).GetMethod("OnParametersSet", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(page, null);
    private static Task Submit(EmployeeEmailConfirmation page) => (Task)typeof(EmployeeEmailConfirmation).GetMethod("ConfirmAsync", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(page, null)!;
    private static HttpResponseMessage Session() => new(HttpStatusCode.OK) { Content = new StringContent("{\"isAuthenticated\":false,\"roles\":[],\"csrfToken\":\"synthetic\"}", Encoding.UTF8, "application/json") };

    private sealed class DeferredRecoveryHandler(bool holdPost = false) : HttpMessageHandler
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<HttpResponseMessage> Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int Posts { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.Method == HttpMethod.Post) { Posts++; if (holdPost) { Started.TrySetResult(); return Release.Task; } return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NoContent)); }
            if (holdPost) return Task.FromResult(Session());
            Started.TrySetResult(); return Release.Task;
        }
    }
}
