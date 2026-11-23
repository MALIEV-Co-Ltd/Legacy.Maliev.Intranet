using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using Bunit;
using Legacy.Maliev.Intranet.Client.Features.Employees.Pages;
using Legacy.Maliev.Intranet.Contracts;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;

namespace Legacy.Maliev.Intranet.Tests;

public sealed class EmployeeRecoveryLifetimeTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task PendingPreflight_DoesNotSendAfterDisposalOrAddressChange(bool resend, bool changeAddress)
    {
        using var context = new BunitContext();
        context.Services.AddLocalization();
        using var handler = new HeldTransport(holdSession: true);
        using var http = new HttpClient(handler) { BaseAddress = new("https://localhost/") };
        var page = CreatePage(resend, context, http);
        SetEmail(page, resend, "original@example.test");
        var pending = Submit(page);
        try
        {
            await handler.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            if (changeAddress) SetEmail(page, resend, "new@example.test");
            else ((IDisposable)page).Dispose();
            handler.Release.TrySetResult(Session());
            await pending.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(0, handler.Posts);
        }
        finally { if (!handler.Release.Task.IsCompleted) handler.Release.TrySetResult(Session()); await pending.WaitAsync(TimeSpan.FromSeconds(5)); ((IDisposable)page).Dispose(); }
    }

    [Theory]
    [InlineData(false, false, false)]
    [InlineData(false, true, false)]
    [InlineData(true, false, false)]
    [InlineData(true, true, false)]
    [InlineData(false, true, true)]
    [InlineData(true, true, true)]
    public async Task DisposedOrChangedAfterDispatch_ObservesCompletionWithoutLateNotice(bool resend, bool changeAddress, bool unavailable)
    {
        using var context = new BunitContext();
        context.Services.AddLocalization();
        using var handler = new HeldTransport(holdSession: false);
        using var http = new HttpClient(handler) { BaseAddress = new("https://localhost/") };
        var page = CreatePage(resend, context, http);
        SetEmail(page, resend, "original@example.test");
        var pending = Submit(page);
        try
        {
            await handler.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            if (changeAddress) SetEmail(page, resend, "new@example.test");
            else ((IDisposable)page).Dispose();
            handler.Release.TrySetResult(new(unavailable ? HttpStatusCode.ServiceUnavailable : HttpStatusCode.Accepted));
            await pending.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(1, handler.Posts);
            Assert.Equal("original@example.test", handler.PostedEmail);
            Assert.Null(page.GetType().GetField("notice", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(page));
        }
        finally { if (!handler.Release.Task.IsCompleted) handler.Release.TrySetResult(new(HttpStatusCode.ServiceUnavailable)); await pending.WaitAsync(TimeSpan.FromSeconds(5)); ((IDisposable)page).Dispose(); }
    }

    private static object CreatePage(bool resend, BunitContext context, HttpClient http)
    {
        var type = resend ? typeof(EmployeeResendConfirmation) : typeof(EmployeeForgotPassword);
        var page = Activator.CreateInstance(type)!;
        type.GetProperty("Http", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(page, http);
        var localizerType = typeof(IStringLocalizer<>).MakeGenericType(type);
        type.GetProperty("Text", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(page, context.Services.GetRequiredService(localizerType));
        return page;
    }

    private static void SetEmail(object page, bool resend, string value)
    {
        var form = page.GetType().GetField(resend ? "form" : "formModel", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(page)!;
        form.GetType().GetProperty("Email")!.SetValue(form, value);
    }

    private static Task Submit(object page) => (Task)page.GetType().GetMethod("SubmitAsync", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(page, null)!;
    private static HttpResponseMessage Session() => new(HttpStatusCode.OK)
    { Content = JsonContent.Create(new EmployeeSessionSummary(false, null, null, [], "csrf-test")) };

    private sealed class HeldTransport(bool holdSession) : HttpMessageHandler
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<HttpResponseMessage> Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int Posts { get; private set; }
        public string? PostedEmail { get; private set; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.Method == HttpMethod.Get && !holdSession) return Session();
            if (request.Method == HttpMethod.Post)
            {
                Posts++;
                PostedEmail = (await request.Content!.ReadFromJsonAsync<EmployeeRecoveryEmailRequest>(cancellationToken: cancellationToken))!.Email;
            }
            Started.TrySetResult();
            return await Release.Task;
        }
    }
}
