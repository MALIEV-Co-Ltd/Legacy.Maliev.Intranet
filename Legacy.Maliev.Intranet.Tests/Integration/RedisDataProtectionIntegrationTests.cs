extern alias Bff;

using System.Security.Claims;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using Legacy.Maliev.Intranet.Auth;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.AspNetCore.DataProtection.StackExchangeRedis;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.StackExchangeRedis;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Http;
using Microsoft.Extensions.Options;
using StackExchange.Redis;
using Testcontainers.Redis;
using BffProgram = Bff::Program;

namespace Legacy.Maliev.Intranet.Tests.Integration;

public sealed class RedisDataProtectionIntegrationTests : IAsyncLifetime
{
    private const string CertificatePassword = "integration-test-only";
    private readonly RedisContainer redis = ValidationContainers.Redis();

    public async Task InitializeAsync() => await redis.StartAsync();

    public async Task DisposeAsync() => await redis.DisposeAsync();

    [Fact]
    public async Task ProviderRecreation_PreservesCookieAndTicketWithoutPlaintextRedisSecrets()
    {
        var certificatePfxBase64 = CreateCertificatePfxBase64();
        var ticket = CreateTicket();
        string protectedCookie;
        string ticketKey;
        LegacyDataProtectionResources? firstResources = null;

        var firstProvider = CreateProvider(certificatePfxBase64);
        try
        {
            firstResources = firstProvider.GetRequiredService<LegacyDataProtectionResources>();
            protectedCookie = CreateCookieFormat(firstProvider).Protect(ticket);
            ticketKey = await firstProvider.GetRequiredService<DistributedTicketStore>().StoreAsync(ticket);

            var database = firstResources.Redis.GetDatabase();
            var rawKeyRing = string.Join(
                Environment.NewLine,
                (await database.ListRangeAsync("legacy:intranet:data-protection-keys"))
                    .Select(value => value.ToString()));
            var rawTicket = await database.HashGetAsync("legacy-intranet:" + ticketKey, "data");
            Assert.False(rawTicket.IsNull);
            var rawTicketText = Encoding.UTF8.GetString((byte[])rawTicket!);

            Assert.Contains("encryptedSecret", rawKeyRing, StringComparison.Ordinal);
            Assert.DoesNotContain("<masterKey", rawKeyRing, StringComparison.Ordinal);
            Assert.DoesNotContain("server-only-access-token", rawKeyRing, StringComparison.Ordinal);
            Assert.DoesNotContain("server-only-refresh-token", rawKeyRing, StringComparison.Ordinal);
            Assert.DoesNotContain("server-only-access-token", rawTicketText, StringComparison.Ordinal);
            Assert.DoesNotContain("server-only-refresh-token", rawTicketText, StringComparison.Ordinal);
            Assert.DoesNotContain("server-only-access-token", protectedCookie, StringComparison.Ordinal);
            Assert.DoesNotContain("server-only-refresh-token", protectedCookie, StringComparison.Ordinal);
        }
        finally
        {
            firstProvider.Dispose();
        }

        Assert.NotNull(firstResources);
        Assert.True(firstResources.IsDisposed);
        Assert.Equal(IntPtr.Zero, firstResources.Certificate.Handle);
        Assert.False(firstResources.Redis.IsConnected);

        using var secondProvider = CreateProvider(certificatePfxBase64);
        var restoredCookieTicket = CreateCookieFormat(secondProvider).Unprotect(protectedCookie);
        var restoredServerTicket = await secondProvider
            .GetRequiredService<DistributedTicketStore>()
            .RetrieveAsync(ticketKey);

        Assert.NotNull(restoredCookieTicket);
        Assert.NotNull(restoredServerTicket);
        AssertTicket(restoredCookieTicket);
        AssertTicket(restoredServerTicket);
    }

    [Fact]
    public async Task ProductionRegistration_ReportsRedisReadyWhenSharedConnectionIsHealthy()
    {
        using var provider = CreateProvider(CreateCertificatePfxBase64());

        var report = await provider.GetRequiredService<HealthCheckService>().CheckHealthAsync();

        Assert.True(report.Entries.TryGetValue("redis", out var redisReport));
        Assert.Equal(HealthStatus.Healthy, redisReport.Status);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("false")]
    [InlineData("True")]
    [InlineData("1")]
    [InlineData("true")]
    public Task CompatibilityHost_ExactStandbyPreservesEncryptedExistingKeys(string? standbyValue) =>
        VerifyRegisteredProductionHostAsync<global::Program>(standbyValue);

