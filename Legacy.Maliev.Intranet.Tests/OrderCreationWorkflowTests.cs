using Legacy.Maliev.Intranet.Contracts;
using Legacy.Maliev.Intranet.Server.Orders;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace Legacy.Maliev.Intranet.Tests;

public sealed class OrderCreationWorkflowTests
{
    private static readonly OrderCreateRequest Input = new(42, "Thai fixture", "requirements", 3, 5, 6, 4, 2, true, false);

    [Fact]
    public async Task Create_CompletesRequiredBoundariesBeforeOptionalNotification()
    {
        var workflow = CreateWorkflow();
        var calls = new List<string>();
        string? attemptKey = null;
        var stored = new StoredOrderFile(0, 0, "maliev.com", "orders/42/fixture.stl");

        var result = await workflow.CreateAsync(
            "workflow-1",
            "fingerprint-1",
            Input,
            "customer@example.com",
            [new FormFile(Stream.Null, 0, 1, "files", "fixture.stl")],
            (_, key, _) => { attemptKey = key; calls.Add("create"); return Task.FromResult(84); },
            (customerId, _, _, key, _) => { Assert.Equal(attemptKey, key); calls.Add($"upload:{customerId}"); return Task.FromResult<IReadOnlyList<StoredOrderFile>>([stored]); },
            (orderId, _, _) => { calls.Add($"link:{orderId}"); return Task.FromResult(11); },
            (orderId, key, _) => { Assert.Equal(attemptKey, key); calls.Add($"status:{orderId}"); return Task.CompletedTask; },
            (email, orderId, _) => { calls.Add($"notify:{email}:{orderId}"); return Task.CompletedTask; },
            (_, _) => Task.CompletedTask,
            (_, _) => Task.CompletedTask,
            (_, _) => Task.CompletedTask,
            CancellationToken.None);

        Assert.Equal(new OrderCreatedResult(84, null), result);
        Assert.True(Guid.TryParse(attemptKey, out _));
        Assert.Equal(
            ["create", "upload:42", "link:84", "status:84", "notify:customer@example.com:84"],
            calls);
    }

    [Fact]
    public async Task Create_WhenRequiredBoundaryFails_CompensatesInReverseWithIndependentTokenAndContinues()
    {
        var workflow = CreateWorkflow();
        var cleanup = new List<string>();
        var cleanupTokens = new List<CancellationToken>();
        var stored = new[]
        {
            new StoredOrderFile(0, 0, "maliev.com", "orders/42/one.stl"),
            new StoredOrderFile(0, 0, "maliev.com", "orders/42/two.stl"),
        };
        using var callerCancellation = new CancellationTokenSource();

        var failure = await Assert.ThrowsAsync<HttpRequestException>(() => workflow.CreateAsync(
            "workflow-2",
            "fingerprint-2",
            Input,
            "customer@example.com",
            [new FormFile(Stream.Null, 0, 1, "files", "fixture.stl")],
            (_, _, _) => Task.FromResult(84),
            (_, _, _, _, _) => Task.FromResult<IReadOnlyList<StoredOrderFile>>(stored),
            (_, file, _) => Task.FromResult(file.ObjectName.EndsWith("one.stl", StringComparison.Ordinal) ? 11 : 12),
            (_, _, _) => { callerCancellation.Cancel(); throw new HttpRequestException("status failed"); },
            (_, _, _) => throw new InvalidOperationException("notification must not run"),
            (fileId, token) =>
            {
                cleanupTokens.Add(token);
                cleanup.Add($"metadata:{fileId}");
                if (fileId == 12) throw new InvalidOperationException("nonfatal cleanup failure");
                return Task.CompletedTask;
            },
            (file, token) => { cleanupTokens.Add(token); cleanup.Add($"stored:{file.ObjectName}"); return Task.CompletedTask; },
            (orderId, token) => { cleanupTokens.Add(token); cleanup.Add($"order:{orderId}"); return Task.CompletedTask; },
            callerCancellation.Token));

        Assert.Equal("status failed", failure.Message);
        Assert.Equal(
            ["metadata:12", "metadata:11", "stored:orders/42/two.stl", "stored:orders/42/one.stl", "order:84"],
            cleanup);
        Assert.All(cleanupTokens, token =>
        {
            Assert.True(token.CanBeCanceled);
            Assert.False(token.IsCancellationRequested);
        });
    }

