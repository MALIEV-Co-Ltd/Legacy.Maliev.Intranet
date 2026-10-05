using System.Globalization;
using System.Net;
using Bunit;
using Legacy.Maliev.Intranet.Client.Features.Accounting.Pages;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Legacy.Maliev.Intranet.Tests;

public sealed class InvoiceCreationBffGuardTests
{
    [Theory]
    [InlineData("{\"invoiceId\":55,\"emailState\":0}")]
    [InlineData("{\"invoiceId\":55,\"state\":0}")]
    [InlineData("{\"invoiceId\":55,\"state\":99,\"state\":0,\"emailState\":0}")]
    [InlineData("{\"invoiceId\":55,\"state\":0,\"EmailState\":2,\"emailState\":0}")]
    [InlineData("{\"invoiceId\":55,\"state\":99,\"emailState\":0}")]
    [InlineData("{\"invoiceId\":55,\"state\":0,\"emailState\":99}")]
    public async Task Create_RealBffCannotTurnUnconfirmedUpstreamReceiptIntoBrowserSuccess(string receipt)
    {
        using var culture = new EnglishCultureScope();
        using var upstream = AccountingBehaviorTestHost.Routes(request =>
            request.Method == HttpMethod.Get && request.RequestUri?.AbsolutePath == "/invoices/from-quotation/84/preview"
                ? AccountingBehaviorTestHost.Json(InvoiceCreationUpstreamReceiptTests.PreviewJson)
                : request.Method == HttpMethod.Post && request.RequestUri?.AbsolutePath == "/invoices/from-quotation/84"
                    ? AccountingBehaviorTestHost.Json(receipt)
                    : new(HttpStatusCode.NotFound));
        await using var factory = AccountingBehaviorTestHost.CreateFactory(upstream);
        using var client = AccountingBehaviorTestHost.CreateClient(factory);
        await AccountingBehaviorTestHost.SignInAsync(client);
        using var context = new BunitContext();
        context.Services.AddLocalization();
        context.Services.AddSingleton(client);
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        var navigation = context.Services.GetRequiredService<NavigationManager>();
        navigation.NavigateTo("/Invoices/Create?quotationId=84");
        var cut = context.Render<Router>(parameters => parameters
            .Add(router => router.AppAssembly, typeof(InvoiceCreate).Assembly)
            .Add(router => router.Found, (RenderFragment<RouteData>)(route => builder =>
            {
                builder.OpenComponent<RouteView>(0);
                builder.AddAttribute(1, nameof(RouteView.RouteData), route);
                builder.CloseComponent();
            })));
        cut.WaitForAssertion(() => Assert.NotNull(cut.Find("#invoice-number")));

        cut.Find("form").Submit();

        cut.WaitForAssertion(() =>
        {
            Assert.Single(upstream.Requests, request => request.Method == "POST");
            Assert.True(cut.Markup.Contains("We could not confirm the invoice outcome.", StringComparison.Ordinal)
                || cut.Markup.Contains("Accounting returned an unconfirmed invoice outcome.", StringComparison.Ordinal));
            Assert.Contains("/Invoices/Create", navigation.Uri, StringComparison.Ordinal);
            Assert.DoesNotContain(context.JSInterop.Invocations, invocation => invocation.Identifier == "sessionStorage.removeItem");
            Assert.Contains(context.JSInterop.Invocations, invocation => invocation.Identifier == "sessionStorage.setItem");
        });
    }

    private sealed class EnglishCultureScope : IDisposable
    {
        private readonly CultureInfo previous = CultureInfo.CurrentCulture;
        private readonly CultureInfo previousUi = CultureInfo.CurrentUICulture;

        public EnglishCultureScope()
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-US");
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("en-US");
        }

        public void Dispose()
        {
            CultureInfo.CurrentCulture = previous;
            CultureInfo.CurrentUICulture = previousUi;
        }
    }
}
