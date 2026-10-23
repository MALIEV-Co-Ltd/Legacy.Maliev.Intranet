extern alias Bff;

using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using Legacy.Maliev.Intranet.Auth;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.StackExchangeRedis;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using StackExchange.Redis;
using Testcontainers.Redis;
using BffProgram = Bff::Program;

namespace Legacy.Maliev.Intranet.Tests.Integration;

/// <summary>Real Redis/crypto/cookie boundaries and two-provider races; only external primary HTTP transports are controlled.</summary>
public sealed class SessionGenerationFenceTests : IAsyncLifetime
{
    private readonly RedisContainer redis = ValidationContainers.Redis();
    public Task InitializeAsync() => redis.StartAsync();
    public Task DisposeAsync() => redis.DisposeAsync().AsTask();

    [Fact]
    public async Task FailedRefresh_StaleReconciliationRead_CannotDeletePeerRenewal()
    {
        using var scenario = new Scenario();
        await using var loser = new Factory(redis.GetConnectionString(), scenario, gateRead: 2);
        await using var peer = new Factory(redis.GetConnectionString(), scenario);
        using var browser = Client(loser);
        await LoginAsync(browser, loser);
        var key = loser.Probe.LastKey!;
        var peerContext = new DefaultHttpContext { RequestServices = peer.Services };
        var peerStore = peer.Services.GetRequiredService<DistributedTicketStore>();
        var ticket = await peerStore.RetrieveAsync(key, peerContext, default);
        Assert.NotNull(ticket);
        scenario.Clock.Advance(TimeSpan.FromMinutes(14));
        var request = browser.GetAsync("/bff/quotation-requests/84/qualification-receipt");
        await loser.Probe.ReadCaptured.Task.WaitAsync(TimeSpan.FromSeconds(15));
        try
        {
            SetRotatedTokens(ticket, scenario);
            await peerStore.RenewAsync(key, ticket, peerContext, default);
        }
        finally { loser.Probe.ReleaseRead.TrySetResult(); }
        using var response = await request;
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        var retained = await peerStore.RetrieveAsync(key);
        Assert.NotNull(retained);
        Assert.True(retained.Properties.GetTokenValue("legacy_refresh_token") == "synthetic-rotation-2",
            "Implicit failed-refresh teardown must not delete a newer distributed generation.");
        Assert.Equal(0, scenario.Forwarded);
        Assert.False(response.Headers.TryGetValues("Set-Cookie", out var cookies) && cookies.Any(cookie =>
            cookie.StartsWith("__Host-Legacy.Maliev.Intranet.Bff=;", StringComparison.Ordinal)),
            "A stale implicit teardown must not expire the same-key peer's browser cookie.");
    }

    [Fact]
    public async Task ExplicitLogout_WinsAgainstLateSuccessfulCookieRenewal_AndPreventsForwarding()
    {
        using var scenario = new Scenario { BlockSuccessfulRefresh = true };
        await using var late = new Factory(redis.GetConnectionString(), scenario);
        await using var logout = new Factory(redis.GetConnectionString(), scenario);
        using var browser = Client(late);
        var login = await LoginAsync(browser, late);
        using var peerBrowser = Client(logout);
        peerBrowser.DefaultRequestHeaders.Add("Cookie", login.Cookie);
        using var peerSession = await peerBrowser.GetAsync("/bff/session");
        using var peerBody = JsonDocument.Parse(await peerSession.Content.ReadAsStringAsync());
        var logoutCsrf = peerBody.RootElement.GetProperty("csrfToken").GetString()!;
        var key = late.Probe.LastKey!;
        scenario.Clock.Advance(TimeSpan.FromMinutes(14));
        var pending = browser.GetAsync("/bff/quotation-requests/84/qualification-receipt");
        await scenario.RefreshEntered.Task.WaitAsync(TimeSpan.FromSeconds(15));
        try
        {
            using var revoke = new HttpRequestMessage(HttpMethod.Post, "/bff/logout");
            revoke.Headers.Add("X-CSRF-TOKEN", logoutCsrf);
            using var response = await peerBrowser.SendAsync(revoke);
            Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        }
        finally { scenario.ReleaseRefresh.TrySetResult(); }
        using var completed = await pending;
        var stored = await logout.Services.GetRequiredService<DistributedTicketStore>().RetrieveAsync(key);
        Assert.Equal((SessionPresent: false, Forwarded: 0), (SessionPresent: stored is not null, Forwarded: scenario.Forwarded));
        Assert.NotEqual(HttpStatusCode.OK, completed.StatusCode);
    }

    [Fact]
    public async Task LateSuccessfulRefresh_CannotOverwritePeerGeneration_OrForwardUncommittedToken()
    {
        using var scenario = new Scenario { BlockSuccessfulRefresh = true };
        await using var late = new Factory(redis.GetConnectionString(), scenario);
        await using var peer = new Factory(redis.GetConnectionString(), scenario);
        using var browser = Client(late);
        await LoginAsync(browser, late);
        var key = late.Probe.LastKey!;
        var peerStore = peer.Services.GetRequiredService<DistributedTicketStore>();
        var peerContext = new DefaultHttpContext { RequestServices = peer.Services };
        var ticket = await peerStore.RetrieveAsync(key, peerContext, default);
        Assert.NotNull(ticket);
        scenario.Clock.Advance(TimeSpan.FromMinutes(14));
        var pending = browser.GetAsync("/bff/quotation-requests/84/qualification-receipt");
        await scenario.RefreshEntered.Task.WaitAsync(TimeSpan.FromSeconds(15));
        try
        {
            scenario.PeerAccessToken = scenario.Token();
            ticket.Properties.StoreTokens(
            [
                new() { Name = "legacy_access_token", Value = scenario.PeerAccessToken },
                new() { Name = "legacy_refresh_token", Value = "synthetic-peer-rotation-3" },
                new() { Name = "legacy_access_expires_at", Value = scenario.Clock.GetUtcNow().AddMinutes(15).ToString("O") },
            ]);
            await peerStore.RenewAsync(key, ticket, peerContext, default);
        }
        finally { scenario.ReleaseRefresh.TrySetResult(); }
        using var response = await pending;
        var retained = await peerStore.RetrieveAsync(key);
        Assert.NotNull(retained);
        Assert.Equal((PeerGeneration: true, UncommittedForwarded: 0),
            (PeerGeneration: retained.Properties.GetTokenValue("legacy_refresh_token") == "synthetic-peer-rotation-3", UncommittedForwarded: scenario.UncommittedForwarded));
    }

    [Fact]
    public async Task CorruptionCleanup_CannotRemoveValidCiphertextWrittenAfterItsRead()
    {
        using var scenario = new Scenario();
        await using var first = new Factory(redis.GetConnectionString(), scenario, gateRead: 1);
        await using var peer = new Factory(redis.GetConnectionString(), scenario);
        var store = first.Services.GetRequiredService<DistributedTicketStore>();
        var key = await store.StoreAsync(Ticket(scenario));
        var cache = peer.Services.GetRequiredService<IDistributedCache>();
        var valid = await cache.GetAsync(key);
        Assert.NotNull(valid);
        await cache.SetAsync(key, new byte[] { 1, 2, 3 }, new() { AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(1) });
        var read = store.RetrieveAsync(key);
        await first.Probe.ReadCaptured.Task.WaitAsync(TimeSpan.FromSeconds(15));
        try { await cache.SetAsync(key, valid, new() { AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(1) }); }
        finally { first.Probe.ReleaseRead.TrySetResult(); }
        Assert.Null(await read);
        Assert.NotNull(await peer.Services.GetRequiredService<DistributedTicketStore>().RetrieveAsync(key));
    }

