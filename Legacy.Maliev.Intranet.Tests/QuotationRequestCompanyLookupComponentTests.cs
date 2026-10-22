using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Bunit;
using Legacy.Maliev.Intranet.Client.Shared.Components;
using Legacy.Maliev.Intranet.Contracts;
using Maliev.ShadcnBlazor.Components.Forms;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using RequestView = Legacy.Maliev.Intranet.Client.Features.Quotations.Pages.QuotationRequests.View;

namespace Legacy.Maliev.Intranet.Tests;

/// <summary>Compiled editor with an explicit component HTTP boundary; not a domain persistence proof.</summary>
public sealed class QuotationRequestCompanyLookupComponentTests : BunitContext
{
    [Fact]
    public Task EnglishSelectionUsesExistingSaveAndPreservesUnrelatedFields() => SelectionAsync("en", "Synthetic Company Limited");

    [Fact]
    public Task ThaiSelectionUsesExistingSaveAndPreservesUnrelatedFields() => SelectionAsync("th", "บริษัทตัวอย่าง");

    private async Task SelectionAsync(string culture, string name)
    {
        var previous = CultureInfo.CurrentUICulture;
        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(culture);
        try
        {
            using var boundary = new ComponentBoundary();
            var cut = RenderPage(boundary);
            await SelectAsync(cut, Company("บริษัทตัวอย่าง", "Synthetic Company Limited", "1234567890123"));
            Assert.Equal(name, cut.Find("#request-company").GetAttribute("value"));
            Assert.Equal("1234567890123", cut.Find("#request-tax").GetAttribute("value"));
            Assert.Equal(0, boundary.UpdateCount);
            Assert.Equal(0, boundary.QualificationWrites);
            await cut.Find("form.quotation-request-form").SubmitAsync();
            cut.WaitForAssertion(() => Assert.Equal(1, boundary.UpdateCount));
            AssertPreserved(boundary, name, "1234567890123");
            Assert.Equal("component-csrf", boundary.Csrf);
            Assert.Equal(0, boundary.QualificationWrites);
            cut.WaitForAssertion(() => Assert.Equal(name, cut.Find("#request-company").GetAttribute("value")));
        }
        finally { CultureInfo.CurrentUICulture = previous; }
    }

    [Fact]
    public async Task MissingNamesAndTaxKeepManualValuesAndDoNotSubmit()
    {
        using var boundary = new ComponentBoundary();
        var cut = RenderPage(boundary);
        await SelectAsync(cut, Company(" ", null, null));
        AssertManualFields(cut);
        Assert.Equal(0, boundary.UpdateCount);
        Assert.Equal(0, boundary.QualificationWrites);
    }

    [Fact]
    public Task UnavailableLookupKeepsManualEditorAndAccessibleFailure() => LookupFailureAsync(HttpStatusCode.ServiceUnavailable);

    [Fact]
    public Task DeniedLookupKeepsManualEditorAndAccessibleFailure() => LookupFailureAsync(HttpStatusCode.Forbidden);

    private async Task LookupFailureAsync(HttpStatusCode status)
    {
        using var boundary = new ComponentBoundary { LookupStatus = status };
        var cut = RenderPage(boundary);
        var search = cut.FindComponent<LookupSearchField<LookupCompany>>();
        await cut.InvokeAsync(() => search.FindComponent<ShadcnInput<string>>().Instance.ValueChanged.InvokeAsync("Synthetic company"));
        cut.WaitForAssertion(() => Assert.Equal(1, boundary.LookupCount));
        var expectedStatus = Services.GetRequiredService<IStringLocalizer<LookupResources>>()[status == HttpStatusCode.Forbidden ? "Forbidden" : "Unavailable"].Value;
        cut.WaitForAssertion(() => Assert.Equal(expectedStatus, search.Find("[role=status]").TextContent));
        Assert.False(cut.Find("#request-company").HasAttribute("disabled"));
        AssertManualFields(cut);
        Assert.Equal(0, boundary.UpdateCount);
        Assert.Equal(0, boundary.QualificationWrites);
    }

    [Fact]
    public async Task InFlightSaveIgnoresSuggestionAndRetainsOriginalRequest()
    {
        using var boundary = new ComponentBoundary { HoldSave = true };
        var cut = RenderPage(boundary);
        Task? submission = null;
        try
        {
            submission = cut.Find("form.quotation-request-form").SubmitAsync();
            await boundary.SaveEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            cut.WaitForAssertion(() => Assert.True(cut.FindComponent<LookupCompanyAssist>().Instance.Disabled));
            await SelectAsync(cut, Company(null, "Late company", "1234567890123"));
            AssertManualFields(cut);
            AssertPreserved(boundary, "Manual company", "0123456789012");
            Assert.Equal(1, boundary.UpdateCount);
        }
        finally
        {
            boundary.ReleaseSave.TrySetResult();
            if (submission is not null) await submission.WaitAsync(TimeSpan.FromSeconds(5));
        }
    }

    [Fact]
    public async Task MissingLocalizedNameUsesAvailableNameAndKeepsManualTax()
    {
        var previous = CultureInfo.CurrentUICulture;
        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("th");
        try
        {
            using var boundary = new ComponentBoundary();
            var cut = RenderPage(boundary);
            await SelectAsync(cut, Company(" ", "Available English name", null));
            Assert.Equal("Available English name", cut.Find("#request-company").GetAttribute("value"));
            Assert.Equal("0123456789012", cut.Find("#request-tax").GetAttribute("value"));
            await cut.Find("form.quotation-request-form").SubmitAsync();
            AssertPreserved(boundary, "Available English name", "0123456789012");
        }
        finally { CultureInfo.CurrentUICulture = previous; }
    }

