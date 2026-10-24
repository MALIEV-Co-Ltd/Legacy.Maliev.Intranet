using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Bunit;
using Legacy.Maliev.Intranet.Client.Features.Accounting.Pages;
using Legacy.Maliev.Intranet.Contracts;
using Maliev.ShadcnBlazor.Components.Selection;
using Maliev.ShadcnBlazor.Components.Forms;
using Microsoft.Extensions.DependencyInjection;

namespace Legacy.Maliev.Intranet.Tests;

public sealed class BillingUiBehaviorTests
{
    [Theory]
    [InlineData("en-US", "Quotation billing")]
    [InlineData("th-TH", "การเรียกเก็บเงินตามใบเสนอราคา")]
    public async Task RendersSeparateServerAmountsAndPreviewsRemainingWithoutIssuance(string culture, string heading)
    {
        var previous = CultureInfo.CurrentCulture;
        var previousUi = CultureInfo.CurrentUICulture;
        CultureInfo.CurrentCulture = CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(culture);
        try
        {
            var account = BillingBffContractTests.Account();
            using var transport = new BrowserTransport(account);
            using var client = new HttpClient(transport) { BaseAddress = new("https://intranet.invalid") };
            using var context = new BunitContext();
            context.JSInterop.Mode = JSRuntimeMode.Loose;
            context.Services.AddLocalization();
            context.Services.AddSingleton(client);
            var cut = context.Render<QuotationBilling>(parameters => parameters.Add(page => page.CustomerId, 7).Add(page => page.AccountId, account.Id));
            cut.WaitForAssertion(() => Assert.Contains(heading, cut.Find("h1").TextContent, StringComparison.Ordinal));
            if (culture == "th-TH")
            {
                Assert.Contains(cut.Find("h1").TextContent, character => character is >= '\u0e00' and <= '\u0e7f');
                Assert.DoesNotContain("?", cut.Find("h1").TextContent, StringComparison.Ordinal);
                Assert.DoesNotContain("\ufffd", cut.Markup, StringComparison.Ordinal);
            }
            cut.WaitForAssertion(() => Assert.Contains("800", cut.Find("[data-testid=unbilled]").TextContent, StringComparison.Ordinal));
            Assert.Contains("200", cut.Find("[data-testid=billed]").TextContent, StringComparison.Ordinal);
            Assert.Equal(BillingStageKind.Full, cut.FindComponent<ShadcnSelect<BillingStageKind>>().Instance.Value);
            await cut.InvokeAsync(() => cut.FindComponent<ShadcnSelect<BillingStageKind>>().Instance.ValueChanged.InvokeAsync(BillingStageKind.Remaining));
            await cut.Find("form").SubmitAsync();
            cut.WaitForAssertion(() => Assert.Contains("800", cut.Find("[data-testid=preview]").TextContent, StringComparison.Ordinal));
            var captured = Assert.Single(transport.Posts);
            Assert.EndsWith("/preview", captured.Path, StringComparison.Ordinal);
            Assert.Equal("fixture-csrf", captured.Csrf);
            Assert.Equal(BillingStageKind.Remaining, captured.Input.Kind);
            Assert.Equal(account.Revision, captured.Input.ExpectedRevision);
            Assert.Null(captured.Input.Amount);
            Assert.Null(captured.Input.Percentage);
            Assert.Equal(account.Snapshot.TaxId, captured.Input.Recipient.TaxId);
            cut.Find("#billing-recipient").Input("Updated office");
            Assert.Empty(cut.FindAll("[data-testid=preview]"));
            Assert.DoesNotContain(transport.Paths, path => path.Contains("issue", StringComparison.Ordinal) || path.Contains("invoice", StringComparison.Ordinal));
        }
        finally { CultureInfo.CurrentCulture = previous; CultureInfo.CurrentUICulture = previousUi; }
    }

    [Fact]
    public void UnavailableAccountShowsErrorAndNoFinancialForm()
    {
        using var transport = new BrowserTransport(BillingBffContractTests.Account()) { Unavailable = true };
        using var client = new HttpClient(transport) { BaseAddress = new("https://intranet.invalid") };
        using var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        context.Services.AddLocalization();
        context.Services.AddSingleton(client);
        var cut = context.Render<QuotationBilling>(parameters => parameters.Add(page => page.CustomerId, 7).Add(page => page.AccountId, Guid.NewGuid()));
        cut.WaitForAssertion(() => Assert.Single(cut.FindAll("[role=alert]")));
        Assert.Empty(cut.FindAll("form"));
        Assert.Empty(transport.Posts);
    }

