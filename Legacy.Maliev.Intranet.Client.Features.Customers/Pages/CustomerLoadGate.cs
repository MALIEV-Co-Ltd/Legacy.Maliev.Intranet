namespace Legacy.Maliev.Intranet.Client.Features.Customers.Pages;

/// <summary>Owns the latest customer-detail request so an older route load cannot publish stale state.</summary>
public sealed class CustomerLoadGate : IDisposable
{
    private readonly object sync = new();
    private CancellationTokenSource? currentSource;
    private long generation;

    /// <summary>Begins a customer request and cancels the previous request.</summary>
    public CustomerLoadLease Begin(int customerId)
    {
        lock (sync)
        {
            currentSource?.Cancel();
            currentSource?.Dispose();
            currentSource = new CancellationTokenSource();
            return new CustomerLoadLease(customerId, ++generation, currentSource.Token);
        }
    }

    /// <summary>Returns whether the request still owns the customer-detail state.</summary>
    public bool IsCurrent(CustomerLoadLease request)
    {
        lock (sync)
        {
            return request.Generation == generation &&
                !request.CancellationToken.IsCancellationRequested;
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        lock (sync)
        {
            currentSource?.Cancel();
            currentSource?.Dispose();
            currentSource = null;
        }
    }
}

/// <summary>Identifies one customer-detail request.</summary>
public readonly record struct CustomerLoadLease(
    int CustomerId,
    long Generation,
    CancellationToken CancellationToken);
