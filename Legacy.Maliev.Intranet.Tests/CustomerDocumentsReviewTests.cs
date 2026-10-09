using Bunit;
using System.Net;
using System.Net.Http.Json;
using Legacy.Maliev.Intranet.Client.Features.Customers.Components;
using Legacy.Maliev.Intranet.Contracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Legacy.Maliev.Intranet.Tests;

/// <summary>Actual components and HTTP response pipeline with controlled owner responses; no production join.</summary>
public sealed class CustomerDocumentsReviewTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DelayedPreviousCustomerCannotOverwriteOrClearCurrentCustomer(bool lateFailure)
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var document = Guid.NewGuid();
        var builder = WebApplication.CreateBuilder(); builder.WebHost.UseTestServer(); builder.Logging.ClearProviders();
        await using var app = builder.Build();
        app.MapGet("/bff/customers/{customer:int}/documents", async (int customer) =>
        {
            if (customer == 42) { entered.TrySetResult(); await release.Task; if (lateFailure) return Results.Text("{malformed", "application/json"); }
            return Results.Json(new[] { new CustomerDocumentSummary(document, customer, "Evidence", customer == 42 ? "old-customer-A" : "current-customer-B", "Internal", 1) });
        });
        app.MapGet("/bff/staff/nda-reminders", () => Array.Empty<CustomerNdaReminder>());
        app.MapGet("/bff/customers/{customer:int}/documents/{id:guid}/versions", () => Array.Empty<CustomerDocumentVersionSummary>());
        await app.StartAsync(); using var context = Context(app.GetTestClient());
        var cut = context.Render<CustomerDocuments>(parameters => parameters.Add(x => x.CustomerId, 0));
        var previousLoad = cut.InvokeAsync(() => cut.Instance.SetParametersAsync(ParameterView.FromDictionary(new Dictionary<string, object?> { [nameof(CustomerDocuments.CustomerId)] = 42 })));
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await cut.Find("#customer-document-title").InputAsync("old customer file title");
        await cut.InvokeAsync(() => cut.Instance.SetParametersAsync(ParameterView.FromDictionary(new Dictionary<string, object?> { [nameof(CustomerDocuments.CustomerId)] = 43 })));
        cut.WaitForAssertion(() => Assert.Contains("current-customer-B", cut.Markup));
        release.TrySetResult(); await previousLoad.WaitAsync(TimeSpan.FromSeconds(5));
        cut.WaitForAssertion(() => { Assert.Contains("current-customer-B", cut.Markup); Assert.DoesNotContain("old-customer-A", cut.Markup); Assert.Equal("", cut.Find("#customer-document-title").GetAttribute("value") ?? ""); });
    }
    [Fact]
    public async Task LaterMalformedHistoryClearsAllPreviouslyLoadedRenderedMetadata()
    {
        var first = Guid.NewGuid(); var second = Guid.NewGuid();
        var builder = WebApplication.CreateBuilder(); builder.WebHost.UseTestServer(); builder.Logging.ClearProviders();
        await using var app = builder.Build();
        app.MapGet("/bff/customers/42/documents", () => new[] { new CustomerDocumentSummary(first, 42, "Evidence", "partial-protected-first", "Internal", 1), new CustomerDocumentSummary(second, 42, "Evidence", "partial-protected-second", "Internal", 1) });
        app.MapGet("/bff/staff/nda-reminders", () => new[] { new CustomerNdaReminder(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 1, 7, "synthetic-staff", DateTimeOffset.UtcNow, "Due") });
        app.MapGet("/bff/customers/42/documents/{document:guid}/versions", (Guid document) => document == first ? Results.Json(new[] { new CustomerDocumentVersionSummary(first, Guid.NewGuid(), 1, "Evidence", new string('a', 64), DateTimeOffset.UtcNow, "PendingVerification", null, null, 1) }) : Results.Text("{malformed", "application/json"));
        await app.StartAsync();
        using var context = Context(app.GetTestClient()); var cut = context.Render<CustomerDocuments>(parameters => parameters.Add(x => x.CustomerId, 42));
        cut.WaitForAssertion(() => Assert.Contains("role=\"alert\"", cut.Markup));
        Assert.DoesNotContain("partial-protected-first", cut.Markup); Assert.DoesNotContain("partial-protected-second", cut.Markup);
        Assert.Empty(cut.FindAll("article")); Assert.Empty(cut.FindAll("details ul li"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RepeatedNdaVerificationRefreshesAuthoritativeParentRevision(bool concurrentOwnerChange)
    {
        var document = Guid.NewGuid(); var version = Guid.NewGuid(); var revision = 1L; var sent = new List<long>();
        var builder = WebApplication.CreateBuilder(); builder.WebHost.UseTestServer(); builder.Logging.ClearProviders();
        await using var app = builder.Build();
        app.MapGet("/bff/customers/42/documents", () => new[] { new CustomerDocumentSummary(document, 42, "Nda", "synthetic-nda", "Internal", revision) });
        app.MapGet("/bff/staff/nda-reminders", () => Array.Empty<CustomerNdaReminder>());
        app.MapGet($"/bff/customers/42/documents/{document:D}/versions", () => new[] { new CustomerDocumentVersionSummary(document, version, 1, "Nda", new string('a', 64), DateTimeOffset.UtcNow, revision > 1 ? "Verified" : "PendingVerification", revision > 1 ? "synthetic-staff" : null, revision > 1 ? DateTimeOffset.UtcNow : null, revision) });
        app.MapGet($"/bff/customers/42/documents/{document:D}/nda", () => Results.NotFound());
        app.MapGet("/bff/session", () => new EmployeeSessionSummary(true, "synthetic-staff", null, [], "synthetic-csrf"));
        app.MapPost($"/bff/customers/42/documents/{document:D}/nda/verification", async (HttpContext request) =>
        {
            var input = await request.Request.ReadFromJsonAsync<CustomerNdaVerificationRequest>(); sent.Add(input!.ExpectedRevision);
            if (input.ExpectedRevision != revision) return Results.Conflict();
            revision++; return Results.Json(new CustomerNdaVerificationReceipt(Guid.NewGuid(), document, version, revision, "Active", "Protected"));
        });
        await app.StartAsync(); using var context = Context(app.GetTestClient());
        var cut = context.Render<CustomerDocuments>(parameters => parameters.Add(x => x.CustomerId, 42));
        cut.WaitForAssertion(() => { Assert.Single(cut.FindAll($"#nda-{version:D}-party-one")); Assert.False(cut.Find("section > button").HasAttribute("disabled")); });
        await Change(cut, version, "party-one", "synthetic first"); await Change(cut, version, "party-two", "synthetic second");
        await Change(cut, version, "coverage", "Customer"); await Change(cut, version, "responsible", "synthetic-staff"); await Change(cut, version, "reason", "synthetic reason");
        if (concurrentOwnerChange) revision = 2;
        await cut.FindComponent<CustomerNdaVerification>().Find($"#nda-{version:D}-verify").ClickAsync(); await cut.FindComponent<CustomerNdaVerification>().Find($"#nda-{version:D}-verify").ClickAsync();
        Assert.Equal(new long[] { 1, 2 }, sent); Assert.Equal(3, revision);
        Assert.Contains("Version 1 - Verified", cut.Find("article ul li").TextContent);
        Assert.DoesNotContain("\u00e2\u20ac\u201d", cut.Find("article ul li").TextContent);
        await cut.InvokeAsync(() => cut.Instance.SetParametersAsync(ParameterView.FromDictionary(new Dictionary<string, object?> { [nameof(CustomerDocuments.CustomerId)] = 0 })));
        cut.WaitForAssertion(() => Assert.Empty(cut.FindAll("article")));
        Assert.DoesNotContain("synthetic-nda", cut.Markup);
    }

    private static Task Change(IRenderedComponent<CustomerDocuments> cut, Guid version, string field, string value) => cut.InvokeAsync(async () =>
    {
        var form = cut.FindComponent<CustomerNdaVerification>();
        if (field == "coverage")
        {
            await form.Find($"#nda-{version:D}-{field}").ClickAsync();
            await form.Find($"[role=option][data-value={value}]").ClickAsync();
        }
        else await form.Find($"#nda-{version:D}-{field}").InputAsync(value);
    });
    private static BunitContext Context(HttpClient http)
    {
        var context = new BunitContext(); context.Services.AddLocalization(); context.Services.AddSingleton(http); context.JSInterop.Mode = JSRuntimeMode.Loose; return context;
    }
}
