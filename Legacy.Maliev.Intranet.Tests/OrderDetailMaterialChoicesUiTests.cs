using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using Bunit;
using Legacy.Maliev.Intranet.Client.Features.Orders.Pages;
using Legacy.Maliev.Intranet.Contracts;
using Maliev.ShadcnBlazor.Components.Forms;
using Maliev.ShadcnBlazor.Components.Selection;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Legacy.Maliev.Intranet.Tests;

/// <summary>Actual OrderDetail/Shadcn rendering with only the same-origin HTTP boundary controlled.</summary>
public sealed class OrderDetailMaterialChoicesUiTests
{
    [Fact]
    public async Task InitialSelections_NoScopedReadAndUnchangedWritePreservesCustomFinishAndColor()
    {
        using var handler = new Transport();
        using var context = Context(handler);
        var cut = Render(context);
        Ready(cut);
        Assert.Equal(1, Select(cut, "order-material").Instance.Value);
        Assert.Equal(7, Select(cut, "order-finish").Instance.Value);
        Assert.Equal(9, Select(cut, "order-color").Instance.Value);
        Assert.Contains(Select(cut, "order-finish").Instance.Options, item => item.Value == 7);
        Assert.Empty(handler.ScopedReads);
        await cut.Find("form").SubmitAsync();
        var write = Assert.Single(handler.Writes);
        Assert.Equal(1, write.MaterialId);
        Assert.Equal(7, write.SurfaceFinishId);
        Assert.Equal(9, write.ColorId);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MaterialChanged_UsesReadOnlyCatalogRouteAndClearsOnlyIncompatibleFinish(bool retain)
    {
        using var handler = new Transport { RetainFinish = retain };
        using var context = Context(handler);
        var cut = Render(context);
        Ready(cut);
        await Change(cut, 2);
        Ready(cut);
        Assert.Equal(retain ? (int?)7 : null, Select(cut, "order-finish").Instance.Value);
        Assert.Equal(9, Select(cut, "order-color").Instance.Value);
        Assert.Equal(new[] { "/bff/catalog/materials/2/surface-finishes" }, handler.ScopedReads);
        Assert.Contains(Select(cut, "order-finish").Instance.Options, item => item.Value == 42);
        await cut.InvokeAsync(() => Select(cut, "order-finish").Instance.ValueChanged.InvokeAsync(42));
        await cut.Find("form").SubmitAsync();
        var write = Assert.Single(handler.Writes);
        Assert.Equal(2, write.MaterialId);
        Assert.Equal(42, write.SurfaceFinishId);
        Assert.Equal(9, write.ColorId);
    }

    [Fact]
    public async Task EmptyScopedChoices_ClearsIncompatibleFinishPreservesColorAndAllowsSave()
    {
        using var handler = new Transport { Defect = "empty" };
        using var context = Context(handler);
        var cut = Render(context);
        Ready(cut);
        await Change(cut, 2);
        Ready(cut);
        Assert.Empty(Select(cut, "order-finish").Instance.Options);
        Assert.Null(Select(cut, "order-finish").Instance.Value);
        Assert.Equal(9, Select(cut, "order-color").Instance.Value);
        Assert.Single(handler.ScopedReads);
        await cut.Find("form").SubmitAsync();
        var write = Assert.Single(handler.Writes);
        Assert.Equal(2, write.MaterialId);
        Assert.Null(write.SurfaceFinishId);
        Assert.Equal(9, write.ColorId);
    }

    [Fact]
    public async Task MaterialCleared_NoRequestClearsFinishNotColor()
    {
        using var handler = new Transport();
        using var context = Context(handler);
        var cut = Render(context);
        Ready(cut);
        await Change(cut, null);
        Assert.Null(Select(cut, "order-material").Instance.Value);
        Assert.Null(Select(cut, "order-finish").Instance.Value);
        Assert.Equal(9, Select(cut, "order-color").Instance.Value);
        Assert.Empty(handler.ScopedReads);
    }

    [Fact]
    public async Task UnknownMaterial_NeverRequestsOrWritesAndRetainsCapturedSelections()
    {
        using var handler = new Transport();
        using var context = Context(handler);
        var cut = Render(context);
        Ready(cut);
        await Change(cut, 999);
        Assert.Equal(1, Select(cut, "order-material").Instance.Value);
        Assert.Equal(7, Select(cut, "order-finish").Instance.Value);
        Assert.True(cut.Find("button[type='submit']").HasAttribute("disabled"));
        await cut.Find("form").SubmitAsync();
        Assert.Empty(handler.Writes);
        Assert.Empty(handler.ScopedReads);
    }

    [Theory]
    [InlineData("forbidden")]
    [InlineData("unavailable")]
    [InlineData("null")]
    [InlineData("malformed")]
    [InlineData("duplicate")]
    [InlineData("invalid")]
    public async Task FailedChoices_PreservesSelectionsLocksWriteAndRequiresExplicitRetry(string defect)
    {
        using var handler = new Transport { Defect = defect };
        using var context = Context(handler);
        var cut = Render(context);
        Ready(cut);
        await Change(cut, 2);
        Assert.Equal(7, Select(cut, "order-finish").Instance.Value);
        Assert.Equal(9, Select(cut, "order-color").Instance.Value);
        Assert.True(cut.Find("button[type='submit']").HasAttribute("disabled"));
        await cut.Find("form").SubmitAsync();
        Assert.Empty(handler.Writes);
        Assert.Single(handler.ScopedReads);
        handler.Defect = null;
        await cut.Find("[role='alert'] button").ClickAsync();
        Ready(cut);
        Assert.Equal(2, handler.ScopedReads.Count);
        Assert.Null(Select(cut, "order-finish").Instance.Value);
    }

    [Fact]
    public async Task NewerMaterial_CancelsHeldReadAndLateResponseCannotOverwriteScopedChoices()
    {
        using var handler = new Transport { HoldSecondMaterial = true };
        using var context = Context(handler);
        var cut = Render(context);
        Ready(cut);
        var pending = Change(cut, 2);
        try
        {
            await handler.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.True(cut.Find("button[type='submit']").HasAttribute("disabled"));
            await cut.Find("form").SubmitAsync();
            Assert.Empty(handler.Writes);
            await Change(cut, 3);
            Ready(cut);
            Assert.Equal(3, Select(cut, "order-material").Instance.Value);
            Assert.Contains(Select(cut, "order-finish").Instance.Options, item => item.Value == 43);
            Assert.True(handler.HeldToken.IsCancellationRequested);
        }
        finally
        {
            handler.Release.TrySetResult();
            await pending.WaitAsync(TimeSpan.FromSeconds(5));
        }
        Assert.DoesNotContain(Select(cut, "order-finish").Instance.Options, item => item.Value == 42);
        Assert.False(cut.Find("button[type='submit']").HasAttribute("disabled"));
    }

    [Fact]
    public async Task Dispose_CancelsPendingReadWithoutWriteOrLateRender()
    {
        using var handler = new Transport { HoldSecondMaterial = true };
        using var context = Context(handler);
        var cut = Render(context);
        Ready(cut);
        var pending = Change(cut, 2);
        try
        {
            await handler.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await context.DisposeComponentsAsync().WaitAsync(TimeSpan.FromSeconds(5));
            Assert.True(handler.HeldToken.IsCancellationRequested);
        }
        finally
        {
            handler.Release.TrySetResult();
            await pending.WaitAsync(TimeSpan.FromSeconds(5));
        }
        Assert.Empty(handler.Writes);
    }

    private static BunitContext Context(Transport handler)
    {
        var context = new BunitContext();
        context.Services.AddLocalization();
        context.Services.AddSingleton(new HttpClient(handler) { BaseAddress = new("https://localhost/") });
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        context.Services.GetRequiredService<NavigationManager>().NavigateTo("/Orders/View?id=84");
        return context;
    }

    private static IRenderedComponent<Router> Render(BunitContext context) => context.Render<Router>(parameters => parameters
        .Add(router => router.AppAssembly, typeof(OrderDetail).Assembly)
        .Add(router => router.Found, (RenderFragment<RouteData>)(route => builder =>
        {
            builder.OpenComponent<RouteView>(0);
            builder.AddAttribute(1, nameof(RouteView.RouteData), route);
            builder.CloseComponent();
        })));

    private static IRenderedComponent<ShadcnSelect<int?>> Select(IRenderedComponent<Router> cut, string id) =>
        cut.FindComponents<ShadcnSelect<int?>>().Single(item => item.FindAll("#" + id).Count != 0);

    private static Task Change(IRenderedComponent<Router> cut, int? id) =>
        cut.InvokeAsync(() => Select(cut, "order-material").Instance.ValueChanged.InvokeAsync(id));

    private static void Ready(IRenderedComponent<Router> cut) => cut.WaitForAssertion(() =>
        Assert.False(cut.Find("button[type='submit']").HasAttribute("disabled")));

    private sealed class Transport : HttpMessageHandler
    {
        public bool RetainFinish { get; init; }
        public bool HoldSecondMaterial { get; init; }
        public string? Defect { get; set; }
        public ConcurrentQueue<string> ScopedReads { get; } = new();
        public ConcurrentQueue<OrderUpdateRequest> Writes { get; } = new();
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public CancellationToken HeldToken { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.AbsolutePath;
            if (request.Method == HttpMethod.Get && path == "/bff/session")
                return Json(new EmployeeSessionSummary(true, "employee", "Synthetic employee", ["Employee"], "synthetic-csrf"));
            if (request.Method == HttpMethod.Get && path == "/bff/orders/84") return Json(Page());
            if (request.Method == HttpMethod.Put && path == "/bff/orders/84")
            {
                Writes.Enqueue((await request.Content!.ReadFromJsonAsync<OrderUpdateRequest>(cancellationToken: cancellationToken))!);
                return new(HttpStatusCode.NoContent);
            }
            if (request.Method != HttpMethod.Get || path is not ("/bff/catalog/materials/2/surface-finishes" or "/bff/catalog/materials/3/surface-finishes"))
                throw new InvalidOperationException("Unexpected controlled material-choice route.");
            ScopedReads.Enqueue(path);
            var second = path.Contains("/2/", StringComparison.Ordinal);
            if (second && HoldSecondMaterial)
            {
                HeldToken = cancellationToken;
                Entered.TrySetResult();
                // Deliberately allow a late server response after caller cancellation.
                await Release.Task.WaitAsync(TimeSpan.FromSeconds(10));
            }
            return Defect switch
            {
                "forbidden" => new(HttpStatusCode.Forbidden),
                "unavailable" => new(HttpStatusCode.ServiceUnavailable),
                "empty" => Json(Array.Empty<CatalogMaterialSurfaceFinish>()),
                "null" => Raw("null"),
                "malformed" => Raw("{"),
                "duplicate" => Json(new[] { new CatalogMaterialSurfaceFinish(42, "หนึ่ง"), new CatalogMaterialSurfaceFinish(42, "สอง") }),
                "invalid" => Json(new[] { new CatalogMaterialSurfaceFinish(0, "Invalid") }),
                _ => Json(RetainFinish
                    ? new[] { new CatalogMaterialSurfaceFinish(7, "Custom finish"), new CatalogMaterialSurfaceFinish(42, "Scoped finish") }
                    : new[] { new CatalogMaterialSurfaceFinish(second ? 42 : 43, "Scoped finish") }),
            };
        }

        protected override void Dispose(bool disposing)
        {
            Release.TrySetResult();
            base.Dispose(disposing);
        }

        private static HttpResponseMessage Json<T>(T value) => new(HttpStatusCode.OK) { Content = JsonContent.Create(value) };
        private static HttpResponseMessage Raw(string value) => new(HttpStatusCode.OK) { Content = new StringContent(value, Encoding.UTF8, "application/json") };
        private static OrderDetailPage Page() => new(
            new OrderDetailItem(Id: 84, CustomerId: null, EmployeeId: null, Name: "Synthetic order", Description: null,
                ProcessId: 1, MaterialId: 1, SurfaceFinishId: 7, ColorId: 9, Quantity: 1, Manufactured: 0, Remaining: 1,
                UnitPrice: null, DiscountPercent: null, Subtotal: null, CurrencyId: null, LeadTime: null,
                PromisedDate: null, FinishedDate: null, Turnaround: null, Comment: null,
                AllowSocialMedia: false, AllowCancellation: false, AllowPayment: false, TrackingNumber: null,
                CreatedDate: null, ModifiedDate: null),
            [new(1, "Process")], [new(1, "Initial"), new(2, "Second"), new(3, "Third")],
            [new(9, "Independent color")], [new(7, "Custom finish")], [], [], null, [], [], []);
    }
}
