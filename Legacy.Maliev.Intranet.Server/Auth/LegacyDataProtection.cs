using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Caching.StackExchangeRedis;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using StackExchange.Redis;

namespace Legacy.Maliev.Intranet.Auth;

/// <summary>Configures the shared server-only Data Protection boundary for both Intranet hosts.</summary>
public static class LegacyDataProtection
{
    /// <summary>Gets the stable application discriminator shared by the compatibility host and BFF.</summary>
    public const string ApplicationName = "Legacy.Maliev.Intranet";

    private const string CacheInstanceName = "legacy-intranet:";
    private const string KeyRingKey = "legacy:intranet:data-protection-keys";

    /// <summary>
    /// Adds an ephemeral Testing provider or a certificate-protected Redis key ring in every other environment.
    /// </summary>
    public static IHostApplicationBuilder AddLegacyIntranetDataProtection(this IHostApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        var dataProtection = builder.Services.AddDataProtection()
            .SetApplicationName(ApplicationName);
        if (builder.Environment.IsEnvironment("Testing"))
        {
            builder.Services.AddDistributedMemoryCache();
            builder.Services.AddSingleton<IDataProtectionProvider>(new EphemeralDataProtectionProvider());
            return builder;
        }

        var redisConnectionString = builder.Configuration.GetConnectionString("redis");
        if (string.IsNullOrWhiteSpace(redisConnectionString))
        {
            throw new InvalidOperationException(
                "Redis connection string 'redis' is required for shared Intranet session protection.");
        }

        var certificatePfxBase64 = builder.Configuration["DataProtection:CertificatePfxBase64"];
        var certificatePassword = builder.Configuration["DataProtection:CertificatePassword"];
        if (string.IsNullOrWhiteSpace(certificatePfxBase64)
            || string.IsNullOrWhiteSpace(certificatePassword))
        {
            throw new InvalidOperationException(
                "A Data Protection certificate is required to encrypt the shared Redis key ring.");
        }

        var certificate = LoadCertificate(certificatePfxBase64, certificatePassword);
        builder.Services.AddSingleton(certificate);
        dataProtection.ProtectKeysWithCertificate(certificate);

        var redisOptions = ConfigurationOptions.Parse(redisConnectionString);
        redisOptions.AbortOnConnectFail = false;
        redisOptions.ConnectRetry = 5;
        redisOptions.ConnectTimeout = 10_000;
        redisOptions.AsyncTimeout = 10_000;
        redisOptions.SyncTimeout = 10_000;
        var redis = ConnectionMultiplexer.Connect(redisOptions);
        builder.Services.AddSingleton<IConnectionMultiplexer>(redis);
        builder.Services.AddStackExchangeRedisCache(options =>
        {
            options.ConnectionMultiplexerFactory = () => Task.FromResult<IConnectionMultiplexer>(redis);
            options.InstanceName = CacheInstanceName;
        });
        dataProtection.PersistKeysToStackExchangeRedis(redis, KeyRingKey);

        return builder;
    }

    private static X509Certificate2 LoadCertificate(string pfxBase64, string password)
    {
        try
        {
            return X509CertificateLoader.LoadPkcs12(
                Convert.FromBase64String(pfxBase64),
                password,
                X509KeyStorageFlags.EphemeralKeySet);
        }
        catch (Exception exception) when (exception is FormatException or CryptographicException)
        {
            throw new InvalidOperationException(
                "The configured Data Protection certificate is invalid.",
                exception);
        }
    }
}
