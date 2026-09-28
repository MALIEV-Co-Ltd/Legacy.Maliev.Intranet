using Legacy.Maliev.Intranet.Client.Shared.Infrastructure;

namespace Legacy.Maliev.Intranet.Tests;

public sealed class UploadAggregateLimitTests
{
    [Theory]
    [InlineData(50L * 1024 * 1024, 50L * 1024 * 1024, false)]
    [InlineData(50L * 1024 * 1024, 50L * 1024 * 1024 + 1, true)]
    [InlineData(1, long.MaxValue, true)]
    public void Exceeds_DetectsAggregateBoundaryWithoutOverflow(long first, long second, bool expected)
    {
        Assert.Equal(expected, UploadAggregateLimit.Exceeds([first, second]));
    }
}
