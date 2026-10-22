using Bunit;
using System.Net;
using System.Text.Json;
using Legacy.Maliev.Intranet.Client.Features.Customers.Components;
using Legacy.Maliev.Intranet.Contracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Legacy.Maliev.Intranet.Tests;

/// <summary>Actual upload controls/HTTP with controlled canonical-owner boundary, not a production authority join.</summary>
public sealed class CustomerDocumentAssociationTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SelectedCommercialAssociationsReachUploadAsImmutableNamedSnapshot(bool delayedSession)
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        string? associations = null; int uploadedCustomer = 0;
        var builder = WebApplication.CreateBuilder(); builder.WebHost.UseTestServer(); builder.Logging.ClearProviders();
        await using var app = builder.Build();
        app.MapGet("/bff/customers/{customer:int}/documents", () => Array.Empty<CustomerDocumentSummary>());
        app.MapGet("/bff/staff/nda-reminders", () => Array.Empty<CustomerNdaReminder>());
        app.MapGet("/bff/session", async () => { entered.TrySetResult(); if (delayedSession) await release.Task; return new EmployeeSessionSummary(true, "synthetic-staff", null, [], "synthetic-csrf"); });
        app.MapPost("/bff/customers/{customer:int}/documents", async (int customer, HttpRequest request) =>
        {
            var form = await request.ReadFormAsync(); associations = form["Associations"].ToString(); uploadedCustomer = customer;
            return Results.Json(new CustomerDocumentVersionReceipt(Guid.NewGuid(), Guid.NewGuid(), customer, 1, new string('a', 64), 1));
        });
        await app.StartAsync(); using var context = Context(app.GetTestClient());
        var cut = context.Render<CustomerDocuments>(p => p.Add(x => x.CustomerId, 42));
        cut.WaitForAssertion(() => Assert.False(cut.Find("section > button").HasAttribute("disabled")));
        await cut.Find("#customer-document-title").InputAsync("synthetic billing evidence");
        await cut.Find("#customer-document-kind").ClickAsync();
        await cut.Find("[role=option][data-value=BillingInstruction]").ClickAsync();
        await cut.Find("#upload-order-ids").InputAsync("11,12");
        await cut.Find("#upload-quotation-id").InputAsync("21");
        await cut.Find("#upload-replacement-ids").InputAsync("31");
        cut.FindComponent<InputFile>().UploadFiles(InputFileContent.CreateFromBinary([1], "synthetic.pdf", null, "application/pdf"));
        var pending = cut.Find("#customer-document-upload").ClickAsync();
        if (delayedSession)
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await cut.Find("#upload-order-ids").InputAsync("99"); release.TrySetResult();
        }
        await pending;
        Assert.Equal(42, uploadedCustomer);
        using var payload = JsonDocument.Parse(associations!);
        Assert.Equal(new[] { "Order:11", "Order:12", "Quotation:21", "Replacement:31" }, payload.RootElement.EnumerateArray().Select(x => x.GetProperty("Kind").GetString() + ":" + x.GetProperty("ResourceId").GetInt32()));
        Assert.DoesNotContain("CustomerId", associations!);
        await cut.Find("#upload-order-ids").InputAsync("88");
        await cut.InvokeAsync(() => cut.Instance.SetParametersAsync(ParameterView.FromDictionary(new Dictionary<string, object?> { [nameof(CustomerDocuments.CustomerId)] = 43 })));
        Assert.Equal("", cut.Find("#upload-order-ids").GetAttribute("value") ?? "");
        Assert.Equal("", cut.Find("#upload-quotation-id").GetAttribute("value") ?? "");
        Assert.Equal("", cut.Find("#upload-replacement-ids").GetAttribute("value") ?? "");
    }

    [Theory]
    [InlineData("0")]
    [InlineData("11,11")]
    [InlineData("2147483648")]
    [InlineData("over-limit")]
    public async Task InvalidSelectedAssociationNeverStartsUpload(string ids)
    {
        if (ids == "over-limit") ids = string.Join(',', Enumerable.Range(1, 101));
        var builder = WebApplication.CreateBuilder(); builder.WebHost.UseTestServer(); builder.Logging.ClearProviders();
        await using var app = builder.Build(); int mutations = 0;
        app.MapGet("/bff/customers/42/documents", () => Array.Empty<CustomerDocumentSummary>());
        app.MapGet("/bff/staff/nda-reminders", () => Array.Empty<CustomerNdaReminder>());
        app.MapGet("/bff/session", () => new EmployeeSessionSummary(true, "synthetic-staff", null, [], "synthetic-csrf"));
        app.MapPost("/bff/customers/42/documents", () => { mutations++; return Results.Ok(); });
        await app.StartAsync(); using var context = Context(app.GetTestClient());
        var cut = context.Render<CustomerDocuments>(p => p.Add(x => x.CustomerId, 42));
        cut.WaitForAssertion(() => Assert.False(cut.Find("section > button").HasAttribute("disabled")));
        await cut.Find("#customer-document-title").InputAsync("synthetic");
        await cut.Find("#upload-order-ids").InputAsync(ids);
        cut.FindComponent<InputFile>().UploadFiles(InputFileContent.CreateFromBinary([1], "synthetic.pdf", null, "application/pdf"));
        await cut.Find("#customer-document-upload").ClickAsync();
        Assert.Equal(0, mutations); Assert.Single(cut.FindAll("p[role=alert]"));
    }

    private static BunitContext Context(HttpClient http) { var context = new BunitContext(); context.Services.AddLocalization(); context.Services.AddSingleton(http); context.JSInterop.Mode = JSRuntimeMode.Loose; return context; }
}