    [Fact]
    public async Task Create_WhenOptionalNotificationFails_ReturnsWarningWithoutRollback()
    {
        var workflow = CreateWorkflow();
        var rollbackCalls = 0;

        var result = await workflow.CreateAsync(
            "workflow-3",
            "fingerprint-3",
            Input,
            "customer@example.com",
            [],
            (_, _, _) => Task.FromResult(84),
            (_, _, _, _, _) => throw new InvalidOperationException("upload must not run"),
            (_, _, _) => throw new InvalidOperationException("link must not run"),
            (_, _, _) => Task.CompletedTask,
            (_, _, _) => throw new HttpRequestException("notification failed"),
            (_, _) => { rollbackCalls++; return Task.CompletedTask; },
            (_, _) => { rollbackCalls++; return Task.CompletedTask; },
            (_, _) => { rollbackCalls++; return Task.CompletedTask; },
            CancellationToken.None);

        Assert.Equal(84, result.Id);
        Assert.Equal("Order created, but the confirmation notification failed.", result.Warning);
        Assert.Equal(0, rollbackCalls);
    }

    [Fact]
    public async Task ExactRetry_ReplaysCompletedResultWithoutRepeatingAnyBoundary()
    {
        var store = new InMemoryOrderCreationStateStore();
        var workflow = new OrderCreationWorkflow(NullLogger<OrderCreationWorkflow>.Instance, store, TimeProvider.System);
        var calls = new Dictionary<string, int>(StringComparer.Ordinal);
        var file = new FormFile(Stream.Null, 0, 1, "files", "fixture.stl");
        Task<OrderCreatedResult> Execute() => workflow.CreateAsync(
            "workflow-replay", "same-fingerprint", Input, "customer@example.com", [file],
            (_, _, _) => { Increment("create"); return Task.FromResult(84); },
            (_, _, _, _, _) => { Increment("upload"); return Task.FromResult<IReadOnlyList<StoredOrderFile>>([new(0, 0, "bucket", "object")]); },
            (_, _, _) => { Increment("link"); return Task.FromResult(11); },
            (_, _, _) => { Increment("status"); return Task.CompletedTask; },
            (_, _, _) => { Increment("notify"); return Task.CompletedTask; },
            (_, _) => { Increment("delete-metadata"); return Task.CompletedTask; },
            (_, _) => { Increment("delete-stored"); return Task.CompletedTask; },
            (_, _) => { Increment("delete-order"); return Task.CompletedTask; },
            CancellationToken.None);

        var first = await Execute();
        var replay = await Execute();

        Assert.Equal(first, replay);
        Assert.Equal(1, calls["create"]);
        Assert.Equal(1, calls["upload"]);
        Assert.Equal(1, calls["link"]);
        Assert.Equal(1, calls["status"]);
        Assert.Equal(1, calls["notify"]);
        Assert.DoesNotContain(calls.Keys, key => key.StartsWith("delete", StringComparison.Ordinal));

        void Increment(string name) => calls[name] = calls.GetValueOrDefault(name) + 1;
    }

    [Fact]
    public async Task FullyCompensatedRetry_RotatesServerAttemptKeyBeforeCreatingAgain()
    {
        var workflow = CreateWorkflow();
        var attemptKeys = new List<string>();
        var statusCalls = 0;
        Task<OrderCreatedResult> Execute() => workflow.CreateAsync(
            "workflow-compensated", "same-fingerprint", Input with { SendConfirmationEmail = false }, null, [],
            (_, key, _) => { attemptKeys.Add(key); return Task.FromResult(84); },
            (_, _, _, _, _) => Task.FromResult<IReadOnlyList<StoredOrderFile>>([]),
            (_, _, _) => Task.FromResult(0),
            (_, _, _) => ++statusCalls == 1 ? throw new HttpRequestException("definitive status failure") : Task.CompletedTask,
            (_, _, _) => Task.CompletedTask,
            (_, _) => Task.CompletedTask,
            (_, _) => Task.CompletedTask,
            (_, _) => Task.CompletedTask,
            CancellationToken.None);

        await Assert.ThrowsAsync<HttpRequestException>(Execute);
        var result = await Execute();

        Assert.Equal(84, result.Id);
        Assert.Equal(2, attemptKeys.Count);
        Assert.NotEqual(attemptKeys[0], attemptKeys[1]);
    }

