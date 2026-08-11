using System.Net;
using Maliev.ShadcnBlazor.BrowserTests.Infrastructure;
using Microsoft.Playwright;

namespace Maliev.ShadcnBlazor.BrowserTests;

[Collection(BrowserCollection.Name)]
public sealed class CustomerResponsiveBrowserTests(PlaywrightFixture playwright)
{
    private const string LongName = "Natthapol Vanasrivilai With An Intentionally Long Customer Display Name";
    private const string LongEmail = "natthapol.vanasrivilai+responsive-customer-fixture@international-maliev.example.com";
    private const string LongCompany = "MALIEV Precision Manufacturing and International Engineering Services Company Limited";

    [Fact]
    public async Task CustomerRecordsRemainOperableAndContainedAcrossSupportedWidths()
    {
        var fixture = BuildFixture();

        await using var context = await playwright.Browser.NewContextAsync(new()
        {
            ViewportSize = new() { Width = 1280, Height = 900 },
            DeviceScaleFactor = 1,
            ReducedMotion = ReducedMotion.Reduce
        });
        var page = await context.NewPageAsync();
        await page.SetContentAsync(fixture);

        await AssertNoDocumentOverflowAsync(page);
        await AssertAtomicAsync(page, ".customer-id-cell", "9000000001");
        await AssertAtomicAsync(page, ".customer-action-cell", "View");
        await AssertFullValueDisclosureAsync(page, ".customer-email-disclosure", LongEmail);

        await page.SetViewportSizeAsync(768, 900);
        Assert.True(await page.Locator(".mud-table-container").EvaluateAsync<bool>(
            "element => element.scrollWidth > element.clientWidth"));
        await AssertNoDocumentOverflowAsync(page);

        foreach (var width in new[] { 390, 320 })
        {
            await page.SetViewportSizeAsync(width, 844);

            var populatedRow = page.Locator(".mud-table-row").Nth(0);
            var emptyCompanyRow = page.Locator(".mud-table-row").Nth(1);
            Assert.InRange(await populatedRow.EvaluateAsync<float>("element => element.getBoundingClientRect().height"), 96, 144);
            Assert.Equal("none", await emptyCompanyRow.Locator(".customer-company-cell").EvaluateAsync<string>(
                "element => getComputedStyle(element).display"));
            Assert.True(await populatedRow.Locator(".customer-action-cell a").EvaluateAsync<bool>(
                "element => element.getBoundingClientRect().height >= 44 && element.getBoundingClientRect().width >= 44"));
            await AssertFullValueDisclosureAsync(page, ".customer-name-disclosure", LongName);
            await AssertFullValueDisclosureAsync(page, ".customer-company-disclosure", LongCompany);
            await AssertNoDocumentOverflowAsync(page);
        }
    }

    private static async Task AssertAtomicAsync(IPage page, string selector, string expectedText)
    {
        var element = page.Locator($".mud-table-row:first-child {selector}");
        Assert.Equal(expectedText, (await element.InnerTextAsync()).Trim());
        Assert.Equal("nowrap", await element.EvaluateAsync<string>("node => getComputedStyle(node).whiteSpace"));
        Assert.InRange(await element.EvaluateAsync<float>("node => node.getBoundingClientRect().height"), 1, 72);
    }

    private static async Task AssertFullValueDisclosureAsync(IPage page, string selector, string expectedText)
    {
        var disclosure = page.Locator($".mud-table-row:first-child {selector}");
        var trigger = disclosure.Locator(".customer-value-trigger");
        await trigger.FocusAsync();
        Assert.True(await trigger.EvaluateAsync<bool>("element => element === document.activeElement"));
        await trigger.PressAsync("Enter");
        Assert.Equal("true", await trigger.GetAttributeAsync("aria-expanded"));
        Assert.False(await disclosure.Locator("[role=menu]").IsHiddenAsync());
        Assert.Equal(expectedText, (await disclosure.Locator(".customer-full-value").InnerTextAsync()).Trim());
        await trigger.PressAsync("Enter");
    }

    private static async Task AssertNoDocumentOverflowAsync(IPage page) =>
        Assert.True(await page.EvaluateAsync<bool>("document.documentElement.scrollWidth <= document.documentElement.clientWidth"));

