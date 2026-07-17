using System.Collections.Concurrent;
using System.Text.Json;
using Legacy.Maliev.Intranet.Auth;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace Legacy.Maliev.Intranet.Server.Orders;

/// <summary>Durable state and mutual exclusion for one cross-service order-creation workflow.</summary>
public interface IOrderCreationStateStore
{
    /// <summary>Executes one workflow identity under a distributed mutual-exclusion lease.</summary>
    Task<T> ExecuteLockedAsync<T>(string key, Func<CancellationToken, Task<T>> operation, CancellationToken cancellationToken);

    /// <summary>Reads a durable workflow checkpoint.</summary>
    Task<OrderCreationCheckpoint?> GetAsync(string key, CancellationToken cancellationToken);

    /// <summary>Persists a durable workflow checkpoint.</summary>
    Task SetAsync(string key, OrderCreationCheckpoint checkpoint, CancellationToken cancellationToken);

    /// <summary>Removes a fully compensated workflow so the same identity may safely start again.</summary>
    Task RemoveAsync(string key, CancellationToken cancellationToken);
}

/// <summary>Persisted phases that distinguish safe replay from uncertain cross-service outcomes.</summary>
public enum OrderCreationPhase
{
    /// <summary>The required workflow is ready to run or resume.</summary>
    Active,
    /// <summary>An upload request was sent but its result is not durably known.</summary>
    Uploading,
    /// <summary>The initial status outcome requires reconciliation.</summary>
    StatusUncertain,
    /// <summary>Every required order boundary has committed.</summary>
    RequiredCommitted,
    /// <summary>The at-most-once optional notification was started.</summary>
    NotificationStarted,
    /// <summary>The exact safe browser result is durable and replayable.</summary>
    Completed,
    /// <summary>A definitive failure is being fully compensated.</summary>
    Compensating,
}

/// <summary>Minimal durable checkpoint; it contains no credentials or customer contact data.</summary>
public sealed record OrderCreationCheckpoint(
    string Fingerprint,
    string DownstreamAttemptId,
    OrderCreationPhase Phase,
    int? OrderId,
    IReadOnlyList<StoredOrderFile> StoredFiles,
    IReadOnlyList<int> LinkedFileIds,
    int LinkedFileCount,
    Legacy.Maliev.Intranet.Contracts.OrderCreatedResult? Result,
    string? UploadPath = null);

/// <summary>Redis implementation backed by the Intranet's existing shared connection.</summary>
public sealed class RedisOrderCreationStateStore(
    LegacyDataProtectionResources resources,
    ILogger<RedisOrderCreationStateStore> logger) : IOrderCreationStateStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly TimeSpan StateLifetime = TimeSpan.FromDays(7);
    private static readonly TimeSpan LockLifetime = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan RenewInterval = TimeSpan.FromSeconds(20);
    private static readonly TimeSpan LockWait = TimeSpan.FromSeconds(10);
    private readonly IDatabase database = resources.Redis.GetDatabase();

    /// <inheritdoc />
    public async Task<T> ExecuteLockedAsync<T>(string key, Func<CancellationToken, Task<T>> operation, CancellationToken cancellationToken)
    {
        var lockKey = LockKey(key);
        var owner = Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32));
        var deadline = DateTimeOffset.UtcNow + LockWait;
        while (!await database.StringSetAsync(lockKey, owner, LockLifetime, When.NotExists))
        {
            if (DateTimeOffset.UtcNow >= deadline) throw new OrderCreationBusyException();
            await Task.Delay(TimeSpan.FromMilliseconds(100), cancellationToken);
        }

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
                exception => logger.LogWarning(exception, "Redis order-creation lease release failed; expiry will release it."));
    }

    /// <inheritdoc />
    public async Task<OrderCreationCheckpoint?> GetAsync(string key, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var value = await database.StringGetAsync(StateKey(key));
        return value.IsNullOrEmpty ? null : JsonSerializer.Deserialize<OrderCreationCheckpoint>((string)value!, JsonOptions);
    }

    /// <inheritdoc />
    public async Task SetAsync(string key, OrderCreationCheckpoint checkpoint, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await database.StringSetAsync(StateKey(key), JsonSerializer.Serialize(checkpoint, JsonOptions), StateLifetime);
    }

    /// <inheritdoc />
    public async Task RemoveAsync(string key, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await database.KeyDeleteAsync(StateKey(key));
    }

    private static RedisKey StateKey(string key) => $"legacy:intranet:order-create:state:{key}";
    private static RedisKey LockKey(string key) => $"legacy:intranet:order-create:lock:{key}";
}

