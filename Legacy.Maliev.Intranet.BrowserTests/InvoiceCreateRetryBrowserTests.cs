using System.Text.Json;
using Legacy.Maliev.Intranet.BrowserTests.Infrastructure;
using Microsoft.Playwright;

namespace Legacy.Maliev.Intranet.BrowserTests;

[Collection(CustomerBrowserCollection.Name)]
public sealed class InvoiceCreateRetryBrowserTests(
    IntranetClientServerFixture server,
    PlaywrightFixture playwright)
{
    [Theory]
    [InlineData("en-TH", 0)]
    [InlineData("en-TH", 1)]
    [InlineData("th-TH", 0)]
    [InlineData("th-TH", 1)]
    public async Task ProviderAccepted_PreservesTruthfulReceiptAndInvoiceLinkAcrossReloadWithoutAnotherCreate(string culture, int state)
    {
        await using var context = await playwright.Browser.NewContextAsync(new() { ViewportSize = new() { Width = 375, Height = 850 } });
        await context.AddInitScriptAsync($"localStorage.setItem('maliev_culture','{culture}');");
        var page = await context.NewPageAsync();
        var thai = culture == "th-TH";
        var createLabel = thai ? "สร้างใบแจ้งหนี้" : "Create invoice";
        var linkLabel = thai ? "เปิดใบแจ้งหนี้เดิม" : "Open existing invoice";
        var notice = thai ? "ผู้ให้บริการยอมรับอีเมลแล้ว แต่ยังไม่ได้ยืนยันว่าผู้รับได้รับอีเมล" : "The email provider accepted the message, but recipient delivery is not confirmed.";
        var writes = new List<(string? Key, string? Body)>();
        await StubAsync(page, writes, (200, $"{{\"invoiceId\":55,\"state\":{state},\"emailState\":3}}"));
        await page.GotoAsync(new Uri(server.BaseUri, "Invoices/Create?quotationId=84").AbsoluteUri);
        await page.Locator("#invoice-number").WaitForAsync();
        await page.GetByRole(AriaRole.Button, new() { Name = createLabel, Exact = true }).ClickAsync();

        await Assertions.Expect(page.GetByText(notice, new() { Exact = false })).ToBeVisibleAsync();
        await Assertions.Expect(page.GetByRole(AriaRole.Link, new() { Name = linkLabel })).ToHaveAttributeAsync("href", "/Invoices/View?id=55");
        Assert.True(await page.GetByRole(AriaRole.Button, new() { Name = createLabel, Exact = true }).IsDisabledAsync());
        Assert.Equal(0, await page.GetByRole(AriaRole.Button, new() { Name = thai ? "ส่งคำขอใบแจ้งหนี้เดิมซ้ำ" : "Retry original invoice" }).CountAsync());
        Assert.Equal("1", await page.EvaluateAsync<string>("sessionStorage.getItem('maliev-invoice-create-unresolved')"));
        using var saved = JsonDocument.Parse(await page.EvaluateAsync<string>("sessionStorage.getItem('maliev-invoice-create-provider-accepted')"));
        Assert.Equal(55, saved.RootElement.GetProperty("invoiceId").GetInt32());
        Assert.Equal(84, saved.RootElement.GetProperty("quotationId").GetInt32());
        Assert.Equal(2, saved.RootElement.EnumerateObject().Count());

        await page.ReloadAsync();
        await Assertions.Expect(page.GetByText(notice, new() { Exact = false })).ToBeVisibleAsync();
        await Assertions.Expect(page.GetByRole(AriaRole.Link, new() { Name = linkLabel })).ToHaveAttributeAsync("href", "/Invoices/View?id=55");
        Assert.True(await page.GetByRole(AriaRole.Button, new() { Name = createLabel, Exact = true }).IsDisabledAsync());
        Assert.True(await page.Locator("#invoice-number").IsDisabledAsync());
        Assert.Single(writes);
        Assert.True(await page.EvaluateAsync<bool>("document.documentElement.scrollWidth <= window.innerWidth + 1"));
    }

    [Theory]
    [InlineData("not-json")]
    [InlineData("{\"invoiceId\":0,\"quotationId\":84}")]
    [InlineData("{\"invoiceId\":55,\"quotationId\":85}")]
    [InlineData("{\"invoiceId\":55,\"quotationId\":0}")]
    [InlineData("{\"invoiceId\":55,\"invoiceId\":56,\"quotationId\":84}")]
    [InlineData("{\"invoiceId\":55,\"quotationId\":84,\"emailState\":3}")]
    public async Task ProviderAccepted_InvalidSavedGuardNeverUnlocksCreationOrInventsInvoiceLink(string savedReceipt)
    {
        await using var context = await playwright.Browser.NewContextAsync();
        var page = await context.NewPageAsync();
        var writes = new List<(string? Key, string? Body)>();
        await StubAsync(page, writes);
        await page.GotoAsync(new Uri(server.BaseUri, "Invoices/Create?quotationId=84").AbsoluteUri);
        await page.Locator("#invoice-number").WaitForAsync();
        await page.EvaluateAsync("value => { sessionStorage.setItem('maliev-invoice-create-provider-accepted', value); sessionStorage.setItem('maliev-invoice-create-unresolved', '1'); }", savedReceipt);
        await page.ReloadAsync();
        await page.GetByText("Check whether the invoice was created", new() { Exact = false }).WaitForAsync();
        Assert.True(await page.GetByRole(AriaRole.Button, new() { Name = "Create invoice", Exact = true }).IsDisabledAsync());
        Assert.Equal(0, await page.GetByRole(AriaRole.Link, new() { Name = "Open existing invoice" }).CountAsync());
        Assert.Equal("1", await page.EvaluateAsync<string>("sessionStorage.getItem('maliev-invoice-create-unresolved')"));
        Assert.Empty(writes);
    }

    [Fact]
    public async Task ProviderAccepted_StorageWriteFailureKeepsKnownInvoiceAndUnresolvedReloadGuard()
    {
        await using var context = await playwright.Browser.NewContextAsync();
        await context.AddInitScriptAsync("""
            const originalSetItem = Storage.prototype.setItem;
            Storage.prototype.setItem = function(key, value) {
                if (key === 'maliev-invoice-create-provider-accepted') throw new Error('fixture storage unavailable');
                return originalSetItem.call(this, key, value);
            };
            """);
        var page = await context.NewPageAsync();
        var writes = new List<(string? Key, string? Body)>();
        await StubAsync(page, writes, (200, """{"invoiceId":55,"state":0,"emailState":3}"""));
        await page.GotoAsync(new Uri(server.BaseUri, "Invoices/Create?quotationId=84").AbsoluteUri);
        await page.Locator("#invoice-number").WaitForAsync();
        await page.GetByRole(AriaRole.Button, new() { Name = "Create invoice", Exact = true }).ClickAsync();
        await Assertions.Expect(page.GetByRole(AriaRole.Link, new() { Name = "Open existing invoice" })).ToHaveAttributeAsync("href", "/Invoices/View?id=55");
        await Assertions.Expect(page.GetByText("Check whether the invoice was created", new() { Exact = false })).ToBeVisibleAsync();
        Assert.Equal("1", await page.EvaluateAsync<string>("sessionStorage.getItem('maliev-invoice-create-unresolved')"));
        await page.ReloadAsync();
        await Assertions.Expect(page.GetByText("Check whether the invoice was created", new() { Exact = false })).ToBeVisibleAsync();
        Assert.True(await page.GetByRole(AriaRole.Button, new() { Name = "Create invoice", Exact = true }).IsDisabledAsync());
        Assert.Single(writes);
    }

    [Theory]
    [InlineData("en-TH", "light", 1440)]
    [InlineData("en-TH", "dark", 1440)]
    [InlineData("en-TH", "light", 375)]
    [InlineData("en-TH", "dark", 375)]
    [InlineData("th-TH", "light", 1440)]
    [InlineData("th-TH", "dark", 1440)]
    [InlineData("th-TH", "light", 375)]
    [InlineData("th-TH", "dark", 375)]
    public async Task ReconciledEmailRequiresRetry_ShowsNoticeAndExistingInvoiceWithoutAnotherCreate(string culture, string theme, int width)
    {
        await using var context = await playwright.Browser.NewContextAsync(new() { ViewportSize = new() { Width = width, Height = 850 }, ReducedMotion = ReducedMotion.Reduce });
        await context.AddInitScriptAsync($"localStorage.setItem('maliev_theme','{theme}'); localStorage.setItem('maliev_culture','{culture}');");
        var page = await context.NewPageAsync();
        var thai = culture == "th-TH";
        var createLabel = thai ? "สร้างใบแจ้งหนี้" : "Create invoice";
        var writes = new List<(string? Key, string? Body)>();
        await StubAsync(page, writes, (200, """{"invoiceId":55,"state":1,"emailState":2,"providerMessageId":null}"""));

        await page.GotoAsync(new Uri(server.BaseUri, "Invoices/Create?quotationId=84").AbsoluteUri);
        await page.Locator("#invoice-number").WaitForAsync();
        var create = page.GetByRole(AriaRole.Button, new() { Name = createLabel, Exact = true });
        await create.FocusAsync();
        await create.PressAsync("Enter");

        await Assertions.Expect(page.GetByText(thai ? "มีใบแจ้งหนี้แล้ว แต่ต้องตรวจสอบการส่งอีเมลให้ชัดเจน" : "The invoice exists, but its email delivery needs explicit reconciliation.", new() { Exact = false }))
            .ToBeVisibleAsync(new() { Timeout = 5000 });
        var existing = page.GetByRole(AriaRole.Link, new() { Name = thai ? "เปิดใบแจ้งหนี้เดิม" : "Open existing invoice" });
        await Assertions.Expect(existing).ToHaveAttributeAsync("href", "/Invoices/View?id=55");
        Assert.Contains("Invoices/Create", page.Url, StringComparison.Ordinal);
        Assert.Single(writes);
        Assert.True(await page.Locator("#invoice-number").IsDisabledAsync());
        Assert.True(await create.IsDisabledAsync());
        await create.PressAsync("Enter");
        Assert.Single(writes);
        await existing.FocusAsync();
        Assert.True(await existing.EvaluateAsync<bool>("element => element === document.activeElement"));
        Assert.Equal(theme, await page.EvaluateAsync<string>("document.documentElement.dataset.malievTheme"));
        Assert.True(await page.EvaluateAsync<bool>("document.documentElement.scrollWidth <= window.innerWidth + 1"));
        Assert.Equal("1", await page.EvaluateAsync<string>("sessionStorage.getItem('maliev-invoice-create-unresolved')"));
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(0, 1)]
    [InlineData(1, 0)]
    [InlineData(1, 1)]
    public async Task ConfirmedReceipt_NavigatesNormallyAndClearsReloadGuard(int state, int emailState)
    {
        await using var context = await playwright.Browser.NewContextAsync();
        var page = await context.NewPageAsync();
        var writes = new List<(string? Key, string? Body)>();
        await StubAsync(page, writes, (200, $"{{\"invoiceId\":55,\"state\":{state},\"emailState\":{emailState},\"providerMessageId\":null}}"));
        await page.RouteAsync("**/bff/invoices/55", route => route.FulfillAsync(new() { Status = 503, ContentType = "application/json", Body = "{}" }));
        await page.GotoAsync(new Uri(server.BaseUri, "Invoices/Create?quotationId=84").AbsoluteUri);
        await page.Locator("#invoice-number").WaitForAsync();
        await page.GetByRole(AriaRole.Button, new() { Name = "Create invoice" }).ClickAsync();
        await Assertions.Expect(page).ToHaveURLAsync(new System.Text.RegularExpressions.Regex("/Invoices/View\\?id=55$"));
        Assert.Null(await page.EvaluateAsync<string?>("sessionStorage.getItem('maliev-invoice-create-unresolved')"));
        Assert.Single(writes);
    }

    [Theory]
    [InlineData("{\"invoiceId\":55,\"state\":99,\"emailState\":2,\"providerMessageId\":null}")]
    [InlineData("{\"invoiceId\":55,\"state\":1,\"emailState\":99,\"providerMessageId\":null}")]
    [InlineData("{\"invoiceId\":55,\"emailState\":0,\"providerMessageId\":null}")]
    [InlineData("{\"invoiceId\":55,\"state\":1,\"providerMessageId\":null}")]
    [InlineData("{\"invoiceId\":55,\"state\":\"reconciled\",\"emailState\":2,\"providerMessageId\":null}")]
    [InlineData("{\"invoiceId\":55,\"state\":99,\"state\":0,\"emailState\":0}")]
    [InlineData("{\"invoiceId\":55,\"state\":0,\"emailState\":2,\"emailState\":0}")]
    public async Task UnconfirmedReceipt_KeepsKnownInvoiceLinkAndBlocksRepeatCreate(string receipt)
    {
        await using var context = await playwright.Browser.NewContextAsync();
        var page = await context.NewPageAsync();
        var writes = new List<(string? Key, string? Body)>();
        await StubAsync(page, writes, (200, receipt));

        await page.GotoAsync(new Uri(server.BaseUri, "Invoices/Create?quotationId=84").AbsoluteUri);
        await page.Locator("#invoice-number").WaitForAsync();
        await page.GetByRole(AriaRole.Button, new() { Name = "Create invoice" }).ClickAsync();

        await Assertions.Expect(page.GetByText("Accounting returned an unconfirmed invoice outcome.", new() { Exact = false }))
            .ToBeVisibleAsync(new() { Timeout = 5000 });
        await Assertions.Expect(page.GetByRole(AriaRole.Link, new() { Name = "Open existing invoice" }))
            .ToHaveAttributeAsync("href", "/Invoices/View?id=55");
        Assert.Single(writes);
        Assert.True(await page.Locator("#invoice-number").IsDisabledAsync());
        Assert.Equal(0, await page.GetByRole(AriaRole.Button, new() { Name = "Retry original invoice" }).CountAsync());
        Assert.True(await page.GetByRole(AriaRole.Button, new() { Name = "Create invoice" }).IsDisabledAsync());
        Assert.Equal("1", await page.EvaluateAsync<string>("sessionStorage.getItem('maliev-invoice-create-unresolved')"));
    }

    [Theory]
    [InlineData("{\"invoiceId\":54,\"invoiceId\":55,\"state\":0,\"emailState\":0}")]
    [InlineData("{\"invoiceId\":55,\"invoiceId\":55,\"state\":0,\"emailState\":0}")]
    public async Task AmbiguousInvoiceReference_BlocksRetryWithoutChoosingAnInvoice(string receipt)
    {
        await using var context = await playwright.Browser.NewContextAsync();
        var page = await context.NewPageAsync();
        var writes = new List<(string? Key, string? Body)>();
        await StubAsync(page, writes, (200, receipt));
        await page.GotoAsync(new Uri(server.BaseUri, "Invoices/Create?quotationId=84").AbsoluteUri);
        await page.Locator("#invoice-number").WaitForAsync();
        var create = page.GetByRole(AriaRole.Button, new() { Name = "Create invoice", Exact = true });
        await create.ClickAsync();
        await Assertions.Expect(page.GetByText("Accounting returned an unconfirmed invoice outcome with an ambiguous invoice reference.", new() { Exact = false })).ToBeVisibleAsync();
        Assert.True(await create.IsDisabledAsync());
        Assert.True(await page.Locator("#invoice-number").IsDisabledAsync());
        Assert.Equal(0, await page.GetByRole(AriaRole.Link, new() { Name = "Open existing invoice" }).CountAsync());
        Assert.Equal(0, await page.GetByRole(AriaRole.Button, new() { Name = "Retry original invoice" }).CountAsync());
        Assert.Contains("Invoices/Create", page.Url, StringComparison.Ordinal);
        Assert.Equal("1", await page.EvaluateAsync<string>("sessionStorage.getItem('maliev-invoice-create-unresolved')"));
        await create.PressAsync("Enter");
        Assert.Single(writes);
    }

    [Theory]
    [InlineData(503)]
    [InlineData(500)]
    public async Task UncertainCreate_ReloadBlocksFreshCreateWithoutPersistingInvoiceIntent(int status)
    {
        await using var context = await playwright.Browser.NewContextAsync();
        var page = await context.NewPageAsync();
        var writes = new List<(string? Key, string? Body)>();
        await StubAsync(page, writes, (status, "{}"));

        await page.GotoAsync(new Uri(server.BaseUri, "Invoices/Create?quotationId=84").AbsoluteUri);
        await page.Locator("#invoice-number").WaitForAsync();
        await page.GetByRole(AriaRole.Button, new() { Name = "Create invoice" }).ClickAsync();
        await page.GetByText("The invoice outcome is uncertain.", new() { Exact = false }).WaitForAsync();
        Assert.Single(writes);
        Assert.True(Guid.TryParse(writes[0].Key, out var originalOperationId));
        Assert.NotEqual(Guid.Empty, originalOperationId);

        await page.ReloadAsync();
        await page.GetByText("Check whether the invoice was created", new() { Exact = false }).WaitForAsync();
        Assert.True(await page.GetByRole(AriaRole.Button, new() { Name = "Create invoice" }).IsDisabledAsync());
        Assert.True(await page.Locator("#invoice-number").IsDisabledAsync());
        Assert.True(await page.GetByRole(AriaRole.Link, new() { Name = "Back to invoices" }).IsVisibleAsync());
        Assert.Equal("1", await page.EvaluateAsync<string>("sessionStorage.getItem('maliev-invoice-create-unresolved')"));
        Assert.Single(writes);
    }

    [Fact]
    public async Task DefinitiveValidationFailure_ReloadAllowsCorrectedCreate()
    {
        await using var context = await playwright.Browser.NewContextAsync();
        var page = await context.NewPageAsync();
        var writes = new List<(string? Key, string? Body)>();
        await StubAsync(page, writes, (400, "{}"), (503, "{}"));

        await page.GotoAsync(new Uri(server.BaseUri, "Invoices/Create?quotationId=84").AbsoluteUri);
        await page.Locator("#invoice-number").WaitForAsync();
        await page.GetByRole(AriaRole.Button, new() { Name = "Create invoice" }).ClickAsync();
        await page.GetByText("Check the invoice fields and try again.").WaitForAsync();
        Assert.Null(await page.EvaluateAsync<string?>("sessionStorage.getItem('maliev-invoice-create-unresolved')"));

        await page.ReloadAsync();
        await page.Locator("#invoice-number").WaitForAsync();
        Assert.True(await page.GetByRole(AriaRole.Button, new() { Name = "Create invoice" }).IsEnabledAsync());
        await page.GetByRole(AriaRole.Button, new() { Name = "Create invoice" }).ClickAsync();

        Assert.Equal(2, writes.Count);
        Assert.NotEqual(writes[0].Key, writes[1].Key);
    }

    [Fact]
    public async Task UncertainCreate_LocksOriginalIntentAndRetriesSameOperation()
    {
        await using var context = await playwright.Browser.NewContextAsync();
        var page = await context.NewPageAsync();
        var errors = new List<string>();
        page.PageError += (_, error) => errors.Add(error);
        var writes = new List<(string? Key, string? Body)>();
        await StubAsync(page, writes);

        await page.GotoAsync(new Uri(server.BaseUri, "Invoices/Create?quotationId=84").AbsoluteUri);
        var number = page.Locator("#invoice-number");
        await number.WaitForAsync();
        Assert.True(await page.Locator("#blazor-error-ui").IsHiddenAsync(), string.Join("\n", errors));
        await page.GetByRole(AriaRole.Button, new() { Name = "Create invoice" }).ClickAsync();
        await page.GetByText("The invoice outcome is uncertain.", new() { Exact = false }).WaitForAsync();
        Assert.True(await number.IsDisabledAsync());
        Assert.True(await page.Locator("#invoice-quotation").IsDisabledAsync());
        Assert.True(await page.Locator("#billing-line1").IsDisabledAsync());
        Assert.True(await page.Locator("#shipping-telephone").IsDisabledAsync());
        Assert.True(await page.Locator("#invoice-comment").IsDisabledAsync());
        Assert.True(await page.Locator("#invoice-email").IsDisabledAsync());
        Assert.Equal(0, await page.Locator("form.invoice-editor__form input:not(:disabled), form.invoice-editor__form textarea:not(:disabled)").CountAsync());
        Assert.True(await page.GetByRole(AriaRole.Button, new() { Name = "Get quotation data" }).IsDisabledAsync());
        Assert.Equal(0, await page.GetByRole(AriaRole.Link, new() { Name = "Back to invoices" }).CountAsync());
        await page.GetByRole(AriaRole.Button, new() { Name = "Retry original invoice" }).ClickAsync();

        Assert.Equal(2, writes.Count);
        Assert.Equal(writes[0].Key, writes[1].Key);
        Assert.Equal(writes[0].Body, writes[1].Body);
        using var second = JsonDocument.Parse(writes[1].Body!);
        Assert.Equal("INV-84", second.RootElement.GetProperty("invoiceNumber").GetString());
        Assert.Empty(errors);
    }

    [Fact]
    public async Task UncertainCreate_SubsequentConflictKeepsOriginalOperationFrozen()
    {
        await using var context = await playwright.Browser.NewContextAsync();
        var page = await context.NewPageAsync();
        var writes = new List<(string? Key, string? Body)>();
        await StubAsync(page, writes, (503, "{}"), (409, "{}"), (503, "{}"));

        await page.GotoAsync(new Uri(server.BaseUri, "Invoices/Create?quotationId=84").AbsoluteUri);
        await page.Locator("#invoice-number").WaitForAsync();
        await page.GetByRole(AriaRole.Button, new() { Name = "Create invoice" }).ClickAsync();
        await page.GetByRole(AriaRole.Button, new() { Name = "Retry original invoice" }).ClickAsync();
        Assert.True(await page.Locator("#invoice-number").IsDisabledAsync());
        await Assertions.Expect(page.GetByText("contact support before starting another invoice", new() { Exact = false })).ToBeVisibleAsync();
        await Assertions.Expect(page.GetByRole(AriaRole.Button, new() { Name = "Retry original invoice" })).ToBeEnabledAsync();
        await page.GetByRole(AriaRole.Button, new() { Name = "Retry original invoice" }).ClickAsync();

        Assert.Equal(3, writes.Count);
        Assert.All(writes, write =>
        {
            Assert.Equal(writes[0].Key, write.Key);
            Assert.Equal(writes[0].Body, write.Body);
        });
    }

    [Theory]
    [InlineData("not-json")]
    [InlineData("{\"invoiceId\":0,\"state\":0,\"emailState\":0,\"providerMessageId\":null}")]
    public async Task UncertainCreate_MalformedSuccessResponseKeepsOriginalOperationFrozen(string malformedResponse)
    {
        await using var context = await playwright.Browser.NewContextAsync();
        var page = await context.NewPageAsync();
        var writes = new List<(string? Key, string? Body)>();
        var errors = new List<string>();
        page.PageError += (_, error) => errors.Add(error);
        await StubAsync(page, writes, (200, malformedResponse), (503, "{}"));

        await page.GotoAsync(new Uri(server.BaseUri, "Invoices/Create?quotationId=84").AbsoluteUri);
        await page.Locator("#invoice-number").WaitForAsync();
        await page.GetByRole(AriaRole.Button, new() { Name = "Create invoice" }).ClickAsync();
        await page.GetByRole(AriaRole.Button, new() { Name = "Retry original invoice" }).ClickAsync();

        Assert.Equal(2, writes.Count);
        Assert.Equal(writes[0].Key, writes[1].Key);
        Assert.Equal(writes[0].Body, writes[1].Body);
        Assert.True(await page.Locator("#invoice-number").IsDisabledAsync());
        Assert.Empty(errors);
    }

    private static async Task StubAsync(
        IPage page,
        List<(string? Key, string? Body)> writes,
        params (int Status, string Body)[] responses)
    {
        await page.RouteAsync("**/bff/session", route => route.FulfillAsync(new()
        {
            Status = 200,
            ContentType = "application/json",
            Body = """{"isAuthenticated":true,"employeeId":"test-employee","displayName":"Test Employee","roles":["Employee"],"csrfToken":"test-csrf","legacyDatabaseId":1,"permissions":["legacy-accounting.invoices.create"]}""",
        }));
        await page.RouteAsync("**/bff/invoices/from-quotation/84/preview", route => route.FulfillAsync(new()
        {
            Status = 200,
            ContentType = "application/json",
            Body = """{"quotationId":84,"customerId":3,"invoiceNumber":"INV-84","salesPerson":"Employee","currency":"THB","comment":null,"shippedVia":null,"fob":null,"terms":null,"billingAddress":{},"shippingAddress":{},"taxIdentification":null,"commercialRegistration":null,"subtotal":1000,"vat":70,"total":1070,"availableWithholdingTax":0,"outstanding":1070,"orderItems":[]}""",
        }));
        await page.RouteAsync("**/bff/invoices/from-quotation/84", async route =>
        {
            writes.Add((route.Request.Headers.GetValueOrDefault("idempotency-key"), route.Request.PostData));
            var response = responses.Length == 0
                ? (Status: 503, Body: "{}")
                : responses[Math.Min(writes.Count - 1, responses.Length - 1)];
            await route.FulfillAsync(new() { Status = response.Status, ContentType = "application/json", Body = response.Body });
        });
    }
}
