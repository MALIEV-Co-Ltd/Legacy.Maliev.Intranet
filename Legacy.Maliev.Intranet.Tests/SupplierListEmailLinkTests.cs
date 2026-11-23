using System.Net;
using System.Net.Http.Json;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using SupplierPage = Legacy.Maliev.Intranet.Client.Features.Procurement.Pages.Suppliers;

namespace Legacy.Maliev.Intranet.Tests;

public sealed class SupplierListEmailLinkTests
{
    [Theory]
    [InlineData("supplier@example.test", "mailto:supplier@example.test", "supplier@example.test")]
    [InlineData("supplier+sales@example.test", "mailto:supplier%2Bsales@example.test", "supplier+sales@example.test")]
    [InlineData("supplier@example.test?subject=unsafe", null, "supplier@example.test?subject=unsafe")]
    [InlineData("not-an-address", null, "not-an-address")]
    [InlineData("", null, "-")]
    public void SupplierEmail_UsesSafeMailLinkOrPlainText(string email, string? href, string expected)
    {
        using var context = new BunitContext();
        context.Services.AddLocalization();
        context.Services.AddLogging();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        using var http = new HttpClient(new SupplierHandler(email)) { BaseAddress = new Uri("https://localhost") };
        context.Services.AddSingleton(http);

        var page = context.Render<SupplierPage>();
        page.WaitForAssertion(() => Assert.Equal(expected, page.Find("td[data-column='email']").TextContent.Trim()));
        var links = page.FindAll("td[data-column='email'] a[href^='mailto:']");
        if (href is null) Assert.Empty(links);
        else Assert.Equal(href, Assert.Single(links).GetAttribute("href"));
    }

    private sealed class SupplierHandler(string email) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new
                {
                    items = new[] { new { id = 42, name = "Example", email, telephone = "", website = "", mobile = "", fax = "" } },
                    pageIndex = 1, totalPages = 1, totalRecords = 1, hasNextPage = false, hasPreviousPage = false,
                }),
            });
    }
}
