using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using Legacy.Maliev.Intranet.Auth;
using Legacy.Maliev.Intranet.Contracts;
using Legacy.Maliev.Intranet.Server.Orders;
using Legacy.Maliev.Intranet.Server.Quotations;
using Microsoft.Extensions.Logging.Abstractions;
using StackExchange.Redis;
using Testcontainers.Redis;

namespace Legacy.Maliev.Intranet.Tests;

public sealed class RedisProcurementStateBehaviorTests : IAsyncLifetime
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly RedisContainer redis = new RedisBuilder("redis:7.4.5-alpine").Build();

    public async Task InitializeAsync() => await redis.StartAsync();

    public async Task DisposeAsync() => await redis.DisposeAsync();

    [Fact]
    public async Task OrderState_RoundTripsUpdatesRemovesAndExpiresAfterSevenDays()
    {
        using var resources = await CreateResourcesAsync();
        var store = new RedisOrderCreationStateStore(resources, NullLogger<RedisOrderCreationStateStore>.Instance);
        var key = Guid.NewGuid().ToString("N");
        var first = new OrderCreationCheckpoint(
            "fingerprint-1", "attempt-1", OrderCreationPhase.Active, null, [], [], 0, null, "orders/attempt-1");
        var updated = new OrderCreationCheckpoint(
            "fingerprint-1", "attempt-1", OrderCreationPhase.Completed, 41,
            [new StoredOrderFile(88, 41, "maliev.com", "orders/41/model.step")], [88], 1,
            new OrderCreatedResult(41, "notification delayed"), "orders/attempt-1");

        await store.SetAsync(key, first, TestContext.Current.CancellationToken);
        AssertJsonEqual(first, await store.GetAsync(key, TestContext.Current.CancellationToken));
        await AssertSevenDayExpiryAsync(resources.Redis.GetDatabase(), $"legacy:intranet:order-create:state:{key}");

        await store.SetAsync(key, updated, TestContext.Current.CancellationToken);
        AssertJsonEqual(updated, await store.GetAsync(key, TestContext.Current.CancellationToken));
        await store.RemoveAsync(key, TestContext.Current.CancellationToken);
        Assert.Null(await store.GetAsync(key, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task QuotationState_RoundTripsUpdatesAndExpiresAfterSevenDays()
    {
        using var resources = await CreateResourcesAsync();
        var store = new RedisQuotationCreationStateStore(resources, NullLogger<RedisQuotationCreationStateStore>.Instance);
        var key = Guid.NewGuid().ToString("N");
        var first = new QuotationCreationCheckpoint(
            "fingerprint-2", "attempt-2", QuotationCreationPhase.Active, null, 0, [], 0, null);
        var updated = new QuotationCreationCheckpoint(
            "fingerprint-2", "attempt-2", QuotationCreationPhase.Completed, 51, 2, [41, 42], 2,
            new QuotationCreatedResult(51, null));

        await store.SetAsync(key, first, TestContext.Current.CancellationToken);
        AssertJsonEqual(first, await store.GetAsync(key, TestContext.Current.CancellationToken));
        await AssertSevenDayExpiryAsync(resources.Redis.GetDatabase(), $"legacy:intranet:quotation-create:state:{key}");

        await store.SetAsync(key, updated, TestContext.Current.CancellationToken);
        AssertJsonEqual(updated, await store.GetAsync(key, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task OrderLock_SerializesSameKeyAndReleasesOwnedLease()
    {
        using var resources = await CreateResourcesAsync();
        var store = new RedisOrderCreationStateStore(resources, NullLogger<RedisOrderCreationStateStore>.Instance);
        var key = Guid.NewGuid().ToString("N");
        var firstEntered = NewSignal();
        var releaseFirst = NewSignal();
        var secondEntered = NewSignal();

        var first = store.ExecuteLockedAsync(key, async cancellationToken =>
        {
            firstEntered.TrySetResult();
            await releaseFirst.Task.WaitAsync(cancellationToken);
            return 1;
        }, TestContext.Current.CancellationToken);
        await firstEntered.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        var second = store.ExecuteLockedAsync(key, _ =>
        {
            secondEntered.TrySetResult();
            return Task.FromResult(2);
        }, TestContext.Current.CancellationToken);

        await Task.Delay(250, TestContext.Current.CancellationToken);
        Assert.False(secondEntered.Task.IsCompleted);
        var database = resources.Redis.GetDatabase();
        var lockKey = $"legacy:intranet:order-create:lock:{key}";
        Assert.True(await database.KeyExistsAsync(lockKey));
        var ttl = await database.KeyTimeToLiveAsync(lockKey);
        Assert.NotNull(ttl);
        Assert.InRange(ttl.Value, TimeSpan.FromSeconds(90), TimeSpan.FromMinutes(2));

        releaseFirst.TrySetResult();
        Assert.Equal(1, await first);
        Assert.Equal(2, await second);
        Assert.True(secondEntered.Task.IsCompletedSuccessfully);
        Assert.False(await database.KeyExistsAsync(lockKey));
    }

    [Fact]
    public async Task QuotationLock_SerializesSameKeyAndReleasesOwnedLease()
    {
        using var resources = await CreateResourcesAsync();
        var store = new RedisQuotationCreationStateStore(resources, NullLogger<RedisQuotationCreationStateStore>.Instance);
        var key = Guid.NewGuid().ToString("N");
        var firstEntered = NewSignal();
        var releaseFirst = NewSignal();
        var secondEntered = NewSignal();

        var first = store.ExecuteLockedAsync(key, async cancellationToken =>
        {
            firstEntered.TrySetResult();
            await releaseFirst.Task.WaitAsync(cancellationToken);
            return 1;
        }, TestContext.Current.CancellationToken);
        await firstEntered.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        var second = store.ExecuteLockedAsync(key, _ =>
        {
            secondEntered.TrySetResult();
            return Task.FromResult(2);
        }, TestContext.Current.CancellationToken);

        await Task.Delay(250, TestContext.Current.CancellationToken);
        Assert.False(secondEntered.Task.IsCompleted);
        var database = resources.Redis.GetDatabase();
        var lockKey = $"legacy:intranet:quotation-create:lock:{key}";
        Assert.True(await database.KeyExistsAsync(lockKey));

        releaseFirst.TrySetResult();
        Assert.Equal(1, await first);
        Assert.Equal(2, await second);
        Assert.False(await database.KeyExistsAsync(lockKey));
    }

    [Fact]
    public async Task OrderLock_OperationFailureReleasesLeaseForImmediateRetry()
    {
        using var resources = await CreateResourcesAsync();
        var store = new RedisOrderCreationStateStore(resources, NullLogger<RedisOrderCreationStateStore>.Instance);
        var key = Guid.NewGuid().ToString("N");

        await Assert.ThrowsAsync<InvalidOperationException>(() => store.ExecuteLockedAsync<int>(
            key, _ => throw new InvalidOperationException("test failure"), TestContext.Current.CancellationToken));

        Assert.False(await resources.Redis.GetDatabase().KeyExistsAsync($"legacy:intranet:order-create:lock:{key}"));
        Assert.Equal(7, await store.ExecuteLockedAsync(key, _ => Task.FromResult(7), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task QuotationLock_OperationFailureReleasesLeaseForImmediateRetry()
    {
        using var resources = await CreateResourcesAsync();
        var store = new RedisQuotationCreationStateStore(resources, NullLogger<RedisQuotationCreationStateStore>.Instance);
        var key = Guid.NewGuid().ToString("N");

        await Assert.ThrowsAsync<InvalidOperationException>(() => store.ExecuteLockedAsync<int>(
            key, _ => throw new InvalidOperationException("test failure"), TestContext.Current.CancellationToken));

        Assert.False(await resources.Redis.GetDatabase().KeyExistsAsync($"legacy:intranet:quotation-create:lock:{key}"));
        Assert.Equal(9, await store.ExecuteLockedAsync(key, _ => Task.FromResult(9), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task StateStores_RejectCorruptDurableJsonInsteadOfInventingWorkflowState()
    {
        using var resources = await CreateResourcesAsync();
        var database = resources.Redis.GetDatabase();
        var orderKey = Guid.NewGuid().ToString("N");
        var quotationKey = Guid.NewGuid().ToString("N");
        await database.StringSetAsync($"legacy:intranet:order-create:state:{orderKey}", "{invalid");
        await database.StringSetAsync($"legacy:intranet:quotation-create:state:{quotationKey}", "{invalid");
        var orderStore = new RedisOrderCreationStateStore(resources, NullLogger<RedisOrderCreationStateStore>.Instance);
        var quotationStore = new RedisQuotationCreationStateStore(resources, NullLogger<RedisQuotationCreationStateStore>.Instance);

        await Assert.ThrowsAsync<JsonException>(() => orderStore.GetAsync(orderKey, TestContext.Current.CancellationToken));
        await Assert.ThrowsAsync<JsonException>(() => quotationStore.GetAsync(quotationKey, TestContext.Current.CancellationToken));
    }

    private async Task<LegacyDataProtectionResources> CreateResourcesAsync()
    {
        var connection = await ConnectionMultiplexer.ConnectAsync(redis.GetConnectionString());
        return new LegacyDataProtectionResources(CreateCertificate(), connection);
    }

    private static X509Certificate2 CreateCertificate()
    {
        using var key = RSA.Create(2048);
        var request = new CertificateRequest(
            "CN=Legacy.Maliev.Intranet.ProcurementStateTests",
            key,
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1);
        return request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddDays(1));
    }

    private static TaskCompletionSource NewSignal() => new(TaskCreationOptions.RunContinuationsAsynchronously);

    private static async Task AssertSevenDayExpiryAsync(IDatabase database, RedisKey key)
    {
        var ttl = await database.KeyTimeToLiveAsync(key);
        Assert.NotNull(ttl);
        Assert.InRange(ttl.Value, TimeSpan.FromDays(6.95), TimeSpan.FromDays(7));
    }

    private static void AssertJsonEqual<T>(T expected, T? actual)
    {
        Assert.NotNull(actual);
        Assert.Equal(JsonSerializer.Serialize(expected, JsonOptions), JsonSerializer.Serialize(actual, JsonOptions));
    }
}
