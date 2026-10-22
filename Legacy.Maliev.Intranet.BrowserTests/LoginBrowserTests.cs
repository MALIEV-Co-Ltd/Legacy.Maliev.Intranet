using Legacy.Maliev.Intranet.BrowserTests.Infrastructure;
using Microsoft.Playwright;

namespace Legacy.Maliev.Intranet.BrowserTests;

[Collection(CustomerBrowserCollection.Name)]
public sealed class LoginBrowserTests(
    IntranetClientServerFixture server,
    PlaywrightFixture playwright)
{
    [Theory]
    [InlineData(1440, false)]
    [InlineData(375, true)]
    public async Task WasmBootShellShowsTheMalievWordmarkAndHorizontalProgress(int width, bool dark)
    {
        await using var context = await playwright.Browser.NewContextAsync(new()
        {
            ViewportSize = new() { Width = width, Height = 900 },
            ReducedMotion = ReducedMotion.Reduce,
        });
        if (dark)
            await context.AddInitScriptAsync("localStorage.setItem('maliev_theme', 'dark')");

        var page = await context.NewPageAsync();
        await page.RouteAsync("**/_framework/blazor.webassembly.js", route => route.AbortAsync());
        await page.RouteAsync("https://accounts.google.com/**", route => route.AbortAsync());
        await page.GotoAsync(server.BaseUri.AbsoluteUri);

        var loading = page.Locator("#workspace-loading");
        await loading.WaitForAsync();
        Assert.Equal("Loading workspace", await loading.Locator(".legacy-loading-status").InnerTextAsync());
        Assert.Equal(1, await loading.Locator(dark ? ".legacy-loading-logo--dark:visible" : ".legacy-loading-logo--light:visible").CountAsync());
        Assert.Equal(0, await loading.Locator("svg.loading-progress").CountAsync());

        var progress = loading.Locator(".loading-progress");
        await progress.EvaluateAsync("element => element.style.setProperty('--blazor-load-percentage', '63%')");
        var ratio = await progress.EvaluateAsync<double>(
            "element => element.firstElementChild.getBoundingClientRect().width / element.getBoundingClientRect().width");
        Assert.InRange(ratio, 0.62, 0.64);
        var progressBox = (await progress.BoundingBoxAsync())!;
        Assert.True(progressBox.Width <= Math.Min(352, width - 48));
        Assert.InRange(Math.Abs(progressBox.X + (progressBox.Width / 2) - (width / 2d)), 0, 1);

        var logoBox = (await loading.Locator(".legacy-loading-brand").BoundingBoxAsync())!;
        var metaBox = (await loading.Locator(".legacy-loading-meta").BoundingBoxAsync())!;
        var contentCenter = (logoBox.Y + metaBox.Y + metaBox.Height) / 2;
        Assert.InRange(Math.Abs(contentCenter - 450), 0, 2);
    }

    [Theory]
    [InlineData("")]
    [InlineData("Dashboard")]
    public async Task AnonymousEntryPointsShowLoginWithoutTheAuthenticatedApplicationShell(string path)
    {
        await using var context = await playwright.Browser.NewContextAsync(new()
        {
            ViewportSize = new() { Width = 1440, Height = 900 },
        });
        var page = await context.NewPageAsync();
        var errors = new List<string>();
        var sidebarImports = 0;
        page.PageError += (_, error) => errors.Add(error);

        await page.RouteAsync("**/shadcn-disclosure-navigation.js", route =>
        {
            Interlocked.Increment(ref sidebarImports);
            return route.ContinueAsync();
        });

        await page.RouteAsync("**/bff/session", route => route.FulfillAsync(new()
        {
            Status = 200,
            ContentType = "application/json",
            Body = "{\"isAuthenticated\":false,\"employeeId\":null,\"displayName\":null,\"roles\":[],\"csrfToken\":\"anonymous-browser-csrf\",\"legacyDatabaseId\":null,\"permissions\":[]}",
        }));
        await page.RouteAsync("https://accounts.google.com/**", route => route.AbortAsync());

        await page.GotoAsync(new Uri(server.BaseUri, path).AbsoluteUri);

        await page.Locator("#legacy-login-email").WaitForAsync();
        Assert.Equal("/Login", new Uri(page.Url).AbsolutePath);
        Assert.Equal(0, await page.Locator(".legacy-layout").CountAsync());
        Assert.Equal(0, await page.Locator(".legacy-navigation").CountAsync());
        Assert.Equal(0, Volatile.Read(ref sidebarImports));
        Assert.False(await page.Locator("#blazor-error-ui").IsVisibleAsync());
        Assert.Empty(errors);
    }

    [Fact]
    public async Task LoginUsesAccessibleShadcnFormsAcrossEmailAndCredentialSteps()
    {
        await using var context = await playwright.Browser.NewContextAsync(new()
        {
            ViewportSize = new() { Width = 320, Height = 844 },
            HasTouch = true,
            ReducedMotion = ReducedMotion.Reduce,
        });
        var page = await context.NewPageAsync();
        var errors = new List<string>();
        page.PageError += (_, error) => errors.Add(error);

        await page.RouteAsync("**/bff/session", route => route.FulfillAsync(new()
        {
            Status = 200,
            ContentType = "application/json",
            Body = "{\"isAuthenticated\":false,\"employeeId\":null,\"displayName\":null,\"roles\":[],\"csrfToken\":\"login-browser-csrf\",\"legacyDatabaseId\":null,\"permissions\":[]}",
        }));
        await page.RouteAsync("**/bff/login", route => route.FulfillAsync(new()
        {
            Status = 401,
            ContentType = "application/json",
            Body = "{}",
        }));
        await page.RouteAsync("https://accounts.google.com/**", route => route.AbortAsync());

        await page.GotoAsync(new Uri(server.BaseUri, "Login").AbsoluteUri);
        var email = page.Locator("#legacy-login-email");
        await email.WaitForAsync();

        var language = page.GetByRole(AriaRole.Combobox, new() { Name = "Language" });
        Assert.Equal("BUTTON", await language.EvaluateAsync<string>("element => element.tagName"));
        Assert.Equal("select-trigger", await language.GetAttributeAsync("data-slot"));
        Assert.Equal(0, await page.Locator(".legacy-language-selector select").CountAsync());
        Assert.True(await language.EvaluateAsync<bool>(
            """
            trigger => {
                const style = getComputedStyle(trigger);
                const canvas = document.createElement('canvas');
                const context = canvas.getContext('2d');
                context.font = style.font;
                const selectedText = trigger.querySelector('[data-slot="select-value"]').textContent;
                const textWidth = context.measureText(selectedText).width;
                const reservedWidth = parseFloat(style.paddingInlineStart) +
                    parseFloat(style.paddingInlineEnd) +
                    parseFloat(style.borderInlineStartWidth) +
                    parseFloat(style.borderInlineEndWidth);
                return trigger.getBoundingClientRect().width - reservedWidth >= textWidth;
            }
            """));
        await language.ClickAsync();
        await page.GetByRole(AriaRole.Listbox).WaitForAsync();
        var languageOptions = page.GetByRole(AriaRole.Option);
        Assert.Equal(2, await languageOptions.CountAsync());
        Assert.Equal(
            new[] { "English", "ไทย" },
            await languageOptions.EvaluateAllAsync<string[]>(
                "options => options.map(option => option.childNodes[0].textContent.trim())"));
        await page.Keyboard.PressAsync("Escape");
        Assert.Equal("false", await language.GetAttributeAsync("aria-expanded"));
        await language.FocusAsync();
        Assert.Equal("none", await page.Locator(".legacy-language-selector").EvaluateAsync<string>(
            "wrapper => getComputedStyle(wrapper).outlineStyle"));

        var title = page.Locator("#legacy-login-title");
        Assert.Equal("Sign in to MALIEV", (await title.InnerTextAsync()).Trim());
        Assert.Equal(0, await title.Locator("img").CountAsync());

        Assert.Equal(1, await page.Locator(".legacy-login-card .shadcn-input").CountAsync());
        Assert.Equal("legacy-login-email", await page.Locator("label").Filter(new() { HasText = "Email" }).GetAttributeAsync("for"));
        Assert.Contains("legacy-login-email-description", await email.GetAttributeAsync("aria-describedby"));
        Assert.Equal("email", await email.GetAttributeAsync("type"));

        var continueButton = page.GetByRole(AriaRole.Button, new() { Name = "Continue with email" });
        Assert.True(await continueButton.IsDisabledAsync());
        await email.FillAsync("browser@maliev.test");
        Assert.False(await continueButton.IsDisabledAsync());
        await continueButton.ClickAsync();

        var password = page.Locator("#legacy-login-password");
        await password.WaitForAsync();
        Assert.True(await password.EvaluateAsync<bool>("input => input === document.activeElement"));
        Assert.Equal("legacy-login-email", await page.Locator("label").Filter(new() { HasText = "Email" }).GetAttributeAsync("for"));
        Assert.Equal("browser@maliev.test", await email.InputValueAsync());
        Assert.False(await email.IsEditableAsync());
        Assert.True(await email.EvaluateAsync<bool>(
            "input => parseFloat(getComputedStyle(input).borderTopWidth) > 0"));
        Assert.Equal("password", await password.GetAttributeAsync("type"));
        var passwordVisibility = page.Locator("[data-slot='input-group-button']");
        await passwordVisibility.WaitForAsync();
        Assert.Equal("BUTTON", await passwordVisibility.EvaluateAsync<string>("button => button.tagName"));
        Assert.Equal("Show password", await passwordVisibility.GetAttributeAsync("aria-label"));
        Assert.Equal("false", await passwordVisibility.GetAttributeAsync("aria-pressed"));
        Assert.True(await passwordVisibility.EvaluateAsync<bool>(
            "button => button.getBoundingClientRect().width >= 44 && button.getBoundingClientRect().height >= 44"));
        var signIn = page.GetByRole(AriaRole.Button, new() { Name = "Sign in with email" });
        Assert.True(await signIn.IsDisabledAsync());
        await password.FillAsync("not-the-password");
        await passwordVisibility.ClickAsync();
        Assert.Equal("text", await password.GetAttributeAsync("type"));
        Assert.Equal("not-the-password", await password.InputValueAsync());
        Assert.True(await passwordVisibility.EvaluateAsync<bool>("button => button === document.activeElement"));
        var hidePassword = page.Locator("[data-slot='input-group-button']");
        Assert.Equal("Hide password", await hidePassword.GetAttributeAsync("aria-label"));
        Assert.Equal("true", await hidePassword.GetAttributeAsync("aria-pressed"));
        await hidePassword.ClickAsync();
        Assert.Equal("password", await password.GetAttributeAsync("type"));
        Assert.Equal(1, await page.Locator("#legacy-login-remember[type='checkbox']").CountAsync());

        Assert.False(await signIn.IsDisabledAsync());
        await page.Locator("#legacy-login-remember").CheckAsync();
        Assert.True(await page.Locator("#legacy-login-remember").IsCheckedAsync());
        await page.Locator("#legacy-login-remember").FocusAsync();
        Assert.Equal("none", await page.Locator(".legacy-login-remember").EvaluateAsync<string>(
            "field => getComputedStyle(field).outlineStyle"));

        foreach (var button in new[]
                 {
                     page.GetByRole(AriaRole.Button, new() { Name = "Change" }),
                     signIn,
                     page.Locator(".legacy-login-theme-toggle"),
                 })
        {
            Assert.True(await button.EvaluateAsync<bool>(
                "element => element.getBoundingClientRect().width >= 44 && element.getBoundingClientRect().height >= 44"));
        }

        await signIn.ClickAsync();
        var credentialError = page.Locator(".legacy-login-error[role='alert']");
        await credentialError.WaitForAsync();
        Assert.Contains("invalid", await credentialError.TextContentAsync(), StringComparison.OrdinalIgnoreCase);
        Assert.Equal(string.Empty, await password.InputValueAsync());
        Assert.Empty(errors);
    }

    [Theory]
    [InlineData("en-US", 375)]
    [InlineData("en-US", 1440)]
    [InlineData("th-TH", 375)]
    [InlineData("th-TH", 1440)]
    public async Task PublishedForms_RejectInvalidInputBeforeHttpAndKeepValidCsrfSubmission(string culture, int width)
    {
        // Real published Client, generated shell, lazy employee feature and form handlers.
        // Only the external BFF boundary is controlled: this is not an auth/DB or full-Aspire proof.
        var published = await server.GetPublishedClientAsync();
        await using var context = await playwright.Browser.NewContextAsync(new()
        {
            ViewportSize = new() { Width = width, Height = 900 },
        });
        await context.AddInitScriptAsync($"localStorage.setItem('maliev_culture', {System.Text.Json.JsonSerializer.Serialize(culture)});");
        var page = await context.NewPageAsync();
        var pageErrors = 0;
        var failedAssets = 0;
        var consoleErrors = 0;
        var consoleDiagnostics = new System.Collections.Concurrent.ConcurrentQueue<string>();
        var authenticated = false;
        var sessions = 0;
        var logins = new System.Collections.Concurrent.ConcurrentQueue<(string? Csrf, string Body)>();
        var creates = new System.Collections.Concurrent.ConcurrentQueue<(string? Csrf, string Body)>();
        var failedAssetDiagnostics = new System.Collections.Concurrent.ConcurrentQueue<string>();
        var failedAssetDiagnosticGate = new object();
        var failedAssetDiagnosticOverflow = 0;
        void CountAndCaptureFailedAsset(IRequest request, Uri uri, int status, string failureCategory)
        {
            lock (failedAssetDiagnosticGate)
            {
                Interlocked.Increment(ref failedAssets);
                if (failedAssetDiagnostics.Count >= 8)
                {
                    Interlocked.Exchange(ref failedAssetDiagnosticOverflow, 1);
                    return;
                }
                var resourceType = request.ResourceType switch
                {
                    "document" => "document",
                    "stylesheet" => "stylesheet",
                    "image" => "image",
                    "media" => "media",
                    "font" => "font",
                    "script" => "script",
                    "xhr" => "xhr",
                    "fetch" => "fetch",
                    "manifest" => "manifest",
                    _ => "other",
                };
                var pathCategory = uri.AbsolutePath.StartsWith("/bff/", StringComparison.Ordinal) ? "bff"
                    : uri.AbsolutePath.StartsWith("/_framework/", StringComparison.Ordinal) ? "framework"
                    : resourceType == "document" ? "document"
                    : resourceType is "stylesheet" or "image" or "media" or "font" or "script" or "manifest"
                        || uri.AbsolutePath.StartsWith("/_content/", StringComparison.Ordinal) ? "public-asset"
                    : "other";
                failedAssetDiagnostics.Enqueue(System.Text.Json.JsonSerializer.Serialize(new
                {
                    resourceType,
                    pathCategory,
                    status,
                    failureCategory,
                }));
            }
        }
        page.PageError += (_, _) => Interlocked.Increment(ref pageErrors);
        page.Console += (_, message) =>
        {
            if (message.Type != "error") return;
            if (Interlocked.Increment(ref consoleErrors) > 8) return;
            var text = message.Text;
            if (new[] { "token", "authorization", "csrf", "password", "cookie", "credential", "email", "@" }
                .Any(value => text.Contains(value, StringComparison.OrdinalIgnoreCase)))
                text = "[redacted sensitive console diagnostic]";
            text = System.Text.RegularExpressions.Regex.Replace(text, @"https?://[^\s""'<>]+", "[url]");
            consoleDiagnostics.Enqueue(text.Length <= 1024 ? text : text[..1024]);
        };
        page.RequestFailed += (_, request) =>
        {
            var uri = new Uri(request.Url);
            if (uri.Authority == published.BaseUri.Authority)
            {
                var failureCategory = request.Failure switch
                {
                    "net::ERR_ABORTED" => "canceled",
                    "net::ERR_CONNECTION_RESET" or "net::ERR_CONNECTION_CLOSED" or "net::ERR_CONNECTION_REFUSED"
                        or "net::ERR_NAME_NOT_RESOLVED" or "net::ERR_INTERNET_DISCONNECTED" or "net::ERR_TIMED_OUT"
                        or "net::ERR_NETWORK_CHANGED" or "net::ERR_ADDRESS_UNREACHABLE" => "network",
                    _ => "unknown",
                };
                CountAndCaptureFailedAsset(request, uri, 0, failureCategory);
            }
        };
        page.Response += (_, response) =>
        {
            var uri = new Uri(response.Url);
            if (uri.Authority == published.BaseUri.Authority && !uri.AbsolutePath.StartsWith("/bff/", StringComparison.Ordinal) && response.Status >= 400)
            {
                CountAndCaptureFailedAsset(response.Request, uri, response.Status, "unknown");
            }
        };
        await page.RouteAsync("https://accounts.google.com/**", route => route.FulfillAsync(new()
        {
            Status = 200,
            ContentType = "text/javascript",
            Body = "",
        }));
        // Login initializes this external BFF nonce request alongside the excluded Google provider.
        await page.RouteAsync("**/bff/google/nonce", route => route.FulfillAsync(new()
        {
            Status = 200,
            ContentType = "application/json",
            Body = System.Text.Json.JsonSerializer.Serialize(new
            {
                clientId = "test-google-client-id",
                nonce = "nonce-at-least-32-characters-long-value",
                expiresAtUtc = DateTimeOffset.UtcNow.AddMinutes(10),
            }),
        }));
        await page.RouteAsync("**/bff/session", route =>
        {
            Interlocked.Increment(ref sessions);
            return route.FulfillAsync(new()
            {
                Status = 200,
                ContentType = "application/json",
                Body = System.Text.Json.JsonSerializer.Serialize(new
                {
                    isAuthenticated = authenticated,
                    employeeId = authenticated ? "published-employee" : null,
                    displayName = authenticated ? "Synthetic Employee" : null,
                    roles = new[] { "Employee" },
                    csrfToken = "published-form-csrf",
                    legacyDatabaseId = (int?)null,
                    permissions = Array.Empty<string>(),
                }),
            });
        });
        await page.RouteAsync("**/bff/login", async route =>
        {
            logins.Enqueue((route.Request.Headers.GetValueOrDefault("x-csrf-token"), route.Request.PostData ?? ""));
            authenticated = true;
            await route.FulfillAsync(new()
            {
                Status = 200,
                ContentType = "application/json",
                Body = "{\"redirectUrl\":\"/Employees/Create\"}",
            });
        });
        await page.RouteAsync("**/bff/employees", route =>
        {
            creates.Enqueue((route.Request.Headers.GetValueOrDefault("x-csrf-token"), route.Request.PostData ?? ""));
            return route.FulfillAsync(new()
            {
                Status = 201,
                ContentType = "application/json",
                Body = "{\"id\":42,\"confirmationEmailSent\":true}",
            });
        });

        await page.GotoAsync(new Uri(published.BaseUri, "Login").AbsoluteUri);
        var email = page.Locator("#legacy-login-email");
        await email.WaitForAsync();
        await email.FillAsync("not-a-work-address@example.invalid");
        await email.PressAsync("Tab");
        await email.Locator("xpath=ancestor::form").EvaluateAsync("form => form.dispatchEvent(new Event('submit', { bubbles: true, cancelable: true }))");
        await Assertions.Expect(page.Locator("#legacy-login-email-error")).ToContainTextAsync(
            culture == "th-TH" ? "ใช้อีเมลที่ทำงานของ MALIEV" : "Use your MALIEV work email.");
        Assert.Empty(logins);
        await email.FillAsync("published@maliev.com");
        await email.PressAsync("Tab");
        await Assertions.Expect(page.Locator(".legacy-login-primary")).ToBeEnabledAsync();
        await page.Locator(".legacy-login-primary").PressAsync("Enter");
        var password = page.Locator("#legacy-login-password");
        await password.WaitForAsync();
        await Assertions.Expect(password).ToBeFocusedAsync();
        Assert.Empty(logins);
        await password.FillAsync(new string('s', 16));
        await password.PressAsync("Tab");
        var signIn = password.Locator("xpath=ancestor::form").GetByRole(AriaRole.Button,
            new() { Name = culture == "th-TH" ? "เข้าสู่ระบบด้วยอีเมล" : "Sign in with email", Exact = true });
        await Assertions.Expect(signIn).ToBeEnabledAsync();
        await signIn.PressAsync("Enter");
        var first = page.Locator("#employee-create-first-name");
        await first.WaitForAsync();
        var login = Assert.Single(logins);
        Assert.Equal("published-form-csrf", login.Csrf);
        using (var body = System.Text.Json.JsonDocument.Parse(login.Body))
        {
            Assert.Equal("published@maliev.com", body.RootElement.GetProperty("email").GetString());
            Assert.Equal(new string('s', 16), body.RootElement.GetProperty("password").GetString());
        }

        var sessionsBeforeInvalid = Volatile.Read(ref sessions);
        var form = page.Locator("form.employee-create__form");
        await form.EvaluateAsync("form => form.dispatchEvent(new Event('submit', { bubbles: true, cancelable: true }))");
        await Assertions.Expect(page.Locator("main.employee-create .employee-create__alert[role='alert']")).ToContainTextAsync(
            culture == "th-TH" ? "โปรดตรวจสอบช่องที่ระบุ แล้วลองอีกครั้ง" : "Review the highlighted fields and try again.");
        Assert.Equal(sessionsBeforeInvalid, Volatile.Read(ref sessions));
        Assert.Empty(creates);
        await first.FocusAsync();
        await Assertions.Expect(first).ToBeFocusedAsync();
        await first.FillAsync("Synthetic");
        await page.Locator("#employee-create-last-name").FillAsync("Employee");
        await page.Locator("#employee-create-email").FillAsync("new-employee@example.invalid");
        await page.Locator("#employee-create-password").FillAsync(new string('s', 16));
        await page.Locator("#employee-create-confirm-password").FillAsync(new string('s', 16));
        await page.Locator("form.employee-create__form button[type='submit']").ClickAsync();
        await page.Locator("a[href='/Employees/View?id=42']").WaitForAsync();
        var create = Assert.Single(creates);
        Assert.Equal("published-form-csrf", create.Csrf);
        using (var body = System.Text.Json.JsonDocument.Parse(create.Body))
        {
            Assert.Equal("Synthetic", body.RootElement.GetProperty("firstName").GetString());
            Assert.Equal("Employee", body.RootElement.GetProperty("lastName").GetString());
            Assert.Equal("new-employee@example.invalid", body.RootElement.GetProperty("email").GetString());
        }
        Assert.Equal(0, Volatile.Read(ref pageErrors));
        Assert.True(Volatile.Read(ref consoleErrors) == 0,
            "Unexpected synthetic-fixture console errors: " + string.Join(" | ", consoleDiagnostics));
        lock (failedAssetDiagnosticGate)
        {
            var failedAssetEnvelope = "[" + string.Join(",", failedAssetDiagnostics) + "]";
            Assert.False(Volatile.Read(ref failedAssetDiagnosticOverflow) != 0,
                "Sanitized same-origin asset diagnostic capacity exceeded: " + failedAssetEnvelope);
            try
            {
                Assert.Equal(0, Volatile.Read(ref failedAssets));
            }
            catch (Xunit.Sdk.XunitException)
            {
                throw new Xunit.Sdk.XunitException(
                    "Expected failedAssets=0; actual=" + Volatile.Read(ref failedAssets)
                    + "; sanitized diagnostics=" + failedAssetEnvelope);
            }
        }
    }

    [Fact]
    public async Task PublishedShell_ServesEveryDeclaredLocalAssetWithoutDuplicateScriptsInDependencyOrder()
    {
        var published = await server.GetPublishedClientAsync();
        await using var context = await playwright.Browser.NewContextAsync();
        var page = await context.NewPageAsync();
        await page.RouteAsync("https://accounts.google.com/**", route => route.FulfillAsync(new()
        {
            Status = 200,
            ContentType = "text/javascript",
            Body = "",
        }));
        await page.RouteAsync("**/bff/session", route => route.FulfillAsync(new()
        {
            Status = 200,
            ContentType = "application/json",
            Body = "{\"isAuthenticated\":false,\"employeeId\":null,\"displayName\":null,\"roles\":[],\"csrfToken\":\"published-form-csrf\",\"permissions\":[]}",
        }));
        await page.GotoAsync(new Uri(published.BaseUri, "Login").AbsoluteUri);
        await page.Locator("#legacy-login-email").WaitForAsync();
        var document = await context.APIRequest.GetAsync(new Uri(published.BaseUri, "index.html").AbsoluteUri, new() { Timeout = 15000 });
        string html;
        try
        {
            Assert.Equal(200, document.Status);
            var bytes = await document.BodyAsync();
            Assert.InRange(bytes.Length, 1, 256 * 1024);
            Assert.Equal(await File.ReadAllBytesAsync(Path.Combine(published.Root, "index.html")), bytes);
            html = System.Text.Encoding.UTF8.GetString(bytes);
        }
        finally { await document.DisposeAsync(); }
        var scripts = await page.EvaluateAsync<string[]>(
            """
            html => Array.from(new DOMParser().parseFromString(html, 'text/html').querySelectorAll('script[src]'))
                .map(element => new URL(element.getAttribute('src'), location.origin))
                .filter(url => url.origin === location.origin).map(url => url.pathname)
            """, html);
        Assert.Equal(new[]
        {
            "/js/google-identity-signin.js", "/js/workspace-culture.js", "/js/workspace-focus.js",
            "/js/workspace-navigation.js", "/js/customer-workspace.js", "/js/dashboard-refresh.js",
            "/_framework/blazor.webassembly.js",
        }, scripts);
        var renderedScripts = await page.Locator("script[src]").EvaluateAllAsync<string[]>(
            "elements => elements.map(element => new URL(element.src)).filter(url => url.origin === location.origin).map(url => url.pathname)");
        Assert.Equal(scripts, renderedScripts);
        var assets = await page.EvaluateAsync<string[]>(
            """
            html => Array.from(new DOMParser().parseFromString(html, 'text/html').querySelectorAll('script[src],link[href],img[src]'))
                .map(element => new URL(element.getAttribute('src') || element.getAttribute('href'), location.origin))
                .filter(url => url.origin === location.origin).map(url => url.pathname)
            """, html);
        Assert.Equal(assets.Length, assets.Distinct(StringComparer.Ordinal).Count());
        Assert.Contains("/_content/Maliev.ShadcnBlazor/css/shadcn-forms.css", assets);
        Assert.Contains("/Legacy.Maliev.Intranet.Client.styles.css", assets);
        foreach (var asset in assets)
        {
            var path = Path.GetFullPath(Path.Combine(published.Root, asset.TrimStart('/')));
            Assert.StartsWith(published.Root + Path.DirectorySeparatorChar, path, StringComparison.Ordinal);
            Assert.InRange(new FileInfo(path).Length, 1L, 8L * 1024 * 1024);
            var expected = await File.ReadAllBytesAsync(path);
            var response = await context.APIRequest.GetAsync(new Uri(published.BaseUri, asset).AbsoluteUri, new() { Timeout = 15000 });
            try
            {
                Assert.Equal(200, response.Status);
                Assert.NotEmpty(expected);
                Assert.Equal(expected, await response.BodyAsync());
                var contentType = response.Headers.GetValueOrDefault("content-type") ?? "";
                Assert.DoesNotContain("text/html", contentType, StringComparison.OrdinalIgnoreCase);
                if (asset.EndsWith(".css", StringComparison.Ordinal))
                    Assert.StartsWith("text/css", contentType, StringComparison.OrdinalIgnoreCase);
                if (asset.EndsWith(".js", StringComparison.Ordinal))
                    Assert.Contains(contentType.Split(';')[0].Trim().ToLowerInvariant(),
                        new[] { "text/javascript", "application/javascript" });
            }
            finally { await response.DisposeAsync(); }
        }
    }
}
