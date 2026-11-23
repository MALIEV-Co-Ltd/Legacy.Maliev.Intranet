using System.Net;
using System.Reflection;
using System.Text;
using Bunit;
using Legacy.Maliev.Intranet.Client.Features.Orders.Pages;
using Legacy.Maliev.Intranet.Contracts;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;

namespace Legacy.Maliev.Intranet.Tests;

public sealed class OrderCreateMaterialLifetimeTests
{
    [Fact]
    public async Task InvalidCurrentMaterialResponseShowsError()
    {
        using var context = new BunitContext();
        context.Services.AddLocalization();
        using var handler = new HeldMaterialHandler();
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://localhost") };
        var page = new OrderCreate();
        Set(page, "Http", http);
        Set(page, "Text", context.Services.GetRequiredService<IStringLocalizer<OrderCreate>>());
        var pending = Select(page, 1);
        try
        {
            await handler.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            handler.Release.TrySetResult(Json("{"));
            await pending.WaitAsync(TimeSpan.FromSeconds(5));
            var error = (string?)typeof(OrderCreate).GetField("error", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(page);
            Assert.False(string.IsNullOrEmpty(error));
        }
        finally
        {
            handler.Release.TrySetResult(Json("{\"colors\":[],\"surfaceFinishes\":[]}"));
            await pending.WaitAsync(TimeSpan.FromSeconds(5));
        }
    }

    [Fact]
    public async Task DisposedPageIgnoresPendingMaterialResponse()
    {
        using var context = new BunitContext();
        context.Services.AddLocalization();
        using var handler = new HeldMaterialHandler();
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://localhost") };
        var page = new OrderCreate();
        Set(page, "Http", http);
        Set(page, "Text", context.Services.GetRequiredService<IStringLocalizer<OrderCreate>>());
        var pending = Select(page, 1);
        try
        {
            await handler.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            page.Dispose();
            handler.Release.TrySetResult(Json("{\"colors\":[{\"id\":11,\"name\":\"Old\"}],\"surfaceFinishes\":[]}"));
            await pending.WaitAsync(TimeSpan.FromSeconds(5));
            var options = (OrderMaterialOptions)typeof(OrderCreate).GetField("materialOptions", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(page)!;
            Assert.Empty(options.Colors);
        }
        finally
        {
            handler.Release.TrySetResult(Json("{\"colors\":[],\"surfaceFinishes\":[]}"));
            await pending.WaitAsync(TimeSpan.FromSeconds(5));
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task OlderMaterialResponseCannotReplaceLatestSelection(bool clearSelection)
    {
        using var context = new BunitContext();
        context.Services.AddLocalization();
        using var handler = new HeldMaterialHandler();
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://localhost") };
        var page = new OrderCreate();
        Set(page, "Http", http);
        Set(page, "Text", context.Services.GetRequiredService<IStringLocalizer<OrderCreate>>());
        var older = Select(page, 1);
        try
        {
            await handler.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await Select(page, clearSelection ? null : 2).WaitAsync(TimeSpan.FromSeconds(5));
            handler.Release.TrySetResult(Json("{\"colors\":[{\"id\":11,\"name\":\"Old\"}],\"surfaceFinishes\":[]}"));
            await older.WaitAsync(TimeSpan.FromSeconds(5));
            var options = (OrderMaterialOptions)typeof(OrderCreate).GetField("materialOptions", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(page)!;
            if (clearSelection) Assert.Empty(options.Colors);
            else Assert.Equal(22, Assert.Single(options.Colors).Id);
        }
        finally
        {
            handler.Release.TrySetResult(Json("{\"colors\":[],\"surfaceFinishes\":[]}"));
            await older.WaitAsync(TimeSpan.FromSeconds(5));
        }
    }

    private static void Set(OrderCreate page, string name, object value) =>
        typeof(OrderCreate).GetProperty(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(page, value);
    private static Task Select(OrderCreate page, int? materialId) =>
        (Task)typeof(OrderCreate).GetMethod("OnMaterialChangedAsync", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(page, [materialId])!;
    private static HttpResponseMessage Json(string body) => new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private sealed class HeldMaterialHandler : HttpMessageHandler
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<HttpResponseMessage> Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.RequestUri!.AbsolutePath.EndsWith("/1", StringComparison.Ordinal))
            {
                Started.TrySetResult();
                return Release.Task;
            }
            return Task.FromResult(Json("{\"colors\":[{\"id\":22,\"name\":\"New\"}],\"surfaceFinishes\":[]}"));
        }
    }
}
