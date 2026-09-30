using System.Collections.Concurrent;
using System.Net;
using Legacy.Maliev.Intranet.BrowserTests.Infrastructure;
using Microsoft.Playwright;

namespace CustomerRevision.Acceptance;

public sealed partial class CustomerRevisionAcceptanceTests
{
    private static async Task ExerciseBrowserEditorsAsync(
        HttpClient first, HttpClient second, HttpClient customer, int id)
    {
        var server = new IntranetClientServerFixture();
        await server.InitializeAsync();
        try
        {
            using var playwright = await Playwright.CreateAsync();
            await using var browser = await playwright.Chromium.LaunchAsync(new() { Headless = true });
            await using var firstContext = await browser.NewContextAsync();
            await using var secondContext = await browser.NewContextAsync();
            var reads = new ConcurrentQueue<string>();
            var writes = new ConcurrentQueue<(int Status, string? Revision)>();
            var failures = new ConcurrentQueue<string>();

            // Transport bridge only: every BFF response comes from the actual pipeline.
            // Cookie jars are independent real Redis tickets; no API response is fabricated.
            await BridgeAsync(firstContext, first);
            await BridgeAsync(secondContext, second);
            var firstPage = await firstContext.NewPageAsync();
            var secondPage = await secondContext.NewPageAsync();
            var url = new Uri(server.BaseUri, $"Customers/View?id={id}").AbsoluteUri;
            await Task.WhenAll(firstPage.GotoAsync(url), secondPage.GotoAsync(url));
            await Task.WhenAll(EditAsync(firstPage, "Winner"), EditAsync(secondPage, "Loser"));
            Assert.Equal(2, reads.Count);
            Assert.Single(reads.Distinct());

            await SaveAsync(firstPage, 204);
            await firstPage.GetByRole(AriaRole.Button, new() { Name = "Edit customer", Exact = true }).WaitForAsync();
            Assert.Equal("Winner", (await ReadPersistedAsync(customer, id)).FirstName);
            var readsAfterWinner = reads.Count;
            await SaveAsync(secondPage, 412);
            await secondPage.GetByText("Another employee updated this customer. Your changes were not saved. Reload the latest details, review them, and re-enter your changes.", new() { Exact = true }).WaitForAsync();
            Assert.Equal("Loser", await FirstName(secondPage).InputValueAsync());
            Assert.Equal("Winner", (await ReadPersistedAsync(customer, id)).FirstName);
            Assert.Equal(readsAfterWinner, reads.Count); // A conflict must not silently reload/discard the draft.

            await secondPage.GetByRole(AriaRole.Button, new() { Name = "Reload latest details", Exact = true }).ClickAsync();
            await secondPage.GetByRole(AriaRole.Button, new() { Name = "Edit customer", Exact = true }).WaitForAsync();
            Assert.Equal(readsAfterWinner + 1, reads.Count);
            Assert.NotEqual(reads.First(), reads.Last());
            await EditAsync(secondPage, "Fresh");
            await SaveAsync(secondPage, 204);
            Assert.Equal("Fresh", (await ReadPersistedAsync(customer, id)).FirstName);
            var recorded = writes.ToArray();
            Assert.Equal(new[] { 204, 412, 204 }, recorded.Select(write => write.Status));
            Assert.Equal(recorded[0].Revision, recorded[1].Revision);
            Assert.NotEqual(recorded[1].Revision, recorded[2].Revision);
            Assert.Empty(failures);

            async Task BridgeAsync(IBrowserContext context, HttpClient employee)
            {
                await context.RouteAsync("**/*", route =>
                    new Uri(route.Request.Url).GetLeftPart(UriPartial.Authority) == server.BaseUri.GetLeftPart(UriPartial.Authority)
                        ? route.ContinueAsync()
                        : route.AbortAsync());
                await context.RouteAsync("**/bff/**", async route =>
                {
                    try
                    {
                        var uri = new Uri(route.Request.Url);
                        using var request = new HttpRequestMessage(new HttpMethod(route.Request.Method), uri.PathAndQuery);
                        if (route.Request.PostDataBuffer is { } body)
                            request.Content = new ByteArrayContent(body);
                        foreach (var header in await route.Request.AllHeadersAsync())
                        {
                            if (header.Key.Equals("cookie", StringComparison.OrdinalIgnoreCase) ||
                                header.Key.Equals("host", StringComparison.OrdinalIgnoreCase) ||
                                header.Key.Equals("content-length", StringComparison.OrdinalIgnoreCase)) continue;
                            if (!request.Headers.TryAddWithoutValidation(header.Key, header.Value))
                                request.Content?.Headers.TryAddWithoutValidation(header.Key, header.Value);
                        }
                        using var response = await employee.SendAsync(request);
                        if (uri.AbsolutePath == $"/bff/customers/{id}/versioned")
                        {
                            if (request.Method == HttpMethod.Get && response.StatusCode == HttpStatusCode.OK)
                                reads.Enqueue(response.Headers.ETag!.ToString());
                            if (request.Method == HttpMethod.Put)
                                writes.Enqueue(((int)response.StatusCode, request.Headers.IfMatch.ToString()));
                        }
                        var headers = response.Headers.Concat(response.Content.Headers)
                            .Where(header => !header.Key.Equals("Set-Cookie", StringComparison.OrdinalIgnoreCase))
                            .ToDictionary(header => header.Key, header => string.Join(",", header.Value));
                        await route.FulfillAsync(new()
                        {
                            Status = (int)response.StatusCode,
                            Headers = headers,
                            BodyBytes = await response.Content.ReadAsByteArrayAsync(),
                        });
                    }
                    catch (Exception error)
                    {
                        failures.Enqueue(error.GetType().Name);
                        await route.AbortAsync();
                    }
                });
            }
        }
        finally
        {
            await server.DisposeAsync();
        }

        static ILocator FirstName(IPage page) => page.GetByRole(AriaRole.Textbox, new() { Name = "First name", Exact = true });
        static async Task EditAsync(IPage page, string name)
        {
            await page.GetByRole(AriaRole.Button, new() { Name = "Edit customer", Exact = true }).ClickAsync();
            await FirstName(page).FillAsync(name);
        }
        static async Task SaveAsync(IPage page, int status)
        {
            var response = await page.RunAndWaitForResponseAsync(
                () => page.GetByRole(AriaRole.Button, new() { Name = "Save changes", Exact = true }).ClickAsync(),
                response => response.Request.Method == "PUT" && response.Url.EndsWith("/versioned", StringComparison.Ordinal));
            Assert.Equal(status, response.Status);
        }
    }
}