    [Theory]
    [InlineData(false, "200.50", 200.50)]
    [InlineData(true, "12.5", 12.5)]
    public async Task DepositAllowsFractionalAmountOrPercentageWithoutIntegerStepConstraint(bool percentage, string text, double expected)
    {
        var previous = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-US");
        try
        {
            var account = BillingBffContractTests.Account();
            using var transport = new BrowserTransport(account);
            using var client = new HttpClient(transport) { BaseAddress = new("https://intranet.invalid") };
            using var context = new BunitContext();
            context.JSInterop.Mode = JSRuntimeMode.Loose;
            context.Services.AddLocalization();
            context.Services.AddSingleton(client);
            var cut = context.Render<QuotationBilling>(parameters => parameters.Add(page => page.CustomerId, 7).Add(page => page.AccountId, account.Id));
            cut.WaitForAssertion(() => Assert.Single(cut.FindAll("form")));
            await cut.InvokeAsync(() => cut.FindComponent<ShadcnSelect<BillingStageKind>>().Instance.ValueChanged.InvokeAsync(BillingStageKind.Deposit));
            if (percentage) await cut.InvokeAsync(() => cut.FindComponent<ShadcnSelect<bool>>().Instance.ValueChanged.InvokeAsync(true));
            Assert.Equal("any", cut.Find("#billing-amount").GetAttribute("step"));
            cut.Find("#billing-amount").Input(text);
            await cut.Find("form").SubmitAsync();
            var posted = Assert.Single(transport.Posts).Input;
            Assert.Equal((decimal)expected, percentage ? posted.Percentage : posted.Amount);
        }
        finally { CultureInfo.CurrentCulture = previous; }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task NavigationDiscardsDelayedPreviousAccountLoadOrPreview(bool preview)
    {
        var account = BillingBffContractTests.Account();
        var next = account with { Id = Guid.NewGuid(), Snapshot = account.Snapshot with { QuotationId = 987 } };
        using var transport = new BrowserTransport(account) { NextAccount = next, DelayRead = !preview, DelayPreview = preview };
        using var client = new HttpClient(transport) { BaseAddress = new("https://intranet.invalid") };
        using var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        context.Services.AddLocalization();
        context.Services.AddSingleton(client);
        var cut = context.Render<QuotationBilling>(parameters => parameters.Add(page => page.CustomerId, 7).Add(page => page.AccountId, account.Id));
        Task? submission = null;
        if (preview)
        {
            cut.WaitForAssertion(() => Assert.Single(cut.FindAll("form")));
            submission = cut.Find("form").SubmitAsync();
        }
        await transport.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        cut.Render(parameters => parameters.Add(page => page.CustomerId, 7).Add(page => page.AccountId, next.Id));
        cut.WaitForAssertion(() => Assert.Contains("987", cut.Find("#billing-summary").TextContent, StringComparison.Ordinal));
        transport.Delayed.SetResult(preview
            ? BrowserTransport.Json(new BillingStagePreview(account.Id, account.Revision, new(800, 0, 800, "THB"), [new(1, new(800, 0, 800, "THB"), "synthetic")]))
            : BrowserTransport.Json(account));
        if (submission is not null) await submission;
        await cut.InvokeAsync(() => Task.CompletedTask);
        cut.WaitForAssertion(() =>
        {
            Assert.Contains("987", cut.Find("#billing-summary").TextContent, StringComparison.Ordinal);
            Assert.Empty(cut.FindAll("[data-testid=preview]"));
            Assert.Empty(cut.FindAll("[role=alert]"));
            Assert.False(cut.Find("button[type=submit]").HasAttribute("disabled"));
        });
    }

    private sealed class BrowserTransport(BillingAccountView account) : HttpMessageHandler
    {
        public BillingAccountView? NextAccount;
        public bool DelayRead, DelayPreview;
        public TaskCompletionSource<HttpResponseMessage> Delayed { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool Unavailable;
        public List<string> Paths { get; } = [];
        public List<(string Path, string? Csrf, BillingPreviewRequest Input)> Posts { get; } = [];
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            var path = request.RequestUri!.AbsolutePath;
            Paths.Add(path);
            if (path == "/bff/session") return Json(new EmployeeSessionSummary(true, "fixture", "Employee", [], "fixture-csrf"));
            if (Unavailable) return new(HttpStatusCode.ServiceUnavailable);
            if (request.Method == HttpMethod.Get)
            {
                if (NextAccount is { } next && path.EndsWith(next.Id.ToString("D"), StringComparison.Ordinal)) return Json(next);
                if (DelayRead) { Started.SetResult(); return await Delayed.Task; }
                return Json(account);
            }
            var input = await request.Content!.ReadFromJsonAsync<BillingPreviewRequest>(cancellationToken: token);
            Posts.Add((path, request.Headers.TryGetValues("X-CSRF-TOKEN", out var csrf) ? csrf.Single() : null, input!));
            if (DelayPreview) { Started.SetResult(); return await Delayed.Task; }
            return Json(new BillingStagePreview(account.Id, account.Revision, new(800, 0, 800, "THB"), [new(1, new(800, 0, 800, "THB"), "synthetic")]));
        }
        internal static HttpResponseMessage Json<T>(T value) => new(HttpStatusCode.OK) { Content = JsonContent.Create(value) };
    }
}
