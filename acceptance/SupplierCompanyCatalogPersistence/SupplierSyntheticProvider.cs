using System.Collections.Concurrent;
using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace SupplierCatalogPersistence.Acceptance;

internal sealed class SupplierSyntheticProvider
{
    internal ConcurrentQueue<object> Observations { get; } = new();
    internal OwnedSyntheticTransport Transport { get; private set; } = null!;
    private int active;
    private int admitted;
    private bool transportDisposed;

    internal static async Task<SupplierSyntheticProvider> StartAsync(SupplierResourceScope owner,
        Func<WebApplication, CancellationToken, Task>? start = null)
    {
        var result = new SupplierSyntheticProvider();
        var builder = WebApplication.CreateSlimBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.ConfigureKestrel(options => options.Listen(IPAddress.Loopback, 0));
        var host = owner.Acquire("company-provider-host", builder.Build, async (value, token) =>
        {
            await value.StopAsync(token);
            await value.DisposeAsync();
            if (Volatile.Read(ref result.active) != 0 || result.Transport is not null && !result.transportDisposed)
                throw new InvalidOperationException("Provider requests or exact transport have not settled.");
            if (Environment.GetEnvironmentVariable("SUPPLIER_RESOURCE_EVIDENCE") is null) return;
            await owner.WriteReceiptAsync("provider.jsonl", new
            {
                schema = 1,
                state = "stopped-disposed",
                admittedRequests = Volatile.Read(ref result.admitted),
                completedRequests = result.Observations.Count,
                activeRequests = Volatile.Read(ref result.active),
                transportAllocated = result.Transport is not null,
                exactTransportDisposed = result.transportDisposed,
                runId = Environment.GetEnvironmentVariable("GITHUB_RUN_ID"),
                runAttempt = Environment.GetEnvironmentVariable("GITHUB_RUN_ATTEMPT"),
            }, token);
        }, phase: 4);
        var startup = owner.Register("company-provider-startup", _ => Task.CompletedTask);
        host.MapPost("/sapi/search/get_suggestion", async context =>
        {
            Interlocked.Increment(ref result.admitted);
            Interlocked.Increment(ref result.active);
            try
            {
                if (context.Request.ContentLength is null or > 4096 ||
                    context.Request.Headers["X-Synthetic-Logical-Origin"] != "https://data.creden.co/sapi/search/get_suggestion")
                    throw new InvalidOperationException("Synthetic upstream contract rejected.");
                using var body = await JsonDocument.ParseAsync(context.Request.Body, cancellationToken: context.RequestAborted);
                var row = body.RootElement;
                if (row.EnumerateObject().Count() != 3 || row.GetProperty("text").GetString() != "Synthetic supplier company" ||
                    row.GetProperty("lang").GetString() != "en" || row.GetProperty("type_search").GetString() != "prefix")
                    throw new InvalidOperationException("Only the exact synthetic positive company request is admitted.");
                await context.Response.WriteAsJsonAsync(new
                {
                    success = true,
                    data = new { result = new[] { new { company_name = new { en = "Synthetic Company Limited" }, id = "1234567890123" } } },
                }, context.RequestAborted);
                result.Observations.Enqueue(new
                {
                    logicalUri = "https://data.creden.co/sapi/search/get_suggestion",
                    physicalLoopback = true,
                    method = "POST",
                    typeSearch = "prefix",
                    query = "Synthetic supplier company",
                    language = "en",
                    status = context.Response.StatusCode,
                });
            }
            finally { Interlocked.Decrement(ref result.active); }
        });
        await owner.StartAsync(startup, token => start is null ? host.StartAsync(token) : start(host, token));
        // Phase 3 follows application-host quiescence (phase 2), before provider stop (phase 4).
        owner.Acquire("company-provider-transport", () => result.Transport = new OwnedSyntheticTransport(new Uri(host.Urls.Single())),
            (value, _) => { value.Dispose(); result.transportDisposed = true; return Task.CompletedTask; }, phase: 3);
        return result;
    }
}

public sealed class SupplierCompanyProviderControls
{
    [Fact]
    public async Task FailedStartupBeforeTransportBirth_StopsOwnedProviderAndReleasesScope()
    {
        var owner = new SupplierResourceScope(TimeSpan.FromMinutes(1));
        try
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() => SupplierSyntheticProvider.StartAsync(owner,
                (_, _) => throw new InvalidOperationException("Synthetic provider startup fault.")));
        }
        finally { await owner.DisposeAsync(); }
        Assert.True(owner.Released);
    }
}
