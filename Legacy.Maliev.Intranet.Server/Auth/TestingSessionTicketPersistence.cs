using Microsoft.Extensions.Caching.Distributed;

namespace Legacy.Maliev.Intranet.Auth;

// Explicit Testing/manual MemoryDistributedCache compatibility only. This is not distributed fencing proof.
internal sealed class TestingSessionTicketPersistence(IDistributedCache cache, TimeProvider clock) : ISessionTicketPersistence
{
    private readonly SemaphoreSlim gate = new(1, 1);
    public async Task<bool> WriteAsync(string key, byte[] ciphertext, byte[]? expectedCiphertext, DateTimeOffset expiry, CancellationToken token)
    {
        await gate.WaitAsync(token);
        try
        {
            var current = await cache.GetAsync(key, token);
            if (expiry <= clock.GetUtcNow() || (expectedCiphertext is null ? current is not null : current is null || !current.AsSpan().SequenceEqual(expectedCiphertext))) return false;
            await cache.SetAsync(key, ciphertext, new() { AbsoluteExpiration = expiry }, token);
            return true;
        }
        finally { gate.Release(); }
    }
    public async Task<bool> RemoveObservedAsync(string key, byte[] expectedCiphertext, CancellationToken token)
    {
        await gate.WaitAsync(token);
        try
        {
            var current = await cache.GetAsync(key, token);
            if (current is null || !current.AsSpan().SequenceEqual(expectedCiphertext)) return false;
            await cache.RemoveAsync(key, token);
            return true;
        }
        finally { gate.Release(); }
    }
    public async Task RevokeAsync(string key, CancellationToken token)
    {
        await gate.WaitAsync(token);
        try { await cache.RemoveAsync(key, token); }
        finally { gate.Release(); }
    }
}