    [Fact]
    public async Task UnknownStatusOutcome_IsRetainedWithoutDeleteAndResumesSameAttempt()
    {
        var workflow = CreateWorkflow();
        var createCalls = 0;
        var deleteCalls = 0;
        var statusCalls = 0;
        string? attemptKey = null;
        Task<OrderCreatedResult> Execute() => workflow.CreateAsync(
            "workflow-status-unknown", "same-fingerprint", Input with { SendConfirmationEmail = false }, null, [],
            (_, key, _) => { createCalls++; attemptKey = key; return Task.FromResult(84); },
            (_, _, _, _, _) => Task.FromResult<IReadOnlyList<StoredOrderFile>>([]),
            (_, _, _) => Task.FromResult(0),
            (_, key, _) =>
            {
                Assert.Equal(attemptKey, key);
                if (++statusCalls == 1) throw new OrderCreationOutcomeUnknownException("lost status response");
                return Task.CompletedTask;
            },
            (_, _, _) => Task.CompletedTask,
            (_, _) => { deleteCalls++; return Task.CompletedTask; },
            (_, _) => { deleteCalls++; return Task.CompletedTask; },
            (_, _) => { deleteCalls++; return Task.CompletedTask; },
            CancellationToken.None);

        await Assert.ThrowsAsync<OrderCreationOutcomeUnknownException>(Execute);
        var result = await Execute();

        Assert.Equal(84, result.Id);
        Assert.Equal(1, createCalls);
        Assert.Equal(2, statusCalls);
        Assert.Equal(0, deleteCalls);
    }

    [Fact]
    public async Task UnknownOrderCreateOutcome_ReplaysTheSameServerAttemptKey()
    {
        var workflow = CreateWorkflow();
        var attemptKeys = new List<string>();
        var createCalls = 0;
        var deleteCalls = 0;
        Task<OrderCreatedResult> Execute() => workflow.CreateAsync(
            "workflow-create-unknown", "same-fingerprint", Input with { SendConfirmationEmail = false }, null, [],
            (_, key, _) =>
            {
                attemptKeys.Add(key);
                if (++createCalls == 1) throw new OrderCreationOutcomeUnknownException("lost create response");
                return Task.FromResult(84);
            },
            (_, _, _, _, _) => Task.FromResult<IReadOnlyList<StoredOrderFile>>([]),
            (_, _, _) => Task.FromResult(0),
            (_, _, _) => Task.CompletedTask,
            (_, _, _) => Task.CompletedTask,
            (_, _) => { deleteCalls++; return Task.CompletedTask; },
            (_, _) => { deleteCalls++; return Task.CompletedTask; },
            (_, _) => { deleteCalls++; return Task.CompletedTask; },
            CancellationToken.None);

        await Assert.ThrowsAsync<OrderCreationOutcomeUnknownException>(Execute);
        var result = await Execute();

        Assert.Equal(84, result.Id);
        Assert.Equal(2, attemptKeys.Count);
        Assert.Equal(attemptKeys[0], attemptKeys[1]);
        Assert.Equal(0, deleteCalls);
    }

