using System.Text.Json;
using Maliev.ShadcnBlazor.BrowserTests.Infrastructure;
using Microsoft.Playwright;

namespace Maliev.ShadcnBlazor.BrowserTests;

[Collection(CustomerBrowserCollection.Name)]
public sealed class CustomerDetailBrowserTests(
    IntranetClientServerFixture server,
    PlaywrightFixture playwright)
{
    [Theory]
    [InlineData(1280)]
    [InlineData(768)]
    [InlineData(390)]
    [InlineData(320)]
    public async Task ProductionCustomerDetailUsesAResponsiveRecordHierarchy(int width)
    {
        await using var context = await playwright.Browser.NewContextAsync(new()
        {
            ViewportSize = new() { Width = width, Height = 900 },
            DeviceScaleFactor = 1,
            ReducedMotion = ReducedMotion.Reduce
        });
        var page = await context.NewPageAsync();
        var errors = new List<string>();
        page.Console += (_, message) => { if (message.Type == "error") errors.Add(message.Text); };
        page.PageError += (_, error) => errors.Add(error);
        await StubCustomerDetailBoundariesAsync(page);

        await page.GotoAsync(new Uri(server.BaseUri, "Customers/View?id=69738").AbsoluteUri);
        await page.GetByRole(AriaRole.Heading, new() { Name = "ธันวรินต์ กวินภัทรลักษณ์", Level = 1 }).WaitForAsync();

        Assert.Equal(1, await page.Locator(".customer-detail").CountAsync());
        Assert.Equal(1, await page.Locator(".customer-detail__header").CountAsync());
        Assert.Equal(1, await page.Locator(".customer-detail__primary").CountAsync());
        Assert.Equal(1, await page.Locator(".customer-detail__secondary").CountAsync());
        Assert.Equal(1, await page.Locator(".customer-detail__addresses").CountAsync());

        var layout = await page.Locator(".customer-detail__layout").EvaluateAsync<JsonElement>("""
            element => {
                const style = getComputedStyle(element);
                const rect = element.getBoundingClientRect();
                return {
                    columns: style.gridTemplateColumns,
                    gap: style.gap,
                    width: rect.width,
                    left: rect.left,
                    right: rect.right,
                    viewport: document.documentElement.clientWidth,
                    scrollWidth: document.documentElement.scrollWidth
                };
            }
            """);
        Assert.Equal(layout.GetProperty("viewport").GetDouble(), layout.GetProperty("scrollWidth").GetDouble());
        Assert.True(layout.GetProperty("left").GetDouble() >= 0, layout.ToString());
        Assert.True(layout.GetProperty("right").GetDouble() <= layout.GetProperty("viewport").GetDouble() + 0.5, layout.ToString());
        var columns = layout.GetProperty("columns").GetString() ?? string.Empty;
        if (width >= 900)
            Assert.Contains(' ', columns);
        else
            Assert.DoesNotContain(' ', columns);

        var detailRows = page.Locator(".customer-detail__details > div");
        Assert.True(await detailRows.CountAsync() >= 5);
        Assert.All(await detailRows.EvaluateAllAsync<string[]>(
            "elements => elements.map(element => getComputedStyle(element).display)"),
            display => Assert.Equal("grid", display));

        Assert.Empty(errors);
    }

    [Fact]
    public async Task ProductionCustomerEditFormUsesShadcnFieldsAndASeparatedActionRow()
    {
        await using var context = await playwright.Browser.NewContextAsync(new()
        {
            ViewportSize = new() { Width = 1280, Height = 1000 },
            DeviceScaleFactor = 1,
            ReducedMotion = ReducedMotion.Reduce
        });
        var page = await context.NewPageAsync();
        await StubCustomerDetailBoundariesAsync(page);

        await page.GotoAsync(new Uri(server.BaseUri, "Customers/View?id=69738").AbsoluteUri);
        await page.GetByRole(AriaRole.Button, new() { Name = "Edit customer", Exact = true }).ClickAsync();

        var formGrid = page.Locator(".customer-detail__form-grid");
        Assert.Equal(1, await formGrid.CountAsync());
        Assert.Contains(' ', await formGrid.EvaluateAsync<string>("element => getComputedStyle(element).gridTemplateColumns"));

        var dateInput = page.GetByLabel("Date of birth", new() { Exact = true });
        var dateGeometry = await dateInput.EvaluateAsync<JsonElement>("""
            element => {
                const control = element.closest('.mud-input-control');
                const input = element.closest('.mud-input');
                const border = input.querySelector('.mud-input-outlined-border') ?? input;
                const label = control.querySelector('.mud-input-label');
                const inputRect = input.getBoundingClientRect();
                const labelRect = label.getBoundingClientRect();
                return {
                    height: inputRect.height,
                    borderWidth: getComputedStyle(border).borderTopWidth,
                    borderRadius: getComputedStyle(border).borderRadius,
                    labelPosition: getComputedStyle(label).position,
                    labelBottom: labelRect.bottom,
                    inputTop: inputRect.top
                };
            }
            """);
        Assert.Equal(36d, dateGeometry.GetProperty("height").GetDouble(), precision: 1);
        Assert.Equal("1px", dateGeometry.GetProperty("borderWidth").GetString());
        Assert.NotEqual("0px", dateGeometry.GetProperty("borderRadius").GetString());
        Assert.Equal("static", dateGeometry.GetProperty("labelPosition").GetString());
        Assert.True(dateGeometry.GetProperty("labelBottom").GetDouble() <= dateGeometry.GetProperty("inputTop").GetDouble() - 6d);

        var actionRow = page.Locator(".customer-detail__form-actions");
        Assert.Equal("1px", await actionRow.EvaluateAsync<string>("element => getComputedStyle(element).borderTopWidth"));
        Assert.True(await actionRow.GetByRole(AriaRole.Button, new() { Name = "Save changes" }).IsVisibleAsync());
        Assert.True(await actionRow.GetByRole(AriaRole.Button, new() { Name = "Cancel" }).IsVisibleAsync());

        await page.SetViewportSizeAsync(390, 844);
        Assert.DoesNotContain(' ', await formGrid.EvaluateAsync<string>("element => getComputedStyle(element).gridTemplateColumns"));
        Assert.True(await dateInput.EvaluateAsync<double>("element => element.closest('.mud-input').getBoundingClientRect().height") >= 44d);
        Assert.Equal(
            await page.EvaluateAsync<double>("() => document.documentElement.clientWidth"),
            await page.EvaluateAsync<double>("() => document.documentElement.scrollWidth"));
    }

    private static async Task StubCustomerDetailBoundariesAsync(IPage page)
    {
        await page.RouteAsync("**/bff/session", route => route.FulfillAsync(new()
        {
            Status = 200,
            ContentType = "application/json",
            Body = JsonSerializer.Serialize(new
            {
                isAuthenticated = true,
                employeeId = "customer-detail-browser-employee",
                displayName = "Customer Detail Browser Employee",
                roles = new[] { "Employee" },
                csrfToken = "customer-detail-browser-csrf",
                legacyDatabaseId = 1,
                permissions = new[]
                {
                    "customers.read",
                    "legacy-customer.customers.update"
                }
            })
        }));
        await page.RouteAsync("**/bff/customers/69738", route => route.FulfillAsync(new()
        {
            Status = 200,
            ContentType = "application/json",
            Body = JsonSerializer.Serialize(new
            {
                id = 69738,
                firstName = "ธันวรินต์",
                lastName = "กวินภัทรลักษณ์",
                fullName = "ธันวรินต์ กวินภัทรลักษณ์",
                telephone = (string?)null,
                mobile = "0612024146",
                fax = (string?)null,
                email = "thunwalin@theonesamui.com",
                dateOfBirth = "1992-08-12T00:00:00Z",
                companyId = 77,
                billingAddressId = 101,
                shippingAddressId = 102,
                createdDate = "2026-07-13T02:43:00Z",
                modifiedDate = "2026-07-13T02:44:00Z",
                billingAddress = new
                {
                    id = 101,
                    building = "128/41",
                    addressLine1 = "หมู่ที่ 1 ตำบลบ่อผุด",
                    addressLine2 = (string?)null,
                    city = "อำเภอเกาะสมุย",
                    state = "สุราษฎร์ธานี",
                    postalCode = "84320",
                    countryId = 764,
                    createdDate = "2026-07-13T02:43:00Z",
                    modifiedDate = "2026-07-13T02:44:00Z"
                },
                company = new
                {
                    id = 77,
                    name = "บริษัท เดอะ สมุย วัน จำกัด",
                    taxNumber = "0845560005099 (สำนักงานใหญ่)",
                    registrar = (string?)null,
                    createdDate = "2026-07-13T02:43:00Z",
                    modifiedDate = "2026-07-13T02:44:00Z"
                },
                shippingAddress = new
                {
                    id = 102,
                    building = "128/41",
                    addressLine1 = "หมู่ที่ 1 ตำบลบ่อผุด",
                    addressLine2 = (string?)null,
                    city = "อำเภอเกาะสมุย",
                    state = "สุราษฎร์ธานี",
                    postalCode = "84320",
                    countryId = 764,
                    createdDate = "2026-07-13T02:43:00Z",
                    modifiedDate = "2026-07-13T02:44:00Z"
                }
            })
        }));
    }
}
