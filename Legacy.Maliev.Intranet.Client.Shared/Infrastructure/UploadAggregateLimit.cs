namespace Legacy.Maliev.Intranet.Client.Shared.Infrastructure;

public static class UploadAggregateLimit
{
    private const long MaximumBytes = 100L * 1024 * 1024;

    public static bool Exceeds(IEnumerable<long> sizes)
    {
        long total = 0;
        foreach (var size in sizes)
        {
            if (size < 0 || size > MaximumBytes - total) return true;
            total += size;
        }

        return false;
    }
}