    [Fact]
    public async Task RedisAdapter_UsesPinnedPrefixedHash_WithEncryptedDataAndAbsoluteTtl()
    {
        using var scenario = new Scenario();
        await using var factory = new Factory(redis.GetConnectionString(), scenario);
        var key = await factory.Services.GetRequiredService<DistributedTicketStore>().StoreAsync(Ticket(scenario));
        var db = factory.Services.GetRequiredService<LegacyDataProtectionResources>().Redis.GetDatabase();
        var physicalKey = "legacy-intranet:" + key;
        Assert.Equal(RedisType.Hash, await db.KeyTypeAsync(physicalKey));
        Assert.False((await db.HashGetAsync(physicalKey, "data")).IsNull);
        Assert.Equal(-1, (long)await db.HashGetAsync(physicalKey, "sldexp"));
        Assert.True((long)await db.HashGetAsync(physicalKey, "absexp") > DateTimeOffset.UtcNow.Ticks);
        var ttl = await db.KeyTimeToLiveAsync(physicalKey);
        Assert.True(ttl > TimeSpan.Zero && ttl <= TimeSpan.FromHours(8));
        Assert.NotNull(await factory.Services.GetRequiredService<DistributedTicketStore>().RetrieveAsync(key));
    }

    [Fact]
    public async Task CallerAbort_DoesNotDeleteOrRenewExistingSession()
    {
        using var scenario = new Scenario();
        await using var factory = new Factory(redis.GetConnectionString(), scenario);
        var store = factory.Services.GetRequiredService<DistributedTicketStore>();
        var key = await store.StoreAsync(Ticket(scenario));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => store.RetrieveAsync(key, new DefaultHttpContext(), cancellation.Token));
        Assert.NotNull(await store.RetrieveAsync(key));
    }

    [Fact]
    public async Task ReconciliationCacheFault_PreservesSession_AndDoesNotForwardCredentials()
    {
        using var scenario = new Scenario();
        await using var factory = new Factory(redis.GetConnectionString(), scenario, failRead: 2);
        using var browser = Client(factory);
        await LoginAsync(browser, factory);
        var key = factory.Probe.LastKey!;
        scenario.Clock.Advance(TimeSpan.FromMinutes(14));
        using var response = await browser.GetAsync("/bff/quotation-requests/84/qualification-receipt");
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal(0, scenario.Forwarded);
        Assert.NotNull(await factory.Services.GetRequiredService<DistributedTicketStore>().RetrieveAsync(key));
    }

    [Fact]
    public async Task ExpiredCookie_DoesNotForwardCredentials_AndRemovesObservedSession()
    {
        using var scenario = new Scenario();
        await using var factory = new Factory(redis.GetConnectionString(), scenario);
        using var browser = Client(factory);
        await LoginAsync(browser, factory);
        var key = factory.Probe.LastKey!;
        scenario.Clock.Advance(TimeSpan.FromHours(9));
        using var response = await browser.GetAsync("/bff/quotation-requests/84/qualification-receipt");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(0, scenario.Forwarded);
        Assert.Null(await factory.Services.GetRequiredService<DistributedTicketStore>().RetrieveAsync(key));
    }

    [Fact]
    public async Task ChangedRefreshOwner_CannotBecomeForwardedEmployee()
    {
        using var scenario = new Scenario { WrongRefreshOwner = true };
        await using var factory = new Factory(redis.GetConnectionString(), scenario);
        using var browser = Client(factory);
        await LoginAsync(browser, factory);
        scenario.Clock.Advance(TimeSpan.FromMinutes(14));
        using var response = await browser.GetAsync("/bff/quotation-requests/84/qualification-receipt");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(0, scenario.Forwarded);
    }

    [Fact]
    public async Task HttpCallerAbort_DuringRefresh_PreservesSession_AndDoesNotForwardCredentials()
    {
        using var scenario = new Scenario { BlockSuccessfulRefresh = true };
        await using var factory = new Factory(redis.GetConnectionString(), scenario);
        using var browser = Client(factory);
        await LoginAsync(browser, factory);
        var key = factory.Probe.LastKey!;
        scenario.Clock.Advance(TimeSpan.FromMinutes(14));
        using var cancellation = new CancellationTokenSource();
        var pending = browser.GetAsync("/bff/quotation-requests/84/qualification-receipt", cancellation.Token);
        await scenario.RefreshEntered.Task.WaitAsync(TimeSpan.FromSeconds(15));
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
        Assert.Equal(0, scenario.Forwarded);
        Assert.NotNull(await factory.Services.GetRequiredService<DistributedTicketStore>().RetrieveAsync(key));
    }

    private static AuthenticationTicket Ticket(Scenario scenario) => new(
        new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "synthetic-employee")], "Cookies")),
        new AuthenticationProperties { ExpiresUtc = scenario.Clock.GetUtcNow().AddHours(8) }, "Cookies");

    [Theory]
    [InlineData("before", false, HttpStatusCode.ServiceUnavailable, false, 0)]
    [InlineData("after", false, HttpStatusCode.Forbidden, true, 1)]
    [InlineData("after", true, HttpStatusCode.ServiceUnavailable, true, 0)]
    [InlineData("canceled-after", false, HttpStatusCode.Forbidden, true, 1)]
    public async Task UnknownMutationOutcome_UsesOnlyReobservedCommittedState(
        string fault, bool failReobservation, HttpStatusCode expected, bool committed, int forwarded)
    {
        using var scenario = new Scenario { BlockSuccessfulRefresh = true };
        await using var factory = new Factory(redis.GetConnectionString(), scenario, failRead: failReobservation ? 2 : 0, mutationFault: fault);
        using var browser = Client(factory);
        await LoginAsync(browser, factory);
        var key = factory.Probe.LastKey!;
        scenario.Clock.Advance(TimeSpan.FromMinutes(14));
        var pending = browser.GetAsync("/bff/quotation-requests/84/qualification-receipt");
        await scenario.RefreshEntered.Task.WaitAsync(TimeSpan.FromSeconds(15));
        scenario.ReleaseRefresh.TrySetResult();
        using var response = await pending;
        Assert.Equal(expected, response.StatusCode);
        Assert.Equal(forwarded, scenario.Forwarded);
        var retained = await factory.Services.GetRequiredService<DistributedTicketStore>().RetrieveAsync(key);
        Assert.NotNull(retained);
        Assert.Equal(committed, retained.Properties.GetTokenValue("legacy_refresh_token") == "synthetic-rotation-2");
        Assert.True(factory.Probe.ReadCount >= 3, "A provider exception must cause a real committed-state read, not assumed success.");
    }

    [Fact]
    public async Task BothProductionHosts_SelectAtomicRedisProvider_AndTheirNormalCookieStore()
    {
        using var scenario = new Scenario();
        await using var bff = new Factory(redis.GetConnectionString(), scenario);
        await using var compatibility = new CompatibilityFactory(redis.GetConnectionString(), scenario);
        foreach (var services in new[] { bff.Services, compatibility.Services })
        {
            Assert.IsType<RedisSessionTicketPersistence>(services.GetRequiredService<ISessionTicketPersistence>());
            Assert.Same(services.GetRequiredService<DistributedTicketStore>(),
                services.GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>().Get("Cookies").SessionStore);
            Assert.NotNull(services.GetRequiredService<LegacyDataProtectionResources>());
        }
    }

    [Fact]
    public async Task UnobservedRenewal_CannotOverwriteExistingCiphertext()
    {
        using var scenario = new Scenario();
        await using var factory = new Factory(redis.GetConnectionString(), scenario);
        var store = factory.Services.GetRequiredService<DistributedTicketStore>();
        var key = await store.StoreAsync(Ticket(scenario));
        var db = factory.Services.GetRequiredService<LegacyDataProtectionResources>().Redis.GetDatabase();
        var before = await db.HashGetAsync("legacy-intranet:" + key, "data");
        var failure = await Record.ExceptionAsync(() => store.RenewAsync(key, Ticket(scenario), new DefaultHttpContext(), default));
        Assert.NotNull(failure);
        Assert.True(before == await db.HashGetAsync("legacy-intranet:" + key, "data"));
    }

    [Theory]
    [InlineData("absexp", "malformed")]
    [InlineData("absexp", "-1")]
    [InlineData("sldexp", "0")]
    public async Task MalformedMetadata_CannotAuthorizeObservedRenewal(string field, string value)
    {
        using var scenario = new Scenario();
        await using var factory = new Factory(redis.GetConnectionString(), scenario);
        var store = factory.Services.GetRequiredService<DistributedTicketStore>();
        var key = await store.StoreAsync(Ticket(scenario));
        var context = new DefaultHttpContext { RequestServices = factory.Services };
        var ticket = await store.RetrieveAsync(key, context, default);
        Assert.NotNull(ticket);
        var db = factory.Services.GetRequiredService<LegacyDataProtectionResources>().Redis.GetDatabase();
        var physical = "legacy-intranet:" + key;
        var before = await db.HashGetAsync(physical, "data");
        await db.HashSetAsync(physical, field, value);
        var failure = await Record.ExceptionAsync(() => store.RenewAsync(key, ticket, context, default));
        Assert.NotNull(failure);
        Assert.True(before == await db.HashGetAsync(physical, "data"));
    }

    [Fact]
    public async Task Renewal_CannotExtendOriginalAbsoluteExpiry()
    {
        using var scenario = new Scenario();
        await using var factory = new Factory(redis.GetConnectionString(), scenario);
        var store = factory.Services.GetRequiredService<DistributedTicketStore>();
        var key = await store.StoreAsync(Ticket(scenario));
        var context = new DefaultHttpContext { RequestServices = factory.Services };
        var ticket = await store.RetrieveAsync(key, context, default);
        Assert.NotNull(ticket);
        var db = factory.Services.GetRequiredService<LegacyDataProtectionResources>().Redis.GetDatabase();
        var physical = "legacy-intranet:" + key;
        var beforeExpiry = await db.HashGetAsync(physical, "absexp");
        ticket.Properties.ExpiresUtc = ticket.Properties.ExpiresUtc!.Value.AddHours(1);
        var failure = await Record.ExceptionAsync(() => store.RenewAsync(key, ticket, context, default));
        Assert.NotNull(failure);
        Assert.True(beforeExpiry == await db.HashGetAsync(physical, "absexp"));
        Assert.True(await db.KeyTimeToLiveAsync(physical) <= TimeSpan.FromHours(8));
    }

    private static void SetRotatedTokens(AuthenticationTicket ticket, Scenario scenario) => ticket.Properties.StoreTokens(
    [
        new() { Name = "legacy_access_token", Value = scenario.Token() },
        new() { Name = "legacy_refresh_token", Value = "synthetic-rotation-2" },
        new() { Name = "legacy_access_expires_at", Value = scenario.Clock.GetUtcNow().AddMinutes(15).ToString("O") },
    ]);


    // Source #307 regressions use the existing production cookie/JWT/Redis boundary.
    // Only external Auth and aggregate-producer HTTP transports are synthetic.
    [Theory]
    [InlineData("/Operations/Check")]
    [InlineData("/Operations/Check/")]
    [InlineData("/operations/check")]
    public async Task OutcomeBrowser_AnonymousCannotReachTheWasmFallbackOrProducer(string path)
    {
        using var scenario = new Scenario();
        await using var factory = new Factory(redis.GetConnectionString(), scenario);
        using var browser = Client(factory);
        using var response = await browser.GetAsync(OutcomeRoute("qualification", path));
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.StartsWith("https://localhost/Login?", response.Headers.Location!.AbsoluteUri, StringComparison.Ordinal);
        Assert.Equal(0, scenario.OutcomeReads);
        Assert.Equal(0, scenario.LoginCalls);
    }

    [Theory]
    [InlineData("quotation", "/Operations/Check")]
    [InlineData("invoice", "/Operations/Check")]
    [InlineData("qualification", "/Operations/Check")]
    [InlineData("quotation", "/Operations/Check/")]
    [InlineData("invoice", "/Operations/Check/")]
    [InlineData("qualification", "/operations/check")]
    public async Task OutcomeBrowser_RealOpaqueSessionReturnsEncodedStaticReceipt(string source, string path)
    {
        using var scenario = OutcomeScenario(source);
        await using var factory = new Factory(redis.GetConnectionString(), scenario);
        using var browser = Client(factory);
        await LoginAsync(browser, factory);
        using var response = await browser.GetAsync(OutcomeRoute(source, path));
        using var receipt = await BrowserReceiptAsync(response);
        Assert.Equal(source, receipt.RootElement.GetProperty("source").GetString());
        Assert.Equal(200, receipt.RootElement.GetProperty("httpStatus").GetInt32());
        Assert.Equal(1, scenario.LoginCalls);
        Assert.Equal(0, scenario.RefreshCalls);
        Assert.Equal(1, scenario.OutcomeReads);
        Assert.Equal(OutcomeProducerPath(source), scenario.LastOutcomePath);
        Assert.Equal(scenario.LastIssuedAccessToken, scenario.LastOutcomeAccessToken);
        Assert.Contains("fromUtc=2026-08-25T17", scenario.LastOutcomeQuery, StringComparison.Ordinal);
        Assert.Contains("toUtc=2026-09-01T17", scenario.LastOutcomeQuery, StringComparison.Ordinal);
        Assert.Equal("Bearer", scenario.LastOutcomeScheme);
        var payload = receipt.RootElement.GetProperty("payload");
        Assert.True(payload.ValueKind == JsonValueKind.Object);
        if (source != "qualification")
        {
            using var original = JsonDocument.Parse(scenario.OutcomeBody!);
            Assert.True(JsonElement.DeepEquals(original.RootElement, payload));
            Assert.True(payload.TryGetProperty("Days", out _));
            Assert.False(payload.TryGetProperty("days", out _));
        }

        // The exact receipt-only CSP accommodation must not replace the shell policy.
        using var shell = await browser.GetAsync("/migration-foundation");
        Assert.Contains("https://accounts.google.com", Assert.Single(shell.Headers.GetValues("Content-Security-Policy")), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("/Operations/OutcomeReadback")]
    [InlineData("/Analytics/OutcomeReadback")]
    public async Task OutcomeBrowser_SummaryNeverChangesTheAllowedMachineQualificationPayload(string apiPath)
    {
        using var scenario = OutcomeScenario("qualification");
        await using var factory = new Factory(redis.GetConnectionString(), scenario);
        using var browser = Client(factory);
        await LoginAsync(browser, factory);
        using var machineResponse = await browser.GetAsync(OutcomeRoute("qualification", apiPath));
        Assert.Equal(HttpStatusCode.OK, machineResponse.StatusCode);
        Assert.Equal("application/json", machineResponse.Content.Headers.ContentType!.MediaType);
        using var machine = JsonDocument.Parse(await machineResponse.Content.ReadAsStringAsync());
        using var original = JsonDocument.Parse(scenario.OutcomeBody!);
        Assert.Equal(original.RootElement.GetRawText(), machine.RootElement.GetProperty("payload").GetRawText());
        Assert.Equal(8, machine.RootElement.GetProperty("payload").GetProperty("requests").GetArrayLength());

        using var response = await browser.GetAsync(OutcomeRoute("qualification"));
        using var receipt = await BrowserReceiptAsync(response);
        var payload = receipt.RootElement.GetProperty("payload");
        Assert.Equal("current_employee_projection", payload.GetProperty("qualificationBasis").GetString());
        Assert.Equal(JsonValueKind.Null, payload.GetProperty("historicalAsOfUtc").ValueKind);
        Assert.Equal(JsonValueKind.Null, payload.GetProperty("qualifiedCustomerCount").ValueKind);
        var counts = payload.GetProperty("counts");
        Assert.Equal(8, counts.GetProperty("persistedRequestCount").GetInt32());
        Assert.Equal(4, counts.GetProperty("identityCompleteRequestCount").GetInt32());
        Assert.Equal(2, counts.GetProperty("employeeQualifiedRequestCount").GetInt32());
        Assert.Equal(1, counts.GetProperty("qualifiedWithCompleteIdentityRequestCount").GetInt32());
        var states = payload.GetProperty("states");
        Assert.Equal(6, states.EnumerateObject().Count());
        Assert.Equal(2, states.GetProperty("qualified").GetInt32());
        Assert.Equal(2, states.GetProperty("unreviewed").GetInt32());
        foreach (var state in new[] { "not_qualified", "duplicate", "stale", "incomplete" })
            Assert.Equal(1, states.GetProperty(state).GetInt32());
        Assert.Equal(8, states.EnumerateObject().Sum(state => state.Value.GetInt32()));
        foreach (var label in new[] { "adAttributionAvailability", "staffTestExclusionAvailability", "matureDelayAvailability" })
            Assert.Equal("unavailable", payload.GetProperty(label).GetString());
        foreach (var forbidden in new[] { "requestId", "transactionId", "journeyId", "requests", "request-41", "11111111-2222-3333-4444-555555555555", "2026-08-26T09:00:00Z" })
            Assert.DoesNotContain(forbidden, receipt.RootElement.GetRawText(), StringComparison.Ordinal);
        Assert.Equal(2, scenario.OutcomeReads);
    }

    [Fact]
    public async Task OutcomeBrowser_ExplicitEmptyRemainsAvailableZeroNotUnknownCustomers()
    {
        using var scenario = OutcomeScenario("qualification");
        scenario.OutcomeBody = """{"fromUtc":"2026-08-25T17:00:00Z","toUtc":"2026-09-01T17:00:00Z","requests":[]}""";
        await using var factory = new Factory(redis.GetConnectionString(), scenario);
        using var browser = Client(factory);
        await LoginAsync(browser, factory);
        using var response = await browser.GetAsync(OutcomeRoute("qualification"));
        using var receipt = await BrowserReceiptAsync(response);
        Assert.Equal(200, receipt.RootElement.GetProperty("httpStatus").GetInt32());
        var payload = receipt.RootElement.GetProperty("payload");
        Assert.All(payload.GetProperty("counts").EnumerateObject(), count => Assert.Equal(0, count.Value.GetInt32()));
        Assert.Equal(6, payload.GetProperty("states").EnumerateObject().Count());
        Assert.All(payload.GetProperty("states").EnumerateObject(), state => Assert.Equal(0, state.Value.GetInt32()));
        Assert.Equal(JsonValueKind.Null, payload.GetProperty("qualifiedCustomerCount").ValueKind);
    }

    [Theory]
    [InlineData("unknown-root")]
    [InlineData("unknown-row")]
    [InlineData("duplicate")]
    [InlineData("invalid-identity")]
    [InlineData("scalar-row")]
    [InlineData("invalid-json")]
    public async Task OutcomeBrowser_RejectsUnsafeProducerBeforeQualificationProjection(string defect)
    {
        using var scenario = OutcomeScenario("qualification");
        scenario.OutcomeBody = defect switch
        {
            "unknown-root" => scenario.OutcomeBody!.Replace("\"requests\":", "\"private\":\"private-producer-marker\",\"requests\":", StringComparison.Ordinal),
            "unknown-row" => scenario.OutcomeBody!.Replace("\"requestId\":41", "\"private\":\"private-producer-marker\",\"requestId\":41", StringComparison.Ordinal),
            "duplicate" => scenario.OutcomeBody!.Replace("\"requestId\":41", "\"requestId\":41,\"requestId\":41", StringComparison.Ordinal),
            "invalid-identity" => scenario.OutcomeBody!.Replace("request-41", "request-999", StringComparison.Ordinal),
            "scalar-row" => """{"fromUtc":"2026-08-25T17:00:00Z","toUtc":"2026-09-01T17:00:00Z","requests":["private-producer-marker"]}""",
            _ => "<script>private-producer-marker</script>",
        };
        await using var factory = new Factory(redis.GetConnectionString(), scenario);
        using var browser = Client(factory);
        await LoginAsync(browser, factory);
        using var response = await browser.GetAsync(OutcomeRoute("qualification"));
        using var receipt = await BrowserReceiptAsync(response);
        Assert.Equal(502, receipt.RootElement.GetProperty("httpStatus").GetInt32());
        Assert.Equal(JsonValueKind.Null, receipt.RootElement.GetProperty("payload").ValueKind);
        Assert.DoesNotContain("private-producer-marker", receipt.RootElement.GetRawText(), StringComparison.Ordinal);
        Assert.Equal(1, scenario.OutcomeReads);
    }

    [Theory]
    [InlineData(401)]
    [InlineData(403)]
    [InlineData(500)]
    public async Task OutcomeBrowser_ProducerDenialNeverLeaksBodyOrRetries(int status)
    {
        using var scenario = OutcomeScenario("qualification");
        scenario.OutcomeStatus = (HttpStatusCode)status;
        scenario.OutcomeBody = "<script>private-producer-marker</script>";
        await using var factory = new Factory(redis.GetConnectionString(), scenario);
        using var browser = Client(factory);
        await LoginAsync(browser, factory);
        using var response = await browser.GetAsync(OutcomeRoute("qualification"));
        using var receipt = await BrowserReceiptAsync(response);
        Assert.Equal(status, receipt.RootElement.GetProperty("httpStatus").GetInt32());
        Assert.Equal(JsonValueKind.Null, receipt.RootElement.GetProperty("payload").ValueKind);
        Assert.DoesNotContain("private-producer-marker", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        Assert.Equal(1, scenario.OutcomeReads);
        Assert.Equal(0, scenario.RefreshCalls);
    }

    [Theory]
    [InlineData("transport", 502)]
    [InlineData("io", 502)]
    [InlineData("timeout", 504)]
    public async Task OutcomeBrowser_TransportFailuresRemainStatusOnly(string fault, int expected)
    {
        using var scenario = OutcomeScenario("qualification");
        scenario.OutcomeFault = fault;
        await using var factory = new Factory(redis.GetConnectionString(), scenario);
        using var browser = Client(factory);
        await LoginAsync(browser, factory);
        using var response = await browser.GetAsync(OutcomeRoute("qualification"));
        using var receipt = await BrowserReceiptAsync(response);
        Assert.Equal(expected, receipt.RootElement.GetProperty("httpStatus").GetInt32());
        Assert.Equal(JsonValueKind.Null, receipt.RootElement.GetProperty("payload").ValueKind);
        Assert.DoesNotContain("private-producer-marker", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        Assert.Equal(1, scenario.OutcomeReads);
    }

    [Theory]
    [InlineData("quotation")]
    [InlineData("invoice")]
    [InlineData("qualification")]
    public async Task OutcomeBrowser_ExistingSourcePermissionGatesBeforeProducerRead(string source)
    {
        using var scenario = OutcomeScenario(source);
        scenario.OutcomePermissions = [];
        await using var factory = new Factory(redis.GetConnectionString(), scenario);
        using var browser = Client(factory);
        await LoginAsync(browser, factory);
        using var response = await browser.GetAsync(OutcomeRoute(source));
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.StartsWith("https://localhost/AccessDenied?", response.Headers.Location!.AbsoluteUri, StringComparison.Ordinal);
        Assert.Equal(0, scenario.OutcomeReads);
        Assert.Equal(0, scenario.RefreshCalls);
    }

    [Theory]
    [InlineData("source")]
    [InlineData("reversed")]
    [InlineData("missing-zone")]
    [InlineData("too-wide")]
    [InlineData("future")]
    public async Task OutcomeBrowser_InvalidWindowOrDestinationCannotReachProducer(string defect)
    {
        using var scenario = OutcomeScenario("qualification");
        await using var factory = new Factory(redis.GetConnectionString(), scenario);
        using var browser = Client(factory);
        await LoginAsync(browser, factory);
        var (source, from, to) = defect switch
        {
            "source" => ("https://other.invalid", OutcomeFrom, OutcomeTo),
            "reversed" => ("qualification", OutcomeTo, OutcomeFrom),
            "missing-zone" => ("qualification", "2026-08-25T17:00:00", OutcomeTo),
            "too-wide" => ("qualification", "2026-07-01T00:00:00Z", OutcomeTo),
            _ => ("qualification", OutcomeFrom, scenario.Clock.GetUtcNow().AddDays(1).UtcDateTime.ToString("O")),
        };
        using var response = await browser.GetAsync($"/Operations/Check?source={Uri.EscapeDataString(source)}&fromUtc={Uri.EscapeDataString(from)}&toUtc={Uri.EscapeDataString(to)}");
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(0, scenario.OutcomeReads);
        Assert.Equal(0, scenario.RefreshCalls);
    }

    [Fact]
    public async Task OutcomeBrowser_TwoIndependentCookieSessionsNeedNoDashboardBootstrap()
    {
        using var scenario = OutcomeScenario("qualification");
        await using var factory = new Factory(redis.GetConnectionString(), scenario);
        using var first = Client(factory);
        using var second = Client(factory);
        var firstLogin = await LoginAsync(first, factory);
        var secondLogin = await LoginAsync(second, factory);
        Assert.NotEqual(firstLogin.Cookie, secondLogin.Cookie);
        foreach (var browser in new[] { first, second })
        {
            using var response = await browser.GetAsync(OutcomeRoute("qualification"));
            using var receipt = await BrowserReceiptAsync(response);
            Assert.Equal(200, receipt.RootElement.GetProperty("httpStatus").GetInt32());
        }
        Assert.Equal(2, scenario.LoginCalls);
        Assert.Equal(2, scenario.OutcomeReads);
        Assert.Equal(0, scenario.RefreshCalls);
    }

    [Fact]
    public async Task OutcomeBrowser_NearExpiryReusesCommittedSessionRefresh()
    {
        using var scenario = OutcomeScenario("qualification");
        scenario.SuccessfulOutcomeRefresh = true;
        await using var factory = new Factory(redis.GetConnectionString(), scenario);
        using var browser = Client(factory);
        await LoginAsync(browser, factory);
        var before = scenario.LastIssuedAccessToken;
        scenario.Clock.Advance(TimeSpan.FromMinutes(14));
        using var response = await browser.GetAsync(OutcomeRoute("qualification"));
        using var receipt = await BrowserReceiptAsync(response);
        Assert.Equal(200, receipt.RootElement.GetProperty("httpStatus").GetInt32());
        Assert.Equal(1, scenario.RefreshCalls);
        Assert.Equal(1, scenario.OutcomeReads);
        Assert.NotEqual(before, scenario.LastOutcomeAccessToken);
        Assert.Equal(scenario.LastIssuedAccessToken, scenario.LastOutcomeAccessToken);
        var retained = await factory.Services.GetRequiredService<DistributedTicketStore>().RetrieveAsync(factory.Probe.LastKey!);
        Assert.NotNull(retained);
        Assert.Equal("synthetic-rotation-2", retained.Properties.GetTokenValue("legacy_refresh_token"));
    }

    [Theory]
    [InlineData(401)]
    [InlineData(429)]
    [InlineData(503)]
    public async Task OutcomeBrowser_UnavailableRefreshCannotForwardOrLeakCredentials(int status)
    {
        using var scenario = OutcomeScenario("qualification");
        scenario.OutcomeRefreshStatus = (HttpStatusCode)status;
        await using var factory = new Factory(redis.GetConnectionString(), scenario);
        using var browser = Client(factory);
        await LoginAsync(browser, factory);
        scenario.Clock.Advance(TimeSpan.FromMinutes(14));
        using var response = await browser.GetAsync(OutcomeRoute("qualification"));
        using var receipt = await BrowserReceiptAsync(response);
        Assert.Equal(401, receipt.RootElement.GetProperty("httpStatus").GetInt32());
        Assert.Equal(JsonValueKind.Null, receipt.RootElement.GetProperty("payload").ValueKind);
        Assert.True(scenario.RefreshCalls > 0);
        Assert.Equal(0, scenario.OutcomeReads);
        if (status != 401)
            Assert.NotNull(await factory.Services.GetRequiredService<DistributedTicketStore>().RetrieveAsync(factory.Probe.LastKey!));
    }

    [Fact]
    public async Task OutcomeBrowser_ExpiredCookieCannotUseAnOtherwiseStoredAccessToken()
    {
        using var scenario = OutcomeScenario("qualification");
        await using var factory = new Factory(redis.GetConnectionString(), scenario);
        using var browser = Client(factory);
        await LoginAsync(browser, factory);
        var key = factory.Probe.LastKey!;
        scenario.Clock.Advance(TimeSpan.FromHours(9));
        using var response = await browser.GetAsync(OutcomeRoute("qualification"));
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.StartsWith("https://localhost/Login?", response.Headers.Location!.AbsoluteUri, StringComparison.Ordinal);
        Assert.Equal(0, scenario.OutcomeReads);
        Assert.Equal(0, scenario.RefreshCalls);
        Assert.Null(await factory.Services.GetRequiredService<DistributedTicketStore>().RetrieveAsync(key));
    }

    [Fact]
    public async Task OutcomeBrowser_ChangedRefreshOwnerCannotBecomeTheForwardedEmployee()
    {
        using var scenario = OutcomeScenario("qualification", wrongRefreshOwner: true);
        await using var factory = new Factory(redis.GetConnectionString(), scenario);
        using var browser = Client(factory);
        await LoginAsync(browser, factory);
        scenario.Clock.Advance(TimeSpan.FromMinutes(14));
        using var response = await browser.GetAsync(OutcomeRoute("qualification"));
        using var receipt = await BrowserReceiptAsync(response);
        Assert.Equal(401, receipt.RootElement.GetProperty("httpStatus").GetInt32());
        Assert.Equal(JsonValueKind.Null, receipt.RootElement.GetProperty("payload").ValueKind);
        Assert.Equal(0, scenario.OutcomeReads);
        Assert.Equal(1, scenario.RefreshCalls);
    }

    private const string OutcomeFrom = "2026-08-25T17:00:00Z";
    private const string OutcomeTo = "2026-09-01T17:00:00Z";

    private static string OutcomeRoute(string source, string path = "/Operations/Check") =>
        $"{path}?source={source}&fromUtc={Uri.EscapeDataString(OutcomeFrom)}&toUtc={Uri.EscapeDataString(OutcomeTo)}";

    private static string OutcomeProducerPath(string source) => source switch
    {
        "quotation" => "/quotations/outcomes/readback",
        "invoice" => "/invoices/outcomes/readback",
        _ => "/quotationrequests/qualification-outcomes/readback",
    };

    private static Scenario OutcomeScenario(string source, bool wrongRefreshOwner = false) => new()
    {
        WrongRefreshOwner = wrongRefreshOwner,
        OutcomePermissions = source switch
        {
            "quotation" => [LegacyEmployeePermissions.QuotationsRead],
            "invoice" => [LegacyEmployeePermissions.AccountingRead],
            _ => [LegacyEmployeePermissions.QuotationRequestsRead],
        },
        OutcomeBody = OutcomeFixture(source),
        OutcomePath = OutcomeProducerPath(source),
    };

    private static string OutcomeFixture(string source) => source switch
    {
        "quotation" => """{"FromUtc":"2026-08-25T17:00:00Z","ToUtc":"2026-09-01T17:00:00Z","Days":[{"DayUtc":"2026-08-26T00:00:00","PersistedQuotationCount":3,"AcceptedQuotationCount":2,"SourceAttributedPersistedQuotationCount":1,"SourceAttributedAcceptedQuotationCount":1,"UnattributedPersistedQuotationCount":2,"UnattributedAcceptedQuotationCount":1,"AcceptedQuotedAmountsByCurrency":[{"CurrencyId":1,"QuotedAmount":123.4500,"AcceptedQuotationCount":1},{"CurrencyId":2,"QuotedAmount":9876543210.12345678,"AcceptedQuotationCount":1}]}],"TechnicalConversionAvailability":"unavailable","QualifiedCustomerAvailability":"unavailable","RevenueAvailability":"unavailable"}""",
        "invoice" => """{"FromUtc":"2026-08-25T17:00:00Z","ToUtc":"2026-09-01T17:00:00Z","Days":[{"DayUtc":"2026-08-26T00:00:00","PaidInvoiceCount":3,"SourceAttributedPaidInvoiceCount":1,"UnattributedPaidInvoiceCount":2,"PaidInvoiceAmountsByCurrency":[{"Currency":"THB","PaidInvoiceTotal":120.5000,"PaidInvoiceCount":2},{"Currency":"USD","PaidInvoiceTotal":25.01,"PaidInvoiceCount":1}]}]}""",
        _ => """{"fromUtc":"2026-08-25T17:00:00Z","toUtc":"2026-09-01T17:00:00Z","requests":[{"requestId":41,"createdUtc":"2026-08-26T09:00:00Z","transactionId":"request-41","journeyId":"11111111-2222-3333-4444-555555555555","state":"qualified"},{"requestId":42,"createdUtc":"2026-08-26T10:00:00Z","state":"qualified"},{"requestId":43,"createdUtc":"2026-08-26T11:00:00Z","transactionId":"request-43","journeyId":"11111111-2222-3333-4444-555555555555","state":"unreviewed"},{"requestId":44,"createdUtc":"2026-08-26T12:00:00Z","state":"unreviewed"},{"requestId":45,"createdUtc":"2026-08-26T13:00:00Z","transactionId":"request-45","journeyId":"11111111-2222-3333-4444-555555555555","state":"not_qualified"},{"requestId":46,"createdUtc":"2026-08-26T14:00:00Z","state":"duplicate"},{"requestId":47,"createdUtc":"2026-08-26T15:00:00Z","transactionId":"request-47","journeyId":"11111111-2222-3333-4444-555555555555","state":"stale"},{"requestId":48,"createdUtc":"2026-08-26T16:00:00Z","state":"incomplete"}]}""",
    };

    private static async Task<JsonDocument> BrowserReceiptAsync(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/html", response.Content.Headers.ContentType!.MediaType);
        Assert.Equal("utf-8", response.Content.Headers.ContentType.CharSet);
        Assert.Contains("no-store", response.Headers.CacheControl!.ToString(), StringComparison.Ordinal);
        Assert.Contains("no-cache", response.Headers.Pragma.ToString(), StringComparison.Ordinal);
        Assert.Equal("nosniff", Assert.Single(response.Headers.GetValues("X-Content-Type-Options")));
        Assert.Equal("default-src 'none'; frame-ancestors 'none'; base-uri 'none'",
            Assert.Single(response.Headers.GetValues("Content-Security-Policy")));
        var html = await response.Content.ReadAsStringAsync();
        Assert.Contains("<!doctype html>", html, StringComparison.Ordinal);
        Assert.Contains("&quot;source&quot;", html, StringComparison.Ordinal);
        Assert.DoesNotContain("<script", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("synthetic-rotation-", html, StringComparison.Ordinal);
        Assert.DoesNotContain("employee@maliev.com", html, StringComparison.Ordinal);
        Assert.DoesNotContain("synthetic-employee", html, StringComparison.Ordinal);
        var start = html.IndexOf("<pre>", StringComparison.Ordinal);
        var end = html.IndexOf("</pre>", StringComparison.Ordinal);
        Assert.True(start >= 0 && end > start);
        return JsonDocument.Parse(WebUtility.HtmlDecode(html[(start + 5)..end]));
    }

    private static HttpClient Client(Factory factory) => factory.CreateClient(new()
    { BaseAddress = new Uri("https://localhost"), HandleCookies = true, AllowAutoRedirect = false });

    private static async Task<(string Cookie, string Csrf)> LoginAsync(HttpClient browser, Factory factory)
    {
        using var session = await browser.GetAsync("/bff/session");
        using var body = JsonDocument.Parse(await session.Content.ReadAsStringAsync());
        var csrf = body.RootElement.GetProperty("csrfToken").GetString()!;
        var antiCookie = session.Headers.GetValues("Set-Cookie").Select(value => value.Split(';')[0]);
        using var request = new HttpRequestMessage(HttpMethod.Post, "/bff/login")
        { Content = JsonContent.Create(new { email = "employee@maliev.com", password = "synthetic-only-password", returnUrl = "/" }) };
        request.Headers.Add("X-CSRF-TOKEN", csrf);
        using var response = await browser.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var cookie = Assert.Single(response.Headers.GetValues("Set-Cookie"), value => value.StartsWith("__Host-Legacy.Maliev.Intranet.Bff=", StringComparison.Ordinal));
        Assert.Contains("secure", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("httponly", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=lax", cookie, StringComparison.OrdinalIgnoreCase);
        var options = factory.Services.GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>().Get("Cookies");
        var opaque = options.TicketDataFormat.Unprotect(cookie.Split(';')[0].Split('=', 2)[1]);
        Assert.NotNull(opaque);
        var key = opaque.Principal.FindFirstValue("Microsoft.AspNetCore.Authentication.Cookies-SessionId");
        Assert.False(string.IsNullOrEmpty(key));
        factory.Probe.ObserveCookieKey(key);
        return (string.Join("; ", antiCookie.Append(cookie.Split(';')[0])), csrf);
    }

    private sealed class Factory(string connection, Scenario scenario, int gateRead = 0, int failRead = 0, string? mutationFault = null) : WebApplicationFactory<BffProgram>
    {
        public ProbeCache Probe { get; private set; } = null!;
        protected override void ConfigureWebHost(IWebHostBuilder builder)
            => ConfigureProduction(builder, connection, scenario, gateRead, failRead, probe => Probe = probe, mutationFault);

        internal static void ConfigureProduction(IWebHostBuilder builder, string connection, Scenario scenario,
            int gateRead, int failRead, Action<ProbeCache> capture, string? mutationFault)
        {
            builder.UseEnvironment("Production");
            TestHostLifecycle.Configure(builder);
            builder.UseSetting("ConnectionStrings:redis", connection);
            builder.UseSetting("DataProtection:CertificatePfxBase64", scenario.Certificate);
            builder.UseSetting("DataProtection:CertificatePassword", "synthetic-only-certificate");
            builder.UseSetting("Jwt:Issuer", "https://synthetic-auth.invalid");
            builder.UseSetting("Jwt:Audience", "synthetic-intranet");
            builder.UseSetting("Jwt:PublicKeyPem", scenario.PublicKey);
            builder.UseSetting("Jwt:KeyId", "synthetic-session-key");
            builder.UseSetting("QuotationQualification:ForwardEmployeeToken", "true");
            foreach (var service in new[] { "Auth", "Quotation", "Catalog", "Customer", "Employee", "Order", "File", "Notification", "Document", "Procurement", "Accounting" })
                builder.UseSetting("Services:" + service, "https://" + service.ToLowerInvariant() + ".invalid");
            builder.ConfigureLogging(logging => logging.ClearProviders());
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<TimeProvider>();
                services.AddSingleton<TimeProvider>(scenario.Clock);
                services.Configure<CookieAuthenticationOptions>("Cookies", options => options.TimeProvider = scenario.Clock);
                services.AddSingleton<IHttpMessageHandlerBuilderFilter>(new TransportFilter(scenario));
                services.RemoveAll<IDistributedCache>();
                services.AddSingleton<IDistributedCache>(provider =>
                {
                    var probe = new ProbeCache(new RedisCache(provider.GetRequiredService<IOptions<RedisCacheOptions>>()), gateRead, failRead);
                    capture(probe);
                    return probe;
                });
                if (mutationFault is not null)
                {
                    services.RemoveAll<ISessionTicketPersistence>();
                    services.AddSingleton<ISessionTicketPersistence>(provider => new UnknownOutcomePersistence(
                        new RedisSessionTicketPersistence(provider.GetRequiredService<LegacyDataProtectionResources>()), mutationFault));
                }
            });
        }
    }

    private sealed class CompatibilityFactory(string connection, Scenario scenario) : WebApplicationFactory<global::Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder) =>
            Factory.ConfigureProduction(builder, connection, scenario, 0, 0, _ => { }, null);
    }

    // Faults only after/before a real atomic provider command. Never fabricates persistence success.
    private sealed class UnknownOutcomePersistence(ISessionTicketPersistence inner, string fault) : ISessionTicketPersistence
    {
        private int failed;
        public async Task<bool> WriteAsync(string key, byte[] ciphertext, byte[]? expectedCiphertext, DateTimeOffset expiry, CancellationToken token)
        {
            if (expectedCiphertext is null || Interlocked.Exchange(ref failed, 1) != 0)
                return await inner.WriteAsync(key, ciphertext, expectedCiphertext, expiry, token);
            if (fault == "before") throw new IOException("Synthetic unknown outcome before command.");
            var committed = await inner.WriteAsync(key, ciphertext, expectedCiphertext, expiry, token);
            Assert.True(committed);
            if (fault == "canceled-after") throw new OperationCanceledException("Synthetic provider cancellation with an uncanceled caller.");
            throw new IOException("Synthetic lost acknowledgement after real commit.");
        }
        public Task<bool> RemoveObservedAsync(string key, byte[] expectedCiphertext, CancellationToken token) => inner.RemoveObservedAsync(key, expectedCiphertext, token);
        public Task RevokeAsync(string key, CancellationToken token) => inner.RevokeAsync(key, token);
    }

    private sealed class Clock : TimeProvider
    {
        private DateTimeOffset now = DateTimeOffset.UtcNow;
        public override DateTimeOffset GetUtcNow() => now;
        public void Advance(TimeSpan duration) => now += duration;
    }

    private sealed class Scenario : IDisposable
    {
        private readonly RSA rsa = RSA.Create(2048);
        public Clock Clock { get; } = new();
        public string PublicKey => rsa.ExportSubjectPublicKeyInfoPem();
        public string Certificate { get; } = CreateCertificate();
        public string? OutcomeBody { get; set; }
        public string? OutcomePath { get; init; }
        public string[]? OutcomePermissions { get; set; }
        public HttpStatusCode OutcomeStatus { get; set; } = HttpStatusCode.OK;
        public string? OutcomeFault { get; set; }
        public bool SuccessfulOutcomeRefresh { get; set; }
        public HttpStatusCode? OutcomeRefreshStatus { get; set; }
        public int OutcomeReads;
        public int LoginCalls;
        public int RefreshCalls;
        public string? LastIssuedAccessToken { get; private set; }
        public string? LastOutcomeAccessToken { get; set; }
        public string? LastOutcomeScheme { get; set; }
        public string? LastOutcomePath { get; set; }
        public string? LastOutcomeQuery { get; set; }
        public bool BlockSuccessfulRefresh { get; init; }
        public bool WrongRefreshOwner { get; init; }
        public TaskCompletionSource RefreshEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource ReleaseRefresh { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int Forwarded;
        public int UncommittedForwarded;
        public string? PeerAccessToken { get; set; }
        public string Token(bool wrongOwner = false)
        {
            var now = DateTime.UtcNow;
            var token = new JwtSecurityToken("https://synthetic-auth.invalid", "synthetic-intranet",
                [new("sub", wrongOwner ? "other-synthetic-employee" : "synthetic-employee"), new("name", "employee@maliev.com"), new("email", "employee@maliev.com"),
                 new("identity_kind", "employee"), new("jti", Guid.NewGuid().ToString("N")),
                 .. (OutcomePermissions ?? [LegacyEmployeePermissions.QuotationRequestsRead]).Select(permission => new Claim("permissions", permission))],
                now.AddMinutes(-1), now.AddHours(1), new SigningCredentials(new RsaSecurityKey(rsa) { KeyId = "synthetic-session-key" }, SecurityAlgorithms.RsaSha256));
            var accessToken = new JwtSecurityTokenHandler().WriteToken(token);
            LastIssuedAccessToken = accessToken;
            return accessToken;
        }
        public object Envelope(bool rotated = false) => new
        { accessToken = Token(rotated && WrongRefreshOwner), refreshToken = rotated ? "synthetic-rotation-2" : "synthetic-rotation-1", tokenType = "Bearer", expiresIn = 900, refreshExpiresAt = Clock.GetUtcNow().AddDays(1) };
        public void Dispose() => rsa.Dispose();
        private static string CreateCertificate()
        {
            using var key = RSA.Create(2048);
            var request = new CertificateRequest("CN=SyntheticSessionFence", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            using var certificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddDays(1));
            return Convert.ToBase64String(certificate.Export(X509ContentType.Pkcs12, "synthetic-only-certificate"));
        }
    }

    private sealed class TransportFilter(Scenario scenario) : IHttpMessageHandlerBuilderFilter
    {
        public Action<HttpMessageHandlerBuilder> Configure(Action<HttpMessageHandlerBuilder> next) => builder =>
        { next(builder); builder.PrimaryHandler = new AuthTransport(scenario); };
    }

    private sealed class AuthTransport(Scenario scenario) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            switch (request.RequestUri!.AbsolutePath)
            {
                case "/auth/v1/login":
                    Interlocked.Increment(ref scenario.LoginCalls);
                    return new(HttpStatusCode.OK) { Content = JsonContent.Create(scenario.Envelope()) };
                case "/auth/v1/revoke": return new(HttpStatusCode.NoContent);
                case "/auth/v1/refresh":
                    Interlocked.Increment(ref scenario.RefreshCalls);
                    if (scenario.SuccessfulOutcomeRefresh) return new(HttpStatusCode.OK) { Content = JsonContent.Create(scenario.Envelope(rotated: true)) };
                    if (scenario.OutcomeRefreshStatus is { } refreshStatus) return new(refreshStatus);
                    if (scenario.WrongRefreshOwner) return new(HttpStatusCode.OK) { Content = JsonContent.Create(scenario.Envelope(rotated: true)) };
                    if (!scenario.BlockSuccessfulRefresh) return new(HttpStatusCode.Unauthorized);
                    scenario.RefreshEntered.TrySetResult();
                    await scenario.ReleaseRefresh.Task.WaitAsync(token);
                    return new(HttpStatusCode.OK) { Content = JsonContent.Create(scenario.Envelope(rotated: true)) };
                case "/quotations/outcomes/readback":
                case "/invoices/outcomes/readback":
                case "/quotationrequests/qualification-outcomes/readback":
                    Assert.Equal(scenario.OutcomePath, request.RequestUri.AbsolutePath);
                    Interlocked.Increment(ref scenario.OutcomeReads);
                    scenario.LastOutcomePath = request.RequestUri.AbsolutePath;
                    scenario.LastOutcomeQuery = request.RequestUri.Query;
                    scenario.LastOutcomeAccessToken = request.Headers.Authorization?.Parameter;
                    scenario.LastOutcomeScheme = request.Headers.Authorization?.Scheme;
                    if (scenario.OutcomeFault == "transport") throw new HttpRequestException("private-producer-marker");
                    if (scenario.OutcomeFault == "io") throw new IOException("private-producer-marker");
                    if (scenario.OutcomeFault == "timeout") throw new OperationCanceledException("private-producer-marker");
                    return new(scenario.OutcomeStatus)
                    {
                        Content = new StringContent(scenario.OutcomeBody!, System.Text.Encoding.UTF8, "application/json"),
                    };
                case "/quotationrequests/84/qualification-receipt":
                    Interlocked.Increment(ref scenario.Forwarded);
                    if (scenario.PeerAccessToken is not null && request.Headers.Authorization?.Parameter != scenario.PeerAccessToken)
                        Interlocked.Increment(ref scenario.UncommittedForwarded);
                    // Negative egress sentinel only: never fabricate upstream success or provider acceptance.
                    return new(HttpStatusCode.Forbidden);
                default: throw new InvalidOperationException("Unexpected synthetic transport route.");
            }
        }
    }

    /// <summary>Scheduling wrapper around actual pinned RedisCache, not an in-memory storage substitute.</summary>
    private sealed class ProbeCache(IDistributedCache inner, int gateRead, int failRead) : IDistributedCache, IDisposable
    {
        private int reads;
        public int ReadCount => Volatile.Read(ref reads);
        public string? LastKey { get; private set; }
        public void ObserveCookieKey(string key) => LastKey = key;
        public TaskCompletionSource ReadCaptured { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource ReleaseRead { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async Task<byte[]?> GetAsync(string key, CancellationToken token = default)
        {
            LastKey = key;
            var read = Interlocked.Increment(ref reads);
            if (read == failRead) throw new IOException("Synthetic reconciliation read failure.");
            var bytes = await inner.GetAsync(key, token);
            if (read == gateRead)
            { ReadCaptured.TrySetResult(); await ReleaseRead.Task.WaitAsync(TimeSpan.FromSeconds(15), token); }
            return bytes;
        }
        public Task SetAsync(string key, byte[] value, DistributedCacheEntryOptions options, CancellationToken token = default)
        { LastKey = key; return inner.SetAsync(key, value, options, token); }
        public Task RemoveAsync(string key, CancellationToken token = default) => inner.RemoveAsync(key, token);
        public Task RefreshAsync(string key, CancellationToken token = default) => inner.RefreshAsync(key, token);
        public byte[]? Get(string key) => inner.Get(key);
        public void Set(string key, byte[] value, DistributedCacheEntryOptions options) => inner.Set(key, value, options);
        public void Remove(string key) => inner.Remove(key);
        public void Refresh(string key) => inner.Refresh(key);
        public void Dispose() => (inner as IDisposable)?.Dispose();
    }
}
