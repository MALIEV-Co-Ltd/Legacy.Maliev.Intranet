using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using Bunit;
using Legacy.Maliev.Intranet.Contracts;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Routing;
using Microsoft.Extensions.DependencyInjection;
using RequestView = Legacy.Maliev.Intranet.Client.Features.Quotations.Pages.QuotationRequests.View;

namespace Legacy.Maliev.Intranet.Tests;

public sealed class QuotationRequestFinderSummaryComponentTests : BunitContext
{
    [Theory]
    [InlineData("en", "Service finder context", "Physical part available", "Strength and durability", "Outdoor or UV exposure", "3D scanning → 3D design")]
    [InlineData("th", "ข้อมูลจากตัวช่วยเลือกบริการ", "มีชิ้นงานจริง", "ความแข็งแรงและความทนทาน", "กลางแจ้งหรือแสงยูวี", "สแกนสามมิติ → ออกแบบสามมิติ")]
    public void FinderSummary_RendersLocalizedValidatedContextAndEncodesOperatorNote(
        string culture, string title, string files, string performance, string environment, string path)
    {
        var previousCulture = CultureInfo.CurrentUICulture;
        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(culture);
        try
        {
            var original = QuotationRequestFinderEnvelopeHttpTests.Envelope.Replace(
                "original staff note", "<img src=x onerror=alert(1)>", StringComparison.Ordinal);
            var cut = RenderPage(original);
            cut.WaitForAssertion(() => Assert.NotNull(cut.Find("#request-internal")));

            var summary = cut.Find("[data-service-finder-context]");
            Assert.Contains(title, summary.TextContent, StringComparison.Ordinal);
            Assert.Contains(files, summary.TextContent, StringComparison.Ordinal);
            Assert.Contains(performance, summary.TextContent, StringComparison.Ordinal);
            Assert.Contains(environment, summary.TextContent, StringComparison.Ordinal);
            Assert.Contains(path, summary.TextContent, StringComparison.Ordinal);
            Assert.DoesNotContain("future_context", summary.TextContent, StringComparison.Ordinal);
            Assert.Empty(cut.FindAll("img,script"));
            Assert.Equal("<img src=x onerror=alert(1)>", cut.Find("#request-internal").TextContent);
            Assert.Contains("&lt;img", cut.Markup, StringComparison.Ordinal);
        }
        finally { CultureInfo.CurrentUICulture = previousCulture; }
    }

    [Theory]
    [MemberData(nameof(QuotationRequestFinderEnvelopeHttpTests.InvalidEnvelopes), MemberType = typeof(QuotationRequestFinderEnvelopeHttpTests))]
    public void InvalidEnvelope_ExposesOnlyOrdinaryTextWithoutFinderSummary(string original)
    {
        var cut = RenderPage(original);
        cut.WaitForAssertion(() => Assert.NotNull(cut.Find("#request-internal")));

        Assert.Empty(cut.FindAll("[data-service-finder-context]"));
        Assert.Equal(original, cut.Find("#request-internal").TextContent);
    }

    private IRenderedComponent<Router> RenderPage(string original)
    {
        Services.AddLocalization();
        Services.AddSingleton(new HttpClient(new PageBoundary(original)) { BaseAddress = new("https://localhost/") });
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.GetRequiredService<NavigationManager>().NavigateTo("/QuotationRequests/View?id=9");
        return Render<Router>(parameters => parameters
            .Add(router => router.AppAssembly, typeof(RequestView).Assembly)
            .Add(router => router.Found, (RenderFragment<RouteData>)(route => builder =>
            {
                builder.OpenComponent<RouteView>(0);
                builder.AddAttribute(1, nameof(RouteView.RouteData), route);
                builder.CloseComponent();
            })));
    }

    private sealed class PageBoundary(string original) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(request.RequestUri!.AbsolutePath switch
            {
                "/bff/session" => Json(new EmployeeSessionSummary(true, "employee", "Fixture", ["Employee"], "fixture-csrf", 7, [])),
                "/bff/quotation-requests/9" => Json(new QuotationRequestDetail(
                    new QuotationRequestItem(9, "Buyer", "Fixture", "buyer@example.test", null, "TH", null, null, "part review", original, false,
                        new DateTime(2030, 7, 18, 0, 0, 0, DateTimeKind.Utc), new DateTime(2030, 7, 18, 8, 30, 0, DateTimeKind.Utc)), [])),
                "/bff/quotation-requests/9/qualification-receipt" => new HttpResponseMessage(HttpStatusCode.NotFound),
                _ => throw new InvalidOperationException($"Unexpected component boundary: {request.Method} {request.RequestUri}"),
            });

        private static HttpResponseMessage Json<T>(T value) => new(HttpStatusCode.OK) { Content = JsonContent.Create(value) };
    }
}
