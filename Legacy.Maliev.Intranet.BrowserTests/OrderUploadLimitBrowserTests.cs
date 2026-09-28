using Legacy.Maliev.Intranet.BrowserTests.Infrastructure;
using Microsoft.Playwright;

namespace Legacy.Maliev.Intranet.BrowserTests;

[Collection(CustomerBrowserCollection.Name)]
public sealed class OrderUploadLimitBrowserTests(
    IntranetClientServerFixture server,
    PlaywrightFixture playwright)
{
    [Theory]
    [InlineData("en-TH", "The combined file size cannot exceed 100 MB.")]
    [InlineData("th-TH", "ขนาดไฟล์รวมต้องไม่เกิน 100 MB")]
    public async Task OrderDetailShowsLocalizedAggregateLimitBeforeSendingFiles(string culture, string expectedMessage)
    {
        await using var context = await playwright.Browser.NewContextAsync(new()
        {
            ViewportSize = new() { Width = 390, Height = 844 },
        });
        await context.AddCookiesAsync([new Cookie
        {
            Url = server.BaseUri.AbsoluteUri,
            Name = "maliev_culture",
            Value = culture,
        }]);
        var page = await context.NewPageAsync();
        var uploadCalls = 0;

        await page.RouteAsync("**/bff/session", route => route.FulfillAsync(new()
        {
            Status = 200,
            ContentType = "application/json",
            Body = """
                {"isAuthenticated":true,"employeeId":"upload-browser-employee","email":"upload.browser@maliev.com","displayName":"Upload Browser Employee","roles":["Employee"],"csrfToken":"upload-browser-csrf","legacyDatabaseId":7,"permissions":["legacy.orders.read","legacy.orders.update"]}
                """,
        }));
        await page.RouteAsync("**/bff/orders/9101", route => route.FulfillAsync(new()
        {
            Status = 200,
            ContentType = "application/json",
            Body = """
                {"order":{"id":9101,"customerId":4401,"employeeId":3,"name":"Upload fixture","processId":1,"quantity":1,"manufactured":0,"remaining":1},"processes":[{"id":1,"name":"CNC"}],"materials":[],"colors":[],"surfaceFinishes":[],"currencies":[],"employees":[],"currentStatus":null,"availableStatuses":[],"history":[],"files":[]}
                """,
        }));
        await page.RouteAsync("**/bff/orders/9101/files", route =>
        {
            Interlocked.Increment(ref uploadCalls);
            return route.FulfillAsync(new() { Status = 500 });
        });

        await page.GotoAsync(new Uri(server.BaseUri, "Orders/View?id=9101").AbsoluteUri);
        var fileInput = page.Locator(".order-files-panel input[type='file']");
        await fileInput.WaitForAsync();
        var tempDirectory = Path.Combine(Path.GetTempPath(), "maliev-upload-browser-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDirectory);
        try
        {
            var atLimitPath = Path.Combine(tempDirectory, "at-limit.bin");
            var extraBytePath = Path.Combine(tempDirectory, "one-more-byte.bin");
            using (var stream = File.Create(atLimitPath))
            {
                stream.SetLength(100L * 1024 * 1024);
            }
            await File.WriteAllBytesAsync(extraBytePath, [0]);

            await fileInput.SetInputFilesAsync([atLimitPath, extraBytePath]);
            await page.Locator(".order-upload-actions button").ClickAsync();
            var alert = page.Locator(".order-files-panel [role='alert']");
            await alert.WaitForAsync();

            Assert.Equal(expectedMessage, await alert.InnerTextAsync());
            Assert.Equal(0, Volatile.Read(ref uploadCalls));
        }
        finally
        {
            Directory.Delete(tempDirectory, recursive: true);
        }
    }
}
