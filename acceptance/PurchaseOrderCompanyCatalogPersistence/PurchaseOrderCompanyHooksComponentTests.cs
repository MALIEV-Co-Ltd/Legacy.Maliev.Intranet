using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Bunit;
using Legacy.Maliev.Intranet.Client.Features.Procurement.Pages;
using Legacy.Maliev.Intranet.Client.Shared.Components;
using Legacy.Maliev.Intranet.Contracts;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.Extensions.DependencyInjection;

namespace PurchaseOrderCompanyCatalogPersistence.Acceptance;

/// <summary>Real Razor callbacks and validation; controlled HTTP is not the joined persistence proof.</summary>
public sealed class PurchaseOrderCompanyHooksComponentTests
{
    [Theory]
    [InlineData("shipping", "en", "Synthetic English Company")]
    [InlineData("billing", "en", "Synthetic English Company")]
    [InlineData("shipping", "th", "บริษัทสังเคราะห์")]
    [InlineData("billing", "th", "บริษัทสังเคราะห์")]
    public async Task Selection_NotifiesOnlyItsDocumentPartyAndPreservesManualState(
        string party, string language, string expectedName)
    {
        using var culture = new CultureScope(language);
        using var transport = new ControlledFormTransport();
        using var context = Context(transport);
        var cut = context.Render<PurchaseOrderCreate>();
        cut.WaitForAssertion(() => Assert.Equal(2, cut.FindComponents<LookupCompanyAssist>().Count));
        var edit = cut.FindComponent<EditForm>().Instance.EditContext!;
        var model = (PurchaseOrderCreateRequest)edit.Model;
        PopulateManual(model);
        var before = JsonSerializer.SerializeToElement(model);
        var notifications = new List<string>();
        edit.OnFieldChanged += (_, changed) => notifications.Add(changed.FieldIdentifier.FieldName);
        var assist = Assist(cut, party);

        await cut.InvokeAsync(() => assist.Instance.Selected.InvokeAsync(Company()));

        Assert.Equal(expectedName, Name(model, party));
        var field = party == "shipping" ? nameof(model.ShippingCompanyName) : nameof(model.BillingCompanyName);
        Assert.Equal([field], notifications);
        var after = JsonSerializer.SerializeToElement(model);
        foreach (var original in before.EnumerateObject().Where(property => property.Name != field))
            Assert.Equal(original.Value.GetRawText(), after.GetProperty(original.Name).GetRawText());
        Assert.Empty(transport.Creates);
        Assert.Equal(0, transport.SessionReads);
    }

    [Theory]
    [InlineData("shipping")]
    [InlineData("billing")]
    public async Task CapturedSelectionDeliveredAfterSubmitBegins_CannotAlterOrdinaryRequest(string party)
    {
        using var culture = new CultureScope("en");
        using var transport = new ControlledFormTransport(holdSession: true);
        using var context = Context(transport);
        var cut = context.Render<PurchaseOrderCreate>();
        cut.WaitForAssertion(() => Assert.Equal(2, cut.FindComponents<LookupCompanyAssist>().Count));
        var model = (PurchaseOrderCreateRequest)cut.FindComponent<EditForm>().Instance.EditContext!.Model;
        PopulateManual(model);
        var original = JsonSerializer.SerializeToElement(model);
        // Capture the real parent callback before child Disabled changes. This models
        // an admitted selection that is delivered after the parent's submission fence.
        var admitted = Assist(cut, party).Instance.Selected;
        var submit = cut.Find("form").SubmitAsync();
        try
        {
            await transport.SessionEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            cut.WaitForAssertion(() => Assert.All(cut.FindComponents<LookupCompanyAssist>(), assist => Assert.True(assist.Instance.Disabled)));
            await cut.InvokeAsync(() => admitted.InvokeAsync(Company()));
            Assert.Equal(original.GetRawText(), JsonSerializer.SerializeToElement(model).GetRawText());
        }
        finally
        {
            transport.ReleaseSession.TrySetResult();
            await submit.WaitAsync(TimeSpan.FromSeconds(5));
        }

        var sent = Assert.Single(transport.Creates);
        using var body = JsonDocument.Parse(sent.Body);
        Assert.Equal(original.GetProperty(nameof(model.ShippingCompanyName)).GetString(), body.RootElement.GetProperty("shippingCompanyName").GetString());
        Assert.Equal(original.GetProperty(nameof(model.BillingCompanyName)).GetString(), body.RootElement.GetProperty("billingCompanyName").GetString());
        Assert.Equal(model.ShippingAddressId, body.RootElement.GetProperty("shippingAddressId").GetInt32());
        Assert.Equal(model.BillingAddressId, body.RootElement.GetProperty("billingAddressId").GetInt32());
        Assert.Equal("controlled-po-csrf", sent.Csrf);
        Assert.True(Guid.TryParse(sent.Attempt, out _));
    }

