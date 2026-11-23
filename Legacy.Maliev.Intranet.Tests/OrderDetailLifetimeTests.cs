using System.Net;
using System.Reflection;
using System.Text;
using System.Text.Json;
using Bunit;
using Legacy.Maliev.Intranet.Client.Features.Orders.Pages;
using Legacy.Maliev.Intranet.Contracts;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging.Abstractions;

namespace Legacy.Maliev.Intranet.Tests;

public sealed class OrderDetailLifetimeTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task OlderOrderResponseCannotReplaceOrRedirectNewerOrder(bool unauthorized)
    {
        using var context = new BunitContext();
        context.Services.AddLocalization();
        using var handler = new DelayedHandler();
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://localhost") };
        var page = CreatePage(context, http, 1);
        var navigation = context.Services.GetRequiredService<NavigationManager>();
        var originalUri = navigation.Uri;
        var older = Load(page);
        try
        {
            await handler.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            page.Id = 2;
            await Load(page).WaitAsync(TimeSpan.FromSeconds(5));
            handler.Release.TrySetResult(unauthorized ? new HttpResponseMessage(HttpStatusCode.Unauthorized) : Detail(1));
            await older.WaitAsync(TimeSpan.FromSeconds(5));
            var detail = (OrderDetailPage)GetField(page, "page")!;
            Assert.Equal(2, detail.Order.Id);
            Assert.Equal(originalUri, navigation.Uri);
        }
        finally
        {
            if (!handler.Release.Task.IsCompleted) handler.Release.TrySetResult(Detail(1));
            await older.WaitAsync(TimeSpan.FromSeconds(5));
            page.Dispose();
        }
    }

    [Fact]
    public async Task ChangedOrderDuringSessionPreflightDoesNotDispatchOldWrite()
    {
        using var context = new BunitContext();
        context.Services.AddLocalization();
        using var handler = new DelayedHandler(holdSession: true);
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://localhost") };
        var page = CreatePage(context, http, 1);
        SetField(page, "page", ReadDetail(1));
        using var request = new HttpRequestMessage(HttpMethod.Put, "/bff/orders/1");
        var pending = Send(page, request);
        try
        {
            await handler.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            page.Id = 2;
            await Load(page).WaitAsync(TimeSpan.FromSeconds(5));
            handler.Release.TrySetResult(Session());
            await pending.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(0, handler.Writes);
            Assert.Equal(2, ((OrderDetailPage)GetField(page, "page")!).Order.Id);
        }
        finally
        {
            if (!handler.Release.Task.IsCompleted) handler.Release.TrySetResult(Session());
            await pending.WaitAsync(TimeSpan.FromSeconds(5));
            page.Dispose();
        }
    }

    [Fact]
    public async Task UncertainWriteRemainsFencedWhenReadPrecedesLateCommit()
    {
        using var context = new BunitContext();
        context.Services.AddLocalization();
        using var handler = new LateCommitHandler();
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://localhost") };
        var page = CreatePage(context, http, 1);
        SetField(page, "page", ReadDetail(1));
        try
        {
            using var first = new HttpRequestMessage(HttpMethod.Put, "/bff/orders/1");
            await Send(page, first).WaitAsync(TimeSpan.FromSeconds(5));
            var text = context.Services.GetRequiredService<IStringLocalizer<OrderDetail>>();
            var failure = Assert.IsType<string>(GetField(page, "error"));
            Assert.Contains(text["InvalidResponse"].Value, failure, StringComparison.Ordinal);
            Assert.Contains(text["WriteOutcomeUnknown"].Value, failure, StringComparison.Ordinal);
            await Load(page).WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal("Before", ((OrderDetailPage)GetField(page, "page")!).Order.Name);
            using var beforeCommit = new HttpRequestMessage(HttpMethod.Put, "/bff/orders/1");
            await Send(page, beforeCommit).WaitAsync(TimeSpan.FromSeconds(5));
            handler.Commit();
            await Load(page).WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal("After", ((OrderDetailPage)GetField(page, "page")!).Order.Name);
            using var afterCommit = new HttpRequestMessage(HttpMethod.Put, "/bff/orders/1");
            await Send(page, afterCommit).WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(1, handler.Writes);
        }
        finally { page.Dispose(); }
    }

    [Fact]
    public async Task ChangedEditDuringSuccessfulWriteIsNotOverwrittenByReload()
    {
        using var context = new BunitContext();
        context.Services.AddLocalization();
        using var handler = new HeldWriteHandler();
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://localhost") };
        var page = CreatePage(context, http, 1);
        SetField(page, "page", ReadDetail(1));
        using var request = new HttpRequestMessage(HttpMethod.Put, "/bff/orders/1");
        var pending = Send(page, request);
        try
        {
            await handler.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            var edit = GetField(page, "edit")!;
            edit.GetType().GetProperty("Name")!.SetValue(edit, "New draft");
            handler.Release.TrySetResult(new HttpResponseMessage(HttpStatusCode.NoContent));
            await pending.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal("New draft", edit.GetType().GetProperty("Name")!.GetValue(edit));
            Assert.Equal(1, handler.Writes);
            Assert.Equal(0, handler.OrderReads);
            var text = context.Services.GetRequiredService<IStringLocalizer<OrderDetail>>();
            Assert.Contains(text["OrderSaved"].Value, Assert.IsType<string>(GetField(page, "error")), StringComparison.Ordinal);
        }
        finally
        {
            if (!handler.Release.Task.IsCompleted) handler.Release.TrySetResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
            await pending.WaitAsync(TimeSpan.FromSeconds(5));
            page.Dispose();
        }
    }

    private static OrderDetail CreatePage(BunitContext context, HttpClient http, int id)
    {
        var page = new OrderDetail { Id = id };
        SetProperty(page, "Http", http);
        SetProperty(page, "Navigation", context.Services.GetRequiredService<NavigationManager>());
        SetProperty(page, "Text", context.Services.GetRequiredService<IStringLocalizer<OrderDetail>>());
        SetProperty(page, "Logger", NullLogger<OrderDetail>.Instance);
        return page;
    }

    private static void SetProperty(OrderDetail page, string name, object value) =>
        typeof(OrderDetail).GetProperty(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(page, value);
    private static void SetField(OrderDetail page, string name, object value) =>
        typeof(OrderDetail).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(page, value);
    private static object? GetField(OrderDetail page, string name) =>
        typeof(OrderDetail).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(page);
    private static Task Load(OrderDetail page) =>
        (Task)typeof(OrderDetail).GetMethod("LoadAsync", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(page, null)!;
    private static Task Send(OrderDetail page, HttpRequestMessage request) =>
        (Task)typeof(OrderDetail).GetMethod("SendAsync", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(page, [request, "saved"])!;
    private static OrderDetailPage ReadDetail(int id) => JsonSerializer.Deserialize<OrderDetailPage>(DetailJson(id), new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
    private static HttpResponseMessage Detail(int id) => Json(DetailJson(id));
    private static HttpResponseMessage Session() => Json("{\"csrfToken\":\"synthetic\"}");
    private static HttpResponseMessage Json(string body) => new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
    private static string DetailJson(int id, string name = "Synthetic") => $$"""{"order":{"id":{{id}},"name":"{{name}}","processId":1,"quantity":1},"processes":[],"materials":[],"colors":[],"surfaceFinishes":[],"currencies":[],"employees":[],"availableStatuses":[],"history":[],"files":[]}""";

    private sealed class DelayedHandler(bool holdSession = false) : HttpMessageHandler
    {
        private int held;
        public int Writes { get; private set; }
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<HttpResponseMessage> Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.Method != HttpMethod.Get) { Writes++; return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NoContent)); }
            var path = request.RequestUri!.AbsolutePath;
            if (((holdSession && path == "/bff/session") || (!holdSession && path == "/bff/orders/1")) && Interlocked.Exchange(ref held, 1) == 0)
            {
                Started.TrySetResult();
                return Release.Task;
            }
            return Task.FromResult(path == "/bff/session" ? Session() : Detail(2));
        }
    }

    private sealed class LateCommitHandler : HttpMessageHandler
    {
        private bool committed;
        public int Writes { get; private set; }
        public void Commit() => committed = true;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.Method != HttpMethod.Get)
            {
                Writes++;
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.BadGateway));
            }
            return Task.FromResult(request.RequestUri!.AbsolutePath == "/bff/session"
                ? Session() : Json(DetailJson(1, committed ? "After" : "Before")));
        }
    }

    private sealed class HeldWriteHandler : HttpMessageHandler
    {
        public int Writes { get; private set; }
        public int OrderReads { get; private set; }
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<HttpResponseMessage> Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.Method != HttpMethod.Get) { Writes++; Started.TrySetResult(); return Release.Task; }
            if (request.RequestUri!.AbsolutePath == "/bff/session") return Task.FromResult(Session());
            OrderReads++;
            return Task.FromResult(Detail(1));
        }
    }
}
