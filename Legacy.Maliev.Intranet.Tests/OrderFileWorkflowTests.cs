using Legacy.Maliev.Intranet.Contracts;
using Legacy.Maliev.Intranet.Server.Orders;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;

namespace Legacy.Maliev.Intranet.Tests;

public sealed class OrderFileWorkflowTests
{
    [Fact]
    public async Task Upload_WhenLinkingFails_CompensatesLinkedMetadataAndEveryStoredFile()
    {
        var workflow = new OrderFileWorkflow(NullLogger<OrderFileWorkflow>.Instance);
        var stored = new[]
        {
            new StoredOrderFile(11, 84, "maliev.com", "orders/84/one.stl"),
            new StoredOrderFile(12, 84, "maliev.com", "orders/84/two.stl"),
        };
        var unlinked = new List<int>();
        var deleted = new List<int>();

        await Assert.ThrowsAsync<HttpRequestException>(() => workflow.UploadAsync(
            84,
            42,
            Array.Empty<IFormFile>(),
            (_, _, _) => Task.FromResult<IReadOnlyList<StoredOrderFile>>(stored),
            (_, file, _) => file.Id == 12
                ? throw new HttpRequestException("link failed")
                : Task.FromResult(new OrderFileItem(file.Id, 84, file.ObjectName, null)),
            (file, _) =>
            {
                unlinked.Add(file.Id);
                return Task.CompletedTask;
            },
            (file, _) =>
            {
                deleted.Add(file.Id);
                return Task.CompletedTask;
            },
            CancellationToken.None));

        Assert.Equal([11], unlinked);
        Assert.Equal([11, 12], deleted);
    }

    [Fact]
    public async Task Remove_WhenFileIsNotOwnedByOrder_DoesNotCrossEitherBoundary()
    {
        var workflow = new OrderFileWorkflow(NullLogger<OrderFileWorkflow>.Instance);
        var deleteCalls = 0;
        var unlinkCalls = 0;

        var removed = await workflow.RemoveAsync(
            99,
            [new StoredOrderFile(11, 84, "maliev.com", "orders/84/one.stl")],
            (_, _) =>
            {
                deleteCalls++;
                return Task.CompletedTask;
            },
            (_, _) =>
            {
                unlinkCalls++;
                return Task.CompletedTask;
            },
            CancellationToken.None);

        Assert.False(removed);
        Assert.Equal(0, deleteCalls);
        Assert.Equal(0, unlinkCalls);
    }

    [Fact]
    public async Task Upload_CompensationUsesIndependentTokenAndContinuesAfterNonfatalCleanupFailure()
    {
        var workflow = new OrderFileWorkflow(NullLogger<OrderFileWorkflow>.Instance);
        var stored = new[]
        {
            new StoredOrderFile(11, 84, "maliev.com", "orders/84/one.stl"),
            new StoredOrderFile(12, 84, "maliev.com", "orders/84/two.stl"),
        };
        var deleted = new List<int>();
        var cleanupTokens = new List<CancellationToken>();
        using var callerCancellation = new CancellationTokenSource();
        callerCancellation.Cancel();

        var primary = await Assert.ThrowsAsync<HttpRequestException>(() => workflow.UploadAsync(
            84,
            42,
            Array.Empty<IFormFile>(),
            (_, _, _) => Task.FromResult<IReadOnlyList<StoredOrderFile>>(stored),
            (_, file, _) => file.Id == 12
                ? throw new HttpRequestException("primary link failure")
                : Task.FromResult(new OrderFileItem(file.Id, 84, file.ObjectName, null)),
            (_, token) =>
            {
                cleanupTokens.Add(token);
                throw new InvalidOperationException("metadata cleanup failure");
            },
            (file, token) =>
            {
                cleanupTokens.Add(token);
                deleted.Add(file.Id);
                return Task.CompletedTask;
            },
            callerCancellation.Token));

        Assert.Equal("primary link failure", primary.Message);
        Assert.Equal([11, 12], deleted);
        Assert.All(cleanupTokens, token =>
        {
            Assert.True(token.CanBeCanceled);
            Assert.False(token.IsCancellationRequested);
        });
    }
}
