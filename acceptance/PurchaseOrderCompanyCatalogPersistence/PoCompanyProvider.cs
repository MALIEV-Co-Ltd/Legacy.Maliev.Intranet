using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;

namespace SupplierCatalogPersistence.Acceptance;

/// <summary>Owned external-provider protocol fixture; original Catalog adapter and transport remain unchanged.</summary>
internal sealed class PoCompanyProvider
{
    internal ConcurrentQueue<Observation> Observations { get; } = new();
    internal OwnedSyntheticTransport Transport { get; private set; } = null!;
    private int active;
    private bool disposed;

    internal static string Query(string party, string language) => $"Synthetic PO {party} {language}";
    internal static string Name(string party, string language) => language == "th"
        ? party == "shipping" ? "บริษัทขนส่งสังเคราะห์" : "บริษัทเรียกเก็บสังเคราะห์"
        : party == "shipping" ? "Synthetic Shipping Limited" : "Synthetic Billing Limited";

    internal static async Task<PoCompanyProvider> StartAsync(SupplierResourceScope owner)
    {
        var result = new PoCompanyProvider();
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        var app = owner.Acquire("po-company-provider-host", builder.Build, async (value, token) =>
        {
            await value.StopAsync(token);
            await value.DisposeAsync();
            if (Volatile.Read(ref result.active) != 0 || !result.disposed)
                throw new InvalidOperationException("Provider transport and requests did not settle before host release.");
        }, phase: 4);
        var startup = owner.Register("po-company-provider-startup", _ => Task.CompletedTask);
        app.MapPost("/sapi/search/get_suggestion", async (HttpContext context) =>
        {
            Interlocked.Increment(ref result.active);
            try
            {
                if (context.Request.Headers["X-Synthetic-Logical-Origin"] != "https://data.creden.co/sapi/search/get_suggestion")
                    throw new InvalidOperationException("Original provider logical URI required.");
                using var body = await JsonDocument.ParseAsync(context.Request.Body, cancellationToken: context.RequestAborted);
                var row = body.RootElement;
                var language = row.GetProperty("lang").GetString();
                var query = row.GetProperty("text").GetString();
                var party = query == Query("shipping", language ?? "") ? "shipping"
                    : query == Query("billing", language ?? "") ? "billing" : null;
                if (row.EnumerateObject().Count() != 3 || row.GetProperty("type_search").GetString() != "prefix" ||
                    language is not ("en" or "th") || party is null)
                    throw new InvalidOperationException("Only four declared synthetic party/language requests are admitted.");
                await context.Response.WriteAsJsonAsync(new
                {
                    success = true,
                    data = new
                    {
                        result = new[]
                        {
                            new { company_name = new { en = Name(party, "en"), th = Name(party, "th") }, id = "1234567890123" },
                        },
                    },
                }, context.RequestAborted);
                result.Observations.Enqueue(new(party, language, query!, context.Response.StatusCode));
            }
            finally { Interlocked.Decrement(ref result.active); }
        });
        await owner.StartAsync(startup, app.StartAsync);
        owner.Acquire("po-company-provider-transport", () => result.Transport = new OwnedSyntheticTransport(new Uri(app.Urls.Single())),
            (value, _) => { value.Dispose(); result.disposed = true; return Task.CompletedTask; }, phase: 3);
        return result;
    }

    internal sealed record Observation(string Party, string Language, string Query, int Status);
}

public sealed class PoOriginalCatalogTransportControls
{
    [Fact]
    public Task InvalidLogicalOrPhysicalRequests_NeverReachExternalFixture() => TransportControls.Run();
}
