using System.Reflection;
using Legacy.Maliev.Intranet.Client.Features.Catalog.Pages;
using Legacy.Maliev.Intranet.Contracts;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.Extensions.Localization;

namespace Legacy.Maliev.Intranet.Tests;

public sealed class MaterialDetailValidationRaceTests
{
    [Fact]
    public async Task Create_ChangedInputDuringValidationDoesNotSendAnUnvalidatedRequest()
    {
        var handler = new RejectUnexpectedRequestHandler();
        var page = new MaterialCreate();
        typeof(MaterialCreate).GetProperty("Http", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(page, new HttpClient(handler) { BaseAddress = new Uri("https://localhost") });
        typeof(MaterialCreate).GetProperty("Text", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(page, new KeyLocalizer<MaterialCreate>());
        var model = (CatalogMaterialUpsertRequest)typeof(MaterialCreate).GetField("model", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(page)!;
        model.Name = "Before";
        var context = new EditContext(model);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        context.OnValidationRequested += (_, args) => args.AddAsyncValidator(_ => release.Task);
        typeof(MaterialCreate).GetField("editContext", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(page, context);
        var submit = typeof(MaterialCreate).GetMethod("SubmitAsync", BindingFlags.Instance | BindingFlags.NonPublic)!;

        var pending = Assert.IsAssignableFrom<Task>(submit.Invoke(page, null));
        Assert.False(pending.IsCompleted);
        model.Name = "Changed while validation was pending";
        release.SetResult();
        await pending;

        Assert.Equal(0, handler.Writes);
        Assert.Equal("ValidationFailed", typeof(MaterialCreate).GetField("error", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(page));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task NavigationOrSameIdReloadDuringValidationDoesNotSendMaterialOrAssociationWrites(bool sameIdReload)
    {
        var handler = new RejectUnexpectedRequestHandler();
        var page = new MaterialDetail { Id = 41 };
        typeof(MaterialDetail).GetProperty("Http", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(page, new HttpClient(handler) { BaseAddress = new Uri("https://localhost") });
        typeof(MaterialDetail).GetProperty("Text", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(page, new KeyLocalizer<MaterialDetail>());
        var context = new EditContext(new object());
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        context.OnValidationRequested += (_, args) => args.AddAsyncValidator(_ => release.Task);
        typeof(MaterialDetail).GetField("editContext", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(page, context);
        var submit = typeof(MaterialDetail).GetMethod("SubmitAsync", BindingFlags.Instance | BindingFlags.NonPublic)!;

        var pending = Assert.IsAssignableFrom<Task>(submit.Invoke(page, null));
        Assert.False(pending.IsCompleted);
        await Assert.IsAssignableFrom<Task>(submit.Invoke(page, null));
        if (sameIdReload)
        {
            var reload = typeof(MaterialDetail).GetMethod("LoadAsync", BindingFlags.Instance | BindingFlags.NonPublic)!;
            await Assert.IsAssignableFrom<Task>(reload.Invoke(page, null));
        }
        else page.Id = 42;
        release.SetResult();
        await pending;

        Assert.Equal(0, handler.Writes);
    }

    private sealed class RejectUnexpectedRequestHandler : HttpMessageHandler
    {
        public int Writes { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.Method != HttpMethod.Get) Writes++;
            return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.NotFound));
        }
    }

    private sealed class KeyLocalizer<T> : IStringLocalizer<T>
    {
        public LocalizedString this[string name] => new(name, name);
        public LocalizedString this[string name, params object[] arguments] => new(name, name);
        public IEnumerable<LocalizedString> GetAllStrings(bool includeParentCultures) => [];
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
    }
}
