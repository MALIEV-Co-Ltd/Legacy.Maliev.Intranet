using System.Net;
using System.Net.Http.Json;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using CustomerListPage = Legacy.Maliev.Intranet.Client.Features.Customers.Pages.Customers;

namespace Legacy.Maliev.Intranet.Tests;

public sealed class CustomerListEmailLinkTests
{
    [Theory]
    [InlineData("customer@example.test", "mailto:customer@example.test")]
    [InlineData("customer+sales@example.test", "mailto:customer%2Bsales@example.test")]
    [InlineData("billing%team@example.test", "mailto:billing%25team@example.test")]
    public void CustomerEmail_IsAnEncodedMailLink(string email, string expectedHref)
    {
        using var context = new BunitContext();
        context.Services.AddLocalization();
        context.Services.AddLogging();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        using var http = new HttpClient(new CustomerHandler(email)) { BaseAddress = new Uri("https://localhost") };
        context.Services.AddSingleton(http);
        var page = context.Render<CustomerListPage>();
        page.WaitForAssertion(() => Assert.Contains(page.FindAll("a[href]"), item => item.GetAttribute("href") == expectedHref));
        var link = Assert.Single(page.FindAll("a[href]"), item => item.GetAttribute("href") == expectedHref);
        Assert.Equal(email, link.TextContent.Trim());
    }

    [Theory]
    [InlineData("customer@example.test?subject=unsafe")]
    [InlineData("not-an-address")]
    public void InvalidEmail_IsPlainText(string email)
    {
        using var context = new BunitContext();
        context.Services.AddLocalization();
        context.Services.AddLogging();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        using var http = new HttpClient(new CustomerHandler(email)) { BaseAddress = new Uri("https://localhost") };
        context.Services.AddSingleton(http);
        var page = context.Render<CustomerListPage>();
        page.WaitForAssertion(() => Assert.Equal(email, page.Find("td[data-column='email']").TextContent.Trim()));
        Assert.Empty(page.FindAll("a[href^='mailto:']"));
    }

    [Fact]
    public void MissingEmail_IsShownAsDashWithoutMailLink()
    {
        using var context = new BunitContext();
        context.Services.AddLocalization();
        context.Services.AddLogging();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        using var http = new HttpClient(new CustomerHandler("")) { BaseAddress = new Uri("https://localhost") };
        context.Services.AddSingleton(http);
        var page = context.Render<CustomerListPage>();
        page.WaitForAssertion(() => Assert.Contains("Example", page.Markup, StringComparison.Ordinal));
        Assert.Equal("-", page.Find("td[data-column='email']").TextContent.Trim());
        Assert.Empty(page.FindAll("a[href^='mailto:']"));
    }

    private sealed class CustomerHandler(string email) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new
                {
                    items = new[] { new { id = 42, firstName = "Example", lastName = "Person", fullName = "Example Person", email, company = (object?)null } },
                    pageIndex = 1, totalPages = 1, totalRecords = 1, hasNextPage = false, hasPreviousPage = false,
                }),
            });
    }
}
