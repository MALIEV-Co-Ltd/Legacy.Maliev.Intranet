using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using Bunit;
using Legacy.Maliev.Intranet.Client.Features.Customers.Components;
using Legacy.Maliev.Intranet.Client.Features.Customers.Pages;
using Legacy.Maliev.Intranet.Contracts;
using Maliev.ShadcnBlazor.Components.Forms;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;

namespace Legacy.Maliev.Intranet.Tests;

/// <summary>Real Company editor inputs and validation with only same-origin transport controlled.</summary>
public sealed class CustomerCompanyInputCapacityComponentTests
{
    private static readonly string[] InputIds = ["relation-company-name", "relation-tax", "relation-registrar"];

    [Theory]
    [InlineData("en", false)]
    [InlineData("en", true)]
    [InlineData("th", false)]
    [InlineData("th", true)]
    public async Task RenderedInputs_RetainOriginal256UnitLimitForBothRelationStates(string language, bool create)
    {
        using var culture = new CultureScope(language);
        using var handler = new Transport(create);
        using var http = new HttpClient(handler) { BaseAddress = new("https://localhost/") };
        using var context = Context(http);
        var cut = await OpenAsync(context, handler);

        foreach (var id in InputIds)
        {
            var input = cut.Find("#" + id);
            Assert.Equal("input", input.LocalName);
            Assert.Equal("256", input.GetAttribute("maxlength"));
            Assert.False(input.HasAttribute("disabled"));
        }
        Assert.Contains(language == "th" ? "เลขประจำตัวผู้เสียภาษี" : "Tax number", cut.Markup, StringComparison.Ordinal);
        Assert.Equal(2, handler.Reads);
        Assert.Equal(0, handler.SessionReads);
        Assert.Empty(handler.Writes);
    }

    [Theory]
    [InlineData("en", false)]
    [InlineData("en", true)]
    [InlineData("th", false)]
    [InlineData("th", true)]
    public async Task AcceptedBoundary_SubmitsLiteralPaddingAndSupplementaryUnitsWithoutTrimming(string language, bool create)
    {
        using var culture = new CultureScope(language);
        using var handler = new Transport(create);
        using var http = new HttpClient(handler) { BaseAddress = new("https://localhost/") };
        using var context = Context(http);
        var cut = await OpenAsync(context, handler);
        var values = new[] { " " + new string('ก', 253) + "  ", new string('T', 254) + "😀", new string('R', 255) + " " };
        foreach (var value in values) Assert.Equal(256, value.Length);
        for (var index = 0; index < InputIds.Length; index++) await ChangeAsync(cut, InputIds[index], values[index]);

        await cut.Find("form").SubmitAsync();

        var write = Assert.Single(handler.Writes);
        Assert.Equal(values[0], write.Name);
        Assert.Equal(values[1], write.TaxNumber);
        Assert.Equal(values[2], write.Registrar);
        Assert.Equal(1, handler.SessionReads);
        Assert.Equal(2, handler.Reads);
        Assert.Empty(cut.FindAll("form"));
    }

