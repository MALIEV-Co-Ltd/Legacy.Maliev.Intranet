using System.Security.Claims;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using Legacy.Maliev.Intranet.Auth;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using StackExchange.Redis;
using Testcontainers.Redis;

namespace Legacy.Maliev.Intranet.Tests.Integration;

public sealed class RedisDataProtectionIntegrationTests : IAsyncLifetime
{
    private const string CertificatePassword = "integration-test-only";
    private readonly RedisContainer redis = new RedisBuilder("redis:7.4.5-alpine").Build();

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
