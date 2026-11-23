using System.Net;
using System.Reflection;
using System.Text;
using System.Text.Json;
using Bunit;
using Legacy.Maliev.Intranet.Client.Features.Catalog.Pages;
using Legacy.Maliev.Intranet.Contracts;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;

namespace Legacy.Maliev.Intranet.Tests;

public sealed class MaterialDetailSaveLifetimeTests
{
    [Fact]
    public async Task DisposedDuringSessionPreflightCannotDispatchWrite()
    {
        using var handler = new SaveHandler("session");
        using var fixture = new Fixture(handler);
        var pending = fixture.Submit();
        try
        {
            await handler.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            fixture.Page.Dispose();
            handler.Release.TrySetResult();
            await pending.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(0, handler.MaterialWrites);
            Assert.Equal(fixture.OriginalUri, fixture.Navigation.Uri);
        }
        finally { handler.Release.TrySetResult(); await pending.WaitAsync(TimeSpan.FromSeconds(5)); }
    }

    [Fact]
    public async Task HeldPutSerializesValidatedSnapshotAfterModelChanges()
    {
        using var handler = new SaveHandler("put");
        using var fixture = new Fixture(handler);
        var pending = fixture.Submit();
        try
        {
            await handler.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            fixture.Model.Name = "Changed after dispatch";
            handler.Release.TrySetResult();
            await pending.WaitAsync(TimeSpan.FromSeconds(5));
            using var body = JsonDocument.Parse(handler.Body!);
            Assert.Equal("Original", body.RootElement.GetProperty("name").GetString());
            Assert.Equal(1, handler.MaterialWrites);
            Assert.Equal(0, handler.AssociationWrites);
            Assert.Null(Field(fixture.Page, "notification"));
            Assert.True((bool)Field(fixture.Page, "saveRequiresReload")!);
            await fixture.Submit();
            Assert.Equal(1, handler.MaterialWrites);
        }
        finally { handler.Release.TrySetResult(); await pending.WaitAsync(TimeSpan.FromSeconds(5)); }
    }

    [Theory]
    [InlineData("ambiguous-503")]
    [InlineData("ambiguous-429")]
    [InlineData("ambiguous-409")]
    public async Task AmbiguousServerFailureRequiresReloadBeforeAnotherWrite(string outcome)
    {
        using var handler = new SaveHandler(outcome);
        using var fixture = new Fixture(handler);
        await fixture.Submit().WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(1, handler.MaterialWrites);
        Assert.True((bool)Field(fixture.Page, "saveRequiresReload")!);
        Assert.NotNull(Field(fixture.Page, "error"));
        await fixture.Submit();
        Assert.Equal(1, handler.MaterialWrites);
        Assert.Equal(0, handler.AssociationWrites);
    }

    [Fact]
    public async Task ReloadBeforeLateCommitDoesNotPermitAnotherWrite()
    {
        using var handler = new LateCommitHandler();
        using var fixture = new Fixture(handler);
        await fixture.Submit().WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(1, handler.MaterialWrites);
        Assert.False(handler.Committed);

        await fixture.Load().WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal("Before", handler.LastReadName);
        Assert.True((bool)Field(fixture.Page, "saveRequiresReload")!);
        await fixture.Submit().WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(1, handler.MaterialWrites);

        handler.Commit();
        Assert.True(handler.Committed);
        Assert.Equal("Original", handler.ServerName);
        await fixture.Submit().WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(1, handler.MaterialWrites);
    }

    [Fact]
    public async Task SupersededAssociationWriteIsNotFollowedByAnotherWrite()
    {
        using var handler = new SaveHandler("association");
        using var fixture = new Fixture(handler);
        ((HashSet<int>)Field(fixture.Page, "selectedColorIds")!).UnionWith([1, 2]);
        var pending = fixture.Submit();
        try
        {
            await handler.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            fixture.Page.Id = 42;
            handler.Release.TrySetResult();
            await pending.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(1, handler.MaterialWrites);
            Assert.Equal(1, handler.AssociationWrites);
            Assert.Equal(fixture.OriginalUri, fixture.Navigation.Uri);
            Assert.Null(Field(fixture.Page, "notification"));
        }
        finally { handler.Release.TrySetResult(); await pending.WaitAsync(TimeSpan.FromSeconds(5)); }
    }

