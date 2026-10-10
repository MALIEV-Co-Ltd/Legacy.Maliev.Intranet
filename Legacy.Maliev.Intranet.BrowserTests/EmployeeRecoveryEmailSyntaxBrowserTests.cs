using System.Collections.Concurrent;
using System.Text.Json;
using Legacy.Maliev.Intranet.BrowserTests.Infrastructure;
using Microsoft.Playwright;

namespace Legacy.Maliev.Intranet.BrowserTests;

[Collection(CustomerBrowserCollection.Name)]
public sealed class EmployeeRecoveryEmailSyntaxBrowserTests(IntranetClientServerFixture server, PlaywrightFixture playwright)
{
    [Theory]
    [InlineData("ForgotPassword", "employee-recovery-email", "password-reset", "en-US")]
    [InlineData("ForgotPassword", "employee-recovery-email", "password-reset", "th-TH")]
    [InlineData("ResendConfirmation", "employee-confirmation-email", "email-confirmation", "en-US")]
    [InlineData("ResendConfirmation", "employee-confirmation-email", "email-confirmation", "th-TH")]
    public async Task NativeSubmit_UsesHistoricalPredicateInsteadOfHtmlEmailSyntax(string pageName, string inputId, string action, string culture)
    {
        await using var context = await playwright.Browser.NewContextAsync();
        await context.AddInitScriptAsync($"localStorage.setItem('maliev_culture', {JsonSerializer.Serialize(culture)});");
        var page = await context.NewPageAsync();
        page.SetDefaultTimeout(15000);
        var posted = new ConcurrentQueue<string>();
        await page.RouteAsync("https://accounts.google.com/**", route => route.AbortAsync());
        await page.RouteAsync("**/bff/session", route => route.FulfillAsync(new()
        {
            Status = 200, ContentType = "application/json",
            Body = "{\"isAuthenticated\":false,\"employeeId\":null,\"displayName\":null,\"roles\":[],\"csrfToken\":\"browser-csrf\"}",
        }));
        await page.RouteAsync($"**/bff/employee-recovery/{action}/request", async route =>
        {
            Assert.Equal("POST", route.Request.Method);
            using var json = JsonDocument.Parse(route.Request.PostData!);
            posted.Enqueue(json.RootElement.GetProperty("email").GetString()!);
            await route.FulfillAsync(new() { Status = 202, ContentType = "application/json", Body = "{\"accepted\":true}" });
        });
        await page.GotoAsync(new Uri(server.BaseUri, $"Employees/{pageName}").AbsoluteUri);
        var input = page.Locator($"#{inputId}");
        await input.WaitForAsync();
        var form = page.Locator("form.employee-recovery__form");
        Assert.True(await form.EvaluateAsync<bool>("form => form.noValidate"));
        var submit = form.Locator("button[type=submit]");
        await input.FillAsync("Recovery <recovery@maliev.test>");
        await submit.ClickAsync();
        await page.WaitForFunctionAsync("id => document.getElementById(id).getAttribute('aria-invalid') === 'true'", inputId);
        await Assertions.Expect(page.Locator("main.employee-recovery")).ToContainTextAsync(
            culture == "th-TH" ? "โปรดระบุอีเมลที่ถูกต้อง" : "Enter a valid email address.");
        Assert.Empty(posted);
        const string quoted = "\"recovery@office\"@maliev.test";
        await input.FillAsync(quoted);
        Assert.True(await input.EvaluateAsync<bool>("input => input.validity.typeMismatch"));
        await page.RunAndWaitForResponseAsync(() => submit.ClickAsync(),
            response => response.Request.Method == "POST" && response.Url.EndsWith($"/bff/employee-recovery/{action}/request", StringComparison.Ordinal));
        Assert.Equal(quoted, Assert.Single(posted));
    }
}
