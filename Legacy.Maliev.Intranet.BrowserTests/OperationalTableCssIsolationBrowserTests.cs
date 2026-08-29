using System.Text.RegularExpressions;
using Legacy.Maliev.Intranet.BrowserTests.Infrastructure;
using Microsoft.Playwright;

namespace Legacy.Maliev.Intranet.BrowserTests;

[Collection(BrowserCollection.Name)]
public sealed class OperationalTableCssIsolationBrowserTests(PlaywrightFixture playwright)
{
    [Fact]
    public async Task Compiled_scoped_styles_reach_fragment_cells_and_native_action_roots()
    {
        var stylesheet = ReadCompiledOperationalTableStylesheet();
        var scope = Regex.Match(stylesheet, @"\[b-(?<scope>[a-z0-9]+)\]").Groups["scope"].Value;
        Assert.False(string.IsNullOrWhiteSpace(scope));

        await using var desktopContext = await playwright.Browser.NewContextAsync(new()
        {
            ViewportSize = new() { Width = 1280, Height = 800 },
        });
        var desktopPage = await desktopContext.NewPageAsync();
        await SetTableAsync(desktopPage, stylesheet, scope);

        var identity = desktopPage.Locator(".operational-table__identity");
        var detail = desktopPage.Locator("a.operational-table__detail");
        var toggle = desktopPage.Locator("button.operational-table__toggle");
        Assert.Equal("sticky", await identity.EvaluateAsync<string>("element => getComputedStyle(element).position"));
        Assert.Equal(36, await detail.EvaluateAsync<double>("element => element.getBoundingClientRect().width"));
        Assert.Equal(36, await toggle.EvaluateAsync<double>("element => element.getBoundingClientRect().height"));

        await using var narrowContext = await playwright.Browser.NewContextAsync(new()
        {
            ViewportSize = new() { Width = 390, Height = 800 },
        });
        var narrowPage = await narrowContext.NewPageAsync();
        await SetTableAsync(narrowPage, stylesheet, scope);

        Assert.Equal("none", await narrowPage.Locator("[data-priority=supporting]").EvaluateAsync<string>("element => getComputedStyle(element).display"));
        Assert.Equal(44, await narrowPage.Locator("a.operational-table__detail").EvaluateAsync<double>("element => element.getBoundingClientRect().width"));
        Assert.Equal(44, await narrowPage.Locator("button.operational-table__toggle").EvaluateAsync<double>("element => element.getBoundingClientRect().height"));

        await using var coarseContext = await playwright.Browser.NewContextAsync(new()
        {
            ViewportSize = new() { Width = 1280, Height = 800 },
            HasTouch = true,
        });
        var coarsePage = await coarseContext.NewPageAsync();
        await SetTableAsync(coarsePage, stylesheet, scope);

        Assert.Equal(44, await coarsePage.Locator("a.operational-table__detail").EvaluateAsync<double>("element => element.getBoundingClientRect().width"));
        Assert.Equal(44, await coarsePage.Locator("button.operational-table__toggle").EvaluateAsync<double>("element => element.getBoundingClientRect().height"));
    }

    [Theory]
    [InlineData(1280)]
    [InlineData(768)]
    [InlineData(390)]
    [InlineData(320)]
    public async Task Scroll_container_owns_table_overflow_without_expanding_the_document(int width)
    {
        var stylesheet = ReadCompiledOperationalTableStylesheet();
        var scope = Regex.Match(stylesheet, @"\[b-(?<scope>[a-z0-9]+)\]").Groups["scope"].Value;
        await using var context = await playwright.Browser.NewContextAsync(new()
        {
            ViewportSize = new() { Width = width, Height = 800 },
        });
        var page = await context.NewPageAsync();
        await SetTableAsync(page, stylesheet, scope);

        var geometry = await page.Locator(".operational-table__scroll").EvaluateAsync<System.Text.Json.JsonElement>("""
            node => ({
                documentClientWidth: document.documentElement.clientWidth,
                documentScrollWidth: document.documentElement.scrollWidth,
                clientWidth: node.clientWidth,
                scrollWidth: node.scrollWidth,
                overflowX: getComputedStyle(node).overflowX
            })
            """);
        Assert.Equal(geometry.GetProperty("documentClientWidth").GetInt32(), geometry.GetProperty("documentScrollWidth").GetInt32());
        Assert.Contains(geometry.GetProperty("overflowX").GetString(), new[] { "auto", "scroll" });
        Assert.Equal(width <= 768, geometry.GetProperty("scrollWidth").GetInt32() > geometry.GetProperty("clientWidth").GetInt32());

        var container = page.Locator(".operational-table__scroll");
        await container.EvaluateAsync("node => node.scrollLeft = node.scrollWidth");
        Assert.True(await page.Locator(".operational-table__actions").IsVisibleAsync());
        if (width <= 720)
        {
            Assert.Equal(44, await page.Locator("a.operational-table__detail").EvaluateAsync<double>("node => node.getBoundingClientRect().width"));
            Assert.Equal(44, await page.Locator("button.operational-table__toggle").EvaluateAsync<double>("node => node.getBoundingClientRect().height"));
        }
    }

    private static async Task SetTableAsync(IPage page, string stylesheet, string scope) =>
        await page.SetContentAsync($"""
            <style>{stylesheet}</style>
            <div class="operational-table__scroll" b-{scope}="">
              <table class="operational-table" b-{scope}="">
                <tbody b-{scope}="">
                  <tr class="operational-table__row" b-{scope}="">
                    <td class="operational-table__identity" data-priority="supporting">Supporting cell</td>
                    <td class="operational-table__actions" b-{scope}="">
                      <span class="operational-table__detail-wrap" b-{scope}=""><a class="operational-table__detail" href="#detail">Detail</a></span>
                      <span class="operational-table__toggle-wrap" b-{scope}=""><button type="button" class="operational-table__toggle">Toggle</button></span>
                    </td>
                  </tr>
                </tbody>
              </table>
            </div>
            """);

    private static string ReadCompiledOperationalTableStylesheet()
    {
        var root = FindRepositoryRoot();
        var path = Path.Combine(
            root,
            "Legacy.Maliev.Intranet.Client.Shared",
            "obj",
            "Release",
            "net10.0",
            "scopedcss",
            "Components",
            "OperationalTable.razor.rz.scp.css");
        return File.ReadAllText(path);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Legacy.Maliev.Intranet.slnx")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new DirectoryNotFoundException("Repository root was not found.");
    }
}
