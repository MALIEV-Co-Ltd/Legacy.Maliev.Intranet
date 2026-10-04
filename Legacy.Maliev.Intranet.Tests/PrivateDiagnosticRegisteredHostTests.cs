using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Xunit.Abstractions;

namespace Legacy.Maliev.Intranet.Tests;

[CollectionDefinition("Private diagnostic native console", DisableParallelization = true)]
public sealed class PrivateDiagnosticNativeConsoleCollection;

[Collection("Private diagnostic native console")]
public sealed class PrivateDiagnosticRegisteredHostTests(ITestOutputHelper output)
{
    private const string Nonce = "aabbccdd00112233445566778899aabb";

    [Theory]
    [InlineData(false, "public", Nonce, "GET", false)]
    [InlineData(true, "public", Nonce, "GET", false)]
    [InlineData(false, "absent", Nonce, "GET", false)]
    [InlineData(true, "absent", Nonce, "GET", false)]
    [InlineData(false, "loopback", null, "GET", false)]
    [InlineData(true, "loopback", null, "GET", false)]
    [InlineData(false, "loopback", "invalid", "GET", false)]
    [InlineData(true, "loopback", "invalid", "GET", false)]
    [InlineData(false, "loopback", Nonce, "POST", false)]
    [InlineData(true, "loopback", Nonce, "POST", false)]
    [InlineData(false, "loopback", Nonce, "GET", true)]
    [InlineData(true, "loopback", Nonce, "GET", true)]
    public async Task Denied_probe_ReturnsEmpty404WithoutRedirectOrIncident(bool bff, string peer, string? nonce, string method, bool duplicate)
    {
        await using var host = new PrivateDiagnosticHostFixture(bff);
        using var client = host.CreateClient();
        var before = host.FeedCount;
        using var request = PrivateDiagnosticHostFixture.Probe(nonce, peer, method);
        if (duplicate) request.Headers.TryAddWithoutValidation("X-Maliev-Diagnostic-Id", Nonce);
        using var response = await client.SendAsync(request);
        await AssertEmpty(response, HttpStatusCode.NotFound);
        AssertDeniedHeaders(response);
        Assert.Empty(Incidents(host));
        Assert.Equal(before, host.FeedCount);
        Assert.Equal(0, host.AttemptedExternalCalls);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Forwarded_loopback_DoesNotAdmitPublicDirectPeer(bool bff)
    {
        await using var host = new PrivateDiagnosticHostFixture(bff);
        using var client = host.CreateClient();
        using var request = PrivateDiagnosticHostFixture.Probe(Nonce, "public");
        request.Headers.Add("X-Forwarded-For", "127.0.0.1");
        request.Headers.Add("X-Forwarded-Proto", "https");
        using var response = await client.SendAsync(request);
        await AssertEmpty(response, HttpStatusCode.NotFound);
        AssertDeniedHeaders(response);
        Assert.Empty(Incidents(host));
        Assert.Equal(0, host.FeedCount);
        Assert.Equal(0, host.AttemptedExternalCalls);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Admitted_probe_UsesRealFrameworkSeverityAndNativeSafeConsole(bool bff)
    {
        await using var host = new PrivateDiagnosticHostFixture(bff);
        using var client = host.CreateClient();
        using var request = PrivateDiagnosticHostFixture.Probe(Nonce.ToUpperInvariant());
        using var response = await client.SendAsync(request);
        await AssertEmpty(response, HttpStatusCode.InternalServerError);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal(Nonce, Assert.Single(response.Headers.GetValues("X-Maliev-Diagnostic-Id")));
        Assert.Equal("no-store", response.Headers.CacheControl?.ToString());
        Assert.Equal("noindex", Assert.Single(response.Headers.GetValues("X-Robots-Tag")));
        Assert.Matches("^[0-9a-f]{32}$", Assert.Single(response.Headers.GetValues("X-Maliev-Health-Instance")));
        var incidents = Incidents(host);
        Assert.Equal(new[] { LogLevel.Warning, LogLevel.Error, LogLevel.Critical }, incidents.Select(record => record.Level));
        Assert.Equal("ObservabilityPipelineProbe", incidents[0].EventName);
        Assert.Null(incidents[0].ExceptionType);
        Assert.Equal("Microsoft.AspNetCore.Diagnostics.ExceptionHandlerMiddleware", incidents[1].Category);
        Assert.Equal(1, incidents[1].EventId);
        Assert.Equal("ProductionObservabilityDiagnosticException", incidents[1].ExceptionType);
        Assert.Equal("UnhandledRequestFailure", incidents[2].EventName);
        Assert.Null(incidents[2].ExceptionType);
        Assert.Equal(0, host.FeedCount);
        Assert.Equal(0, host.AttemptedExternalCalls);

        // Provider disposal drains its queue before inspecting actual output.
        await host.DisposeAsync();
        var lines = host.NativeOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(line => JsonDocument.Parse(line)).ToArray();
        try
        {
            var native = lines.Where(document => document.RootElement.GetProperty("Category").GetString() is { } category
                && (category.EndsWith("PrivateRequestObservation", StringComparison.Ordinal)
                    || category == "Microsoft.AspNetCore.Diagnostics.ExceptionHandlerMiddleware")).ToArray();
            Assert.Equal(new[] { "WARNING", "ERROR", "CRITICAL" }, native.Select(document => document.RootElement.GetProperty("severity").GetString()));
            Assert.All(native, document => Assert.Contains(Nonce, document.RootElement.GetRawText(), StringComparison.Ordinal));
            Assert.DoesNotContain("203.0.113.7", host.NativeOutput, StringComparison.Ordinal);
        }
        finally
        {
            foreach (var document in lines) document.Dispose();
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Repeat_probe_IsQuiet429UntilOneMinuteBoundary(bool bff)
    {
        await using var host = new PrivateDiagnosticHostFixture(bff);
        using var client = host.CreateClient();
        using var first = await SendProbe(client);
        await AssertEmpty(first, HttpStatusCode.InternalServerError);
        host.Clock.Advance(TimeSpan.FromSeconds(59));
        using var repeat = await SendProbe(client);
        await AssertEmpty(repeat, HttpStatusCode.TooManyRequests);
        AssertDeniedHeaders(repeat);
        Assert.Equal(3, Incidents(host).Length);
        host.Clock.Advance(TimeSpan.FromSeconds(1));
        using var recovered = await SendProbe(client);
        await AssertEmpty(recovered, HttpStatusCode.InternalServerError);
        Assert.Equal(6, Incidents(host).Length);
        Assert.Equal(0, host.FeedCount);
        Assert.Equal(0, host.AttemptedExternalCalls);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Registered_healthGet_HasSameOpaqueHostMarkerAndNoSyntheticIncident(bool bff)
    {
        await using var host = new PrivateDiagnosticHostFixture(bff);
        using var client = host.CreateClient();
        using var live = await client.GetAsync($"/{host.Prefix}/liveness");
        using var ready = await client.GetAsync($"/{host.Prefix}/readiness");
        Assert.Equal(HttpStatusCode.OK, live.StatusCode);
        Assert.Equal(HttpStatusCode.OK, ready.StatusCode);
        Assert.True(live.Headers.Contains("X-Maliev-Health-Instance"));
        Assert.True(ready.Headers.Contains("X-Maliev-Health-Instance"));
        var marker = Assert.Single(live.Headers.GetValues("X-Maliev-Health-Instance"));
        Assert.Matches("^[0-9a-f]{32}$", marker);
        Assert.Equal(marker, Assert.Single(ready.Headers.GetValues("X-Maliev-Health-Instance")));
        Assert.Equal("no-store", live.Headers.CacheControl?.ToString());
        Assert.Equal("no-store", ready.Headers.CacheControl?.ToString());
        Assert.Empty(Incidents(host));
        Assert.Equal(0, host.AttemptedExternalCalls);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Aspire_liveness_KeepsExistingWireWithoutNewMarker(bool bff)
    {
        await using var host = new PrivateDiagnosticHostFixture(bff);
        using var client = host.CreateClient();
        using var response = await client.GetAsync($"/{host.Prefix}/aspire-liveness");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Healthy", await response.Content.ReadAsStringAsync());
        Assert.False(response.Headers.Contains("X-Maliev-Health-Instance"));
        Assert.Empty(Incidents(host));
        Assert.Equal(0, host.AttemptedExternalCalls);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Ordinary_authChallenge_RemainsHostSpecific(bool bff)
    {
        await using var host = new PrivateDiagnosticHostFixture(bff);
        using var client = host.CreateClient();
        using var response = await client.GetAsync(bff ? "/bff/diagnostics/events" : "/Employees");
        Assert.Equal(bff ? HttpStatusCode.Unauthorized : HttpStatusCode.Redirect, response.StatusCode);
        if (bff) Assert.Null(response.Headers.Location);
        else Assert.Contains("/Login", response.Headers.Location?.OriginalString ?? string.Empty, StringComparison.Ordinal);
        Assert.False(response.Headers.Contains("X-Maliev-Health-Instance"));
        Assert.Empty(Incidents(host));
        Assert.Equal(0, host.FeedCount);
        Assert.Equal(0, host.AttemptedExternalCalls);
    }

    [Theory]
    [InlineData("GET")]
    [InlineData("HEAD")]
    public async Task Bff_existingJavaScript_RemainsAnonymousStaticContent(string method)
    {
        await using var host = new PrivateDiagnosticHostFixture(true);
        using var client = host.CreateClient();
        using var request = new HttpRequestMessage(new HttpMethod(method), "/js/workspace-navigation.js");
        using var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Null(response.Headers.Location);
        Assert.NotEqual("text/html", response.Content.Headers.ContentType?.MediaType);
        var body = await response.Content.ReadAsStringAsync();
        if (method == "GET")
        {
            Assert.Contains("window.malievNavigation", body, StringComparison.Ordinal);
            Assert.Contains("window.history.replaceState", body, StringComparison.Ordinal);
        }
        else Assert.Empty(body);
        Assert.False(response.Headers.Contains("X-Maliev-Health-Instance"));
        Assert.False(response.Headers.Contains("X-Maliev-Diagnostic-Id"));
        Assert.Empty(Incidents(host));
        Assert.Equal(0, host.FeedCount);
        Assert.Equal(0, host.AttemptedExternalCalls);
    }

    [Theory]
    [InlineData("GET")]
    [InlineData("HEAD")]
    public async Task Bff_missingFrameworkJavaScript_RemainsUncached404NotShell(string method)
    {
        await using var host = new PrivateDiagnosticHostFixture(true);
        using var client = host.CreateClient();
        using var request = new HttpRequestMessage(new HttpMethod(method), "/_framework/missing-private-diagnostic-fixture.js");
        using var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Null(response.Headers.Location);
        Assert.True(response.Headers.CacheControl?.NoStore);
        Assert.True(response.Headers.CacheControl?.NoCache);
        Assert.NotEqual("text/html", response.Content.Headers.ContentType?.MediaType);
        Assert.DoesNotContain("<div id=\"app\">", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        Assert.False(response.Headers.Contains("X-Maliev-Health-Instance"));
        Assert.False(response.Headers.Contains("X-Maliev-Diagnostic-Id"));
        Assert.Empty(Incidents(host));
        Assert.Equal(0, host.FeedCount);
        Assert.Equal(0, host.AttemptedExternalCalls);
    }

    [Fact]
    public async Task Bff_nonFileClientRoute_RemainsAnonymousShellWithoutMarker()
    {
        await using var host = new PrivateDiagnosticHostFixture(true);
        using var client = host.CreateClient();
        using var response = await client.GetAsync("/migration-foundation");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Null(response.Headers.Location);
        Assert.Equal("text/html", response.Content.Headers.ContentType?.MediaType);
        var html = await response.Content.ReadAsStringAsync();
        Assert.Contains("<div id=\"app\">", html, StringComparison.Ordinal);
        Assert.Contains("_framework/blazor.webassembly.js", html, StringComparison.Ordinal);
        Assert.Contains("noindex,nofollow,noarchive", html, StringComparison.Ordinal);
        Assert.False(response.Headers.Contains("X-Maliev-Health-Instance"));
        Assert.False(response.Headers.Contains("X-Maliev-Diagnostic-Id"));
        Assert.Empty(Incidents(host));
        Assert.Equal(0, host.FeedCount);
        Assert.Equal(0, host.AttemptedExternalCalls);
    }

    [Fact]
    public async Task Bff_frameworkBootstrap_RetainsExisting200WithPassiveExceptionIdentity()
    {
        await using var host = new PrivateDiagnosticHostFixture(true, observeFrameworkFailure: true);
        using var client = host.CreateClient();
        using var response = await client.GetAsync("/_framework/blazor.webassembly.js");
        output.WriteLine("FrameworkStatusCode={0}; ExternalAttemptCount={1}; ExceptionIdentities={2}",
            (int)response.StatusCode, host.AttemptedExternalCalls, JsonSerializer.Serialize(host.FrameworkFailures));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Null(response.Headers.Location);
        Assert.NotEqual("text/html", response.Content.Headers.ContentType?.MediaType);
        Assert.False(response.Headers.Contains("X-Maliev-Health-Instance"));
        Assert.Equal(0, host.AttemptedExternalCalls);
    }

    private static void AssertDeniedHeaders(HttpResponseMessage response)
    {
        Assert.Equal("no-store", response.Headers.CacheControl?.ToString());
        Assert.True(response.Headers.Contains("X-Robots-Tag"));
        Assert.Equal("noindex", Assert.Single(response.Headers.GetValues("X-Robots-Tag")));
        Assert.False(response.Headers.Contains("X-Maliev-Diagnostic-Id"));
        Assert.False(response.Headers.Contains("X-Maliev-Health-Instance"));
    }

    private static async Task<HttpResponseMessage> SendProbe(HttpClient client)
    {
        using var request = PrivateDiagnosticHostFixture.Probe(Nonce);
        return await client.SendAsync(request);
    }

    private static DiagnosticRecord[] Incidents(PrivateDiagnosticHostFixture host) => host.Records.Snapshot
        .Where(record => record.Category.EndsWith("PrivateRequestObservation", StringComparison.Ordinal)
            || record.Category == "Microsoft.AspNetCore.Diagnostics.ExceptionHandlerMiddleware")
        .ToArray();

    private static async Task AssertEmpty(HttpResponseMessage response, HttpStatusCode status)
    {
        Assert.Equal(status, response.StatusCode);
        Assert.Null(response.Headers.Location);
        Assert.Empty(await response.Content.ReadAsByteArrayAsync());
    }
}
