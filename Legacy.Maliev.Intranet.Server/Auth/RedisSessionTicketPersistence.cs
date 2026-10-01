using System.Globalization;
using StackExchange.Redis;

namespace Legacy.Maliev.Intranet.Auth;

/// <summary>Session-only ciphertext comparison on the existing pinned RedisCache hash representation.</summary>
public sealed class RedisSessionTicketPersistence(LegacyDataProtectionResources resources) : ISessionTicketPersistence
{
    private const string Prefix = "legacy-intranet:";
    private const string WriteScript = """
        local now = redis.call('TIME')
        local nowms = now[1] * 1000 + math.floor(now[2] / 1000)
        local expires = tonumber(ARGV[4])
        if not expires or expires <= nowms then return 0 end
        if ARGV[1] == 'create' then
            if redis.call('EXISTS', KEYS[1]) ~= 0 then return 0 end
            redis.call('HSET', KEYS[1], 'data', ARGV[2], 'absexp', ARGV[3], 'sldexp', '-1')
        else
            if redis.call('TYPE', KEYS[1]).ok ~= 'hash' then return 0 end
            if redis.call('PTTL', KEYS[1]) <= 0 then return 0 end
            if redis.call('HGET', KEYS[1], 'data') ~= ARGV[5] then return 0 end
            if redis.call('HGET', KEYS[1], 'absexp') ~= ARGV[3] then return 0 end
            if redis.call('HGET', KEYS[1], 'sldexp') ~= '-1' then return 0 end
            redis.call('HSET', KEYS[1], 'data', ARGV[2])
        end
        redis.call('PEXPIREAT', KEYS[1], ARGV[4])
        return 1
        """;
    private const string RemoveScript = """
        if redis.call('TYPE', KEYS[1]).ok == 'hash' and redis.call('HGET', KEYS[1], 'data') == ARGV[1] then
            return redis.call('DEL', KEYS[1])
        end
        return 0
        """;

    /// <inheritdoc />
    public async Task<bool> WriteAsync(string key, byte[] ciphertext, byte[]? expectedCiphertext, DateTimeOffset expiry, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var result = await resources.Redis.GetDatabase().ScriptEvaluateAsync(WriteScript,
            [Prefix + key],
            [expectedCiphertext is null ? "create" : "renew", ciphertext,
             expiry.UtcTicks.ToString(CultureInfo.InvariantCulture), expiry.ToUnixTimeMilliseconds().ToString(CultureInfo.InvariantCulture),
             expectedCiphertext ?? []]).WaitAsync(token);
        token.ThrowIfCancellationRequested();
        return (long)result == 1;
    }

    /// <inheritdoc />
    public async Task<bool> RemoveObservedAsync(string key, byte[] expectedCiphertext, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var result = await resources.Redis.GetDatabase().ScriptEvaluateAsync(RemoveScript, [Prefix + key], [expectedCiphertext]).WaitAsync(token);
        token.ThrowIfCancellationRequested();
        return (long)result == 1;
    }

    /// <inheritdoc />
    public async Task RevokeAsync(string key, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        await resources.Redis.GetDatabase().KeyDeleteAsync(Prefix + key).WaitAsync(token);
        token.ThrowIfCancellationRequested();
    }
}
