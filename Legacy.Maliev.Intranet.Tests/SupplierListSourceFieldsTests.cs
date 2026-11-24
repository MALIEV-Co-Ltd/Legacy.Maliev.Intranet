using System.Net;
using System.Net.Http.Json;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using SupplierPage = Legacy.Maliev.Intranet.Client.Features.Procurement.Pages.Suppliers;

namespace Legacy.Maliev.Intranet.Tests;

public sealed class SupplierListSourceFieldsTests
{
    [Fact]
    public void SupplierWebsiteWithMarkupCharactersRendersAsSafeLinkAndText()
    {
        const string website = "https://supplier.example/path?q=<script>";
        using var context = new BunitContext();
        context.Services.AddLocalization();
        context.Services.AddLogging();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        using var handler = new SupplierHandler(website);
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://localhost") };
        context.Services.AddSingleton(http);

        var page = context.Render<SupplierPage>();
        page.WaitForAssertion(() => Assert.Contains("Example", page.Markup, StringComparison.Ordinal));
        var cell = page.Find("td[data-column='website']");
        var link = cell.QuerySelector("a");
        Assert.NotNull(link);
        Assert.Equal("https://supplier.example/path?q=%3Cscript%3E", link.GetAttribute("href"));
        Assert.Equal(website, link.TextContent);
        Assert.Empty(cell.QuerySelectorAll("script"));
    }

    [Theory]
    [InlineData("https://supplier.example/path", true)]
    [InlineData("http://supplier.example/path", true)]
    [InlineData("javascript:alert(1)", false)]
    [InlineData("https://user:password@supplier.example/path", false)]
    [InlineData("/relative", false)]
    [InlineData("", false)]
    [InlineData("<img src=x onerror=alert(1)>", false)]
    public void SupplierWebsiteLinksOnlyToSafeHttpUrls(string website, bool linked)
    {
        using var context = new BunitContext();
        context.Services.AddLocalization();
        context.Services.AddLogging();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        using var handler = new SupplierHandler(website);
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://localhost") };
        context.Services.AddSingleton(http);

        var page = context.Render<SupplierPage>();
        page.WaitForAssertion(() => Assert.Contains("089-000-0000", page.Markup, StringComparison.Ordinal));
        Assert.Contains("02-987-6543", page.Markup, StringComparison.Ordinal);
        var links = page.FindAll("a[href]").Where(link => link.GetAttribute("href") == website).ToArray();
        if (linked)
        {
            var link = Assert.Single(links);
            Assert.Equal("_blank", link.GetAttribute("target"));
            Assert.Equal("noopener noreferrer", link.GetAttribute("rel"));
        }
        else
        {
            Assert.Empty(links);
            if (website.Length == 0)
            {
                Assert.Equal("-", page.Find("td[data-column='website']").TextContent.Trim());
            }
            if (website.StartsWith("<img", StringComparison.Ordinal))
            {
                Assert.Empty(page.FindAll("img[src='x']"));
                Assert.Contains("&lt;img", page.Markup, StringComparison.Ordinal);
            }
            if (website.Contains("user:password", StringComparison.Ordinal))
            {
                Assert.DoesNotContain("user:password", page.Markup, StringComparison.Ordinal);
            }
        }
    }

    private sealed class SupplierHandler(string website) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new
                {
                    items = new[] { new { id = 42, name = "Example", email = "supplier@example.test", telephone = "02-123-4567", website, mobile = "089-000-0000", fax = "02-987-6543" } },
                    pageIndex = 1,
                    totalPages = 1,
                    totalRecords = 1,
                    hasNextPage = false,
                    hasPreviousPage = false,
                }),
            });
    }
}