    private static string BuildFixture()
    {
        var root = FindRoot();
        var pagePath = Path.Combine(root, "Legacy.Maliev.Intranet.Client.Features.Customers", "Pages", "Customers.razor");
        var pageSource = File.ReadAllText(pagePath);
        var componentCss = File.ReadAllText(Path.ChangeExtension(pagePath, ".razor.css"))
            .Replace(" ::deep ", " ", StringComparison.Ordinal);

        var supportsDisclosures = pageSource.Contains("<MudMenu", StringComparison.Ordinal);
        var firstRow = BuildRow(supportsDisclosures, "9000000001", LongName, LongEmail, LongCompany);
        var secondRow = BuildRow(supportsDisclosures, "42", "Short Name", "short@maliev.com", null);

        return $$"""
            <!doctype html>
            <html><head><meta name="viewport" content="width=device-width,initial-scale=1">
            <style>
            * { box-sizing: border-box; }
            html, body { margin: 0; width: 100%; max-width: 100%; }
            body { font: 14px/1.4 sans-serif; }
            .customer-shell { width: 100%; padding: 16px; }
            .mud-table-container { width: 100%; }
            .mud-table-root { border-collapse: collapse; }
            .mud-table-cell { padding: 12px 16px; }
            .mud-table-row { border: 1px solid #ddd; }
            {{componentCss}}
            </style></head><body>
            <main class="customer-shell">
              <div class="customers-table-shell">
                <div class="mud-table-container">
                  <table class="customers-table mud-table-root">
                    <thead class="mud-table-head"><tr><th class="mud-table-cell customer-id-cell">ID</th><th class="mud-table-cell customer-name-cell">Name</th><th class="mud-table-cell customer-email-cell">Email</th><th class="mud-table-cell customer-company-cell">Company</th><th class="mud-table-cell customer-action-cell">Actions</th></tr></thead>
                    <tbody class="mud-table-body">{{firstRow}}{{secondRow}}</tbody>
                  </table>
                </div>
              </div>
            </main>
            <script>
            document.querySelectorAll('.customer-value-trigger').forEach(trigger => trigger.addEventListener('click', () => {
              const menu = trigger.nextElementSibling;
              const open = trigger.getAttribute('aria-expanded') === 'true';
              trigger.setAttribute('aria-expanded', open ? 'false' : 'true');
              menu.hidden = open;
            }));
            </script></body></html>
            """;
    }

    private static string BuildRow(bool supportsDisclosures, string id, string name, string email, string? company)
    {
        var encodedName = WebUtility.HtmlEncode(name);
        var encodedEmail = WebUtility.HtmlEncode(email);
        var encodedCompany = WebUtility.HtmlEncode(company);
        var nameContent = supportsDisclosures ? Disclosure("customer-name-disclosure", "customer-name-value", encodedName) : encodedName;
        var emailContent = supportsDisclosures ? Disclosure("customer-email-disclosure", "customer-email-value", encodedEmail) : $"<span class=\"customer-email-value\">{encodedEmail}</span>";
        var companyContent = company is null
            ? string.Empty
            : supportsDisclosures ? Disclosure("customer-company-disclosure", "customer-company-value", encodedCompany!) : encodedCompany;

        return $$"""
            <tr class="mud-table-row">
              <td class="mud-table-cell customer-id-cell">{{WebUtility.HtmlEncode(id)}}</td>
              <td class="mud-table-cell customer-name-cell">{{nameContent}}</td>
              <td class="mud-table-cell customer-email-cell">{{emailContent}}</td>
              <td class="mud-table-cell customer-company-cell">{{companyContent}}</td>
              <td class="mud-table-cell customer-action-cell"><a href="#view">View</a></td>
            </tr>
            """;
    }

    private static string Disclosure(string disclosureClass, string valueClass, string value) =>
        $"<div class=\"customer-value-disclosure {disclosureClass}\"><button type=\"button\" class=\"mud-button-root customer-value-trigger {valueClass}\" aria-expanded=\"false\"><span class=\"mud-button-label\">{value}</span></button><div role=\"menu\" hidden><span class=\"customer-full-value\">{value}</span></div></div>";

    private static string FindRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Legacy.Maliev.Intranet.slnx")))
            directory = directory.Parent;

        return directory?.FullName ?? throw new DirectoryNotFoundException("Could not locate Legacy.Maliev.Intranet root.");
    }
}
