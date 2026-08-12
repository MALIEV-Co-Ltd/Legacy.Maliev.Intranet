using Maliev.ShadcnBlazor.BrowserTests.Infrastructure;

namespace Maliev.ShadcnBlazor.BrowserTests;

[Collection(BrowserCollection.Name)]
public sealed class LegacyLinkBrowserTests(ShowcaseServerFixture server, PlaywrightFixture playwright)
{
    [Theory]
    [InlineData(1280, 36)]
    [InlineData(390, 44)]
    public async Task NavigationLinkUsesShadcnGeometryAndFocusVisible(int width, double minimumHeight)
    {
        await using var context = await playwright.Browser.NewContextAsync();
        var page = await context.NewPageAsync();
        await page.SetViewportSizeAsync(width, 800);
        await page.GotoAsync(new Uri(server.BaseUri, "/components/legacy-link").ToString());
        await page.GetByTestId("legacy-link-fixture").WaitForAsync();

        foreach (var role in new[] { "inline", "record", "navigation", "external" })
        {
            Assert.True(await page.Locator($"[data-link-role={role}]").CountAsync() > 0, $"The {role} link fixture is missing.");
        }

        var link = page.Locator("[data-link-role=navigation]").First;
        Assert.Equal("0px", await link.EvaluateAsync<string>("e => getComputedStyle(e).borderTopWidth"));
        await link.FocusAsync();
        Assert.True(await link.EvaluateAsync<double>("e => e.getBoundingClientRect().height") >= minimumHeight);
        Assert.NotEqual("none", await link.EvaluateAsync<string>("e => getComputedStyle(e).boxShadow"));
    }
}
