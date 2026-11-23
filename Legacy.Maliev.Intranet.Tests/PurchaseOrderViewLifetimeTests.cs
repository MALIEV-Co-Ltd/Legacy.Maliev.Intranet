using System.Net;
using System.Reflection;
using System.Text;
using Bunit;
using Legacy.Maliev.Intranet.Client.Features.Procurement.Pages;
using Legacy.Maliev.Intranet.Contracts;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;

namespace Legacy.Maliev.Intranet.Tests;

public sealed class PurchaseOrderViewLifetimeTests
{
    [Fact]
    public async Task RouteChangeDuringSessionPreflightDoesNotDeleteAnotherOrder()
    {
        using var context = new BunitContext();
        context.Services.AddLocalization();
        using var handler = new HeldHandler();
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://localhost") };
        var page = Create(context, http);
        page.Id = 1;
        SetField(page, "loading", false);
        SetField(page, "detail", new PurchaseOrderDetail(1, null, null, null, null, null, null, null, null, [], []));
        var pending = Invoke(page, "DeleteAsync");
        try
        {
            await handler.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            page.Id = 2;
            handler.Release.TrySetResult(Json("{\"csrfToken\":\"token\"}"));
            await pending.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(0, handler.Deletes);
        }
        finally
        {
            if (!handler.Release.Task.IsCompleted) handler.Release.TrySetResult(Json("{}"));
            await pending.WaitAsync(TimeSpan.FromSeconds(5));
        }
    }

    [Fact]
    public async Task OlderDetailResponseCannotReplaceNewerOrder()
    {
        using var context = new BunitContext();
        context.Services.AddLocalization();
        using var handler = new HeldHandler();
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://localhost") };
        var page = Create(context, http);
        page.Id = 1;
        var older = Invoke(page, "LoadAsync");
        try
        {
            await handler.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            page.Id = 2;
            await Invoke(page, "LoadAsync").WaitAsync(TimeSpan.FromSeconds(5));
            handler.Release.TrySetResult(Json("{\"id\":1,\"items\":[],\"downloads\":[]}"));
            await older.WaitAsync(TimeSpan.FromSeconds(5));
            var detail = (PurchaseOrderDetail?)typeof(PurchaseOrderView).GetField("detail", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(page);
            Assert.Equal(2, detail?.Id);
        }
        finally
        {
            if (!handler.Release.Task.IsCompleted) handler.Release.TrySetResult(Json("{}"));
            await older.WaitAsync(TimeSpan.FromSeconds(5));
        }
    }

    [Theory]
    [InlineData("{\"id\":2,\"items\":null,\"downloads\":[]}")]
    [InlineData("{\"id\":2,\"items\":[],\"downloads\":null}")]
    [InlineData("{\"id\":2,\"items\":[null],\"downloads\":[]}")]
    [InlineData("{\"id\":2,\"items\":[],\"downloads\":[null]}")]
    [InlineData("{\"id\":2,\"items\":[],\"downloads\":[{\"name\":\"x\",\"url\":null}]}")]
    [InlineData("{\"id\":2,\"items\":[],\"downloads\":[{\"name\":\"x\",\"url\":\"relative\"}]}")]
    public async Task MalformedCollectionsDoNotBecomeVisible(string body)
    {
        using var context = new BunitContext();
        context.Services.AddLocalization();
        using var handler = new StaticHandler(body);
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://localhost") };
        var page = Create(context, http);
        page.Id = 2;
        await Invoke(page, "LoadAsync");
        Assert.Null(typeof(PurchaseOrderView).GetField("detail", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(page));
        Assert.NotNull(typeof(PurchaseOrderView).GetField("error", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(page));
    }

    [Fact]
    public async Task OlderFailureCannotOverwriteNewerOrder()
    {
        using var context = new BunitContext();
        context.Services.AddLocalization();
        using var handler = new HeldHandler();
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://localhost") };
        var page = Create(context, http);
        page.Id = 1;
        var older = Invoke(page, "LoadAsync");
        try
        {
            await handler.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            page.Id = 2;
            await Invoke(page, "LoadAsync").WaitAsync(TimeSpan.FromSeconds(5));
            handler.Release.TrySetResult(new HttpResponseMessage(HttpStatusCode.InternalServerError));
            await older.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Null(typeof(PurchaseOrderView).GetField("error", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(page));
            Assert.Equal(2, ((PurchaseOrderDetail?)typeof(PurchaseOrderView).GetField("detail", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(page))?.Id);
        }
        finally
        {
            if (!handler.Release.Task.IsCompleted) handler.Release.TrySetResult(Json("{}"));
            await older.WaitAsync(TimeSpan.FromSeconds(5));
        }
    }

    [Fact]
    public async Task DisposedViewIgnoresLateDetail()
    {
        using var context = new BunitContext();
        context.Services.AddLocalization();
        using var handler = new HeldHandler();
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://localhost") };
        var page = Create(context, http);
        page.Id = 1;
        var pending = Invoke(page, "LoadAsync");
        try
        {
            await handler.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            page.Dispose();
            handler.Release.TrySetResult(Json("{\"id\":1,\"items\":[],\"downloads\":[]}"));
            await pending.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Null(typeof(PurchaseOrderView).GetField("detail", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(page));
        }
        finally
        {
            if (!handler.Release.Task.IsCompleted) handler.Release.TrySetResult(Json("{}"));
            await pending.WaitAsync(TimeSpan.FromSeconds(5));
        }
    }

    private sealed class StaticHandler(string body) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(Json(body));
    }

    private static PurchaseOrderView Create(BunitContext context, HttpClient http)
    {
        var page = new PurchaseOrderView();
        typeof(PurchaseOrderView).GetProperty("Http", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(page, http);
        typeof(PurchaseOrderView).GetProperty("Text", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(page, context.Services.GetRequiredService<IStringLocalizer<PurchaseOrderView>>());
        return page;
    }

    private static void SetField(PurchaseOrderView page, string name, object value) =>
        typeof(PurchaseOrderView).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(page, value);
    private static Task Invoke(PurchaseOrderView page, string name) =>
        (Task)typeof(PurchaseOrderView).GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(page, null)!;
    private static HttpResponseMessage Json(string body) => new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private sealed class HeldHandler : HttpMessageHandler
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<HttpResponseMessage> Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int Deletes { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.Method == HttpMethod.Delete) { Deletes++; return Task.FromResult(Json("{}")); }
            if (request.RequestUri!.AbsolutePath is "/bff/session" or "/bff/purchase-orders/1")
            {
                Started.TrySetResult();
                return Release.Task;
            }
            return Task.FromResult(Json("{\"id\":2,\"items\":[],\"downloads\":[]}"));
        }
    }
}