    [Fact]
    public async Task ServerErrorAfterOrderWrite_ReplaysSameAttemptWithoutCompensation()
    {
        var workflow = CreateWorkflow();
        var keys = new List<string>();
        var createCalls = 0;
        var deleteCalls = 0;
        Task<OrderCreatedResult> Execute() => workflow.CreateAsync(
            "workflow-create-503", "same-fingerprint", Input with { SendConfirmationEmail = false }, null, [],
            (_, key, _) =>
            {
                keys.Add(key);
                if (++createCalls == 1)
                {
                    throw new HttpRequestException("server failed after commit", null, System.Net.HttpStatusCode.ServiceUnavailable);
                }
                return Task.FromResult(84);
            },
            (_, _, _, _, _) => Task.FromResult<IReadOnlyList<StoredOrderFile>>([]),
            (_, _, _) => Task.FromResult(0),
            (_, _, _) => Task.CompletedTask,
            (_, _, _) => Task.CompletedTask,
            (_, _) => { deleteCalls++; return Task.CompletedTask; },
            (_, _) => { deleteCalls++; return Task.CompletedTask; },
            (_, _) => { deleteCalls++; return Task.CompletedTask; },
            CancellationToken.None);

        await Assert.ThrowsAsync<OrderCreationOutcomeUnknownException>(Execute);
        var result = await Execute();

        Assert.Equal(84, result.Id);
        Assert.Equal(keys[0], keys[1]);
        Assert.Equal(0, deleteCalls);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CancellationOrTimeoutAfterWrite_RetainsCheckpointWithoutCompensation(bool timeout)
    {
        var workflow = CreateWorkflow();
        var createCalls = 0;
        var statusCalls = 0;
        var deleteCalls = 0;
        string? attemptKey = null;
        Task<OrderCreatedResult> Execute() => workflow.CreateAsync(
            $"workflow-interrupted-{timeout}", "same-fingerprint", Input with { SendConfirmationEmail = false }, null, [],
            (_, key, _) => { createCalls++; attemptKey = key; return Task.FromResult(84); },
            (_, _, _, _, _) => Task.FromResult<IReadOnlyList<StoredOrderFile>>([]),
            (_, _, _) => Task.FromResult(0),
            (_, key, _) =>
            {
                Assert.Equal(attemptKey, key);
                if (++statusCalls == 1)
                {
                    if (timeout) throw new Polly.Timeout.TimeoutRejectedException("status timed out");
                    throw new OperationCanceledException("request cancelled");
                }
                return Task.CompletedTask;
            },
            (_, _, _) => Task.CompletedTask,
            (_, _) => { deleteCalls++; return Task.CompletedTask; },
            (_, _) => { deleteCalls++; return Task.CompletedTask; },
            (_, _) => { deleteCalls++; return Task.CompletedTask; },
            CancellationToken.None);

        await Assert.ThrowsAsync<OrderCreationOutcomeUnknownException>(Execute);
        var result = await Execute();

        Assert.Equal(84, result.Id);
        Assert.Equal(1, createCalls);
        Assert.Equal(2, statusCalls);
        Assert.Equal(0, deleteCalls);
    }

    [Fact]
    public async Task UnknownUploadOutcome_RetriesSamePersistedAttemptWithoutCompensation()
    {
        var workflow = CreateWorkflow();
        var uploadCalls = 0;
        var deleteCalls = 0;
        var attemptKeys = new List<string>();
        Task<OrderCreatedResult> Execute() => workflow.CreateAsync(
            "workflow-upload-unknown", "same-fingerprint", Input, "customer@example.com",
            [new FormFile(Stream.Null, 0, 1, "files", "fixture.stl")],
            (_, _, _) => Task.FromResult(84),
            (_, _, _, key, _) =>
            {
                attemptKeys.Add(key);
                if (++uploadCalls == 1) throw new OrderCreationOutcomeUnknownException("lost upload response");
                return Task.FromResult<IReadOnlyList<StoredOrderFile>>([new(0, 0, "maliev.com", "orders/42/fixture.stl")]);
            },
            (_, _, _) => Task.FromResult(0),
            (_, _, _) => Task.CompletedTask,
            (_, _, _) => Task.CompletedTask,
            (_, _) => { deleteCalls++; return Task.CompletedTask; },
            (_, _) => { deleteCalls++; return Task.CompletedTask; },
            (_, _) => { deleteCalls++; return Task.CompletedTask; },
            CancellationToken.None);

        await Assert.ThrowsAsync<OrderCreationOutcomeUnknownException>(Execute);
        var result = await Execute();

        Assert.Equal(84, result.Id);
        Assert.Equal(2, uploadCalls);
        Assert.Equal(attemptKeys[0], attemptKeys[1]);
        Assert.Equal(0, deleteCalls);
    }

    [Fact]
    public async Task ServerErrorAfterUploadWrite_RetriesSamePersistedAttemptWithoutCompensation()
    {
        var workflow = CreateWorkflow();
        var uploadCalls = 0;
        var deleteCalls = 0;
        var attemptKeys = new List<string>();
        Task<OrderCreatedResult> Execute() => workflow.CreateAsync(
            "workflow-upload-503", "same-fingerprint", Input, "customer@example.com",
            [new FormFile(Stream.Null, 0, 1, "files", "fixture.stl")],
            (_, _, _) => Task.FromResult(84),
            (_, _, _, key, _) =>
            {
                attemptKeys.Add(key);
                if (++uploadCalls == 1)
                {
                    throw new HttpRequestException("server failed after upload", null, System.Net.HttpStatusCode.BadGateway);
                }
                return Task.FromResult<IReadOnlyList<StoredOrderFile>>([new(0, 0, "maliev.com", "orders/42/fixture.stl")]);
            },
            (_, _, _) => Task.FromResult(0),
            (_, _, _) => Task.CompletedTask,
            (_, _, _) => Task.CompletedTask,
            (_, _) => { deleteCalls++; return Task.CompletedTask; },
            (_, _) => { deleteCalls++; return Task.CompletedTask; },
            (_, _) => { deleteCalls++; return Task.CompletedTask; },
            CancellationToken.None);

        await Assert.ThrowsAsync<OrderCreationOutcomeUnknownException>(Execute);
        var result = await Execute();

        Assert.Equal(84, result.Id);
        Assert.Equal(2, uploadCalls);
        Assert.Equal(attemptKeys[0], attemptKeys[1]);
        Assert.Equal(0, deleteCalls);
    }

    [Fact]
    public async Task UploadRetryAcrossUtcMidnight_ReusesPersistedAttemptAndPath()
    {
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 7, 17, 23, 59, 0, TimeSpan.Zero));
        var workflow = CreateWorkflow(clock);
        var attemptKeys = new List<string>();
        var uploadPaths = new List<string>();
        var uploadCalls = 0;
        Task<OrderCreatedResult> Execute() => workflow.CreateAsync(
            "workflow-upload-midnight", "same-fingerprint", Input, "customer@example.com",
            [new FormFile(Stream.Null, 0, 1, "files", "fixture.stl")],
            (_, _, _) => Task.FromResult(84),
            (_, _, path, key, _) =>
            {
                uploadPaths.Add(path);
                attemptKeys.Add(key);
                if (++uploadCalls == 1) throw new OrderCreationOutcomeUnknownException("lost upload response");
                return Task.FromResult<IReadOnlyList<StoredOrderFile>>([new(0, 0, "maliev.com", "orders/42/fixture.stl")]);
            },
            (_, _, _) => Task.FromResult(11),
            (_, _, _) => Task.CompletedTask,
            (_, _, _) => Task.CompletedTask,
            (_, _) => Task.CompletedTask,
            (_, _) => Task.CompletedTask,
            (_, _) => Task.CompletedTask,
            CancellationToken.None);

        await Assert.ThrowsAsync<OrderCreationOutcomeUnknownException>(Execute);
        clock.Advance(TimeSpan.FromDays(1));
        var result = await Execute();

        Assert.Equal(84, result.Id);
        Assert.Equal(attemptKeys[0], attemptKeys[1]);
        Assert.Equal(uploadPaths[0], uploadPaths[1]);
        Assert.Equal("uploads/42/2026-07-17", uploadPaths[0]);
    }

    private static OrderCreationWorkflow CreateWorkflow(TimeProvider? timeProvider = null) => new(
        NullLogger<OrderCreationWorkflow>.Instance,
        new InMemoryOrderCreationStateStore(),
        timeProvider ?? TimeProvider.System);
}