    [Theory]
    [InlineData("shipping")]
    [InlineData("billing")]
    public async Task OversizedSuggestion_RemainsUntruncatedAndOrdinaryValidationStopsSave(string party)
    {
        using var culture = new CultureScope("en");
        using var transport = new ControlledFormTransport();
        using var context = Context(transport);
        var cut = context.Render<PurchaseOrderCreate>();
        cut.WaitForAssertion(() => Assert.Equal(2, cut.FindComponents<LookupCompanyAssist>().Count));
        var edit = cut.FindComponent<EditForm>().Instance.EditContext!;
        var model = (PurchaseOrderCreateRequest)edit.Model;
        PopulateManual(model);
        var oversized = new string('x', 257);
        await cut.InvokeAsync(() => Assist(cut, party).Instance.Selected.InvokeAsync(Company() with { NameEn = oversized }));

        Assert.Equal(oversized, Name(model, party));
        var field = party == "shipping" ? nameof(model.ShippingCompanyName) : nameof(model.BillingCompanyName);
        Assert.NotEmpty(edit.GetValidationMessages(new FieldIdentifier(model, field)));
        await cut.Find("form").SubmitAsync();
        Assert.Empty(transport.Creates);
        Assert.Equal(0, transport.SessionReads);
    }

    private static IRenderedComponent<LookupCompanyAssist> Assist(IRenderedComponent<PurchaseOrderCreate> cut, string party) =>
        Assert.Single(cut.FindComponents<LookupCompanyAssist>(), value => value.Instance.Id == $"purchase-order-{party}-company-lookup");

    private static string Name(PurchaseOrderCreateRequest model, string party) =>
        party == "shipping" ? model.ShippingCompanyName : model.BillingCompanyName;

    private static LookupCompany Company() => new("บริษัทสังเคราะห์", "Synthetic English Company", "0100000000001",
        "active", "synthetic", null, null, null, DateTimeOffset.UnixEpoch);

    private static void PopulateManual(PurchaseOrderCreateRequest model)
    {
        model.SupplierId = 41;
        model.EmployeeId = 51;
        model.ShippingAddressId = 61;
        model.BillingAddressId = 62;
        model.ShippingCompanyName = "Manual shipping";
        model.BillingCompanyName = "Manual billing";
        model.SupplierContactPerson = "Synthetic supplier contact";
        model.ShippingContactPerson = "Synthetic shipping contact";
        model.BillingContactPerson = "Synthetic billing contact";
        model.ShippingTelephone = "020000061";
        model.BillingTelephone = "020000062";
        model.ShippingMobile = "0800000061";
        model.BillingMobile = "0800000062";
        model.ShippingFax = "020000161";
        model.BillingFax = "020000162";
        model.Fob = "Synthetic FOB";
        model.Terms = "Synthetic terms";
        model.ShippingMethod = "Synthetic courier";
        model.Notes = "Synthetic manual notes";
        model.Items = [new() { PartNumber = "SYNTHETIC-PO", Description = "Synthetic part", Quantity = 2, UnitPrice = 12.5m }];
    }

    private static BunitContext Context(ControlledFormTransport transport)
    {
        var context = new BunitContext();
        context.Services.AddLocalization();
        context.Services.AddSingleton(new HttpClient(transport) { BaseAddress = new("https://localhost/") });
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        return context;
    }

    private sealed record SentCreate(string Body, string? Csrf, string? Attempt);

    private sealed class ControlledFormTransport(bool holdSession = false) : HttpMessageHandler
    {
        internal int SessionReads { get; private set; }
        internal TaskCompletionSource SessionEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource ReleaseSession { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal List<SentCreate> Creates { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            if (request.Method == HttpMethod.Get && request.RequestUri!.AbsolutePath == "/bff/purchase-orders/create-options")
                return new(HttpStatusCode.OK) { Content = JsonContent.Create(new PurchaseOrderCreateOptions(
                    [new(41, "Synthetic supplier")], [new(51, "Synthetic employee")],
                    [new(61, "Synthetic shipping street", "Bangkok"), new(62, "Synthetic billing street", "Bangkok")])) };
            if (request.Method == HttpMethod.Get && request.RequestUri!.AbsolutePath == "/bff/session")
            {
                SessionReads++;
                SessionEntered.TrySetResult();
                if (holdSession) await ReleaseSession.Task.WaitAsync(token);
                return new(HttpStatusCode.OK) { Content = JsonContent.Create(new EmployeeSessionSummary(true,
                    "employee", "Synthetic", ["Employee"], "controlled-po-csrf", 51, [])) };
            }
            if (request.Method == HttpMethod.Post && request.RequestUri!.AbsolutePath == "/bff/purchase-orders")
            {
                Creates.Add(new(await request.Content!.ReadAsStringAsync(token),
                    request.Headers.TryGetValues("X-CSRF-TOKEN", out var csrf) ? csrf.Single() : null,
                    request.Headers.TryGetValues("Idempotency-Key", out var attempt) ? attempt.Single() : null));
                return new(HttpStatusCode.Created) { Content = JsonContent.Create(new CreatedPurchaseOrder(71)) };
            }
            throw new InvalidOperationException("Unexpected controlled component-test request.");
        }
    }

    private sealed class CultureScope : IDisposable
    {
        private readonly CultureInfo original = CultureInfo.CurrentCulture;
        private readonly CultureInfo originalUi = CultureInfo.CurrentUICulture;
        internal CultureScope(string language) => CultureInfo.CurrentCulture = CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(language);
        public void Dispose()
        {
            CultureInfo.CurrentCulture = original;
            CultureInfo.CurrentUICulture = originalUi;
        }
    }
}