    private static void AssertPreserved(ComponentBoundary boundary, string name, string tax)
    {
        var saved = Assert.IsType<QuotationRequestUpdate>(boundary.Saved);
        using var before = JsonDocument.Parse(boundary.Original.InternalComment!);
        using var after = JsonDocument.Parse(saved.InternalComment!);
        Assert.True(JsonElement.DeepEquals(before.RootElement, after.RootElement));
        Assert.Equal(boundary.Original with { CompanyName = name, TaxIdentification = tax, InternalComment = saved.InternalComment }, saved);
    }

    private static LookupCompany Company(string? thai, string? english, string? tax)
        => new(thai, english, tax, null, null, null, null, null, null);

    private static Task SelectAsync(IRenderedComponent<Router> cut, LookupCompany company)
        => cut.InvokeAsync(() => cut.FindComponent<LookupCompanyAssist>().Instance.Selected.InvokeAsync(company));

    private static void AssertManualFields(IRenderedComponent<Router> cut)
    {
        Assert.Equal("Manual company", cut.Find("#request-company").GetAttribute("value"));
        Assert.Equal("0123456789012", cut.Find("#request-tax").GetAttribute("value"));
        Assert.Equal("Buyer", cut.Find("#request-first-name").GetAttribute("value"));
        Assert.Equal("TH", cut.Find("#request-country").GetAttribute("value"));
        Assert.Equal("original staff note", cut.Find("#request-internal").TextContent);
    }

    private IRenderedComponent<Router> RenderPage(ComponentBoundary boundary)
    {
        Services.AddLocalization();
        Services.AddSingleton(new HttpClient(boundary) { BaseAddress = new("https://component.test/") });
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.GetRequiredService<NavigationManager>().NavigateTo("/QuotationRequests/View?id=9");
        var cut = Render<Router>(parameters => parameters
            .Add(router => router.AppAssembly, typeof(RequestView).Assembly)
            .Add(router => router.Found, (RenderFragment<RouteData>)(route => builder =>
            {
                builder.OpenComponent<RouteView>(0);
                builder.AddAttribute(1, nameof(RouteView.RouteData), route);
                builder.CloseComponent();
            })));
        cut.WaitForAssertion(() => Assert.NotNull(cut.Find("#request-company")));
        return cut;
    }

    private sealed class ComponentBoundary : HttpMessageHandler
    {
        public QuotationRequestUpdate Original { get; } = new("Buyer", "Fixture", "buyer@example.test", "manual phone", "TH",
            "Manual company", "0123456789012", "Manual message", QuotationRequestFinderEnvelopeHttpTests.Envelope, false,
            new DateTime(2030, 7, 18, 8, 30, 0, DateTimeKind.Utc));
        public QuotationRequestUpdate? Saved { get; private set; }
        public int UpdateCount { get; private set; }
        public int LookupCount { get; private set; }
        public int QualificationWrites { get; private set; }
        public string? Csrf { get; private set; }
        public HttpStatusCode LookupStatus { get; init; } = HttpStatusCode.ServiceUnavailable;
        public bool HoldSave { get; init; }
        public TaskCompletionSource SaveEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource ReleaseSave { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            var path = request.RequestUri!.AbsolutePath;
            if (request.Method == HttpMethod.Get && path == "/bff/session")
                return Json(new EmployeeSessionSummary(true, "employee", "Fixture", ["Employee"], "component-csrf", 7, []));
            if (request.Method == HttpMethod.Get && path == "/bff/quotation-requests/9")
            {
                var value = Saved ?? Original;
                return Json(new QuotationRequestDetail(new QuotationRequestItem(9, value.FirstName, value.LastName, value.Email,
                    value.TelephoneNumber, value.Country, value.CompanyName, value.TaxIdentification, value.Message,
                    value.InternalComment, value.Done, new DateTime(2030, 7, 18, 0, 0, 0, DateTimeKind.Utc), value.ModifiedDate), []));
            }
            if (request.Method == HttpMethod.Get && path == "/bff/quotation-requests/9/qualification-receipt")
                return new(HttpStatusCode.NotFound);
            if (request.Method == HttpMethod.Get && path == "/bff/lookups/companies/search")
            {
                LookupCount++;
                return new(LookupStatus);
            }
            if (request.Method != HttpMethod.Get && path.Contains("/qualification", StringComparison.Ordinal))
            {
                QualificationWrites++;
                throw new InvalidOperationException("Company lookup must not write qualification.");
            }
            if (request.Method == HttpMethod.Put && path == "/bff/quotation-requests/9")
            {
                Saved = await request.Content!.ReadFromJsonAsync<QuotationRequestUpdate>(cancellationToken: token);
                Csrf = Assert.Single(request.Headers.GetValues("X-CSRF-TOKEN"));
                UpdateCount++;
                SaveEntered.TrySetResult();
                if (HoldSave) await ReleaseSave.Task.WaitAsync(TimeSpan.FromSeconds(5), token);
                return new(HttpStatusCode.NoContent);
            }
            throw new InvalidOperationException($"Unexpected component boundary {request.Method} {path}");
        }

        private static HttpResponseMessage Json<T>(T value) => new(HttpStatusCode.OK) { Content = JsonContent.Create(value) };
    }
}
