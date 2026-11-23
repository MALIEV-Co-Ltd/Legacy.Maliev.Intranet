using System.Net;
using System.Reflection;
using System.Text;
using Bunit;
using Legacy.Maliev.Intranet.Client.Features.Procurement.Pages;
using Legacy.Maliev.Intranet.Contracts;
using Maliev.ShadcnBlazor.Components.Forms;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging.Abstractions;

namespace Legacy.Maliev.Intranet.Tests;

public sealed class SupplierViewLifetimeTests
{
    [Fact]
    public void SupplierDetails_RenderWhileCountryLookupIsPending()
    {
        using var context = new BunitContext();
        context.Services.AddLocalization();
        context.Services.AddLogging();
        using var handler = new HeldCountryHandler();
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://localhost") };
        context.Services.AddSingleton(http);
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        context.Services.GetRequiredService<NavigationManager>().NavigateTo("/Suppliers/View?id=42");
        var page = context.Render<SupplierView>();
        try
        {
            page.WaitForAssertion(() =>
            {
                Assert.Contains("Supplier", page.Markup, StringComparison.Ordinal);
                Assert.True(page.FindComponent<ShadcnSelect<int>>().Instance.Disabled);
            });
            Assert.False(handler.Release.Task.IsCompleted);
            handler.Release.TrySetResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("[{\"id\":66,\"name\":\"Thailand\"}]", Encoding.UTF8, "application/json"),
            });
            page.WaitForAssertion(() => Assert.Contains(page.FindComponent<ShadcnSelect<int>>().Instance.Options, option => option.Value == 66 && option.Text == "Thailand"));
        }
        finally
        {
            handler.Release.TrySetResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
        }
    }

    private sealed class HeldCountryHandler : HttpMessageHandler
    {
        public TaskCompletionSource<HttpResponseMessage> Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            request.RequestUri!.AbsolutePath.EndsWith("/edit-countries", StringComparison.Ordinal)
                ? Release.Task
                : Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{\"id\":42,\"name\":\"Supplier\",\"address1\":\"Test\",\"countryId\":66}", Encoding.UTF8, "application/json") });
    }

    [Fact]
    public void EditForm_ShowsCountryNamesAndPreservesAnUnlistedExistingCountry()
    {
        using var context = new BunitContext();
        context.Services.AddLocalization();
        context.Services.AddLogging();
        using var http = new HttpClient(new CountryAndSupplierHandler()) { BaseAddress = new Uri("https://localhost") };
        context.Services.AddSingleton(http);
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        context.Services.GetRequiredService<NavigationManager>().NavigateTo("/Suppliers/View?id=42");
        var page = context.Render<SupplierView>();
        page.WaitForAssertion(() =>
        {
            var options = page.FindComponent<ShadcnSelect<int>>().Instance.Options;
            Assert.Contains(options, option => option.Value == 66 && option.Text == "Thailand");
            Assert.Contains(options, option => option.Value == 99);
        });
    }

    private sealed class CountryAndSupplierHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var json = request.RequestUri!.AbsolutePath.EndsWith("/edit-countries", StringComparison.Ordinal)
                ? "[{\"id\":66,\"name\":\"Thailand\"}]"
                : "{\"id\":42,\"name\":\"Supplier\",\"address1\":\"Test\",\"countryId\":99}";
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") });
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SupersededReadCannotReplaceOrRedirect(bool unauthorized)
    {
        using var context = new BunitContext();
        context.Services.AddLocalization();
        var handler = new DelayedSupplierHandler();
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://localhost") };
        var page = CreatePage(context, http);
        page.Id = 1;
        var navigation = context.Services.GetRequiredService<NavigationManager>();
        var original = navigation.Uri;
        var old = Load(page);
        try
        {
            await handler.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            page.Id = 2;
            await Load(page);
            handler.Release.TrySetResult(unauthorized ? new HttpResponseMessage(HttpStatusCode.Unauthorized) : Supplier(1));
            await old;
            var detail = typeof(SupplierView).GetField("detail", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(page)!;
            Assert.Equal(2, detail.GetType().GetProperty("Id")!.GetValue(detail));
            Assert.Equal(original, navigation.Uri);
        }
        finally { handler.Release.TrySetResult(Supplier(1)); await old.WaitAsync(TimeSpan.FromSeconds(5)); page.Dispose(); }
    }

    [Fact]
    public async Task DisposedReadCannotRedirect()
    {
        using var context = new BunitContext();
        context.Services.AddLocalization();
        var handler = new DelayedSupplierHandler();
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://localhost") };
        var page = CreatePage(context, http);
        page.Id = 1;
        var navigation = context.Services.GetRequiredService<NavigationManager>();
        var original = navigation.Uri;
        var pending = Load(page);
        try
        {
            await handler.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            page.Dispose();
            handler.Release.TrySetResult(new HttpResponseMessage(HttpStatusCode.Unauthorized));
            await pending;
            Assert.Equal(original, navigation.Uri);
        }
        finally { handler.Release.TrySetResult(new HttpResponseMessage(HttpStatusCode.Unauthorized)); await pending.WaitAsync(TimeSpan.FromSeconds(5)); page.Dispose(); }
    }

    [Fact]
    public async Task ChangedSupplierDuringPreflightCannotDispatchOldWrite()
    {
        using var context = new BunitContext();
        context.Services.AddLocalization();
        var handler = new DelayedSessionHandler();
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://localhost") };
        var page = CreatePage(context, http);
        page.Id = 1;
        var pending = Save(page);
        try
        {
            await handler.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            page.Id = 2;
            handler.Release.TrySetResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"csrfToken\":\"test-token\"}", Encoding.UTF8, "application/json"),
            });
            await pending.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(0, handler.Writes);
        }
        finally
        {
            handler.Release.TrySetResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
            await pending.WaitAsync(TimeSpan.FromSeconds(5));
            page.Dispose();
        }
    }

    [Fact]
    public async Task SameSupplierReloadDuringPreflightDoesNotInventUncertainWrite()
    {
        using var context = new BunitContext();
        context.Services.AddLocalization();
        var handler = new DelayedSessionHandler();
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://localhost") };
        var page = CreatePage(context, http);
        page.Id = 1;
        var pending = Save(page);
        try
        {
            await handler.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await Load(page);
            handler.Release.TrySetResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"csrfToken\":\"test-token\"}", Encoding.UTF8, "application/json"),
            });
            await pending.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(0, handler.Writes);
            Assert.False((bool)typeof(SupplierView).GetField("writeOutcomeUnknown", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(page)!);
        }
        finally { handler.Release.TrySetResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)); await pending.WaitAsync(TimeSpan.FromSeconds(5)); page.Dispose(); }
    }

    [Fact]
    public async Task DispatchedWriteKeepsSnapshotAndCannotNavigateAfterDisposal()
    {
        using var context = new BunitContext();
        context.Services.AddLocalization();
        var handler = new DelayedWriteHandler();
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://localhost") };
        var page = CreatePage(context, http);
        page.Id = 1;
        var model = (SupplierCreateRequest)typeof(SupplierView).GetField("model", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(page)!;
        model.Name = "First";
        var original = context.Services.GetRequiredService<NavigationManager>().Uri;
        var pending = Save(page);
        try
        {
            await handler.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            model.Name = "Changed";
            page.Id = 2;
            page.Dispose();
            var body = await handler.Request!.Content!.ReadAsStringAsync();
            Assert.Contains("\"name\":\"First\"", body, StringComparison.Ordinal);
            Assert.DoesNotContain("Changed", body, StringComparison.Ordinal);
            handler.Release.TrySetResult(new HttpResponseMessage(HttpStatusCode.NoContent));
            await pending.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(1, handler.Writes);
            Assert.Equal(original, context.Services.GetRequiredService<NavigationManager>().Uri);
        }
        finally
        {
            handler.Release.TrySetResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
            await pending.WaitAsync(TimeSpan.FromSeconds(5));
            page.Dispose();
        }
    }

    [Theory]
    [InlineData(false, HttpStatusCode.Unauthorized)]
    [InlineData(false, HttpStatusCode.Conflict)]
    [InlineData(false, HttpStatusCode.TooManyRequests)]
    [InlineData(false, HttpStatusCode.ServiceUnavailable)]
    [InlineData(true, HttpStatusCode.Unauthorized)]
    [InlineData(true, HttpStatusCode.Conflict)]
    [InlineData(true, HttpStatusCode.TooManyRequests)]
    [InlineData(true, HttpStatusCode.ServiceUnavailable)]
    public async Task UncertainWriteCannotBeRepeated(bool delete, HttpStatusCode status)
    {
        using var context = new BunitContext();
        context.Services.AddLocalization();
        var handler = new FailedWriteHandler(status);
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://localhost") };
        var page = CreatePage(context, http);
        page.Id = 1;
        if (delete) { await OpenDelete(page); await Delete(page); await OpenDelete(page); await Delete(page); }
        else { await Save(page); await Save(page); }
        Assert.Equal(1, handler.Writes);
        Assert.True((bool)typeof(SupplierView).GetField("writeOutcomeUnknown", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(page)!);
        page.Dispose();
    }

    [Fact]
    public async Task DeleteConfirmationCannotApplyToAnotherSupplier()
    {
        using var context = new BunitContext();
        context.Services.AddLocalization();
        var handler = new FailedWriteHandler(HttpStatusCode.NoContent);
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://localhost") };
        var page = CreatePage(context, http);
        page.Id = 1;
        await OpenDelete(page);
        page.Id = 2;
        await Delete(page);
        Assert.Equal(0, handler.Writes);
        page.Dispose();
    }

    [Fact]
    public async Task CanceledDeleteConfirmationCannotBeReplayed()
    {
        using var context = new BunitContext();
        context.Services.AddLocalization();
        var handler = new FailedWriteHandler(HttpStatusCode.NoContent);
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://localhost") };
        var page = CreatePage(context, http);
        page.Id = 1;
        await OpenDelete(page);
        await (Task)typeof(SupplierView).GetMethod("HandleDeleteDialogChangedAsync", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(page, [false])!;
        await Delete(page);
        Assert.Equal(0, handler.Writes);
        page.Dispose();
    }

    [Fact]
    public async Task ChangedFormDuringDispatchedSaveRequiresReload()
    {
        using var context = new BunitContext();
        context.Services.AddLocalization();
        var handler = new DelayedWriteHandler();
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://localhost") };
        var page = CreatePage(context, http);
        page.Id = 1;
        var model = (SupplierCreateRequest)typeof(SupplierView).GetField("model", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(page)!;
        model.Name = "First";
        var pending = Save(page);
        try
        {
            await handler.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            model.Name = "Changed";
            handler.Release.TrySetResult(new HttpResponseMessage(HttpStatusCode.NoContent));
            await pending.WaitAsync(TimeSpan.FromSeconds(5));
            await Save(page);
            Assert.Equal(1, handler.Writes);
            Assert.True((bool)typeof(SupplierView).GetField("writeOutcomeUnknown", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(page)!);
        }
        finally { handler.Release.TrySetResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)); await pending.WaitAsync(TimeSpan.FromSeconds(5)); page.Dispose(); }
    }

    [Fact]
    public async Task PostDispatchTransportFailureRequiresReload()
    {
        using var context = new BunitContext();
        context.Services.AddLocalization();
        var handler = new ThrowingWriteHandler();
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://localhost") };
        var page = CreatePage(context, http);
        page.Id = 1;
        await Save(page);
        await Save(page);
        Assert.Equal(1, handler.Writes);
        Assert.True((bool)typeof(SupplierView).GetField("writeOutcomeUnknown", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(page)!);
        page.Dispose();
    }

    private static SupplierView CreatePage(BunitContext context, HttpClient http)
    {
        var page = new SupplierView();
        Set(page, "Http", http);
        Set(page, "Text", context.Services.GetRequiredService<IStringLocalizer<SupplierView>>());
        Set(page, "Navigation", context.Services.GetRequiredService<NavigationManager>());
        Set(page, "Logger", NullLogger<SupplierView>.Instance);
        return page;
    }
    private static void Set(SupplierView page, string name, object value) =>
        typeof(SupplierView).GetProperty(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(page, value);
    private static Task Load(SupplierView page) =>
        (Task)typeof(SupplierView).GetMethod("LoadAsync", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(page, null)!;
    private static Task Save(SupplierView page) =>
        (Task)typeof(SupplierView).GetMethod("SaveAsync", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(page, null)!;
    private static Task Delete(SupplierView page) =>
        (Task)typeof(SupplierView).GetMethod("DeleteAsync", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(page, null)!;
    private static Task OpenDelete(SupplierView page) =>
        (Task)typeof(SupplierView).GetMethod("HandleDeleteDialogChangedAsync", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(page, [true])!;
    private static HttpResponseMessage Supplier(int id) => new(HttpStatusCode.OK)
    {
        Content = new StringContent($$"""{"id":{{id}},"name":"Supplier","address1":"Test","countryId":1}""", Encoding.UTF8, "application/json"),
    };
    private sealed class DelayedSupplierHandler : HttpMessageHandler
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<HttpResponseMessage> Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.RequestUri!.AbsolutePath.EndsWith("/1", StringComparison.Ordinal)) { Started.TrySetResult(); return Release.Task; }
            return Task.FromResult(Supplier(2));
        }
    }
    private sealed class DelayedSessionHandler : HttpMessageHandler
    {
        public int Writes { get; private set; }
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<HttpResponseMessage> Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.RequestUri!.AbsolutePath == "/bff/session") { Started.TrySetResult(); return Release.Task; }
            if (request.Method == HttpMethod.Get) return Task.FromResult(Supplier(1));
            Writes++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NoContent));
        }
    }
    private sealed class DelayedWriteHandler : HttpMessageHandler
    {
        public int Writes { get; private set; }
        public HttpRequestMessage? Request { get; private set; }
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<HttpResponseMessage> Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.RequestUri!.AbsolutePath == "/bff/session")
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{\"csrfToken\":\"test-token\"}", Encoding.UTF8, "application/json") });
            Writes++;
            Request = request;
            Started.TrySetResult();
            return Release.Task;
        }
    }
    private sealed class FailedWriteHandler(HttpStatusCode status) : HttpMessageHandler
    {
        public int Writes { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.RequestUri!.AbsolutePath == "/bff/session")
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{\"csrfToken\":\"test-token\"}", Encoding.UTF8, "application/json") });
            Writes++;
            return Task.FromResult(new HttpResponseMessage(status));
        }
    }
    private sealed class ThrowingWriteHandler : HttpMessageHandler
    {
        public int Writes { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.RequestUri!.AbsolutePath == "/bff/session")
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{\"csrfToken\":\"test-token\"}", Encoding.UTF8, "application/json") });
            Writes++;
            throw new HttpRequestException("synthetic failure");
        }
    }
}
