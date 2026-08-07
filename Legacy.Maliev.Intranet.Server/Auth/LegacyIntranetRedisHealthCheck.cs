using Microsoft.Extensions.Diagnostics.HealthChecks;
using StackExchange.Redis;

namespace Legacy.Maliev.Intranet.Auth;

/// <summary>Checks the Redis connection shared by Data Protection and Intranet workflow state.</summary>
public sealed class LegacyIntranetRedisHealthCheck(IConnectionMultiplexer redis) : IHealthCheck
{
    /// <inheritdoc />
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        _ = context;

        try
        {
            if (!redis.IsConnected)
            {
                return HealthCheckResult.Unhealthy("Redis is disconnected.");
            }

            await redis.GetDatabase().PingAsync().WaitAsync(cancellationToken);
            return HealthCheckResult.Healthy("Redis is connected.");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (exception is RedisException or InvalidOperationException)
        {
            return HealthCheckResult.Unhealthy("Redis health check failed.", exception);
        }
    }
}
