using System.Collections.Concurrent;
using System.Text.Json;
using Legacy.Maliev.Intranet.BrowserTests.Infrastructure;
using Legacy.Maliev.Intranet.Contracts;
using Microsoft.Playwright;

namespace Legacy.Maliev.Intranet.BrowserTests;

public sealed class EmployeeAdministrationLifecycleBrowserTests
{
    [Fact]
    public async Task RoutedEditFencesDuplicateSaveAndUsesFreshSessionReadForIdentityChanges()
    {
        var server = new IntranetClientServerFixture();
        var playwright = new PlaywrightFixture();
        try
        {
            await server.InitializeAsync();
            await playwright.InitializeAsync();
            await ExerciseAsync(server.BaseUri, playwright.Browser);
        }
        finally
        {
            try { await playwright.DisposeAsync(); }
            finally { await server.DisposeAsync(); }
        }

        // The owned fixture has stopped listening before the test can pass.
        using var http = new HttpClient(new HttpClientHandler { UseProxy = false }) { Timeout = TimeSpan.FromSeconds(3) };
        await Assert.ThrowsAsync<HttpRequestException>(async () =>
        {
            using var response = await http.GetAsync(server.BaseUri);
        });
    }

    private static async Task ExerciseAsync(Uri origin, IBrowser browser)
    {
        const int employeeId = 41;
        const string detailPath = "/bff/employees/41";
        const string editPath = detailPath + "/edit";
        const string phone = "+66812345678";
        const string email = "synthetic@example.invalid";
        var json = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        var edit = new EmployeeAdministrationEdit(
            new(employeeId, 7, "Synthetic", "Employee", "Synthetic Employee", phone, email, null, null, null, null),
            new("synthetic-identity", email, email, true, phone, false, true, null, true, 0, employeeId, new string('A', 64)),
            null, [new(7, "Engineer", null, null, null)], "\"" + new string('a', 64) + "\"",
            "\"" + new string('A', 64) + "\"", null, [new(66, "Thailand")]);
        var detail = new EmployeeDetail(employeeId, 7, "Synthetic", "Employee", "Synthetic Employee", phone,
            email, null, null, null, null, null, null);
        var firstWrite = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseWrite = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var unexpected = new ConcurrentQueue<string>();
        var writes = new ConcurrentQueue<(string? Token, EmployeeAdministrationSaveRequest Body)>();
        int sessionReads = 0, editReads = 0, viewReads = 0;

        await using var context = await browser.NewContextAsync(new()
        {
            ServiceWorkers = ServiceWorkerPolicy.Block,
        });
        await context.AddInitScriptAsync("localStorage.setItem('maliev_culture', 'en-US');");
        await context.RouteWebSocketAsync("**/*", route => _ = route.CloseAsync());
        await context.RouteAsync("**/*", async route =>
        {
            var request = route.Request;
            var uri = new Uri(request.Url);
            if (uri.GetLeftPart(UriPartial.Authority) != origin.GetLeftPart(UriPartial.Authority))
            {
                await route.AbortAsync();
                return;
            }

            object payload;
            if (request.Method == "GET" && uri.AbsolutePath == "/bff/session")
            {
                var token = $"synthetic-csrf-{Interlocked.Increment(ref sessionReads)}";
                payload = new
                {
                    isAuthenticated = true,
                    employeeId = "synthetic-administrator",
                    displayName = "Synthetic Administrator",
                    roles = new[] { "Employee" },
                    legacyDatabaseId = 42,
                    permissions = new[] { "legacy-employee.employees.read", "legacy-employee.employees.update" },
                    csrfToken = token,
                };
            }
            else if (request.Method == "GET" && uri.AbsolutePath == detailPath)
            {
                Interlocked.Increment(ref viewReads);
                payload = detail;
            }
            else if (request.Method == "GET" && uri.AbsolutePath == editPath)
            {
                Interlocked.Increment(ref editReads);
                payload = edit;
            }
            else if (request.Method == "PUT" && uri.AbsolutePath == editPath
                && JsonSerializer.Deserialize<EmployeeAdministrationSaveRequest>(request.PostData ?? "null", json) is { } body)
            {
                request.Headers.TryGetValue("x-csrf-token", out var token);
                writes.Enqueue((token, body));
                firstWrite.TrySetResult();
                await releaseWrite.Task.WaitAsync(TimeSpan.FromSeconds(15));
                payload = new EmployeeAdministrationSaveResult("complete", ["identity", "profile"], false, 200);
            }
            else if (request.Method == "GET" && !uri.AbsolutePath.StartsWith("/bff", StringComparison.OrdinalIgnoreCase))
            {
                await route.ContinueAsync();
                return;
            }
            else
            {
                unexpected.Enqueue(request.Method + " " + uri.AbsolutePath);
                await route.AbortAsync();
                return;
            }

            await route.FulfillAsync(new() { Status = 200, ContentType = "application/json", Body = JsonSerializer.Serialize(payload, json) });
        });

        await using var page = await context.NewPageAsync();
        page.SetDefaultTimeout(15_000);
        page.SetDefaultNavigationTimeout(20_000);
        try
        {
            await page.GotoAsync(new Uri(origin, "/Employees/View?id=41").AbsoluteUri);
            await Assertions.Expect(page.Locator("main.employee-detail h1")).ToHaveTextAsync("Synthetic Employee");
            await page.Locator("a[href='/Employees/Edit?id=41']").ClickAsync();
            var form = page.Locator("form.employee-edit__form[data-employee-id='41']");
            var save = form.Locator("button[type=submit]");
            await Assertions.Expect(save).ToBeEnabledAsync();

            foreach (var id in new[] { "email-confirmed", "phone-confirmed", "lockout" })
                await form.Locator("#employee-edit-" + id).ClickAsync();

            var readsBefore = Volatile.Read(ref sessionReads);
            await save.ClickAsync();
            await firstWrite.Task.WaitAsync(TimeSpan.FromSeconds(10));
            await Assertions.Expect(save).ToBeDisabledAsync();
            await Assertions.Expect(page.Locator("main.employee-edit")).ToHaveAttributeAsync("aria-busy", "true");
            await form.DispatchEventAsync("submit", new { bubbles = true, cancelable = true });
            await form.DispatchEventAsync("submit", new { bubbles = true, cancelable = true });
            releaseWrite.TrySetResult();
            await Assertions.Expect(page.Locator("main.employee-edit")).ToContainTextAsync("All requested save steps completed.");
            Assert.Equal(readsBefore + 1, Volatile.Read(ref sessionReads));
            var first = Assert.Single(writes);
            Assert.Equal($"synthetic-csrf-{readsBefore + 1}", first.Token);
            Assert.False(first.Body.EmailConfirmed);
            Assert.True(first.Body.PhoneNumberConfirmed);
            Assert.False(first.Body.LockoutEnabled);

            await page.GetByRole(AriaRole.Button, new() { Name = "Reload current data (discard edits)" }).ClickAsync();
            await Assertions.Expect(save).ToBeEnabledAsync();
            var beforeSecond = Volatile.Read(ref sessionReads);
            await save.ClickAsync();
            await Assertions.Expect(page.Locator("main.employee-edit")).ToContainTextAsync("All requested save steps completed.");
            var completed = writes.ToArray();
            Assert.Equal(2, completed.Length);
            Assert.Equal(beforeSecond + 1, Volatile.Read(ref sessionReads));
            Assert.Equal($"synthetic-csrf-{beforeSecond + 1}", completed[1].Token);
            Assert.NotEqual(first.Token, completed[1].Token);
            Assert.True(completed[1].Body.EmailConfirmed);
            Assert.False(completed[1].Body.PhoneNumberConfirmed);
            Assert.True(completed[1].Body.LockoutEnabled);
            Assert.Equal(1, Volatile.Read(ref viewReads));
            Assert.Equal(2, Volatile.Read(ref editReads));
            Assert.Empty(unexpected);
        }
        finally { releaseWrite.TrySetResult(); }
    }
}