    private static object? Field(MaterialDetail page, string name) =>
        typeof(MaterialDetail).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(page);

    private sealed class Fixture : IDisposable
    {
        private readonly BunitContext context = new();
        private readonly HttpClient http;
        public MaterialDetail Page { get; } = new() { Id = 41 };
        public CatalogMaterialUpsertRequest Model { get; }
        public NavigationManager Navigation { get; }
        public string OriginalUri { get; }
        public Fixture(HttpMessageHandler handler)
        {
            context.Services.AddLocalization();
            http = new HttpClient(handler) { BaseAddress = new Uri("https://localhost") };
            Navigation = context.Services.GetRequiredService<NavigationManager>();
            OriginalUri = Navigation.Uri;
            SetProperty("Http", http);
            SetProperty("Text", context.Services.GetRequiredService<IStringLocalizer<MaterialDetail>>());
            SetProperty("Navigation", Navigation);
            Model = (CatalogMaterialUpsertRequest)Field(Page, "model")!;
            Model.MaterialGroupId = 1;
            Model.Name = "Original";
            SetField("editContext", new EditContext(Model));
        }
        public Task Submit() => (Task)typeof(MaterialDetail).GetMethod("SubmitAsync", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(Page, null)!;
        public Task Load() => (Task)typeof(MaterialDetail).GetMethod("LoadAsync", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(Page, null)!;
        private void SetProperty(string name, object value) => typeof(MaterialDetail).GetProperty(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(Page, value);
        private void SetField(string name, object value) => typeof(MaterialDetail).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(Page, value);
        public void Dispose() { Page.Dispose(); http.Dispose(); context.Dispose(); }
    }

    private sealed class LateCommitHandler : HttpMessageHandler
    {
        public int MaterialWrites { get; private set; }
        public bool Committed { get; private set; }
        public string ServerName => Committed ? "Original" : "Before";
        public string? LastReadName { get; private set; }
        public void Commit() => Committed = true;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.RequestUri!.AbsolutePath == "/bff/session")
                return Task.FromResult(Json("{\"isAuthenticated\":true,\"roles\":[],\"csrfToken\":\"test\"}"));
            if (request.Method == HttpMethod.Put)
            {
                MaterialWrites++;
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.BadGateway));
            }
            if (request.RequestUri.AbsolutePath == "/bff/catalog/materials/41")
            {
                LastReadName = ServerName;
                return Task.FromResult(Json($$"""{"id":41,"materialGroupId":1,"name":"{{LastReadName}}","machinable":true,"printable":false}"""));
            }
            return Task.FromResult(Json("[]"));
        }

        private static HttpResponseMessage Json(string body) => new(HttpStatusCode.OK)
        { Content = new StringContent(body, Encoding.UTF8, "application/json") };
    }

    private sealed class SaveHandler(string hold) : HttpMessageHandler
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int MaterialWrites { get; private set; }
        public int AssociationWrites { get; private set; }
        public string? Body { get; private set; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.RequestUri!.AbsolutePath == "/bff/session")
            {
                if (hold == "session") { Started.TrySetResult(); await Release.Task; }
                return Json("{\"isAuthenticated\":true,\"roles\":[],\"csrfToken\":\"test\"}");
            }
            if (request.Method == HttpMethod.Put)
            {
                MaterialWrites++;
                if (hold == "put") { Started.TrySetResult(); await Release.Task; }
                Body = await request.Content!.ReadAsStringAsync(cancellationToken);
                if (hold == "ambiguous-503") return new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
                if (hold == "ambiguous-429") return new HttpResponseMessage(HttpStatusCode.TooManyRequests);
                if (hold == "ambiguous-409") return new HttpResponseMessage(HttpStatusCode.Conflict);
                return new HttpResponseMessage(HttpStatusCode.NoContent);
            }
            AssociationWrites++;
            if (hold == "association") { Started.TrySetResult(); await Release.Task; }
            return new HttpResponseMessage(HttpStatusCode.NoContent);
        }
        private static HttpResponseMessage Json(string body) => new(HttpStatusCode.OK)
        { Content = new StringContent(body, Encoding.UTF8, "application/json") };
    }
}
