using System.Net;
using System.Reflection;
using System.Text;
using Bunit;
using Legacy.Maliev.Intranet.Client.Features.Employees.Pages;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;

namespace Legacy.Maliev.Intranet.Tests;

public sealed class EmployeeViewLifetimeTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SupersededResponseCannotReplaceDetailOrRedirect(bool unauthorized)
    {
        using var context = new BunitContext();
        context.Services.AddLocalization();
        var handler = new DelayedEmployeeHandler();
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://localhost") };
        var page = new EmployeeView { Id = 1 };
        Set(page, "Http", http);
        Set(page, "Text", context.Services.GetRequiredService<IStringLocalizer<EmployeeView>>());
        var navigation = context.Services.GetRequiredService<NavigationManager>();
        Set(page, "Navigation", navigation);
        var original = navigation.Uri;
        var older = Load(page);
        await handler.FirstStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        page.Id = 2;
        await Load(page);
        handler.CompleteFirst(unauthorized);
        await older;
        var detail = typeof(EmployeeView).GetField("detail", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(page);
        Assert.Equal(2, detail!.GetType().GetProperty("Id")!.GetValue(detail));
        Assert.Equal(original, navigation.Uri);
        Assert.False((bool)typeof(EmployeeView).GetField("loading", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(page)!);
        page.Dispose();
    }

    [Fact]
    public async Task DisposedViewCannotRedirectAfterResponse()
    {
        using var context = new BunitContext();
        context.Services.AddLocalization();
        var handler = new DelayedEmployeeHandler();
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://localhost") };
        var page = new EmployeeView { Id = 1 };
        Set(page, "Http", http);
        Set(page, "Text", context.Services.GetRequiredService<IStringLocalizer<EmployeeView>>());
        var navigation = context.Services.GetRequiredService<NavigationManager>();
        Set(page, "Navigation", navigation);
        var original = navigation.Uri;
        var pending = Load(page);
        await handler.FirstStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        page.Dispose();
        handler.CompleteFirst(true);
        await pending;
        Assert.Equal(original, navigation.Uri);
    }

    private static void Set(EmployeeView page, string property, object value) =>
        typeof(EmployeeView).GetProperty(property, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(page, value);
    private static Task Load(EmployeeView page) =>
        (Task)typeof(EmployeeView).GetMethod("LoadAsync", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(page, null)!;

    private sealed class DelayedEmployeeHandler : HttpMessageHandler
    {
        private readonly TaskCompletionSource<HttpResponseMessage> first = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource FirstStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public void CompleteFirst(bool unauthorized) => first.SetResult(unauthorized
            ? new HttpResponseMessage(HttpStatusCode.Unauthorized)
            : Response(1));
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.RequestUri!.AbsolutePath.EndsWith("/1", StringComparison.Ordinal))
            {
                FirstStarted.TrySetResult();
                return first.Task;
            }
            return Task.FromResult(Response(2));
        }
        private static HttpResponseMessage Response(int id) => new(HttpStatusCode.OK)
        {
            Content = new StringContent($$"""{"id":{{id}},"firstName":"Test","lastName":"Person","fullName":"Test Person","email":"test@example.invalid"}""", Encoding.UTF8, "application/json"),
        };
    }
}
