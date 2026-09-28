using Legacy.Maliev.Intranet.BrowserTests.Infrastructure;
using Microsoft.Playwright;

namespace Legacy.Maliev.Intranet.BrowserTests;

[Collection(CustomerBrowserCollection.Name)]
public sealed class ErrorRouteBrowserTests(
    IntranetClientServerFixture server,
    PlaywrightFixture playwright)
{
    [Theory]
    [InlineData(1440, "light")]
    [InlineData(375, "dark")]
    public async Task AuthorizedErrorRoute_IsReadableWithoutPrivateInputOrOverflow(int width, string theme)
    {
        await using var context = await playwright.Browser.NewContextAsync(new()
        {
            ViewportSize = new() { Width = width, Height = 850 },
            ReducedMotion = ReducedMotion.Reduce,
        });
        await context.AddInitScriptAsync($"localStorage.setItem('maliev_theme', '{theme}')");
        var page = await context.NewPageAsync();
        await StubSessionAsync(page, authenticated: true);
        await page.RouteAsync("**/bff/error-context*", route => route.FulfillAsync(new()
        {
            Status = 200,
            ContentType = "application/json",
            Body = "{\"pageReference\":\"server-reference-42\"}",
        }));
        await page.RouteAsync("https://accounts.google.com/**", route => route.AbortAsync());

        await page.GotoAsync(new Uri(server.BaseUri,
            "Error?code=503&email=private%40example.com&incidentId=forged-reference").AbsoluteUri);
        await page.GetByRole(AriaRole.Heading, new() { Name = "We could not complete that request" }).WaitForAsync();
        await page.GetByText("server-reference-42").WaitForAsync();
        var alert = page.GetByRole(AriaRole.Alert);
        var text = await alert.InnerTextAsync();

        Assert.Contains("503", text, StringComparison.Ordinal);
        Assert.Contains("server-reference-42", text, StringComparison.Ordinal);
        Assert.DoesNotContain("private@example.com", text, StringComparison.Ordinal);
        Assert.DoesNotContain("forged-reference", text, StringComparison.Ordinal);
        Assert.Equal(theme, await page.EvaluateAsync<string>("document.documentElement.dataset.malievTheme"));
        Assert.True(await page.EvaluateAsync<bool>(
            "document.documentElement.scrollWidth <= window.innerWidth + 1"));
    }

    [Fact]
    public async Task AnonymousErrorRoute_RedirectsToLoginWithoutRenderingFailureDetails()
    {
        await using var context = await playwright.Browser.NewContextAsync();
        var page = await context.NewPageAsync();
        await StubSessionAsync(page, authenticated: false);
        await page.RouteAsync("https://accounts.google.com/**", route => route.AbortAsync());

        await page.GotoAsync(new Uri(server.BaseUri, "Error?code=500").AbsoluteUri);
        await page.Locator("#legacy-login-email").WaitForAsync();

        Assert.Equal("/Login", new Uri(page.Url).AbsolutePath);
        Assert.Equal(0, await page.GetByRole(AriaRole.Alert).CountAsync());
    }

    [Fact]
    public async Task UnavailableReferenceEndpoint_DoesNotHideTheSafeErrorPage()
    {
        await using var context = await playwright.Browser.NewContextAsync();
        var page = await context.NewPageAsync();
        await StubSessionAsync(page, authenticated: true);
        await page.RouteAsync("**/bff/error-context*", route => route.AbortAsync());
        await page.RouteAsync("https://accounts.google.com/**", route => route.AbortAsync());

        await page.GotoAsync(new Uri(server.BaseUri, "Error?code=502").AbsoluteUri);
        var heading = page.GetByRole(AriaRole.Heading, new() { Name = "We could not complete that request" });
        await heading.WaitForAsync();

        Assert.Contains("502", await page.GetByRole(AriaRole.Alert).InnerTextAsync(), StringComparison.Ordinal);
        Assert.False(await page.Locator("#blazor-error-ui").IsVisibleAsync());
    }

    private static Task StubSessionAsync(IPage page, bool authenticated) =>
        page.RouteAsync("**/bff/session", route => route.FulfillAsync(new()
        {
            Status = 200,
            ContentType = "application/json",
            Body = authenticated
                ? "{\"isAuthenticated\":true,\"employeeId\":\"test-employee\",\"displayName\":\"Test Employee\",\"roles\":[\"Employee\"],\"csrfToken\":\"test-csrf\",\"legacyDatabaseId\":1,\"permissions\":[]}"
                : "{\"isAuthenticated\":false,\"employeeId\":null,\"displayName\":null,\"roles\":[],\"csrfToken\":\"test-csrf\",\"legacyDatabaseId\":null,\"permissions\":[]}",
        }));
}
