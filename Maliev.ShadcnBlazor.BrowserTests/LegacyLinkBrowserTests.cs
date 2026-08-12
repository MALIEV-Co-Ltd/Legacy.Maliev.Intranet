using Maliev.ShadcnBlazor.BrowserTests.Infrastructure;
using Microsoft.Playwright;

namespace Maliev.ShadcnBlazor.BrowserTests;

[Collection(BrowserCollection.Name)]
public sealed class LegacyLinkBrowserTests(ShowcaseServerFixture server, PlaywrightFixture playwright)
{
    [Theory]
    [InlineData(1280, 36)]
    [InlineData(768, 36)]
    [InlineData(390, 44)]
    [InlineData(320, 44)]
    public async Task NavigationLinkUsesShadcnGeometryFocusVisibleAndNoDocumentOverflow(int width, double minimumHeight)
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
        Assert.True(await page.EvaluateAsync<bool>("() => document.documentElement.clientWidth === document.documentElement.scrollWidth"));
    }

    [Fact]
    public async Task LinkFixtureProvidesBilingualTextSafeMixedCaseRelAndDisabledSemantics()
    {
        await using var context = await playwright.Browser.NewContextAsync();
        var page = await context.NewPageAsync();
        await page.GotoAsync(new Uri(server.BaseUri, "/components/legacy-link").ToString());
        await page.GetByTestId("legacy-link-fixture").WaitForAsync();

        Assert.Equal("Semantic link fixture | ตัวอย่างลิงก์เชิงความหมาย", await page.TitleAsync());
        await page.GetByRole(AriaRole.Region, new() { Name = "Semantic link roles | บทบาทลิงก์เชิงความหมาย" }).WaitForAsync();
        await page.GetByRole(AriaRole.Link, new() { Name = "Customer navigation | การนำทางลูกค้า" }).WaitForAsync();

        var external = page.GetByTestId("mixed-case-external-link");
        Assert.Equal("_BLANK", await external.GetAttributeAsync("target"));
        Assert.Equal("external noopener noreferrer", await external.GetAttributeAsync("rel"));

        var disabled = page.GetByTestId("disabled-icon-only-link");
        Assert.Equal("SPAN", await disabled.EvaluateAsync<string>("element => element.tagName"));
        Assert.Equal("true", await disabled.GetAttributeAsync("aria-disabled"));
        Assert.Equal("Disabled customer history | ประวัติลูกค้าที่ปิดใช้งาน", await disabled.GetAttributeAsync("aria-label"));
        Assert.Null(await disabled.GetAttributeAsync("href"));
    }

    [Fact]
    public async Task NavigationLinkUsesCoarsePointerTargetAndRespectsReducedMotionAndForcedColors()
    {
        await using var coarseContext = await playwright.Browser.NewContextAsync(new()
        {
            ViewportSize = new() { Width = 768, Height = 800 },
            HasTouch = true,
            ReducedMotion = ReducedMotion.Reduce
        });
        var coarsePage = await coarseContext.NewPageAsync();
        await coarsePage.GotoAsync(new Uri(server.BaseUri, "/components/legacy-link").ToString());
        var coarseLink = coarsePage.Locator("[data-link-role=navigation]").First;
        await coarseLink.WaitForAsync();
        Assert.True(await coarseLink.EvaluateAsync<double>("element => element.getBoundingClientRect().height") >= 44);
        Assert.True(await coarseLink.EvaluateAsync<bool>("element => parseFloat(getComputedStyle(element).transitionDuration) <= 0.01"));

        await using var forcedColorsContext = await playwright.Browser.NewContextAsync(new()
        {
            ViewportSize = new() { Width = 390, Height = 800 },
            ForcedColors = ForcedColors.Active
        });
        var forcedColorsPage = await forcedColorsContext.NewPageAsync();
        await forcedColorsPage.GotoAsync(new Uri(server.BaseUri, "/components/legacy-link").ToString());
        var forcedColorsLink = forcedColorsPage.Locator("[data-link-role=navigation]").First;
        await forcedColorsLink.WaitForAsync();
        await forcedColorsLink.FocusAsync();
        Assert.Equal("none", await forcedColorsLink.EvaluateAsync<string>("element => getComputedStyle(element).boxShadow"));
        Assert.Equal("solid", await forcedColorsLink.EvaluateAsync<string>("element => getComputedStyle(element).outlineStyle"));
    }
}