/// <summary>Renews a distributed lease, cancels work on ownership loss, and never masks work with unlock failure.</summary>
public static class OrderCreationLeaseGuard
{
    /// <summary>Runs work while the caller-owned lease can be renewed.</summary>
    public static async Task<T> ExecuteAsync<T>(
        Func<Task<bool>> renew,
        Func<Task> release,
        Func<CancellationToken, Task<T>> operation,
        TimeSpan renewInterval,
        CancellationToken cancellationToken,
        Action<Exception>? releaseFailure = null)
    {
        using var leaseLost = new CancellationTokenSource();
        using var operationCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, leaseLost.Token);
        using var stopRenewal = new CancellationTokenSource();
        var renewal = RenewAsync(renew, renewInterval, leaseLost, stopRenewal.Token);
        try
        {
            T result;
            try
            {
                result = await operation(operationCancellation.Token);
            }
            catch (OperationCanceledException exception) when (
                leaseLost.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
            {
                throw new OrderCreationOutcomeUnknownException(
                    "The order-creation lease was lost; the durable checkpoint was retained.",
                    exception);
            }

            if (leaseLost.IsCancellationRequested)
            {
                throw new OrderCreationOutcomeUnknownException(
                    "The order-creation lease was lost; the durable checkpoint was retained.");
            }
            return result;
        }
        finally
        {
            stopRenewal.Cancel();
            try { await renewal; } catch (OperationCanceledException) { }
            try
            {
                await release().WaitAsync(TimeSpan.FromSeconds(2), CancellationToken.None);
            }
            catch (Exception exception) when (exception is not OutOfMemoryException and not StackOverflowException and not AccessViolationException)
            {
                // Durable state and expiry preserve safety; unlock transport failure must not replace the operation result.
                releaseFailure?.Invoke(exception);
            }
        }
    }

    private static async Task RenewAsync(
        Func<Task<bool>> renew,
        TimeSpan interval,
        CancellationTokenSource leaseLost,
        CancellationToken cancellationToken)
    {
        while (true)
        {
            await Task.Delay(interval, cancellationToken);
            try
            {
                if (await renew()) continue;
            }
            catch (Exception exception) when (exception is not OutOfMemoryException and not StackOverflowException and not AccessViolationException)
            {
                // Any inability to prove ownership is treated as lease loss.
            }
            leaseLost.Cancel();
            return;
        }
    }
}

/// <summary>Process-local testing implementation with the same serialization-free semantics.</summary>
public sealed class InMemoryOrderCreationStateStore : IOrderCreationStateStore
{
    private readonly ConcurrentDictionary<string, OrderCreationCheckpoint> states = new(StringComparer.Ordinal);
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
    public Task<OrderCreationCheckpoint?> GetAsync(string key, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(states.TryGetValue(key, out var state) ? state : null);
    }

    /// <inheritdoc />
    public Task SetAsync(string key, OrderCreationCheckpoint checkpoint, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        states[key] = checkpoint;
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task RemoveAsync(string key, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        states.TryRemove(key, out _);
        return Task.CompletedTask;
    }
}

/// <summary>The same workflow identity was used with different browser inputs.</summary>
public sealed class OrderCreationConflictException : Exception;

/// <summary>Another instance is still processing this workflow identity.</summary>
public sealed class OrderCreationBusyException : Exception;

/// <summary>A downstream outcome cannot yet be proven and therefore must not be compensated.</summary>
public sealed class OrderCreationOutcomeUnknownException(string message, Exception? inner = null) : Exception(message, inner);
