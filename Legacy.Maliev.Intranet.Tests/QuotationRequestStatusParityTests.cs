using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using RequestIndex = Legacy.Maliev.Intranet.Client.Features.Quotations.Pages.QuotationRequests.Index;

namespace Legacy.Maliev.Intranet.Tests;

public sealed class QuotationRequestStatusParityTests
{
    [Theory]
    [InlineData("en", "Completed", "Ongoing", "Open")]
    [InlineData("th", "ดำเนินการเสร็จแล้ว", "กำลังดำเนินการ", "รอดำเนินการ")]
    public void StatusColumn_PreservesAllThreeSourceStates(string culture, string done, string ongoing, string open)
    {
        var previous = CultureInfo.CurrentUICulture;
        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(culture);
        try
        {
            using var context = new BunitContext();
            context.Services.AddLocalization();
            context.Services.AddLogging();
            context.JSInterop.Mode = JSRuntimeMode.Loose;
            using var handler = new RequestHandler();
            using var http = new HttpClient(handler) { BaseAddress = new Uri("https://localhost") };
            context.Services.AddSingleton(http);
            var page = context.Render<RequestIndex>();
            page.WaitForAssertion(() => Assert.Equal(3, page.FindAll("td[data-column='status']").Count));
            var statuses = page.FindAll("td[data-column='status']").Select(cell => cell.TextContent.Trim()).ToArray();
            Assert.Equal(new[] { "✓" + done, "?" + ongoing, "+" + open }, statuses);
            var icons = page.FindAll("td[data-column='status'] [data-status-icon]");
            Assert.Equal(new[] { "done", "ongoing", "open" }, icons.Select(icon => icon.GetAttribute("data-status-icon")));
            Assert.All(icons, icon => Assert.Equal("true", icon.GetAttribute("aria-hidden")));
            Assert.Equal("customer@example.test", page.FindAll("td[data-column='email']")[0].TextContent.Trim());
            Assert.Equal("02-123-4567", page.FindAll("td[data-column='telephone']")[0].TextContent.Trim());
            Assert.Equal("Thailand", page.FindAll("td[data-column='country']")[0].TextContent.Trim());
            Assert.Equal("1234567890123", page.FindAll("td[data-column='tax']")[0].TextContent.Trim());
            Assert.Equal("-", page.FindAll("td[data-column='tax']")[1].TextContent.Trim());
        }
        finally { CultureInfo.CurrentUICulture = previous; }
    }

    private sealed class RequestHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new
                {
                    items = new[]
                    {
                        new { id = 1, firstName = "One", done = (bool?)true, email = (string?)"customer@example.test", telephoneNumber = (string?)"02-123-4567", country = (string?)"Thailand", taxIdentification = (string?)"1234567890123" },
                        new { id = 2, firstName = "Two", done = (bool?)false, email = (string?)null, telephoneNumber = (string?)null, country = (string?)null, taxIdentification = (string?)null },
                        new { id = 3, firstName = "Three", done = (bool?)null, email = (string?)null, telephoneNumber = (string?)null, country = (string?)null, taxIdentification = (string?)null },
                    },
                    pageIndex = 1, totalPages = 1, totalRecords = 3, hasNextPage = false, hasPreviousPage = false,
                }),
            });
    }
}