    [Theory]
    [InlineData(null)]
    [InlineData("false")]
    [InlineData("True")]
    [InlineData("1")]
    [InlineData("true")]
    public Task BffHost_ExactStandbyPreservesEncryptedExistingKeys(string? standbyValue) =>
        VerifyRegisteredProductionHostAsync<BffProgram>(standbyValue);

    private async Task VerifyRegisteredProductionHostAsync<TProgram>(string? flag) where TProgram : class
    {
        RequireAbsentStandbyEnvironment(flag);
        var certificate = CreateCertificatePfxBase64();
        using var seed = CreateProvider(certificate);
        var protectedCookie = CreateCookieFormat(seed).Protect(CreateTicket());
        var before = await KeyRingFingerprintAsync(seed);
        var disposal = new RegisteredHostDisposalWitness();
        LegacyDataProtectionResources resources;
        await using (var factory = CreateRegisteredHost<TProgram>(
            "Production", flag, redis.GetConnectionString(), certificate, disposal))
        {
            var services = factory.Services;
            Assert.Equal(flag, services.GetRequiredService<IConfiguration>()["MALIEV_OBSERVABILITY_STANDBY"]);
            var options = services.GetRequiredService<IOptions<KeyManagementOptions>>().Value;
            Assert.Equal(flag != "true", options.AutoGenerateKeys);
            Assert.IsType<RedisXmlRepository>(options.XmlRepository);
            Assert.NotNull(options.XmlEncryptor);
            Assert.Equal(LegacyDataProtection.ApplicationName,
                services.GetRequiredService<IOptions<DataProtectionOptions>>().Value.ApplicationDiscriminator);
            resources = services.GetRequiredService<LegacyDataProtectionResources>();
            Assert.IsType<RedisSessionTicketPersistence>(services.GetRequiredService<ISessionTicketPersistence>());
            var cache = services.GetRequiredService<IOptions<RedisCacheOptions>>().Value;
            Assert.Equal("legacy-intranet:", cache.InstanceName);
            Assert.NotNull(cache.ConnectionMultiplexerFactory);
            Assert.Same(resources.Redis, await cache.ConnectionMultiplexerFactory!());
            var ready = await services.GetRequiredService<LegacyIntranetRedisHealthCheck>()
                .CheckHealthAsync(new HealthCheckContext());
            Assert.Equal(HealthStatus.Healthy, ready.Status);
            var restored = CreateCookieFormat(services).Unprotect(protectedCookie);
            Assert.NotNull(restored);
            AssertTicket(restored);
            Assert.Equal(before, await KeyRingFingerprintAsync(services));
            Assert.False(disposal.Completion.IsCompleted);
        }
        // Join DI's winning cleanup when app.RunAsync and the test factory dispose
        // concurrently. This new five-second join bounds observation only; it does
        // not change the host shutdown budget or either cleanup assertion below.
        await disposal.Completion.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(resources.IsDisposed);
        Assert.False(resources.Redis.IsConnected);
        Assert.Equal(before, await KeyRingFingerprintAsync(seed));
    }

