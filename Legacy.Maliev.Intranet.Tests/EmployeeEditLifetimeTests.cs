using System.Net;
using System.Reflection;
using Bunit;
using Legacy.Maliev.Intranet.Client.Features.Employees.Pages;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;

namespace Legacy.Maliev.Intranet.Tests;

public sealed class EmployeeEditLifetimeTests
{
    [Fact]
    public async Task DisposedEditCannotRedirectOnLateUnauthorizedRead()
    {
        using var context = new BunitContext();
        context.Services.AddLocalization();
        using var handler = new DelayedRead();
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://localhost") };
        var page = new EmployeeEdit { Id = 1 };
        Set(page, "Http", http);
        Set(page, "Text", context.Services.GetRequiredService<IStringLocalizer<EmployeeEdit>>());
        var navigation = context.Services.GetRequiredService<NavigationManager>();
        Set(page, "Navigation", navigation);
        var original = navigation.Uri;
        var pending = (Task)typeof(EmployeeEdit).GetMethod("ReloadAsync", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(page, null)!;
        await handler.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        page.Dispose();
        handler.Complete();
        await pending;
        Assert.Equal(original, navigation.Uri);
    }

    private static void Set(EmployeeEdit page, string name, object value) =>
        typeof(EmployeeEdit).GetProperty(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(page, value);

    private sealed class DelayedRead : HttpMessageHandler
    {
        private readonly TaskCompletionSource<HttpResponseMessage> result = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public void Complete() => result.SetResult(new HttpResponseMessage(HttpStatusCode.Unauthorized));
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Started.TrySetResult();
            return result.Task;
        }
    }
}
