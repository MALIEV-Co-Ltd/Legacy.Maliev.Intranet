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

/// <summary>Real two-provider Redis/crypto/cookie races; only external primary Auth transport is controlled.</summary>
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
                 new("identity_kind", "employee"), new("jti", Guid.NewGuid().ToString("N")), new("permissions", LegacyEmployeePermissions.QuotationRequestsRead)],
                now.AddMinutes(-1), now.AddHours(1), new SigningCredentials(new RsaSecurityKey(rsa) { KeyId = "synthetic-session-key" }, SecurityAlgorithms.RsaSha256));
            return new JwtSecurityTokenHandler().WriteToken(token);
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
                case "/auth/v1/login": return new(HttpStatusCode.OK) { Content = JsonContent.Create(scenario.Envelope()) };
                case "/auth/v1/revoke": return new(HttpStatusCode.NoContent);
                case "/auth/v1/refresh":
                    if (scenario.WrongRefreshOwner) return new(HttpStatusCode.OK) { Content = JsonContent.Create(scenario.Envelope(rotated: true)) };
                    if (!scenario.BlockSuccessfulRefresh) return new(HttpStatusCode.Unauthorized);
                    scenario.RefreshEntered.TrySetResult();
                    await scenario.ReleaseRefresh.Task.WaitAsync(token);
                    return new(HttpStatusCode.OK) { Content = JsonContent.Create(scenario.Envelope(rotated: true)) };
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