    [Theory]
    [InlineData(false, "relation-company-name")]
    [InlineData(true, "relation-company-name")]
    [InlineData(false, "relation-tax")]
    [InlineData(true, "relation-tax")]
    [InlineData(false, "relation-registrar")]
    [InlineData(true, "relation-registrar")]
    public async Task ProgrammaticOverflow_RemainsInvalidBeforeSessionOrWrite(bool create, string id)
    {
        using var culture = new CultureScope("en");
        using var handler = new Transport(create);
        using var http = new HttpClient(handler) { BaseAddress = new("https://localhost/") };
        using var context = Context(http);
        var cut = await OpenAsync(context, handler);
        await ChangeAsync(cut, InputIds[0], "Valid company");
        foreach (var value in new[] { new string('X', 256) + " ", new string('X', 257), new string('X', 255) + "😀" })
        {
            // Exercise the real binding callback even when browser maxlength would prevent typing this value.
            await ChangeAsync(cut, id, value);
            await cut.Find("form").SubmitAsync();
            Assert.NotEmpty(cut.FindAll(".validation-errors li"));
            Assert.Equal(0, handler.SessionReads);
            Assert.Empty(handler.Writes);
            Assert.Equal(2, handler.Reads);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task OptionalNulls_RemainNullAndRequiredNameStillRefusesBlank(bool create)
    {
        using var culture = new CultureScope("en");
        using var handler = new Transport(create);
        using var http = new HttpClient(handler) { BaseAddress = new("https://localhost/") };
        using var context = Context(http);
        var cut = await OpenAsync(context, handler);
        await ChangeAsync(cut, InputIds[0], "  ");
        await cut.Find("form").SubmitAsync();
        Assert.Equal(0, handler.SessionReads);
        Assert.Empty(handler.Writes);
        await ChangeAsync(cut, InputIds[0], " Literal company ");
        await cut.Find("form").SubmitAsync();
        var write = Assert.Single(handler.Writes);
        Assert.Equal(" Literal company ", write.Name);
        Assert.Null(write.TaxNumber);
        Assert.Null(write.Registrar);
    }

    private static BunitContext Context(HttpClient http)
    {
        var context = new BunitContext();
        context.Services.AddLocalization();
        context.Services.AddSingleton(http);
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        return context;
    }

    private static async Task<IRenderedComponent<CustomerRelations>> OpenAsync(BunitContext context, Transport handler)
    {
        var text = context.Services.GetRequiredService<IStringLocalizer<CustomerView>>();
        var cut = context.Render<CustomerRelations>(parameters => parameters
            .Add(component => component.Customer, handler.Profile)
            .Add(component => component.Permissions, new[] { "legacy-customer.customers.update", "legacy-customer.companies.read", "legacy-customer.companies.create", "legacy-customer.companies.update" })
            .Add(component => component.Localize, key => text[key].Value));
        await cut.Find(".customer-relations__actions button").ClickAsync();
        cut.WaitForAssertion(() => Assert.Single(cut.FindAll("#relation-company-name")));
        return cut;
    }

    private static Task ChangeAsync(IRenderedComponent<CustomerRelations> cut, string id, string value) => cut.InvokeAsync(() =>
        Assert.Single(cut.FindComponents<ShadcnInput<string>>(), component => component.FindAll("#" + id).Count != 0)
            .Instance.ValueChanged.InvokeAsync(value));

    private sealed class Transport(bool create) : HttpMessageHandler
    {
        private const string CustomerVersion = "\"0000000a\"";
        private static readonly string RelationVersion = '"' + new string('A', 64) + '"';
        public CustomerDetail Profile { get; } = new(42, "Synthetic", "Customer", "Synthetic Customer", null, null, null,
            "synthetic@example.invalid", null, create ? null : 77, null, null, null, null, null,
            create ? null : new(77, "Original company", null, null, null, null), null);
        public int Reads { get; private set; }
        public int SessionReads { get; private set; }
        public List<CustomerCompanyEdit> Writes { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            var path = request.RequestUri!.AbsolutePath;
            if (request.Method == HttpMethod.Get && path == "/bff/customers/42/versioned")
            {
                Reads++;
                return WithVersions(new(HttpStatusCode.OK) { Content = JsonContent.Create(Profile) }, CustomerVersion);
            }
            if (request.Method == HttpMethod.Get && path == $"/bff/customers/42/relations/company/{(create ? "new" : "77")}")
            {
                Reads++;
                return WithVersions(new(HttpStatusCode.OK) { Content = JsonContent.Create(new CustomerRelationDetail(42, Profile.CompanyId, Profile.Company, null)) }, RelationVersion);
            }
            if (request.Method == HttpMethod.Get && path == "/bff/session")
            {
                SessionReads++;
                return new(HttpStatusCode.OK) { Content = JsonContent.Create(new EmployeeSessionSummary(true, "employee", "Synthetic", ["Employee"], "controlled-company-csrf", 7, [])) };
            }
            if (request.Method == HttpMethod.Put && path == $"/bff/customers/42/relations/company/{(create ? "new" : "77")}")
            {
                Assert.Equal(RelationVersion, Assert.Single(request.Headers.GetValues("If-Match")));
                Assert.Equal(CustomerVersion, Assert.Single(request.Headers.GetValues("X-Customer-If-Match")));
                Assert.Equal("controlled-company-csrf", Assert.Single(request.Headers.GetValues("X-CSRF-TOKEN")));
                Writes.Add(await request.Content!.ReadFromJsonAsync<CustomerCompanyEdit>(token) ?? throw new InvalidOperationException("Missing synthetic Company input."));
                var response = WithVersions(new(HttpStatusCode.NoContent), '"' + new string('B', 64) + '"');
                response.Headers.Add("X-Relation-Id", "77");
                return response;
            }
            throw new InvalidOperationException("Unexpected Company editor transport.");
        }

        private static HttpResponseMessage WithVersions(HttpResponseMessage response, string version)
        {
            response.Headers.TryAddWithoutValidation("ETag", version);
            response.Headers.Add("X-Customer-ETag", CustomerVersion);
            return response;
        }
    }

    private sealed class CultureScope : IDisposable
    {
        private readonly CultureInfo culture = CultureInfo.CurrentCulture;
        private readonly CultureInfo uiCulture = CultureInfo.CurrentUICulture;
        public CultureScope(string language) => CultureInfo.CurrentCulture = CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(language);
        public void Dispose() { CultureInfo.CurrentCulture = culture; CultureInfo.CurrentUICulture = uiCulture; }
    }
}
