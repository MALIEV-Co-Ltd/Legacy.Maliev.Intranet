using System.Collections.Concurrent;
using System.Text.Json;
using Legacy.Maliev.Intranet.Auth;
using Legacy.Maliev.Intranet.Contracts;
using Legacy.Maliev.Intranet.Server.Orders;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace Legacy.Maliev.Intranet.Server.Quotations;

/// <summary>Durable checkpoints and distributed exclusion for quotation creation.</summary>
public interface IQuotationCreationStateStore
{
    /// <summary>Executes one workflow identity while holding its exclusive lease.</summary>
    Task<T> ExecuteLockedAsync<T>(string key, Func<CancellationToken, Task<T>> operation, CancellationToken cancellationToken);
    /// <summary>Reads a workflow checkpoint.</summary>
    Task<QuotationCreationCheckpoint?> GetAsync(string key, CancellationToken cancellationToken);
    /// <summary>Persists a workflow checkpoint.</summary>
    Task SetAsync(string key, QuotationCreationCheckpoint checkpoint, CancellationToken cancellationToken);
}

/// <summary>Durable quotation creation phases.</summary>
public enum QuotationCreationPhase
{
    /// <summary>Required child creation is active or resumable.</summary>
    Active,
    /// <summary>Quotation, lines, links, and order statuses have committed.</summary>
    RequiredCommitted,
    /// <summary>At-most-once document delivery has started.</summary>
    FinalizationStarted,
    /// <summary>The exact browser result is durable.</summary>
    Completed,
}

/// <summary>Minimal durable state with no credentials or customer contact data.</summary>
public sealed record QuotationCreationCheckpoint(
    string Fingerprint,
    string DownstreamAttemptId,
    QuotationCreationPhase Phase,
    int? QuotationId,
    int CreatedLineCount,
    IReadOnlyList<int> LinkedOrderIds,
    int QuotedOrderCount,
    QuotationCreatedResult? Result);

/// <summary>Redis-backed production store using the existing Intranet connection.</summary>
public sealed class RedisQuotationCreationStateStore(
    LegacyDataProtectionResources resources,
    ILogger<RedisQuotationCreationStateStore> logger) : IQuotationCreationStateStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly TimeSpan StateLifetime = TimeSpan.FromDays(7);
    private static readonly TimeSpan LockLifetime = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan RenewInterval = TimeSpan.FromSeconds(20);
    private readonly IDatabase database = resources.Redis.GetDatabase();

    /// <inheritdoc />
    public async Task<T> ExecuteLockedAsync<T>(string key, Func<CancellationToken, Task<T>> operation, CancellationToken cancellationToken)
    {
        var lockKey = LockKey(key);
        var owner = Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32));
        var deadline = DateTimeOffset.UtcNow + TimeSpan.FromSeconds(10);
        while (!await database.StringSetAsync(lockKey, owner, LockLifetime, When.NotExists))
        {
            if (DateTimeOffset.UtcNow >= deadline) throw new QuotationCreationBusyException();
            await Task.Delay(TimeSpan.FromMilliseconds(100), cancellationToken);
        }

        try
        {
            return await OrderCreationLeaseGuard.ExecuteAsync(
                async () => (long)await database.ScriptEvaluateAsync(
                    "if redis.call('get', KEYS[1]) == ARGV[1] then return redis.call('pexpire', KEYS[1], ARGV[2]) else return 0 end",
                    [lockKey],
                    [owner, (long)LockLifetime.TotalMilliseconds]) == 1,
                async () =>
                {
                    await database.ScriptEvaluateAsync(
                        "if redis.call('get', KEYS[1]) == ARGV[1] then return redis.call('del', KEYS[1]) else return 0 end",
                        [lockKey],
                        [owner]);
                },
                operation,
                RenewInterval,
                cancellationToken,
                exception => logger.LogWarning(exception, "Redis quotation-creation lease release failed; expiry will release it."));
        }
        catch (OrderCreationOutcomeUnknownException exception)
        {
            throw new QuotationCreationOutcomeUnknownException("The quotation-creation lease was lost; its checkpoint was retained.", exception);
        }
    }

    /// <inheritdoc />
    public async Task<QuotationCreationCheckpoint?> GetAsync(string key, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var value = await database.StringGetAsync(StateKey(key));
        return value.IsNullOrEmpty ? null : JsonSerializer.Deserialize<QuotationCreationCheckpoint>((string)value!, JsonOptions);
    }

    /// <inheritdoc />
    public async Task SetAsync(string key, QuotationCreationCheckpoint checkpoint, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await database.StringSetAsync(StateKey(key), JsonSerializer.Serialize(checkpoint, JsonOptions), StateLifetime);
    }

    private static RedisKey StateKey(string key) => $"legacy:intranet:quotation-create:state:{key}";
    private static RedisKey LockKey(string key) => $"legacy:intranet:quotation-create:lock:{key}";
}

/// <summary>Process-local test store with matching exclusive semantics.</summary>
public sealed class InMemoryQuotationCreationStateStore : IQuotationCreationStateStore
{
    private readonly ConcurrentDictionary<string, QuotationCreationCheckpoint> states = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, SemaphoreSlim> locks = new(StringComparer.Ordinal);

    /// <inheritdoc />
    public async Task<T> ExecuteLockedAsync<T>(string key, Func<CancellationToken, Task<T>> operation, CancellationToken cancellationToken)
    {
        var gate = locks.GetOrAdd(key, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try { return await operation(cancellationToken); }
        finally { gate.Release(); }
    }

    /// <inheritdoc />
    public Task<QuotationCreationCheckpoint?> GetAsync(string key, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(states.TryGetValue(key, out var state) ? state : null);
    }

    /// <inheritdoc />
    public Task SetAsync(string key, QuotationCreationCheckpoint checkpoint, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        states[key] = checkpoint;
        return Task.CompletedTask;
    }
}

/// <summary>The same workflow identity was reused with different input.</summary>
public sealed class QuotationCreationConflictException : Exception;
/// <summary>Another process still owns the workflow lease.</summary>
public sealed class QuotationCreationBusyException : Exception;
/// <summary>A required downstream outcome cannot yet be proven.</summary>
public sealed class QuotationCreationOutcomeUnknownException(string message, Exception? inner = null) : Exception(message, inner);
