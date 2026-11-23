using System.Net;
using System.Reflection;
using System.Text;
using Bunit;
using Legacy.Maliev.Intranet.Client.Features.Customers.Pages;
using Legacy.Maliev.Intranet.Contracts;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;

namespace Legacy.Maliev.Intranet.Tests;

public sealed class CustomerCreateLifetimeTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DisposedDuringSessionPreflightDoesNotCreateOrRedirect(bool unauthorized)
    {
        using var context = new BunitContext();
        context.Services.AddLocalization();
        using var handler = new DelayedCreateHandler(holdSession: true);
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://localhost") };
        var page = CreatePage(context, http);
        var navigation = context.Services.GetRequiredService<NavigationManager>();
        var original = navigation.Uri;
        var pending = Submit(page);
        try
        {
            await handler.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            page.Dispose();
            handler.Release.TrySetResult(unauthorized ? new HttpResponseMessage(HttpStatusCode.Unauthorized) : Session());
            await pending.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(0, handler.Creates);
            Assert.Equal(original, navigation.Uri);
        }
        finally { if (!handler.Release.Task.IsCompleted) handler.Release.TrySetResult(Session()); await pending.WaitAsync(TimeSpan.FromSeconds(5)); }
    }

    [Fact]
    public async Task DisposedAfterCreateDispatchDoesNotNavigateOrReplay()
    {
        using var context = new BunitContext();
        context.Services.AddLocalization();
        using var handler = new DelayedCreateHandler(holdSession: false);
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://localhost") };
        var page = CreatePage(context, http);
        var navigation = context.Services.GetRequiredService<NavigationManager>();
        var original = navigation.Uri;
        var pending = Submit(page);
        try
        {
            await handler.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            page.Dispose();
            handler.Release.TrySetResult(Json("{\"id\":42,\"onboardingDelivery\":\"delivered\"}"));
            await pending.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(1, handler.Creates);
            Assert.Equal(original, navigation.Uri);
        }
        finally { if (!handler.Release.Task.IsCompleted) handler.Release.TrySetResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)); await pending.WaitAsync(TimeSpan.FromSeconds(5)); }
    }

    [Fact]
    public async Task EditingDuringSessionPreflightDoesNotDispatchStaleCreate()
    {
        using var context = new BunitContext();
        context.Services.AddLocalization();
        using var handler = new DelayedCreateHandler(holdSession: true);
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://localhost") };
        var page = CreatePage(context, http);
        var model = (CreateCustomerAccountRequest)typeof(CustomerCreate)
            .GetField("model", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(page)!;
        var pending = Submit(page);
        try
        {
            await handler.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            model.FirstName = "Changed";
            handler.Release.TrySetResult(Session());
            await pending.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(0, handler.Creates);
        }
        finally { if (!handler.Release.Task.IsCompleted) handler.Release.TrySetResult(Session()); await pending.WaitAsync(TimeSpan.FromSeconds(5)); }
    }

    [Fact]
    public async Task CreatedCustomerWithPendingOnboardingCannotBeCreatedAgain()
    {
        using var context = new BunitContext();
        context.Services.AddLocalization();
        using var handler = new DelayedCreateHandler(holdSession: false);
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://localhost") };
        var page = CreatePage(context, http);
        var pending = Submit(page);
        try
        {
            await handler.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            handler.Release.TrySetResult(Json("{\"id\":42,\"onboardingDelivery\":\"pending\"}"));
            await pending.WaitAsync(TimeSpan.FromSeconds(5));
            await Submit(page).WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(1, handler.Creates);
        }
        finally { if (!handler.Release.Task.IsCompleted) handler.Release.TrySetResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)); await pending.WaitAsync(TimeSpan.FromSeconds(5)); }
    }

    private static CustomerCreate CreatePage(BunitContext context, HttpClient http)
    {
        var page = new CustomerCreate();
        Set(page, "Http", http);
        Set(page, "Text", context.Services.GetRequiredService<IStringLocalizer<CustomerCreate>>());
        Set(page, "Navigation", context.Services.GetRequiredService<NavigationManager>());
        return page;
    }
    private static void Set(CustomerCreate page, string name, object value) =>
        typeof(CustomerCreate).GetProperty(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(page, value);
    private static Task Submit(CustomerCreate page) =>
        (Task)typeof(CustomerCreate).GetMethod("SubmitAsync", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(page, null)!;
    private static HttpResponseMessage Json(string body) => new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
    private static HttpResponseMessage Session() => Json("{\"isAuthenticated\":true,\"employeeId\":\"test\",\"displayName\":\"Test\",\"roles\":[],\"csrfToken\":\"synthetic\"}");

    private sealed class DelayedCreateHandler(bool holdSession) : HttpMessageHandler
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<HttpResponseMessage> Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int Creates { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.RequestUri!.AbsolutePath == "/bff/session")
            {
                if (!holdSession) return Task.FromResult(Session());
                Started.TrySetResult();
                return Release.Task;
            }
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Equal("/bff/customers", request.RequestUri.AbsolutePath);
            Creates++;
            Started.TrySetResult();
            return Release.Task;
        }
    }
}
