using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Security.Claims;
using Legacy.Maliev.Intranet.Auth;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Legacy.Maliev.Intranet.Tests;

public sealed class DataProtectionContractTests
{
    [Fact]
    public void IndependentProviders_SharingEncryptedKeyRingCanReadOnlyTheSameApplicationPurpose()
    {
        var keyRingDirectory = Directory.CreateTempSubdirectory("legacy-intranet-key-ring-");
        try
        {
            using var certificate = CreateCertificate();
            using var firstServices = CreateProvider(keyRingDirectory, certificate, LegacyDataProtection.ApplicationName);
            var properties = new AuthenticationProperties();
            properties.StoreTokens(
            [
                new AuthenticationToken { Name = "access_token", Value = "server-only-access-token" },
                new AuthenticationToken { Name = "refresh_token", Value = "server-only-refresh-token" },
            ]);
            var ticket = new AuthenticationTicket(
                new ClaimsPrincipal(new ClaimsIdentity(
                    [new Claim(ClaimTypes.NameIdentifier, "employee-id")],
                    CookieAuthenticationDefaults.AuthenticationScheme)),
                properties,
                CookieAuthenticationDefaults.AuthenticationScheme);
            var protectedCookie = CreateCookieFormat(firstServices).Protect(ticket);

            var rawKeyRing = string.Join(
                Environment.NewLine,
                keyRingDirectory.EnumerateFiles("*.xml").Select(file => File.ReadAllText(file.FullName)));
            Assert.Contains("encryptedSecret", rawKeyRing, StringComparison.Ordinal);
            Assert.DoesNotContain("<masterKey", rawKeyRing, StringComparison.Ordinal);
            Assert.DoesNotContain("server-only-access-token", rawKeyRing, StringComparison.Ordinal);
            Assert.DoesNotContain("server-only-refresh-token", rawKeyRing, StringComparison.Ordinal);

            using var secondServices = CreateProvider(keyRingDirectory, certificate, LegacyDataProtection.ApplicationName);
            var restoredTicket = CreateCookieFormat(secondServices).Unprotect(protectedCookie);
            Assert.NotNull(restoredTicket);
            Assert.Equal("employee-id", restoredTicket.Principal.FindFirstValue(ClaimTypes.NameIdentifier));
            Assert.Equal("server-only-access-token", restoredTicket.Properties.GetTokenValue("access_token"));
            Assert.Equal("server-only-refresh-token", restoredTicket.Properties.GetTokenValue("refresh_token"));

            using var otherApplication = CreateProvider(keyRingDirectory, certificate, "Legacy.Maliev.Intranet.Other");
            Assert.Null(CreateCookieFormat(otherApplication).Unprotect(protectedCookie));
        }
        finally
        {
            keyRingDirectory.Delete(recursive: true);
        }
    }

    [Fact]
    public void BothServerHosts_UseTheSharedRedisAndCertificateDataProtectionContract()
    {
        var root = FindRoot();
        var registrationPath = Path.Combine(
            root,
            "Legacy.Maliev.Intranet.Server",
            "Auth",
            "LegacyDataProtection.cs");
        Assert.True(File.Exists(registrationPath), "The shared Data Protection registration is missing.");

        var registration = File.ReadAllText(registrationPath);
        Assert.Contains("Legacy.Maliev.Intranet", registration, StringComparison.Ordinal);
        Assert.Contains("legacy:intranet:data-protection-keys", registration, StringComparison.Ordinal);
        Assert.Contains("DataProtection:CertificatePfxBase64", registration, StringComparison.Ordinal);
        Assert.Contains("DataProtection:CertificatePassword", registration, StringComparison.Ordinal);
        Assert.Contains("ProtectKeysWithCertificate", registration, StringComparison.Ordinal);
        Assert.Contains("PersistKeysToStackExchangeRedis", registration, StringComparison.Ordinal);
        Assert.Contains("ConnectionMultiplexerFactory", registration, StringComparison.Ordinal);

        foreach (var host in new[] { "Legacy.Maliev.Intranet", "Legacy.Maliev.Intranet.Bff" })
        {
            var program = File.ReadAllText(Path.Combine(root, host, "Program.cs"));
            Assert.Contains("AddLegacyIntranetDataProtection", program, StringComparison.Ordinal);
            Assert.DoesNotContain("AddRedisDistributedCache", program, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void ProductionRegistration_WithoutRedis_FailsClosed()
    {
        var builder = CreateHostBuilder("Production");
        builder.Configuration["ConnectionStrings:redis"] = null;

        var exception = Assert.Throws<InvalidOperationException>(
            () => builder.AddLegacyIntranetDataProtection());

        Assert.Contains("Redis connection string 'redis' is required", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ProductionRegistration_WithoutCertificate_FailsClosedBeforeConnectingToRedis()
    {
        var builder = CreateHostBuilder("Production");
        builder.Configuration["ConnectionStrings:redis"] = "127.0.0.1:1";
        builder.Configuration["DataProtection:CertificatePfxBase64"] = null;
        builder.Configuration["DataProtection:CertificatePassword"] = null;

        var exception = Assert.Throws<InvalidOperationException>(
            () => builder.AddLegacyIntranetDataProtection());

        Assert.Contains("Data Protection certificate is required", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void TestingRegistration_UsesInMemoryCacheWithoutRedisOrCertificate()
    {
        var builder = CreateHostBuilder("Testing");
        builder.AddLegacyIntranetDataProtection();
        using var services = builder.Services.BuildServiceProvider();

        Assert.NotNull(services.GetRequiredService<IDistributedCache>());
        Assert.IsType<EphemeralDataProtectionProvider>(
            services.GetRequiredService<IDataProtectionProvider>());
    }

    private static ServiceProvider CreateProvider(
        DirectoryInfo keyRingDirectory,
        X509Certificate2 certificate,
        string applicationName)
    {
        var services = new ServiceCollection();
        services.AddDataProtection()
            .SetApplicationName(applicationName)
            .PersistKeysToFileSystem(keyRingDirectory)
            .ProtectKeysWithCertificate(certificate);
        return services.BuildServiceProvider();
    }

    private static TicketDataFormat CreateCookieFormat(IServiceProvider services) =>
        new(services.GetRequiredService<IDataProtectionProvider>().CreateProtector(
            "Microsoft.AspNetCore.Authentication.Cookies.CookieAuthenticationMiddleware",
            CookieAuthenticationDefaults.AuthenticationScheme,
            "v2"));

    private static X509Certificate2 CreateCertificate()
    {
        using var key = RSA.Create(2048);
        var request = new CertificateRequest(
            "CN=Legacy.Maliev.Intranet.Tests",
            key,
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1);
        return request.CreateSelfSigned(
            DateTimeOffset.UtcNow.AddMinutes(-1),
            DateTimeOffset.UtcNow.AddDays(1));
    }

    private static HostApplicationBuilder CreateHostBuilder(string environmentName) =>
        Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
        {
            EnvironmentName = environmentName,
            ApplicationName = "Legacy.Maliev.Intranet.Tests",
        });

    private static string FindRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Legacy.Maliev.Intranet.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new DirectoryNotFoundException("Could not find repository root.");
    }
}
