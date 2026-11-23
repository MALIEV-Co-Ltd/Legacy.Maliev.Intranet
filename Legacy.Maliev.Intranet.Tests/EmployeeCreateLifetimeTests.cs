using System.Net;
using System.Globalization;
using System.Reflection;
using System.Text;
using Bunit;
using Legacy.Maliev.Intranet.Client.Features.Employees.Pages;
using Legacy.Maliev.Intranet.Contracts;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging.Abstractions;

namespace Legacy.Maliev.Intranet.Tests;

public sealed class EmployeeCreateLifetimeTests
{
    [Theory]
    [InlineData("en", HttpStatusCode.Unauthorized, "The request reported that your session is not authorized.")]
    [InlineData("en", HttpStatusCode.Forbidden, "The request reported insufficient permission.")]
    [InlineData("en", HttpStatusCode.BadRequest, "The service reported invalid employee details.")]
    [InlineData("en", HttpStatusCode.Conflict, "The service reported an account conflict.")]
    [InlineData("en", HttpStatusCode.TooManyRequests, "The service reported too many requests.")]
    [InlineData("th", HttpStatusCode.Unauthorized, "ระบบแจ้งว่าเซสชันของคุณไม่ได้รับอนุญาต")]
    [InlineData("th", HttpStatusCode.Forbidden, "ระบบแจ้งว่าคุณไม่มีสิทธิ์เพียงพอ")]
    [InlineData("th", HttpStatusCode.BadRequest, "ระบบแจ้งว่าข้อมูลพนักงานไม่ถูกต้อง")]
    [InlineData("th", HttpStatusCode.Conflict, "ระบบแจ้งว่าข้อมูลบัญชีขัดแย้งกัน")]
    [InlineData("th", HttpStatusCode.TooManyRequests, "ระบบแจ้งว่ามีคำขอมากเกินไป")]
    public async Task UncertainStatusRendersLocalizedContext(string culture, HttpStatusCode status, string expected)
    {
        var previousCulture = CultureInfo.CurrentCulture;
        var previousUiCulture = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture);
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(culture);
            using var context = new BunitContext();
            context.Services.AddLocalization();
            context.Services.AddLogging();
            using var handler = new HeldHandler(holdSession: false);
            using var http = new HttpClient(handler) { BaseAddress = new Uri("https://localhost") };
            context.Services.AddSingleton(http);
            context.JSInterop.Mode = JSRuntimeMode.Loose;
            var page = context.Render<EmployeeCreate>();
            var pending = page.InvokeAsync(() => Submit(page.Instance));
            try
            {
                await handler.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
                handler.Release.TrySetResult(new HttpResponseMessage(status));
                await pending.WaitAsync(TimeSpan.FromSeconds(5));
                page.Render();
                Assert.Contains(expected, page.Find("main").TextContent, StringComparison.Ordinal);
                var warning = culture == "th"
                    ? "อาจสร้างข้อมูลพนักงานแล้ว โปรดตรวจสอบรายชื่อพนักงานก่อนลองอีกครั้ง"
                    : "The employee may have been created. Check the employee list before trying again.";
                Assert.Contains(warning, page.Find("main").TextContent, StringComparison.Ordinal);
                Assert.True((bool)typeof(EmployeeCreate).GetField("outcomeUnknown", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(page.Instance)!);
            }
            finally { handler.Release.TrySetResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)); await pending.WaitAsync(TimeSpan.FromSeconds(5)); }
        }
        finally { CultureInfo.CurrentCulture = previousCulture; CultureInfo.CurrentUICulture = previousUiCulture; }
    }

    [Fact]
    public void ErrorTextIsRenderedAsTextRatherThanHtml()
    {
        using var context = new BunitContext();
        context.Services.AddLocalization();
        context.Services.AddLogging();
        using var http = new HttpClient(new HeldHandler(holdSession: true)) { BaseAddress = new Uri("https://localhost") };
        context.Services.AddSingleton(http);
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        var page = context.Render<EmployeeCreate>();
        typeof(EmployeeCreate).GetField("error", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(page.Instance, "<script>synthetic</script>");
        page.Render();
        Assert.Contains("<script>synthetic</script>", page.Find("main").TextContent, StringComparison.Ordinal);
        Assert.Empty(page.FindAll("main script"));
    }

    [Fact]
    public async Task ChangedInputDuringPreflightCannotDispatchUnvalidatedValues()
    {
        using var context = new BunitContext();
        context.Services.AddLocalization();
        using var handler = new HeldHandler(holdSession: true);
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://localhost") };
        var page = CreatePage(context, http);
        var pending = Submit(page);
        try
        {
            await handler.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            var model = (CreateEmployeeAccountRequest)typeof(EmployeeCreate).GetField("model", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(page)!;
            model.FirstName = "Changed";
            handler.Release.TrySetResult(Session());
            await pending.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(0, handler.Creates);
        }
        finally { handler.Release.TrySetResult(Session()); await pending.WaitAsync(TimeSpan.FromSeconds(5)); page.Dispose(); }
    }

    [Fact]
    public async Task UnauthorizedPreflightStillRedirectsWithoutCreating()
    {
        using var context = new BunitContext();
        context.Services.AddLocalization();
        using var handler = new HeldHandler(holdSession: true);
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://localhost") };
        var page = CreatePage(context, http);
        var pending = Submit(page);
        try
        {
            await handler.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            handler.Release.TrySetResult(new HttpResponseMessage(HttpStatusCode.Unauthorized));
            await pending.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(0, handler.Creates);
            Assert.Contains("/Login?returnUrl=", context.Services.GetRequiredService<NavigationManager>().Uri, StringComparison.Ordinal);
        }
        finally { handler.Release.TrySetResult(Session()); await pending.WaitAsync(TimeSpan.FromSeconds(5)); page.Dispose(); }
    }

    [Fact]
    public async Task DisposedDuringPreflightCannotDispatchCreate()
    {
        using var context = new BunitContext();
        context.Services.AddLocalization();
        using var handler = new HeldHandler(holdSession: true);
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://localhost") };
        var page = CreatePage(context, http);
        var pending = Submit(page);
        try
        {
            await handler.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            page.Dispose();
            handler.Release.TrySetResult(Session());
            await pending.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(0, handler.Creates);
        }
        finally { handler.Release.TrySetResult(Session()); await pending.WaitAsync(TimeSpan.FromSeconds(5)); }
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.Conflict)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    public async Task UncertainCreateCannotBeResubmitted(HttpStatusCode status)
    {
        using var context = new BunitContext();
        context.Services.AddLocalization();
        using var handler = new HeldHandler(holdSession: false);
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://localhost") };
        var page = CreatePage(context, http);
        var navigation = context.Services.GetRequiredService<NavigationManager>();
        var original = navigation.Uri;
        var pending = Submit(page);
        try
        {
            await handler.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            handler.Release.TrySetResult(new HttpResponseMessage(status));
            await pending.WaitAsync(TimeSpan.FromSeconds(5));
            await Submit(page);
            Assert.Equal(1, handler.Creates);
            Assert.Equal(original, navigation.Uri);
            Assert.True((bool)typeof(EmployeeCreate).GetField("outcomeUnknown", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(page)!);
        }
        finally { handler.Release.TrySetResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)); await pending.WaitAsync(TimeSpan.FromSeconds(5)); page.Dispose(); }
    }

    [Fact]
    public async Task DisposedAfterDispatchDoesNotUpdatePageOrReplay()
    {
        using var context = new BunitContext();
        context.Services.AddLocalization();
        using var handler = new HeldHandler(holdSession: false);
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://localhost") };
        var page = CreatePage(context, http);
        var pending = Submit(page);
        try
        {
            await handler.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            page.Dispose();
            handler.Release.TrySetResult(Json("{\"id\":42,\"confirmationEmailSent\":true}"));
            await pending.WaitAsync(TimeSpan.FromSeconds(5));
            await Submit(page);
            Assert.Equal(1, handler.Creates);
            Assert.Null(typeof(EmployeeCreate).GetField("createdId", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(page));
        }
        finally { handler.Release.TrySetResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)); await pending.WaitAsync(TimeSpan.FromSeconds(5)); }
    }

    [Fact]
    public async Task UnexpectedFailureAfterDispatchDoesNotAllowRetry()
    {
        using var context = new BunitContext();
        context.Services.AddLocalization();
        using var handler = new HeldHandler(holdSession: false);
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://localhost") };
        var page = CreatePage(context, http);
        var pending = Submit(page);
        try
        {
            await handler.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            handler.Release.TrySetException(new InvalidOperationException("synthetic failure"));
            await pending.WaitAsync(TimeSpan.FromSeconds(5));
            await Submit(page);
            Assert.Equal(1, handler.Creates);
        }
        finally { handler.Release.TrySetResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)); await pending.WaitAsync(TimeSpan.FromSeconds(5)); page.Dispose(); }
    }

    private static EmployeeCreate CreatePage(BunitContext context, HttpClient http)
    {
        var page = new EmployeeCreate();
        Set(page, "Http", http);
        Set(page, "Text", context.Services.GetRequiredService<IStringLocalizer<EmployeeCreate>>());
        Set(page, "Navigation", context.Services.GetRequiredService<NavigationManager>());
        Set(page, "Logger", NullLogger<EmployeeCreate>.Instance);
        return page;
    }
    private static void Set(EmployeeCreate page, string name, object value) =>
        typeof(EmployeeCreate).GetProperty(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(page, value);
    private static Task Submit(EmployeeCreate page) =>
        (Task)typeof(EmployeeCreate).GetMethod("SubmitAsync", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(page, null)!;
    private static HttpResponseMessage Json(string body) => new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
    private static HttpResponseMessage Session() => Json("{\"csrfToken\":\"test-token\"}");

    private sealed class HeldHandler(bool holdSession) : HttpMessageHandler
    {
        public int Creates { get; private set; }
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
            Creates++;
            Started.TrySetResult();
            return Release.Task;
        }
    }
}
