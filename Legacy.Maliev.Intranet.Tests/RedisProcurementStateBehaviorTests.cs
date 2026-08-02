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

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class RedisProcurementStateCollection : ICollectionFixture<RedisProcurementStateFixture>
{
    public const string Name = "Redis procurement state";
}

public sealed class RedisProcurementStateFixture : IAsyncLifetime
{
    private readonly RedisContainer redis = new RedisBuilder("redis:7.4.5-alpine").Build();

    public string ConnectionString => redis.GetConnectionString();

    public Task InitializeAsync() => redis.StartAsync();

    public Task DisposeAsync() => redis.DisposeAsync().AsTask();
}

[Collection(RedisProcurementStateCollection.Name)]
public sealed class RedisProcurementStateBehaviorTests(RedisProcurementStateFixture fixture)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly TimeSpan CompletionTimeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan ContentionProbeTimeout = TimeSpan.FromMilliseconds(750);

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
    public async Task OrderLock_SerializesIndependentConnectionsAndAllowsBoundedRetry()
    {
        using var ownerResources = await CreateResourcesAsync();
        using var contenderResources = await CreateResourcesAsync();
        var ownerStore = new RedisOrderCreationStateStore(ownerResources, NullLogger<RedisOrderCreationStateStore>.Instance);
        var contenderStore = new RedisOrderCreationStateStore(contenderResources, NullLogger<RedisOrderCreationStateStore>.Instance);
        var key = Guid.NewGuid().ToString("N");
        var ownerEntered = NewSignal();
        var releaseOwner = NewSignal();
        var contenderEntered = NewSignal();

        var owner = ownerStore.ExecuteLockedAsync(key, async cancellationToken =>
        {
            ownerEntered.TrySetResult();
            await releaseOwner.Task.WaitAsync(cancellationToken);
            return 1;
        }, TestContext.Current.CancellationToken);
        await ownerEntered.Task.WaitAsync(CompletionTimeout, TestContext.Current.CancellationToken);

        using (var blocked = new CancellationTokenSource(ContentionProbeTimeout))
        {
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => contenderStore.ExecuteLockedAsync(key, _ =>
            {
                contenderEntered.TrySetResult();
                return Task.FromResult(2);
            }, blocked.Token).WaitAsync(CompletionTimeout));
        }
        Assert.False(contenderEntered.Task.IsCompleted);

        var database = contenderResources.Redis.GetDatabase();
        var lockKey = $"legacy:intranet:order-create:lock:{key}";
        Assert.True(await database.KeyExistsAsync(lockKey));
        var ttl = await database.KeyTimeToLiveAsync(lockKey);
        Assert.NotNull(ttl);
        Assert.InRange(ttl.Value, TimeSpan.FromSeconds(90), TimeSpan.FromMinutes(2));

        releaseOwner.TrySetResult();
        Assert.Equal(1, await owner.WaitAsync(CompletionTimeout));
        Assert.Equal(2, await contenderStore.ExecuteLockedAsync(key, _ => Task.FromResult(2), TestContext.Current.CancellationToken).WaitAsync(CompletionTimeout));
        Assert.False(await database.KeyExistsAsync(lockKey));
    }

    [Fact]
    public async Task QuotationLock_SerializesIndependentConnectionsAndAllowsBoundedRetry()
    {
        using var ownerResources = await CreateResourcesAsync();
        using var contenderResources = await CreateResourcesAsync();
        var ownerStore = new RedisQuotationCreationStateStore(ownerResources, NullLogger<RedisQuotationCreationStateStore>.Instance);
        var contenderStore = new RedisQuotationCreationStateStore(contenderResources, NullLogger<RedisQuotationCreationStateStore>.Instance);
        var key = Guid.NewGuid().ToString("N");
        var ownerEntered = NewSignal();
        var releaseOwner = NewSignal();
        var contenderEntered = NewSignal();

        var owner = ownerStore.ExecuteLockedAsync(key, async cancellationToken =>
        {
            ownerEntered.TrySetResult();
            await releaseOwner.Task.WaitAsync(cancellationToken);
            return 1;
        }, TestContext.Current.CancellationToken);
        await ownerEntered.Task.WaitAsync(CompletionTimeout, TestContext.Current.CancellationToken);

        using (var blocked = new CancellationTokenSource(ContentionProbeTimeout))
        {
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => contenderStore.ExecuteLockedAsync(key, _ =>
            {
                contenderEntered.TrySetResult();
                return Task.FromResult(2);
            }, blocked.Token).WaitAsync(CompletionTimeout));
        }
        Assert.False(contenderEntered.Task.IsCompleted);

        var database = contenderResources.Redis.GetDatabase();
        var lockKey = $"legacy:intranet:quotation-create:lock:{key}";
        Assert.True(await database.KeyExistsAsync(lockKey));

        releaseOwner.TrySetResult();
        Assert.Equal(1, await owner.WaitAsync(CompletionTimeout));
        Assert.Equal(2, await contenderStore.ExecuteLockedAsync(key, _ => Task.FromResult(2), TestContext.Current.CancellationToken).WaitAsync(CompletionTimeout));
        Assert.False(await database.KeyExistsAsync(lockKey));
    }

    [Fact]
    public async Task OrderLock_StaleOwnerCannotDeleteReplacementLease()
    {
        using var resources = await CreateResourcesAsync();
        var store = new RedisOrderCreationStateStore(resources, NullLogger<RedisOrderCreationStateStore>.Instance);
        var key = Guid.NewGuid().ToString("N");
        var lockKey = $"legacy:intranet:order-create:lock:{key}";
        var entered = NewSignal();
        var release = NewSignal();
        var operation = store.ExecuteLockedAsync(key, async cancellationToken =>
        {
            entered.TrySetResult();
            await release.Task.WaitAsync(cancellationToken);
            return 1;
        }, TestContext.Current.CancellationToken);
        await entered.Task.WaitAsync(CompletionTimeout, TestContext.Current.CancellationToken);

        var database = resources.Redis.GetDatabase();
        var originalOwner = await database.StringGetAsync(lockKey);
        Assert.False(originalOwner.IsNullOrEmpty);
        Assert.True(await database.StringSetAsync(lockKey, "replacement-owner", TimeSpan.FromMinutes(2), When.Always));

        release.TrySetResult();
        Assert.Equal(1, await operation.WaitAsync(CompletionTimeout));
        Assert.Equal("replacement-owner", (string?)await database.StringGetAsync(lockKey));
        Assert.True(await database.KeyDeleteAsync(lockKey));
    }

    [Fact]
    public async Task QuotationLock_StaleOwnerCannotDeleteReplacementLease()
    {
        using var resources = await CreateResourcesAsync();
        var store = new RedisQuotationCreationStateStore(resources, NullLogger<RedisQuotationCreationStateStore>.Instance);
        var key = Guid.NewGuid().ToString("N");
        var lockKey = $"legacy:intranet:quotation-create:lock:{key}";
        var entered = NewSignal();
        var release = NewSignal();
        var operation = store.ExecuteLockedAsync(key, async cancellationToken =>
        {
            entered.TrySetResult();
            await release.Task.WaitAsync(cancellationToken);
            return 1;
        }, TestContext.Current.CancellationToken);
        await entered.Task.WaitAsync(CompletionTimeout, TestContext.Current.CancellationToken);

        var database = resources.Redis.GetDatabase();
        var originalOwner = await database.StringGetAsync(lockKey);
        Assert.False(originalOwner.IsNullOrEmpty);
        Assert.True(await database.StringSetAsync(lockKey, "replacement-owner", TimeSpan.FromMinutes(2), When.Always));

        release.TrySetResult();
        Assert.Equal(1, await operation.WaitAsync(CompletionTimeout));
        Assert.Equal("replacement-owner", (string?)await database.StringGetAsync(lockKey));
        Assert.True(await database.KeyDeleteAsync(lockKey));
    }

    [Fact]
    public async Task OrderLock_OperationFailureReleasesLeaseForImmediateRetry()
    {
        using var resources = await CreateResourcesAsync();
        var store = new RedisOrderCreationStateStore(resources, NullLogger<RedisOrderCreationStateStore>.Instance);
        var key = Guid.NewGuid().ToString("N");

        await Assert.ThrowsAsync<InvalidOperationException>(() => store.ExecuteLockedAsync<int>(
            key, _ => throw new InvalidOperationException("test failure"), TestContext.Current.CancellationToken).WaitAsync(CompletionTimeout));

        Assert.False(await resources.Redis.GetDatabase().KeyExistsAsync($"legacy:intranet:order-create:lock:{key}"));
        Assert.Equal(7, await store.ExecuteLockedAsync(key, _ => Task.FromResult(7), TestContext.Current.CancellationToken).WaitAsync(CompletionTimeout));
    }

    [Fact]
    public async Task QuotationLock_OperationFailureReleasesLeaseForImmediateRetry()
    {
        using var resources = await CreateResourcesAsync();
        var store = new RedisQuotationCreationStateStore(resources, NullLogger<RedisQuotationCreationStateStore>.Instance);
        var key = Guid.NewGuid().ToString("N");

        await Assert.ThrowsAsync<InvalidOperationException>(() => store.ExecuteLockedAsync<int>(
            key, _ => throw new InvalidOperationException("test failure"), TestContext.Current.CancellationToken).WaitAsync(CompletionTimeout));

        Assert.False(await resources.Redis.GetDatabase().KeyExistsAsync($"legacy:intranet:quotation-create:lock:{key}"));
        Assert.Equal(9, await store.ExecuteLockedAsync(key, _ => Task.FromResult(9), TestContext.Current.CancellationToken).WaitAsync(CompletionTimeout));
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

        await Assert.ThrowsAsync<JsonException>(() => orderStore.GetAsync(orderKey, TestContext.Current.CancellationToken).WaitAsync(CompletionTimeout));
        await Assert.ThrowsAsync<JsonException>(() => quotationStore.GetAsync(quotationKey, TestContext.Current.CancellationToken).WaitAsync(CompletionTimeout));
    }

    private async Task<LegacyDataProtectionResources> CreateResourcesAsync()
    {
        var connection = await ConnectionMultiplexer.ConnectAsync(fixture.ConnectionString).WaitAsync(CompletionTimeout);
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
        var ttl = await database.KeyTimeToLiveAsync(key).WaitAsync(CompletionTimeout);
        Assert.NotNull(ttl);
        Assert.InRange(ttl.Value, TimeSpan.FromDays(6.95), TimeSpan.FromDays(7));
    }

    private static void AssertJsonEqual<T>(T expected, T? actual)
    {
        Assert.NotNull(actual);
        Assert.Equal(JsonSerializer.Serialize(expected, JsonOptions), JsonSerializer.Serialize(actual, JsonOptions));
    }
}
