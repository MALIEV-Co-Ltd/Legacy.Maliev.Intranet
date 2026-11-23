using System.Net;
using System.Reflection;
using System.Text;
using Bunit;
using PurchaseOrdersPage = Legacy.Maliev.Intranet.Client.Features.Procurement.Pages.PurchaseOrders;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;

namespace Legacy.Maliev.Intranet.Tests;

public sealed class PurchaseOrdersResponseLifetimeTests
{
    [Theory]
    [InlineData("[null]")]
    [InlineData("[{\"id\":1},{\"id\":1}]")]
    public async Task InvalidEmployeeRowsAreRejected(string items)
    {
        using var context = new BunitContext();
        context.Services.AddLocalization();
        using var handler = new ResponsesHandler(items, failEmployees: false);
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://localhost") };
        var page = CreatePage(context, http);

        await Load(page);

        Assert.NotNull(Field(page, "error"));
        Assert.Null(Field(page, "page"));
        Assert.True(handler.OrdersContent.Disposed);
    }

    [Fact]
    public async Task SuccessfulSiblingResponseIsDisposedWhenOtherRequestFails()
    {
        using var context = new BunitContext();
        context.Services.AddLocalization();
        using var handler = new ResponsesHandler("[]", failEmployees: true);
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://localhost") };
        var page = CreatePage(context, http);

        await Load(page);

        Assert.NotNull(Field(page, "error"));
        Assert.True(handler.OrdersContent.Disposed);
    }

    private static PurchaseOrdersPage CreatePage(BunitContext context, HttpClient http)
    {
        var page = new PurchaseOrdersPage();
        typeof(PurchaseOrdersPage).GetProperty("Http", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(page, http);
        typeof(PurchaseOrdersPage).GetProperty("Text", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(page, context.Services.GetRequiredService<IStringLocalizer<PurchaseOrdersPage>>());
        return page;
    }
    private static object? Field(PurchaseOrdersPage page, string name) => typeof(PurchaseOrdersPage).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(page);
    private static Task Load(PurchaseOrdersPage page) => (Task)typeof(PurchaseOrdersPage).GetMethod("LoadAsync", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(page, null)!;

    private sealed class TrackedContent(string text) : StringContent(text, Encoding.UTF8, "application/json")
    {
        public bool Disposed { get; private set; }
        protected override void Dispose(bool disposing) { if (disposing) Disposed = true; base.Dispose(disposing); }
    }

    private sealed class ResponsesHandler(string employeeItems, bool failEmployees) : HttpMessageHandler
    {
        public TrackedContent OrdersContent { get; } = new("{\"items\":[],\"pageIndex\":1,\"totalPages\":0,\"totalRecords\":0}");
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.RequestUri!.AbsolutePath == "/bff/employees")
            {
                if (failEmployees) return Task.FromException<HttpResponseMessage>(new HttpRequestException("simulated"));
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{\"items\":" + employeeItems + ",\"pageIndex\":1,\"totalPages\":1,\"totalRecords\":2}", Encoding.UTF8, "application/json") });
            }
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = OrdersContent });
        }
    }
}