    private static async Task<(int Count, string Sha256)> KeyRingFingerprintAsync(IServiceProvider services)
    {
        var database = services.GetRequiredService<LegacyDataProtectionResources>().Redis.GetDatabase();
        var entries = await database.ListRangeAsync("legacy:intranet:data-protection-keys");
        Assert.NotEmpty(entries);
        Assert.True(entries.All(value => value.ToString().Contains("encryptedSecret", StringComparison.Ordinal)
            && !value.ToString().Contains("<masterKey", StringComparison.Ordinal)),
            "The disposable key ring must retain encrypted synthetic key material.");
        var hashes = entries.Select(value => Convert.ToHexString(SHA256.HashData((byte[])value!)));
        return (entries.Length, Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join(';', hashes)))));
    }

    internal static WebApplicationFactory<TProgram> CreateRegisteredHost<TProgram>(
        string environment, string? flag, string? connection = null, string? certificate = null,
        RegisteredHostDisposalWitness? disposal = null) where TProgram : class
    {
        using var signingKey = RSA.Create(2048);
        var publicKey = signingKey.ExportSubjectPublicKeyInfoPem();
        return new WebApplicationFactory<TProgram>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment(environment);
            TestHostLifecycle.Configure(builder);
            if (flag is not null) builder.UseSetting("MALIEV_OBSERVABILITY_STANDBY", flag);
            if (connection is not null) builder.UseSetting("ConnectionStrings:redis", connection);
            if (certificate is not null)
            {
                builder.UseSetting("DataProtection:CertificatePfxBase64", certificate);
                builder.UseSetting("DataProtection:CertificatePassword", CertificatePassword);
            }
            builder.UseSetting("Jwt:Issuer", "https://synthetic-auth.invalid");
            builder.UseSetting("Jwt:Audience", "synthetic-intranet");
            builder.UseSetting("Jwt:PublicKeyPem", publicKey);
            builder.UseSetting("Jwt:KeyId", "synthetic-standby-key");
            foreach (var service in new[] { "Auth", "Quotation", "Catalog", "Customer", "Employee", "Order", "File", "Notification", "Document", "Procurement", "Accounting" })
                builder.UseSetting("Services:" + service, "https://" + service.ToLowerInvariant() + ".invalid");
            builder.ConfigureServices(services =>
            {
                services.AddSingleton<IHttpMessageHandlerBuilderFilter, RejectOutboundHttpFilter>();
                if (disposal is null) return;
                Assert.Equal("Production", environment);
                var registration = Assert.Single(services,
                    descriptor => descriptor.ServiceType == typeof(LegacyDataProtectionResources));
                Assert.Equal(ServiceLifetime.Singleton, registration.Lifetime);
                var originalFactory = registration.ImplementationFactory;
                Assert.NotNull(originalFactory);
                services.AddSingleton(_ => disposal);
                // Capture the passive witness before the real resource so reverse
                // DI disposal signals completion only after real cleanup returns.
                services[services.IndexOf(registration)] = ServiceDescriptor.Describe(
                    registration.ServiceType, provider =>
                    {
                        _ = provider.GetRequiredService<RegisteredHostDisposalWitness>();
                        return originalFactory(provider);
                    }, registration.Lifetime);
            });
        });
    }

    internal sealed class RegisteredHostDisposalWitness : IDisposable
    {
        private readonly TaskCompletionSource completion = new(TaskCreationOptions.RunContinuationsAsynchronously);

        internal Task Completion => completion.Task;

        public void Dispose() => completion.TrySetResult();
    }

    internal static void RequireAbsentStandbyEnvironment(string? flag)
    {
        if (flag is not null) return;
        // UseSetting(null) becomes an empty command-line value in the actual minimal host.
        foreach (var prefix in new[] { "", "ASPNETCORE_", "DOTNET_" })
            Assert.True(Environment.GetEnvironmentVariable(prefix + "MALIEV_OBSERVABILITY_STANDBY") is null,
                "The absent standby case requires no ambient standby configuration.");
    }

    private sealed class RejectOutboundHttpFilter : IHttpMessageHandlerBuilderFilter
    {
        public Action<HttpMessageHandlerBuilder> Configure(Action<HttpMessageHandlerBuilder> next) => builder =>
        {
            next(builder);
            builder.AdditionalHandlers.Insert(0, new RejectOutboundHttpHandler());
        };
    }

    private sealed class RejectOutboundHttpHandler : DelegatingHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Standby registration tests do not permit outbound HTTP.");
    }

    private ServiceProvider CreateProvider(string certificatePfxBase64)
    {
        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
        {
            EnvironmentName = "Production",
            ApplicationName = "Legacy.Maliev.Intranet.RedisIntegrationTests",
        });
        builder.Configuration["ConnectionStrings:redis"] = redis.GetConnectionString();
        builder.Configuration["DataProtection:CertificatePfxBase64"] = certificatePfxBase64;
        builder.Configuration["DataProtection:CertificatePassword"] = CertificatePassword;
        builder.Configuration["MALIEV_OBSERVABILITY_STANDBY"] = "false";
        builder.AddLegacyIntranetDataProtection();
        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.AddSingleton<DistributedTicketStore>();
        return builder.Services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateOnBuild = true,
            ValidateScopes = true,
        });
    }

    private static AuthenticationTicket CreateTicket()
    {
        var properties = new AuthenticationProperties
        {
            ExpiresUtc = DateTimeOffset.UtcNow.AddHours(1),
        };
        properties.StoreTokens(
        [
            new AuthenticationToken { Name = "access_token", Value = "server-only-access-token" },
            new AuthenticationToken { Name = "refresh_token", Value = "server-only-refresh-token" },
        ]);
        return new AuthenticationTicket(
            new ClaimsPrincipal(new ClaimsIdentity(
                [new Claim(ClaimTypes.NameIdentifier, "employee-id")],
                CookieAuthenticationDefaults.AuthenticationScheme)),
            properties,
            CookieAuthenticationDefaults.AuthenticationScheme);
    }

    private static TicketDataFormat CreateCookieFormat(IServiceProvider services) =>
        new(services.GetRequiredService<IDataProtectionProvider>().CreateProtector(
            "Microsoft.AspNetCore.Authentication.Cookies.CookieAuthenticationMiddleware",
            CookieAuthenticationDefaults.AuthenticationScheme,
            "v2"));

    private static void AssertTicket(AuthenticationTicket ticket)
    {
        Assert.Equal("employee-id", ticket.Principal.FindFirstValue(ClaimTypes.NameIdentifier));
        Assert.Equal("server-only-access-token", ticket.Properties.GetTokenValue("access_token"));
        Assert.Equal("server-only-refresh-token", ticket.Properties.GetTokenValue("refresh_token"));
    }

    private static string CreateCertificatePfxBase64()
    {
        using var key = RSA.Create(2048);
        var request = new CertificateRequest(
            "CN=Legacy.Maliev.Intranet.RedisIntegrationTests",
            key,
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1);
        using var certificate = request.CreateSelfSigned(
            DateTimeOffset.UtcNow.AddMinutes(-1),
            DateTimeOffset.UtcNow.AddDays(1));
        return Convert.ToBase64String(certificate.Export(X509ContentType.Pkcs12, CertificatePassword));
    }
}

