using Legacy.Maliev.Intranet.BrowserTests.Infrastructure;
using Microsoft.Playwright;

namespace Legacy.Maliev.Intranet.BrowserTests;

[Collection(CustomerBrowserCollection.Name)]
public sealed class LoginBrowserTests(
    IntranetClientServerFixture server,
    PlaywrightFixture playwright)
{
    [Theory]
    [InlineData(1440, false)]
    [InlineData(375, true)]
    public async Task WasmBootShellShowsTheMalievWordmarkAndHorizontalProgress(int width, bool dark)
    {
        await using var context = await playwright.Browser.NewContextAsync(new()
        {
            ViewportSize = new() { Width = width, Height = 900 },
            ReducedMotion = ReducedMotion.Reduce,
        });
        if (dark)
            await context.AddInitScriptAsync("localStorage.setItem('maliev_theme', 'dark')");

        var page = await context.NewPageAsync();
        await page.RouteAsync("**/_framework/blazor.webassembly.js", route => route.AbortAsync());
        await page.RouteAsync("https://accounts.google.com/**", route => route.AbortAsync());
        await page.GotoAsync(server.BaseUri.AbsoluteUri);

        var loading = page.Locator("#workspace-loading");
        await loading.WaitForAsync();
        Assert.Equal("Loading workspace", await loading.Locator(".legacy-loading-status").InnerTextAsync());
        Assert.Equal(1, await loading.Locator(dark ? ".legacy-loading-logo--dark:visible" : ".legacy-loading-logo--light:visible").CountAsync());
        Assert.Equal(0, await loading.Locator("svg.loading-progress").CountAsync());

        var progress = loading.Locator(".loading-progress");
        await progress.EvaluateAsync("element => element.style.setProperty('--blazor-load-percentage', '63%')");
        var ratio = await progress.EvaluateAsync<double>(
            "element => element.firstElementChild.getBoundingClientRect().width / element.getBoundingClientRect().width");
        Assert.InRange(ratio, 0.62, 0.64);
        var progressBox = (await progress.BoundingBoxAsync())!;
        Assert.True(progressBox.Width <= Math.Min(352, width - 48));
        Assert.InRange(Math.Abs(progressBox.X + (progressBox.Width / 2) - (width / 2d)), 0, 1);

        var logoBox = (await loading.Locator(".legacy-loading-brand").BoundingBoxAsync())!;
        var metaBox = (await loading.Locator(".legacy-loading-meta").BoundingBoxAsync())!;
        var contentCenter = (logoBox.Y + metaBox.Y + metaBox.Height) / 2;
        Assert.InRange(Math.Abs(contentCenter - 450), 0, 2);
    }

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
        await language.FocusAsync();
        Assert.Equal("none", await page.Locator(".legacy-language-selector").EvaluateAsync<string>(
            "wrapper => getComputedStyle(wrapper).outlineStyle"));

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
        await continueButton.ClickAsync();

        var password = page.Locator("#legacy-login-password");
        await password.WaitForAsync();
        Assert.True(await password.EvaluateAsync<bool>("input => input === document.activeElement"));
        Assert.Equal("legacy-login-email", await page.Locator("label").Filter(new() { HasText = "Email" }).GetAttributeAsync("for"));
        Assert.Equal("browser@maliev.test", await email.InputValueAsync());
        Assert.False(await email.IsEditableAsync());
        Assert.True(await email.EvaluateAsync<bool>(
            "input => parseFloat(getComputedStyle(input).borderTopWidth) > 0"));
        Assert.Equal("password", await password.GetAttributeAsync("type"));
        var passwordVisibility = page.Locator("[data-slot='input-group-button']");
        await passwordVisibility.WaitForAsync();
        Assert.Equal("BUTTON", await passwordVisibility.EvaluateAsync<string>("button => button.tagName"));
        Assert.Equal("Show password", await passwordVisibility.GetAttributeAsync("aria-label"));
        Assert.Equal("false", await passwordVisibility.GetAttributeAsync("aria-pressed"));
        Assert.True(await passwordVisibility.EvaluateAsync<bool>(
            "button => button.getBoundingClientRect().width >= 44 && button.getBoundingClientRect().height >= 44"));
        var signIn = page.GetByRole(AriaRole.Button, new() { Name = "Sign in with email" });
        Assert.True(await signIn.IsDisabledAsync());
        await password.FillAsync("not-the-password");
        await passwordVisibility.ClickAsync();
        Assert.Equal("text", await password.GetAttributeAsync("type"));
        Assert.Equal("not-the-password", await password.InputValueAsync());
        Assert.True(await passwordVisibility.EvaluateAsync<bool>("button => button === document.activeElement"));
        var hidePassword = page.Locator("[data-slot='input-group-button']");
        Assert.Equal("Hide password", await hidePassword.GetAttributeAsync("aria-label"));
        Assert.Equal("true", await hidePassword.GetAttributeAsync("aria-pressed"));
        await hidePassword.ClickAsync();
        Assert.Equal("password", await password.GetAttributeAsync("type"));
        Assert.Equal(1, await page.Locator("#legacy-login-remember[type='checkbox']").CountAsync());

        Assert.False(await signIn.IsDisabledAsync());
        await page.Locator("#legacy-login-remember").CheckAsync();
        Assert.True(await page.Locator("#legacy-login-remember").IsCheckedAsync());
        await page.Locator("#legacy-login-remember").FocusAsync();
        Assert.Equal("none", await page.Locator(".legacy-login-remember").EvaluateAsync<string>(
            "field => getComputedStyle(field).outlineStyle"));

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
        var credentialError = page.Locator(".legacy-login-error[role='alert']");
        await credentialError.WaitForAsync();
        Assert.Contains("invalid", await credentialError.TextContentAsync(), StringComparison.OrdinalIgnoreCase);
        Assert.Equal(string.Empty, await password.InputValueAsync());
        Assert.Empty(errors);
    }
}
