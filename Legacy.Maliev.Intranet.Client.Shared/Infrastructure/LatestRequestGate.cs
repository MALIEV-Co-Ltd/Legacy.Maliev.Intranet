namespace Legacy.Maliev.Intranet.Client.Shared.Infrastructure;

public sealed class LatestRequestGate : IDisposable
{
    private readonly object sync = new();
    private CancellationTokenSource? currentSource;
    private long currentVersion;
    private bool disposed;

    public Lease Begin(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        lock (sync)
        {
            currentSource?.Cancel();
            currentSource?.Dispose();
            currentSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            return new Lease(this, ++currentVersion, currentSource.Token);
        }
    }

    private bool IsCurrent(long version)
    {
        lock (sync)
        {
            return !disposed && version == currentVersion && currentSource?.IsCancellationRequested == false;
        }
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        lock (sync)
        {
            currentSource?.Cancel();
            currentSource?.Dispose();
            currentSource = null;
        }
    }

    public sealed class Lease : IDisposable
    {
        private readonly LatestRequestGate owner;

        internal Lease(LatestRequestGate owner, long version, CancellationToken cancellationToken)
        {
            this.owner = owner;
            Version = version;
            CancellationToken = cancellationToken;
        }

        public long Version { get; }
        public CancellationToken CancellationToken { get; }
        public bool IsCurrent => owner.IsCurrent(Version);
        public void Dispose() { }
    }
}
