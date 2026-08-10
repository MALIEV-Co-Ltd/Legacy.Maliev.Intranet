using Maliev.ShadcnBlazor.BrowserTests.Infrastructure;
using Microsoft.Playwright;

namespace Maliev.ShadcnBlazor.BrowserTests;

[Collection(BrowserCollection.Name)]
public sealed class FoundationSmokeTests(ShowcaseServerFixture server, PlaywrightFixture playwright)
{
    [Fact]
    public async Task FoundationFixtureHasHealthyConsoleAndSwitchesThemeAndDirection()
    {
        var errors = new List<string>();
        await using var context = await playwright.Browser.NewContextAsync(new()
        {
            ViewportSize = new() { Width = 1440, Height = 900 },
            DeviceScaleFactor = 1
        });
        var page = await context.NewPageAsync();
        page.Console += (_, message) => { if (message.Type == "error") errors.Add(message.Text); };
        page.PageError += (_, error) => errors.Add(error);

        await page.GotoAsync(new Uri(server.BaseUri, "/components/foundation").ToString());
        await page.GetByTestId("foundation-fixture").WaitForAsync();
        Assert.Equal("Foundation Fixture", await page.TitleAsync());
        Assert.Empty(errors);

        var root = page.Locator("[data-shadcn-scope]");
        Assert.Equal("light", await root.GetAttributeAsync("data-shadcn-theme"));
        Assert.Equal("ltr", await root.GetAttributeAsync("dir"));
        var background = await page.GetByTestId("token-background").EvaluateAsync<string>(
            "element => getComputedStyle(element).backgroundColor");
        Assert.NotEqual("rgba(0, 0, 0, 0)", background);

        await page.GetByTestId("theme-toggle").ClickAsync();
        await Assertions.Expect(root).ToHaveAttributeAsync("data-shadcn-theme", "dark");
        await page.GetByTestId("direction-toggle").ClickAsync();
        await Assertions.Expect(root).ToHaveAttributeAsync("dir", "rtl");

        var evidence = Path.Combine(Path.GetTempPath(), $"maliev-shadcn-foundation-{Guid.NewGuid():N}.png");
        await page.ScreenshotAsync(new() { Path = evidence, FullPage = false });
        var evidenceInfo = new FileInfo(evidence);
        Assert.True(evidenceInfo.Exists);
        Assert.NotEqual(0, evidenceInfo.Length);
        Assert.Empty(errors);
    }
}
