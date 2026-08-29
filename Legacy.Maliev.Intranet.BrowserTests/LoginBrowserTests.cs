using Legacy.Maliev.Intranet.BrowserTests.Infrastructure;
using Microsoft.Playwright;

namespace Legacy.Maliev.Intranet.BrowserTests;

[Collection(CustomerBrowserCollection.Name)]
public sealed class LoginBrowserTests(
    IntranetClientServerFixture server,
    PlaywrightFixture playwright)
{
    [Theory]
    [InlineData("")]
    [InlineData("Dashboard")]
    public async Task AnonymousEntryPointsShowLoginWithoutTheAuthenticatedApplicationShell(string path)
    {
        await using var context = await playwright.Browser.NewContextAsync(new()
        {
            ViewportSize = new() { Width = 1440, Height = 900 },
        });
        var page = await context.NewPageAsync();
        var errors = new List<string>();
        page.PageError += (_, error) => errors.Add(error);

        await page.RouteAsync("**/bff/session", route => route.FulfillAsync(new()
        {
            Status = 200,
            ContentType = "application/json",
            Body = "{\"isAuthenticated\":false,\"employeeId\":null,\"displayName\":null,\"roles\":[],\"csrfToken\":\"anonymous-browser-csrf\",\"legacyDatabaseId\":null,\"permissions\":[]}",
        }));
        await page.RouteAsync("https://accounts.google.com/**", route => route.AbortAsync());

        await page.GotoAsync(new Uri(server.BaseUri, path).AbsoluteUri);

        await page.Locator("#legacy-login-email").WaitForAsync();
        Assert.Equal("/Login", new Uri(page.Url).AbsolutePath);
        Assert.Equal(0, await page.Locator(".legacy-layout").CountAsync());
        Assert.Equal(0, await page.Locator(".legacy-navigation").CountAsync());
        Assert.Empty(errors);
    }

    [Fact]
    public async Task LoginUsesAccessibleShadcnFormsAcrossEmailAndCredentialSteps()
    {
        await using var context = await playwright.Browser.NewContextAsync(new()
        {
            ViewportSize = new() { Width = 320, Height = 844 },
            HasTouch = true,
            ReducedMotion = ReducedMotion.Reduce,
        });
        var page = await context.NewPageAsync();
        var errors = new List<string>();
        page.PageError += (_, error) => errors.Add(error);

        await page.RouteAsync("**/bff/session", route => route.FulfillAsync(new()
        {
            Status = 200,
            ContentType = "application/json",
            Body = "{\"isAuthenticated\":false,\"employeeId\":null,\"displayName\":null,\"roles\":[],\"csrfToken\":\"login-browser-csrf\",\"legacyDatabaseId\":null,\"permissions\":[]}",
        }));
        await page.RouteAsync("**/bff/login", route => route.FulfillAsync(new()
        {
            Status = 401,
            ContentType = "application/json",
            Body = "{}",
        }));
        await page.RouteAsync("https://accounts.google.com/**", route => route.AbortAsync());

        await page.GotoAsync(new Uri(server.BaseUri, "Login").AbsoluteUri);
        var email = page.Locator("#legacy-login-email");
        await email.WaitForAsync();

        var language = page.GetByRole(AriaRole.Combobox, new() { Name = "Language" });
        Assert.True(await language.EvaluateAsync<bool>(
            """
            select => {
                const style = getComputedStyle(select);
                const canvas = document.createElement('canvas');
                const context = canvas.getContext('2d');
                context.font = style.font;
                const selectedText = select.options[select.selectedIndex].text;
                const textWidth = context.measureText(selectedText).width;
                const reservedWidth = parseFloat(style.paddingInlineStart) +
                    parseFloat(style.paddingInlineEnd) +
                    parseFloat(style.borderInlineStartWidth) +
                    parseFloat(style.borderInlineEndWidth);
                return select.getBoundingClientRect().width - reservedWidth >= textWidth;
            }
            """));

        var title = page.Locator("#legacy-login-title");
        Assert.Equal("Sign in to MALIEV", (await title.InnerTextAsync()).Trim());
        Assert.Equal(0, await title.Locator("img").CountAsync());

        Assert.Equal(1, await page.Locator(".legacy-login-card .shadcn-input").CountAsync());
        Assert.Equal("legacy-login-email", await page.Locator("label").Filter(new() { HasText = "Email" }).GetAttributeAsync("for"));
        Assert.Contains("legacy-login-email-description", await email.GetAttributeAsync("aria-describedby"));
        Assert.Equal("email", await email.GetAttributeAsync("type"));

        var continueButton = page.GetByRole(AriaRole.Button, new() { Name = "Continue with email" });
        Assert.True(await continueButton.IsDisabledAsync());
        await email.FillAsync("browser@maliev.test");
        Assert.False(await continueButton.IsDisabledAsync());
        await email.PressAsync("Enter");

        var password = page.Locator("#legacy-login-password");
        await password.WaitForAsync();
        Assert.Equal("password", await password.GetAttributeAsync("type"));
        Assert.Equal(1, await page.Locator("#legacy-login-remember[type='checkbox']").CountAsync());

        var signIn = page.GetByRole(AriaRole.Button, new() { Name = "Sign in with email" });
        Assert.True(await signIn.IsDisabledAsync());
        await password.FillAsync("not-the-password");
        Assert.False(await signIn.IsDisabledAsync());
        await page.Locator("#legacy-login-remember").CheckAsync();
        Assert.True(await page.Locator("#legacy-login-remember").IsCheckedAsync());

        foreach (var button in new[]
                 {
                     page.GetByRole(AriaRole.Button, new() { Name = "Change" }),
                     signIn,
                     page.Locator(".legacy-login-theme-toggle"),
                 })
        {
            Assert.True(await button.EvaluateAsync<bool>(
                "element => element.getBoundingClientRect().width >= 44 && element.getBoundingClientRect().height >= 44"));
        }

        await signIn.ClickAsync();
        await page.GetByRole(AriaRole.Alert).WaitForAsync();
        Assert.Contains("invalid", await page.GetByRole(AriaRole.Alert).TextContentAsync(), StringComparison.OrdinalIgnoreCase);
        Assert.Equal(string.Empty, await password.InputValueAsync());
        Assert.Empty(errors);
    }
}
