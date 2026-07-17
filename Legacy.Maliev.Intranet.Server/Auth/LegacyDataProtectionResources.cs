using System.Security.Cryptography.X509Certificates;
using StackExchange.Redis;

namespace Legacy.Maliev.Intranet.Auth;

/// <summary>Owns the shared runtime resources used by the session cache and Data Protection key ring.</summary>
public sealed class LegacyDataProtectionResources(
    X509Certificate2 certificate,
    IConnectionMultiplexer redis) : IDisposable
{
    private int disposed;

    /// <summary>Gets the runtime-only certificate used to encrypt Data Protection keys.</summary>
    public X509Certificate2 Certificate { get; } = certificate;

    /// <summary>Gets the single Redis connection shared by the cache and key repository.</summary>
    public IConnectionMultiplexer Redis { get; } = redis;

    /// <summary>Gets whether the owned resources have been disposed.</summary>
    public bool IsDisposed => Volatile.Read(ref disposed) != 0;

    /// <inheritdoc />
    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0)
        {
            return;
        }

        try
        {
            Redis.Dispose();
        }
        finally
        {
            Certificate.Dispose();
        }
    }
}
