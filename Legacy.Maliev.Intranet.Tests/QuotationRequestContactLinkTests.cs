using System.Net;
using System.Net.Http.Json;
using Bunit;
using Legacy.Maliev.Intranet.Client.Features.Quotations.Pages.QuotationRequests;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Legacy.Maliev.Intranet.Tests;

public sealed class QuotationRequestContactLinkTests
{
    [Theory]
    [InlineData("customer+work@example.test", "+66 (2) 123-4567", "mailto:customer%2Bwork@example.test", "tel:+6621234567")]
    [InlineData("customer@example.test?subject=unsafe", "123?body=unsafe", null, null)]
    [InlineData("", "", null, null)]
    public void PersistedContact_UsesSafeLinks(string email, string phone, string? mailHref, string? phoneHref)
    {
        using var context = new BunitContext();
        context.Services.AddLocalization();
        context.Services.AddLogging();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        using var http = new HttpClient(new Handler(email, phone)) { BaseAddress = new Uri("https://localhost") };
        context.Services.AddSingleton(http);

        context.Services.GetRequiredService<NavigationManager>().NavigateTo("/QuotationRequests/View?id=42");
        var page = context.Render<Router>(parameters => parameters
            .Add(component => component.AppAssembly, typeof(View).Assembly)
            .Add(component => component.Found, (RenderFragment<RouteData>)(data => builder =>
            {
                builder.OpenComponent<RouteView>(0);
                builder.AddAttribute(1, nameof(RouteView.RouteData), data);
                builder.CloseComponent();
            })));
        page.WaitForAssertion(() => Assert.NotEmpty(page.FindAll(".quotation-request-contact")));
        var contact = page.Find(".quotation-request-contact");
        var mail = contact.QuerySelector("a[href^='mailto:']");
        var telephone = contact.QuerySelector("a[href^='tel:']");
        Assert.Equal(mailHref, mail?.GetAttribute("href"));
        Assert.Equal(phoneHref, telephone?.GetAttribute("href"));
        if (email.Length > 0) Assert.Contains(email, contact.TextContent, StringComparison.Ordinal);
        if (phone.Length > 0) Assert.Contains(phone, contact.TextContent, StringComparison.Ordinal);
    }

    private sealed class Handler(string email, string phone) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Assert.Equal(HttpMethod.Get, request.Method);
            if (request.RequestUri!.AbsolutePath == "/bff/session")
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = JsonContent.Create(new { isAuthenticated = true, csrfToken = "test" }),
                });
            if (request.RequestUri!.AbsolutePath == "/bff/quotation-requests/42")
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = JsonContent.Create(new { request = new { id = 42, email, telephoneNumber = phone }, files = Array.Empty<object>() }),
                });
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        }
    }
}