public sealed class RegisteredTestingDataProtectionStandbyTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("false")]
    [InlineData("True")]
    [InlineData("1")]
    [InlineData("true")]
    public Task CompatibilityTestingHost_KeepsEphemeralProtectionAndMemoryCache(string? standbyValue) =>
        VerifyTestingHostAsync<global::Program>(standbyValue);

    [Theory]
    [InlineData(null)]
    [InlineData("false")]
    [InlineData("True")]
    [InlineData("1")]
    [InlineData("true")]
    public Task BffTestingHost_KeepsEphemeralProtectionAndMemoryCache(string? standbyValue) =>
        VerifyTestingHostAsync<BffProgram>(standbyValue);

    private static async Task VerifyTestingHostAsync<TProgram>(string? flag) where TProgram : class
    {
        RedisDataProtectionIntegrationTests.RequireAbsentStandbyEnvironment(flag);
        await using var factory = RedisDataProtectionIntegrationTests.CreateRegisteredHost<TProgram>("Testing", flag);
        var services = factory.Services;
        Assert.Equal(flag, services.GetRequiredService<IConfiguration>()["MALIEV_OBSERVABILITY_STANDBY"]);
        Assert.IsType<EphemeralDataProtectionProvider>(services.GetRequiredService<IDataProtectionProvider>());
        Assert.IsType<MemoryDistributedCache>(services.GetRequiredService<IDistributedCache>());
        var persistence = services.GetRequiredService<ISessionTicketPersistence>();
        Assert.Equal("TestingSessionTicketPersistence", persistence.GetType().Name);
        Assert.Equal(typeof(LegacyDataProtection).Assembly, persistence.GetType().Assembly);
        Assert.True(services.GetRequiredService<IOptions<KeyManagementOptions>>().Value.AutoGenerateKeys);
        Assert.Null(services.GetService<LegacyDataProtectionResources>());
    }
}
