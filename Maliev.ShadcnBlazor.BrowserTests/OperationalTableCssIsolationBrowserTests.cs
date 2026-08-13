using System.Text.RegularExpressions;
using Maliev.ShadcnBlazor.BrowserTests.Infrastructure;
using Microsoft.Playwright;

namespace Maliev.ShadcnBlazor.BrowserTests;

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
