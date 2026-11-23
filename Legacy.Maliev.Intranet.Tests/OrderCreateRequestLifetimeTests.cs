using System.Net;
using System.Reflection;
using System.Text;
using Bunit;
using Legacy.Maliev.Intranet.Client.Features.Orders.Pages;
using Legacy.Maliev.Intranet.Contracts;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;

namespace Legacy.Maliev.Intranet.Tests;

public sealed class OrderCreateRequestLifetimeTests
{
    [Fact]
    public async Task OlderCustomerLookupCannotReplaceNewerLookup()
    {
        using var context = new BunitContext();
        context.Services.AddLocalization();
        using var handler = new HeldHandler();
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://localhost") };
        var page = Page(context, http);
        SetModel(page, "CustomerId", 1);
        var older = Invoke(page, "LoadAsync", 1);
        try
        {
            await handler.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            SetModel(page, "CustomerId", 2);
            await Invoke(page, "LoadAsync", 2).WaitAsync(TimeSpan.FromSeconds(5));
            handler.Release.TrySetResult(Json(PageJson(1)));
            await older.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(2, ((OrderCreatePage)GetField(page, "createPage")!).Customer!.Id);
        }
        finally { handler.Release.TrySetResult(Json(PageJson(1))); await older.WaitAsync(TimeSpan.FromSeconds(5)); }
    }

    [Fact]
    public async Task EditingCustomerDuringLookupDoesNotAcceptOldCustomer()
    {
        using var context = new BunitContext();
        context.Services.AddLocalization();
        using var handler = new HeldHandler();
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://localhost") };
        var page = Page(context, http);
        SetModel(page, "CustomerId", 1);
        var pending = Invoke(page, "LoadAsync", 1);
        try
        {
            await handler.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            SetModel(page, "CustomerId", 2);
            handler.Release.TrySetResult(Json(PageJson(1)));
            await pending.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Null(GetField(page, "createPage"));
        }
        finally { handler.Release.TrySetResult(Json(PageJson(1))); await pending.WaitAsync(TimeSpan.FromSeconds(5)); }
    }
    [Fact]
    public async Task DisposedPageIgnoresPendingInitialization()
    {
        using var context = new BunitContext();
        context.Services.AddLocalization();
        using var handler = new HeldHandler();
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://localhost") };
        var page = Page(context, http);
        SetModel(page, "CustomerId", 1);
        var pending = Invoke(page, "LoadAsync", 1);
        try
        {
            await handler.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            page.Dispose();
            handler.Release.TrySetResult(Json(PageJson(1)));
            await pending.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Null(GetField(page, "createPage"));
        }
        finally { handler.Release.TrySetResult(Json(PageJson(1))); await pending.WaitAsync(TimeSpan.FromSeconds(5)); }
    }

    [Fact]
    public async Task ConcurrentSubmitAndConfirmedWarningCannotCreateTwice()
    {
        using var context = new BunitContext();
        context.Services.AddLocalization();
        using var handler = new HeldHandler(holdPost: true);
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://localhost") };
        var page = Page(context, http);
        SetModel(page, "CustomerId", 1);
        SetModel(page, "ProcessId", 1);
        SetModel(page, "Name", "Test");
        SetField(page, "session", new EmployeeSessionSummary(true, "1", "Test", [], "token"));
        var customer = new CustomerDetail(1, "A", "B", "A B", null, null, null, "a@example.test", null, null, null, null, null, null, null, null, null);
        SetField(page, "createPage", new OrderCreatePage([], [], customer));
        var pending = Invoke(page, "CreateAsync");
        try
        {
            await handler.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await Invoke(page, "CreateAsync");
            handler.Release.TrySetResult(Json("{\"id\":42,\"warning\":\"pending\"}"));
            await pending.WaitAsync(TimeSpan.FromSeconds(5));
            await Invoke(page, "CreateAsync");
            Assert.Equal(1, handler.Posts);
        }
        finally { handler.Release.TrySetResult(Json("{}")); await pending.WaitAsync(TimeSpan.FromSeconds(5)); }
    }

    private static OrderCreate Page(BunitContext context, HttpClient http)
    {
        var page = new OrderCreate();
        typeof(OrderCreate).GetProperty("Http", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(page, http);
        typeof(OrderCreate).GetProperty("Text", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(page, context.Services.GetRequiredService<IStringLocalizer<OrderCreate>>());
        return page;
    }
    private static object? GetField(OrderCreate page, string name) => typeof(OrderCreate).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(page);
    private static void SetField(OrderCreate page, string name, object value) => typeof(OrderCreate).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(page, value);
    private static void SetModel(OrderCreate page, string name, object value) { var model = GetField(page, "model")!; model.GetType().GetProperty(name)!.SetValue(model, value); }
    private static Task Invoke(OrderCreate page, string name, params object?[] args) => (Task)typeof(OrderCreate).GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(page, args)!;
    private static HttpResponseMessage Json(string body) => new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
    private static string PageJson(int id) => System.Text.Json.JsonSerializer.Serialize(new
    {
        processes = Array.Empty<object>(),
        materials = Array.Empty<object>(),
        customer = new { id, firstName = "A", lastName = "B", fullName = "A B", email = "a@example.test" },
    });

    private sealed class HeldHandler(bool holdPost = false) : HttpMessageHandler
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<HttpResponseMessage> Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int Posts { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.Method == HttpMethod.Post) { Posts++; Started.TrySetResult(); return holdPost ? Release.Task : Task.FromResult(Json("{}")); }
            if (request.RequestUri!.AbsolutePath == "/bff/session") return Task.FromResult(Json("{\"isAuthenticated\":true,\"csrfToken\":\"token\",\"roles\":[]}"));
            if (request.RequestUri.Query.Contains("customerId=1", StringComparison.Ordinal)) { Started.TrySetResult(); return Release.Task; }
            return Task.FromResult(Json(PageJson(2)));
        }
    }
}
