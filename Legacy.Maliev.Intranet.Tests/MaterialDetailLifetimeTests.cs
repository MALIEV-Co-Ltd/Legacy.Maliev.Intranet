using System.Net;
using System.Reflection;
using System.Text;
using Bunit;
using Legacy.Maliev.Intranet.Client.Features.Catalog.Pages;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;

namespace Legacy.Maliev.Intranet.Tests;

public sealed class MaterialDetailLifetimeTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SupersededMaterialResponseCannotReplaceNewerEditorOrRedirect(bool unauthorized)
    {
        using var context = new BunitContext();
        context.Services.AddLocalization();
        using var handler = new DelayedMaterialHandler();
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://localhost") };
        var page = CreatePage(context, http);
        var navigation = context.Services.GetRequiredService<NavigationManager>();
        var originalUri = navigation.Uri;
        var older = Load(page);
        try
        {
            await handler.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            page.Id = 42;
            await Load(page).WaitAsync(TimeSpan.FromSeconds(5));
            handler.Complete(unauthorized);
            await older.WaitAsync(TimeSpan.FromSeconds(5));
            var material = typeof(MaterialDetail).GetField("material", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(page);
            Assert.Equal(42, material!.GetType().GetProperty("Id")!.GetValue(material));
            Assert.Equal(originalUri, navigation.Uri);
            Assert.False((bool)typeof(MaterialDetail).GetField("loading", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(page)!);
        }
        finally { handler.Complete(unauthorized); await older.WaitAsync(TimeSpan.FromSeconds(5)); page.Dispose(); }
    }

    [Fact]
    public async Task MismatchedMaterialResponseIsNotShownOrCopiedIntoEditor()
    {
        using var context = new BunitContext();
        context.Services.AddLocalization();
        using var handler = new DelayedMaterialHandler { WrongDetail = true };
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://localhost") };
        var page = CreatePage(context, http);
        page.Id = 42;
        await Load(page).WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Null(typeof(MaterialDetail).GetField("material", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(page));
        Assert.NotNull(typeof(MaterialDetail).GetField("error", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(page));
        page.Dispose();
    }

    [Fact]
    public async Task DisposedEditorCannotRedirectAfterUnauthorizedResponse()
    {
        using var context = new BunitContext();
        context.Services.AddLocalization();
        using var handler = new DelayedMaterialHandler();
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://localhost") };
        var page = CreatePage(context, http);
        var navigation = context.Services.GetRequiredService<NavigationManager>();
        var originalUri = navigation.Uri;
        var pending = Load(page);
        try
        {
            await handler.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            page.Dispose();
            handler.Complete(true);
            await pending.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(originalUri, navigation.Uri);
        }
        finally { handler.Complete(true); await pending.WaitAsync(TimeSpan.FromSeconds(5)); page.Dispose(); }
    }

    private static MaterialDetail CreatePage(BunitContext context, HttpClient http)
    {
        var page = new MaterialDetail { Id = 41 };
        Set(page, "Http", http);
        Set(page, "Text", context.Services.GetRequiredService<IStringLocalizer<MaterialDetail>>());
        Set(page, "Navigation", context.Services.GetRequiredService<NavigationManager>());
        return page;
    }

    private static void Set(MaterialDetail page, string name, object value) =>
        typeof(MaterialDetail).GetProperty(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(page, value);
    private static Task Load(MaterialDetail page) =>
        (Task)typeof(MaterialDetail).GetMethod("LoadAsync", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(page, null)!;

    private sealed class DelayedMaterialHandler : HttpMessageHandler
    {
        public bool WrongDetail { get; init; }
        private readonly TaskCompletionSource<HttpResponseMessage> first = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public void Complete(bool unauthorized) => first.TrySetResult(unauthorized
            ? new HttpResponseMessage(HttpStatusCode.Unauthorized) : Detail(41));

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path == "/bff/catalog/materials/41") { Started.TrySetResult(); return first.Task; }
            return Task.FromResult(path == "/bff/catalog/materials/42" ? Detail(WrongDetail ? 41 : 42) : Json("[]"));
        }

        private static HttpResponseMessage Detail(int id) => Json($$"""{"id":{{id}},"materialGroupId":7,"machinable":true,"printable":false,"name":"Material"}""");
        private static HttpResponseMessage Json(string body) => new(HttpStatusCode.OK)
        { Content = new StringContent(body, Encoding.UTF8, "application/json") };
    }
}
