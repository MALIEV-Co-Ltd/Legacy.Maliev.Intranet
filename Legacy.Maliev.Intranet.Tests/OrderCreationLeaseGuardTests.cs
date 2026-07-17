using Legacy.Maliev.Intranet.Server.Orders;

namespace Legacy.Maliev.Intranet.Tests;

public sealed class OrderCreationLeaseGuardTests
{
    [Fact]
    public async Task LeaseLoss_CancelsExecutorAndReturnsUnknownOutcome()
    {
        var operationCancelled = false;
        var releaseCalls = 0;

        var exception = await Assert.ThrowsAsync<OrderCreationOutcomeUnknownException>(() =>
            OrderCreationLeaseGuard.ExecuteAsync(
                () => Task.FromResult(false),
                () => { releaseCalls++; return Task.CompletedTask; },
                async token =>
                {
                    try { await Task.Delay(TimeSpan.FromMinutes(1), token); }
                    catch (OperationCanceledException) { operationCancelled = true; throw; }
                    return 84;
                },
                TimeSpan.FromMilliseconds(5),
                CancellationToken.None));

        Assert.Contains("lease was lost", exception.Message, StringComparison.Ordinal);
        Assert.True(operationCancelled);
        Assert.Equal(1, releaseCalls);
    }

    [Fact]
    public async Task UnlockFailure_DoesNotMaskCompletedResult()
    {
        var result = await OrderCreationLeaseGuard.ExecuteAsync(
            () => Task.FromResult(true),
            () => throw new HttpRequestException("redis release unavailable"),
            _ => Task.FromResult(84),
            TimeSpan.FromMinutes(1),
            CancellationToken.None);

        Assert.Equal(84, result);
    }
}
