using System.Text.Json;
using Maliev.ShadcnBlazor.BrowserTests.Infrastructure;
using Microsoft.Playwright;

namespace Maliev.ShadcnBlazor.BrowserTests;

[Collection(CustomerBrowserCollection.Name)]
public sealed class CustomerResponsiveBrowserTests(
    IntranetClientServerFixture server,
    PlaywrightFixture playwright)
{
    private const string LongName = "Natthapol Vanasrivilai With An Intentionally Long Customer Display Name";
    private const string LongEmail = "natthapol.vanasrivilai+responsive-customer-fixture@international-maliev.example.com";
    private const string LongCompany = "MALIEV Precision Manufacturing and International Engineering Services Company Limited";

    [Fact]
    public async Task ProductionCustomerPageRemainsOperableAndContainedAcrossSupportedWidths()
    {
        await using var context = await playwright.Browser.NewContextAsync(new()
        {
            ViewportSize = new() { Width = 1280, Height = 900 },
            DeviceScaleFactor = 1,
            ReducedMotion = ReducedMotion.Reduce
        });
        var page = await context.NewPageAsync();
        var errors = new List<string>();
        page.Console += (_, message) => { if (message.Type == "error") errors.Add(message.Text); };
        page.PageError += (_, error) => errors.Add(error);
        await StubProductionBoundariesAsync(page);

        await page.GotoAsync(new Uri(server.BaseUri, "customers").AbsoluteUri);
        await page.Locator(".customers-table .mud-table-body .mud-table-row").First.WaitForAsync();

        Assert.Equal("/customers", new Uri(page.Url).AbsolutePath);
        await AssertNoDocumentOverflowAsync(page);
        await AssertAtomicAsync(page, ".customer-id-cell", "2000000001");
        await AssertAtomicAsync(page, ".customer-action-cell", "View");
        await AssertDisclosuresContainedAsync(page);
        await AssertFullValueDisclosureAsync(page, ".customer-email-disclosure", LongEmail);

        await page.SetViewportSizeAsync(768, 900);
        Assert.True(await page.Locator(".mud-table-container").EvaluateAsync<bool>(
            "element => element.scrollWidth > element.clientWidth"));
        await AssertDisclosuresContainedAsync(page);
        await AssertNoDocumentOverflowAsync(page);

        foreach (var width in new[] { 390, 320 })
        {
            await page.SetViewportSizeAsync(width, 844);
            await page.WaitForFunctionAsync("""
                () => getComputedStyle(document.querySelector('.customers-table .mud-table-body .mud-table-row .mud-table-cell')).padding === '0px'
                """);

            var populatedRow = page.Locator(".customers-table .mud-table-body .mud-table-row").Nth(0);
            var emptyCompanyRow = page.Locator(".customers-table .mud-table-body .mud-table-row").Nth(1);
            var rowMetrics = await populatedRow.EvaluateAsync<JsonElement>("""
                element => {
                    const style = getComputedStyle(element);
                    return {
                        height: element.getBoundingClientRect().height,
                        display: style.display,
                        rows: style.gridTemplateRows,
                        rowGap: style.rowGap,
                        padding: style.padding,
                        cells: Array.from(element.children).map(cell => ({
                            classes: cell.className,
                            height: cell.getBoundingClientRect().height,
                            display: getComputedStyle(cell).display,
                            padding: getComputedStyle(cell).padding
                        }))
                    };
                }
                """);
            var rowHeight = rowMetrics.GetProperty("height").GetDouble();
            Assert.True(rowHeight is >= 144 and <= 176, rowMetrics.ToString());
            Assert.Equal("none", await emptyCompanyRow.Locator(".customer-company-cell").EvaluateAsync<string>(
                "element => getComputedStyle(element).display"));
            Assert.True(await populatedRow.Locator(".customer-action-cell a").EvaluateAsync<bool>(
                "element => element.getBoundingClientRect().height >= 44 && element.getBoundingClientRect().width >= 44"));
            await AssertDisclosuresContainedAsync(page);
            await AssertFullValueDisclosureAsync(page, ".customer-name-disclosure", LongName);
            await AssertFullValueDisclosureAsync(page, ".customer-company-disclosure", LongCompany);
            await AssertNoDocumentOverflowAsync(page);
        }

        Assert.Empty(errors);
    }

    [Fact]
    public async Task ProductionCustomerToolbarKeepsSearchPrimaryAndActionsCompactAcrossSupportedWidths()
    {
        await using var context = await playwright.Browser.NewContextAsync(new()
        {
            ViewportSize = new() { Width = 1280, Height = 900 },
            DeviceScaleFactor = 1,
            ReducedMotion = ReducedMotion.Reduce
        });
        var page = await context.NewPageAsync();
        await StubProductionBoundariesAsync(page);

        await page.GotoAsync(new Uri(server.BaseUri, "customers").AbsoluteUri);
        await page.Locator(".customers-table .mud-table-body .mud-table-row").First.WaitForAsync();

        var expectedMutedSurface = await page.EvaluateAsync<string>("""
            () => {
                const probe = document.createElement('span');
                probe.style.background = 'var(--shadcn-muted)';
                document.querySelector('[data-shadcn-scope]').append(probe);
                const color = getComputedStyle(probe).backgroundColor;
                probe.remove();
                return color;
            }
            """);
        var surface = await page.Locator(".list-toolbar").EvaluateAsync<JsonElement>("""
            element => {
                const style = getComputedStyle(element);
                return {
                    background: style.backgroundColor,
                    borderWidth: style.borderTopWidth,
                    boxShadow: style.boxShadow,
                    borderRadius: style.borderRadius
                };
            }
            """);
        Assert.Equal(expectedMutedSurface, surface.GetProperty("background").GetString());
        Assert.Equal("0px", surface.GetProperty("borderWidth").GetString());
        Assert.Equal("none", surface.GetProperty("boxShadow").GetString());
        Assert.NotEqual("0px", surface.GetProperty("borderRadius").GetString());

        foreach (var width in new[] { 1280, 768, 390, 320 })
        {
            await page.SetViewportSizeAsync(width, 844);
            if (width <= 900)
            {
                await page.WaitForFunctionAsync("""
                    () => Array.from(document.querySelectorAll('.list-toolbar .mud-input'))
                        .every(input => input.getBoundingClientRect().height >= 44)
                    """);
            }
            var metrics = await page.Locator(".list-toolbar").EvaluateAsync<JsonElement>("""
                element => {
                    const bounds = node => {
                        const rect = node.getBoundingClientRect();
                        return { x: rect.x, y: rect.y, width: rect.width, height: rect.height };
                    };
                    const controls = Array.from(element.querySelectorAll('.mud-input-control'));
                    const buttons = Array.from(element.querySelectorAll('.list-toolbar__actions .mud-button-root'));
                    return {
                        toolbar: bounds(element),
                        search: bounds(controls[0]),
                        sort: bounds(controls[1]),
                        pageSize: bounds(controls[2]),
                        actions: bounds(element.querySelector('.list-toolbar__actions')),
                        inputs: controls.map(control => {
                            const input = control.querySelector('.mud-input-control-input-container > .mud-input');
                            return {
                                ...bounds(input),
                                classes: input.className,
                                controlClasses: control.className,
                                controlHeightToken: getComputedStyle(control).getPropertyValue('--shadcn-control-height').trim(),
                                inputHeightToken: getComputedStyle(input).getPropertyValue('--shadcn-control-height').trim()
                            };
                        }),
                        buttons: buttons.map(bounds)
                    };
                }
                """);

            var toolbarHeight = metrics.GetProperty("toolbar").GetProperty("height").GetDouble();
            var maximumHeight = width >= 1024 ? 100 : width > 600 ? 200 : 250;
            Assert.True(toolbarHeight <= maximumHeight, metrics.ToString());

            var searchWidth = metrics.GetProperty("search").GetProperty("width").GetDouble();
            var sortWidth = metrics.GetProperty("sort").GetProperty("width").GetDouble();
            Assert.True(searchWidth >= sortWidth, metrics.ToString());

            if (width <= 900)
            {
                foreach (var input in metrics.GetProperty("inputs").EnumerateArray())
                    Assert.True(input.GetProperty("height").GetDouble() >= 44, metrics.ToString());
                foreach (var button in metrics.GetProperty("buttons").EnumerateArray())
                    Assert.True(button.GetProperty("height").GetDouble() >= 44, metrics.ToString());
            }

            if (width <= 600)
            {
                var sortY = metrics.GetProperty("sort").GetProperty("y").GetDouble();
                var pageSizeY = metrics.GetProperty("pageSize").GetProperty("y").GetDouble();
                Assert.InRange(Math.Abs(sortY - pageSizeY), 0, 1);
            }

            var actionDivider = await page.Locator(".list-toolbar__actions").EvaluateAsync<string>(
                "element => getComputedStyle(element).borderInlineStartWidth");
            Assert.Equal(width > 900 ? "1px" : "0px", actionDivider);

            if (width == 320)
            {
                Assert.True(sortWidth >= 152, metrics.ToString());
                var selectedSort = page.Locator(".list-toolbar .mud-select-input").First;
                Assert.Equal("Newest customers", (await selectedSort.InnerTextAsync()).Trim());
                Assert.True(await selectedSort.EvaluateAsync<bool>("element => element.scrollWidth <= element.clientWidth"));
            }

            await AssertNoDocumentOverflowAsync(page);
        }
    }

    [Fact]
    public async Task CustomerSearchClearControlsRestoreTheUnfilteredUrlAndResultsAfterDebounce()
    {
        await using var context = await playwright.Browser.NewContextAsync(new()
        {
            ViewportSize = new() { Width = 1280, Height = 900 },
            DeviceScaleFactor = 1,
            ReducedMotion = ReducedMotion.Reduce
        });
        var page = await context.NewPageAsync();
        await StubProductionBoundariesAsync(page);

        await page.GotoAsync(new Uri(server.BaseUri, "customers").AbsoluteUri);
        var rows = page.Locator(".customers-table .mud-table-body .mud-table-row");
        await rows.First.WaitForAsync();
        Assert.Equal(2, await rows.CountAsync());

        var search = page.Locator(".list-toolbar input").First;
        await search.FillAsync("Natthapol");
        await page.WaitForURLAsync(url => url.Contains("search=Natthapol", StringComparison.Ordinal));

        await page.Locator(".list-toolbar .mud-input-clear-button").ClickAsync();
        await page.WaitForURLAsync(url => url.Contains("search=&", StringComparison.Ordinal));
        await page.WaitForFunctionAsync("() => document.querySelectorAll('.customers-table .mud-table-body .mud-table-row').length === 2");

        await search.FillAsync("Natthapol");
        await page.WaitForURLAsync(url => url.Contains("search=Natthapol", StringComparison.Ordinal));
        await page.Locator(".list-toolbar__actions .mud-button-root").First.ClickAsync();
        await page.WaitForURLAsync(url => url.Contains("search=&", StringComparison.Ordinal));
        await page.WaitForFunctionAsync("() => document.querySelectorAll('.customers-table .mud-table-body .mud-table-row').length === 2");
    }

    [Fact]
    public async Task ThaiCustomerToolbarStacksBothActionsWithoutClippingAtNarrowWidth()
    {
        await using var context = await playwright.Browser.NewContextAsync(new()
        {
            ViewportSize = new() { Width = 320, Height = 844 },
            DeviceScaleFactor = 1,
            ReducedMotion = ReducedMotion.Reduce
        });
        await context.AddInitScriptAsync("localStorage.setItem('maliev_culture', 'th-TH')");
        var page = await context.NewPageAsync();
        await StubProductionBoundariesAsync(page);

        await page.GotoAsync(new Uri(server.BaseUri, "customers").AbsoluteUri);
        var search = page.Locator(".list-toolbar input").First;
        await search.FillAsync("Natthapol");
        await page.WaitForURLAsync(url => url.Contains("search=Natthapol", StringComparison.Ordinal));

        var actions = page.Locator(".list-toolbar__actions .mud-button-root");
        await actions.Nth(1).WaitForAsync();
        Assert.Equal(2, await actions.CountAsync());
        Assert.Equal("ล้างตัวกรอง", (await actions.Nth(0).InnerTextAsync()).Trim());
        Assert.Equal("รีเฟรชข้อมูล", (await actions.Nth(1).InnerTextAsync()).Trim());

        var geometry = await actions.EvaluateAllAsync<JsonElement>("""
            elements => elements.map(element => {
                const rect = element.getBoundingClientRect();
                return {
                    x: rect.x,
                    y: rect.y,
                    width: rect.width,
                    height: rect.height,
                    scrollWidth: element.scrollWidth,
                    clientWidth: element.clientWidth,
                    whiteSpace: getComputedStyle(element).whiteSpace
                };
            })
            """);
        var clear = geometry[0];
        var refresh = geometry[1];
        Assert.InRange(Math.Abs(clear.GetProperty("x").GetDouble() - refresh.GetProperty("x").GetDouble()), 0, 1);
        Assert.True(refresh.GetProperty("y").GetDouble() > clear.GetProperty("y").GetDouble(), geometry.ToString());
        foreach (var button in geometry.EnumerateArray())
        {
            Assert.True(button.GetProperty("height").GetDouble() >= 44, geometry.ToString());
            Assert.True(button.GetProperty("scrollWidth").GetDouble() <= button.GetProperty("clientWidth").GetDouble(), geometry.ToString());
        }

        var selectedSort = page.Locator(".list-toolbar .mud-select-input").First;
        Assert.Equal("ลูกค้าใหม่ล่าสุด", (await selectedSort.InnerTextAsync()).Trim());
        Assert.True(await selectedSort.EvaluateAsync<bool>("element => element.getBoundingClientRect().width >= 152"));
        Assert.True(await selectedSort.EvaluateAsync<bool>("element => element.scrollWidth <= element.clientWidth"));

        await page.Locator(".list-toolbar .mud-select").First.ClickAsync();
        var sortOptions = page.Locator(".mud-popover-open .mud-list-item");
        await sortOptions.First.WaitForAsync();
        Assert.Equal(new[]
        {
            "ลูกค้าใหม่ล่าสุด",
            "ลูกค้าเก่าสุด",
            "อัปเดตล่าสุด",
            "อัปเดตนานที่สุด",
            "บริษัท ก–ฮ",
            "บริษัท ฮ–ก",
            "อีเมล A–Z",
            "อีเมล Z–A",
            "ID มากไปน้อย",
            "ID น้อยไปมาก"
        }, await sortOptions.AllInnerTextsAsync());
        var sortMenuGeometry = await page.Locator(".mud-popover-open").Last.EvaluateAsync<JsonElement>("""
            element => {
                const rect = element.getBoundingClientRect();
                return { left: rect.left, right: rect.right, width: rect.width, viewport: innerWidth };
            }
            """);
        Assert.True(sortMenuGeometry.GetProperty("left").GetDouble() >= 8, sortMenuGeometry.ToString());
        Assert.True(sortMenuGeometry.GetProperty("right").GetDouble() <= sortMenuGeometry.GetProperty("viewport").GetDouble() - 8, sortMenuGeometry.ToString());
        Assert.True(sortMenuGeometry.GetProperty("width").GetDouble() <= 304, sortMenuGeometry.ToString());
        await page.Keyboard.PressAsync("Escape");

        await page.GotoAsync(new Uri(server.BaseUri, "customers?index=2&size=25").AbsoluteUri);
        await page.Locator(".customers-pagination--top").WaitForAsync();
        Assert.Equal(1, await page.GetByRole(AriaRole.Navigation, new() { Name = "หน้าลูกค้าก่อนรายการ" }).CountAsync());
        Assert.Equal(1, await page.GetByRole(AriaRole.Navigation, new() { Name = "หน้าลูกค้าหลังรายการ" }).CountAsync());
        await AssertNoDocumentOverflowAsync(page);
    }

    [Theory]
    [InlineData(1280)]
    [InlineData(768)]
    [InlineData(390)]
    [InlineData(320)]
    public async Task CustomerResultsExposeCompactPaginationBeforeAndAfterRecords(int width)
    {
        await using var context = await playwright.Browser.NewContextAsync(new()
        {
            ViewportSize = new() { Width = width, Height = 844 },
            DeviceScaleFactor = 1,
            ReducedMotion = ReducedMotion.Reduce
        });
        var page = await context.NewPageAsync();
        await StubProductionBoundariesAsync(page);

        await page.GotoAsync(new Uri(server.BaseUri, "customers?index=2&size=25").AbsoluteUri);
        await page.Locator(".customers-table .mud-table-body .mud-table-row").First.WaitForAsync();

        var topPager = page.Locator(".customers-pagination--top");
        var bottomPager = page.Locator(".customers-pagination--bottom");
        await topPager.WaitForAsync();
        await bottomPager.WaitForAsync();
        Assert.Equal(1, await page.GetByRole(AriaRole.Navigation, new() { Name = "Customer pages before results" }).CountAsync());
        Assert.Equal(1, await page.GetByRole(AriaRole.Navigation, new() { Name = "Customer pages after results" }).CountAsync());
        Assert.Equal("Page 2 of 4 · 76 records", (await page.Locator(".customers-results-summary").InnerTextAsync()).Trim());
        Assert.True(await topPager.EvaluateAsync<bool>("element => element.getBoundingClientRect().top < document.querySelector('.customers-table .mud-table-body .mud-table-row').getBoundingClientRect().top"));
        Assert.True(await bottomPager.EvaluateAsync<bool>("element => element.getBoundingClientRect().top > document.querySelector('.customers-table .mud-table-body .mud-table-row:last-child').getBoundingClientRect().bottom"));

        var topButtons = topPager.GetByRole(AriaRole.Button);
        Assert.Equal(2, await topButtons.CountAsync());
        Assert.True(await topButtons.Nth(0).IsEnabledAsync());
        Assert.True(await topButtons.Nth(1).IsEnabledAsync());
        if (width <= 390)
        {
            var targets = await page.Locator(".customers-pagination .mud-button-root").EvaluateAllAsync<JsonElement>("""
                elements => elements.map(element => {
                    const rect = element.getBoundingClientRect();
                    return { width: rect.width, height: rect.height };
                })
                """);
            foreach (var target in targets.EnumerateArray())
            {
                Assert.True(target.GetProperty("width").GetDouble() >= 44, targets.ToString());
                Assert.True(target.GetProperty("height").GetDouble() >= 44, targets.ToString());
            }
        }
        await topButtons.Nth(1).ClickAsync();
        await page.WaitForURLAsync(url => url.Contains("index=3", StringComparison.Ordinal));
        Assert.Equal("Page 3 of 4 · 76 records", (await page.Locator(".customers-results-summary").InnerTextAsync()).Trim());
        await topButtons.Nth(1).ClickAsync();
        await page.WaitForURLAsync(url => url.Contains("index=4", StringComparison.Ordinal));
        Assert.False(await topButtons.Nth(1).IsEnabledAsync());
        await AssertNoDocumentOverflowAsync(page);
    }

    [Fact]
    public async Task CustomerSortUsesOutcomeLabelsAndSortableHeadersWithoutChangingContractValues()
    {
        await using var context = await playwright.Browser.NewContextAsync(new()
        {
            ViewportSize = new() { Width = 1280, Height = 900 },
            DeviceScaleFactor = 1,
            ReducedMotion = ReducedMotion.Reduce
        });
        var page = await context.NewPageAsync();
        await StubProductionBoundariesAsync(page);

        var expectedSorts = new (string Value, string Label)[]
        {
            ("CustomerCreatedDate_Descending", "Newest customers"),
            ("CustomerCreatedDate_Ascending", "Oldest customers"),
            ("CustomerModifiedDate_Descending", "Recently updated"),
            ("CustomerModifiedDate_Ascending", "Updated longest ago"),
            ("CustomerCompany_Ascending", "Company A–Z"),
            ("CustomerCompany_Descending", "Company Z–A"),
            ("CustomerEmail_Ascending", "Email A–Z"),
            ("CustomerEmail_Descending", "Email Z–A"),
            ("CustomerId_Descending", "Highest ID first"),
            ("CustomerId_Ascending", "Lowest ID first")
        };

        foreach (var expected in expectedSorts)
        {
            await page.GotoAsync(new Uri(server.BaseUri, $"customers?sort={expected.Value}&index=2&size=25").AbsoluteUri);
            await page.Locator(".customers-table .mud-table-body .mud-table-row").First.WaitForAsync();
            Assert.Contains($"sort={expected.Value}", page.Url, StringComparison.Ordinal);
            Assert.Equal(expected.Label, (await page.GetByRole(AriaRole.Combobox, new() { Name = "Sort by" }).InnerTextAsync()).Trim());
        }

        await page.GotoAsync(new Uri(server.BaseUri, "customers?sort=CustomerCreatedDate_Descending&index=3&size=25").AbsoluteUri);
        await page.Locator(".customers-table .mud-table-body .mud-table-row").First.WaitForAsync();
        Assert.Equal(0, await page.GetByRole(AriaRole.Button, new() { Name = "Sort by name" }).CountAsync());
        Assert.Equal(3, await page.Locator(".customer-sort-button").CountAsync());

        var headerSorts = new (string Name, string Cell, string Ascending, string Descending)[]
        {
            ("Sort by ID", ".customer-id-cell", "CustomerId_Ascending", "CustomerId_Descending"),
            ("Sort by email", ".customer-email-cell", "CustomerEmail_Ascending", "CustomerEmail_Descending"),
            ("Sort by company", ".customer-company-cell", "CustomerCompany_Ascending", "CustomerCompany_Descending")
        };
        foreach (var header in headerSorts)
        {
            await page.GotoAsync(new Uri(server.BaseUri, "customers?sort=CustomerCreatedDate_Descending&index=3&size=25").AbsoluteUri);
            await page.Locator(".customers-table .mud-table-body .mud-table-row").First.WaitForAsync();
            var button = page.GetByRole(AriaRole.Button, new() { Name = header.Name });
            await button.FocusAsync();
            await button.PressAsync("Enter");
            await page.WaitForURLAsync(url => url.Contains($"sort={header.Ascending}", StringComparison.Ordinal) && url.Contains("index=1", StringComparison.Ordinal));
            Assert.Equal("ascending", await page.Locator($".mud-table-head {header.Cell}").GetAttributeAsync("aria-sort"));

            await button.FocusAsync();
            await button.PressAsync("Space");
            await page.WaitForURLAsync(url => url.Contains($"sort={header.Descending}", StringComparison.Ordinal) && url.Contains("index=1", StringComparison.Ordinal));
            Assert.Equal("descending", await page.Locator($".mud-table-head {header.Cell}").GetAttributeAsync("aria-sort"));
        }
    }

    [Theory]
    [InlineData(390)]
    [InlineData(320)]
    public async Task NarrowCustomerCardsExcludeHiddenSortableHeadersFromFocusAndAccessibility(int width)
    {
        await using var context = await playwright.Browser.NewContextAsync(new()
        {
            ViewportSize = new() { Width = width, Height = 844 },
            DeviceScaleFactor = 1,
            ReducedMotion = ReducedMotion.Reduce
        });
        var page = await context.NewPageAsync();
        await StubProductionBoundariesAsync(page);

        await page.GotoAsync(new Uri(server.BaseUri, "customers").AbsoluteUri);
        await page.Locator(".customers-table .mud-table-body .mud-table-row").First.WaitForAsync();

        Assert.False(await page.Locator(".customers-table .mud-table-head").IsVisibleAsync());
        foreach (var name in new[] { "Sort by ID", "Sort by email", "Sort by company" })
            Assert.Equal(0, await page.GetByRole(AriaRole.Button, new() { Name = name }).CountAsync());

        await page.EvaluateAsync("() => document.activeElement?.blur()");
        for (var index = 0; index < 20; index++)
        {
            await page.Keyboard.PressAsync("Tab");
            Assert.False(await page.EvaluateAsync<bool>("() => document.activeElement?.classList.contains('customer-sort-button') === true"));
        }
    }

    private static async Task StubProductionBoundariesAsync(IPage page)
    {
        var session = JsonSerializer.Serialize(new
        {
            isAuthenticated = true,
            employeeId = "browser-test-employee",
            displayName = "Browser Test Employee",
            roles = new[] { "Employee" },
            csrfToken = "browser-test-csrf",
            legacyDatabaseId = 1,
            permissions = new[] { "customers.read", "customers.create" }
        });
        var customerItems = new object[]
        {
            new
            {
                id = 2000000001,
                firstName = "Natthapol",
                lastName = "Vanasrivilai",
                fullName = LongName,
                email = LongEmail,
                company = new { id = 77, name = LongCompany }
            },
            new
            {
                id = 42,
                firstName = "Short",
                lastName = "Name",
                fullName = "Short Name",
                email = "short@maliev.com",
                company = (object?)null
            }
        };

        await page.RouteAsync("**/bff/session", route => route.FulfillAsync(new()
        {
            Status = 200,
            ContentType = "application/json",
            Body = session
        }));
        await page.RouteAsync("**/bff/customers?*", async route =>
        {
            var uri = new Uri(route.Request.Url);
            var query = System.Web.HttpUtility.ParseQueryString(uri.Query);
            var isSearch = string.Equals(query["search"], "Natthapol", StringComparison.OrdinalIgnoreCase);
            var pageIndex = int.TryParse(query["index"], out var parsedIndex) ? parsedIndex : 1;
            var isPagedJourney = pageIndex > 1;
            var body = JsonSerializer.Serialize(new
            {
                items = isSearch ? customerItems.Take(1).ToArray() : customerItems,
                pageIndex,
                totalPages = isPagedJourney ? 4 : 1,
                totalRecords = isSearch ? 1 : isPagedJourney ? 76 : 2,
                hasNextPage = isPagedJourney && pageIndex < 4,
                hasPreviousPage = isPagedJourney
            });
            await route.FulfillAsync(new()
            {
                Status = 200,
                ContentType = "application/json",
                Body = body
            });
        });
    }

    private static async Task AssertAtomicAsync(IPage page, string selector, string expectedText)
    {
        var element = page.Locator($".customers-table .mud-table-body .mud-table-row:first-child {selector}");
        Assert.Equal(expectedText, (await element.InnerTextAsync()).Trim());
        Assert.Equal("nowrap", await element.EvaluateAsync<string>("node => getComputedStyle(node).whiteSpace"));
        Assert.InRange(await element.EvaluateAsync<float>("node => node.getBoundingClientRect().height"), 1, 72);
    }

    private static async Task AssertFullValueDisclosureAsync(IPage page, string selector, string expectedText)
    {
        var trigger = page.Locator($".customers-table .mud-table-body .mud-table-row:first-child {selector} button");
        await trigger.FocusAsync();
        Assert.True(await trigger.EvaluateAsync<bool>("element => element === document.activeElement"));
        await trigger.PressAsync("Enter");

        var menu = page.Locator(".mud-popover-open").Last;
        await menu.WaitForAsync();
        Assert.Equal(expectedText, (await menu.Locator(".customer-full-value").InnerTextAsync()).Trim());
        await page.Keyboard.PressAsync("Escape");
        await menu.WaitForAsync(new() { State = WaitForSelectorState.Hidden });
    }

    private static async Task AssertDisclosuresContainedAsync(IPage page)
    {
        var geometry = await page.Locator(".customers-table .mud-table-body .mud-table-row:first-child .customer-value-disclosure")
            .EvaluateAllAsync<JsonElement>("""
                elements => elements.map(root => {
                    const cell = root.closest('.mud-table-cell');
                    const trigger = root.querySelector('button');
                    const label = trigger?.querySelector('.mud-button-label');
                    const rootRect = root.getBoundingClientRect();
                    const cellRect = cell.getBoundingClientRect();
                    const triggerRect = trigger.getBoundingClientRect();
                    const style = getComputedStyle(root);
                    return {
                        rootLeft: rootRect.left,
                        rootRight: rootRect.right,
                        rootWidth: rootRect.width,
                        cellLeft: cellRect.left,
                        cellRight: cellRect.right,
                        cellWidth: cellRect.width,
                        triggerLeft: triggerRect.left,
                        triggerRight: triggerRect.right,
                        triggerWidth: triggerRect.width,
                        labelScrollWidth: label.scrollWidth,
                        labelClientWidth: label.clientWidth,
                        borderTopWidth: style.borderTopWidth,
                        boxShadow: style.boxShadow
                    };
                })
                """);

        Assert.NotEmpty(geometry.EnumerateArray());
        foreach (var disclosure in geometry.EnumerateArray())
        {
            Assert.True(disclosure.GetProperty("rootLeft").GetDouble() >= disclosure.GetProperty("cellLeft").GetDouble() - 0.5, geometry.ToString());
            Assert.True(disclosure.GetProperty("rootRight").GetDouble() <= disclosure.GetProperty("cellRight").GetDouble() + 0.5, geometry.ToString());
            Assert.True(disclosure.GetProperty("triggerLeft").GetDouble() >= disclosure.GetProperty("cellLeft").GetDouble() - 0.5, geometry.ToString());
            Assert.True(disclosure.GetProperty("triggerRight").GetDouble() <= disclosure.GetProperty("cellRight").GetDouble() + 0.5, geometry.ToString());
            Assert.True(disclosure.GetProperty("rootWidth").GetDouble() <= disclosure.GetProperty("cellWidth").GetDouble() + 0.5, geometry.ToString());
            Assert.True(disclosure.GetProperty("triggerWidth").GetDouble() <= disclosure.GetProperty("cellWidth").GetDouble() + 0.5, geometry.ToString());
            Assert.Equal("0px", disclosure.GetProperty("borderTopWidth").GetString());
            Assert.Equal("none", disclosure.GetProperty("boxShadow").GetString());
        }
    }

    private static async Task AssertNoDocumentOverflowAsync(IPage page)
    {
        var geometry = await page.EvaluateAsync<JsonElement>("""
            () => {
                const root = document.documentElement;
                const offenders = Array.from(document.querySelectorAll('body *'))
                    .map(element => ({
                        tag: element.tagName,
                        classes: element.className?.toString() ?? '',
                        left: element.getBoundingClientRect().left,
                        right: element.getBoundingClientRect().right,
                        scrollWidth: element.scrollWidth,
                        clientWidth: element.clientWidth
                    }))
                    .filter(item => item.right > root.clientWidth + 0.5)
                    .slice(0, 5);
                const containers = Array.from(document.querySelectorAll('.customers-table-shell, .customers-table, .mud-table-container'))
                    .map(element => {
                        const rect = element.getBoundingClientRect();
                        const style = getComputedStyle(element);
                        return {
                            classes: element.className?.toString() ?? '',
                            left: rect.left,
                            right: rect.right,
                            scrollWidth: element.scrollWidth,
                            clientWidth: element.clientWidth,
                            overflowX: style.overflowX,
                            minWidth: style.minWidth,
                            width: style.width
                        };
                    });
                return { scrollWidth: root.scrollWidth, clientWidth: root.clientWidth, offenders, containers };
            }
            """);
        var horizontalTravel = await page.EvaluateAsync<double>("""
            () => {
                window.scrollTo({ left: 100000, behavior: 'instant' });
                const travel = window.scrollX;
                window.scrollTo({ left: 0, behavior: 'instant' });
                return travel;
            }
            """);
        Assert.True(horizontalTravel == 0, geometry.ToString());
    }
}
