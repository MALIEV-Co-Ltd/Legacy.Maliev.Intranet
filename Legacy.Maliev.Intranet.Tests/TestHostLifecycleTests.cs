extern alias Bff;

using System.Net;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using Legacy.Maliev.Intranet.Auth;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit.Abstractions;
using BffProgram = Bff::Program;

namespace Legacy.Maliev.Intranet.Tests;

[Collection("Test host lifecycle")]
public sealed class TestHostLifecycleTests(ITestOutputHelper output)
{
    [Fact]
    public async Task DisposedBffHosts_ReleaseTheirPipelineWhileKeepingLoginRateLimitsEnforced()
    {
        // OpenTelemetry initializes a process-wide diagnostics worker on its first host.
        // Warm that one-time initialization before checking later per-host lifetimes.
        _ = await ExerciseAndDisposeAsync();
        var hosts = new List<WeakReference>();
        for (var index = 0; index < 4; index++)
            hosts.Add(await ExerciseAndDisposeAsync());

        for (var attempt = 0; attempt < 5 && hosts.Any(host => host.IsAlive); attempt++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            await Task.Delay(100);
        }
        output.WriteLine($"Retained disposed factories: {hosts.Count(host => host.IsAlive)}; managed bytes: {GC.GetTotalMemory(false)}");
        Assert.All(hosts, host => Assert.False(host.IsAlive, "Disposed BFF factory remains rooted by its middleware timer."));
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static async Task<WeakReference> ExerciseAndDisposeAsync()
    {
        // This route rejects the email before calling Auth. Use the real auth client
        // without IHttpClientFactory's intentional two-minute pooled-handler lifetime,
        // so the weak-reference assertion isolates middleware shutdown ownership.
        using var authHttp = new HttpClient { BaseAddress = new Uri("https://disposable-auth.invalid/") };
        await using var factory = new WebApplicationFactory<BffProgram>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            TestJwtConfiguration.Configure(builder);
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<ILegacyAuthClient>();
                services.AddSingleton<ILegacyAuthClient>(provider => ActivatorUtilities.CreateInstance<LegacyAuthClient>(provider, authHttp));
            });
        });
        using var client = factory.CreateClient(new() { BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false, HandleCookies = true });
        using var session = await client.GetAsync("/bff/session");
        using var payload = System.Text.Json.JsonDocument.Parse(await session.Content.ReadAsStringAsync());
        var csrf = payload.RootElement.GetProperty("csrfToken").GetString();
        for (var attempt = 0; attempt < 11; attempt++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, "/bff/login")
            {
                Content = JsonContent.Create(new { email = "disposable@example.test", password = "correct-password", returnUrl = "/Dashboard", rememberMe = false }),
            };
            request.Headers.Add("X-CSRF-TOKEN", csrf);
            using var response = await client.SendAsync(request);
            Assert.Equal(attempt < 10 ? HttpStatusCode.Unauthorized : HttpStatusCode.TooManyRequests, response.StatusCode);
        }
        return new WeakReference(factory);
    }
}

[CollectionDefinition("Test host lifecycle", DisableParallelization = true)]
public sealed class TestHostLifecycleCollection;
