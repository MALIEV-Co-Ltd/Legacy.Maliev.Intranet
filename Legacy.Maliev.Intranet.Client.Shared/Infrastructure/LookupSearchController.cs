namespace Legacy.Maliev.Intranet.Client.Shared.Infrastructure;

/// <summary>Cancels superseded requests and rejects stale providers which ignore cancellation.</summary>
public sealed class LookupSearchController<T>(TimeProvider? clock = null) : IDisposable
{
    private readonly LatestRequestGate gate = new();
    private readonly TimeProvider clock = clock ?? TimeProvider.System;

    public async Task SearchAsync(Func<CancellationToken, Task<T>> search, Action<T> accept,
        Action<Exception> fail, TimeSpan? debounce = null)
    {
        using var lease = gate.Begin();
        try
        {
            await Task.Delay(debounce ?? TimeSpan.FromMilliseconds(350), clock, lease.CancellationToken);
            var result = await search(lease.CancellationToken);
            if (lease.IsCurrent) accept(result);
        }
        catch (OperationCanceledException) when (lease.CancellationToken.IsCancellationRequested) { }
        catch (Exception error) when (error is HttpRequestException or LookupRequestException or OperationCanceledException)
        {
            if (lease.IsCurrent) fail(error);
        }
    }

    public void Invalidate() { using var lease = gate.Begin(); }
    public void Dispose() => gate.Dispose();
}
