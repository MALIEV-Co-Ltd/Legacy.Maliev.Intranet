using Microsoft.Extensions.Options;
using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace Legacy.Maliev.Intranet.Auth;

/// <summary>Runtime-only credentials for the Intranet BFF service identity.</summary>
public sealed class ServiceAuthenticationOptions
{
    /// <summary>Registered legacy service client identifier.</summary>
    public string ClientId { get; set; } = "legacy-intranet";
    /// <summary>Secret projected at runtime from the consolidated legacy secret.</summary>
    public string ClientSecret { get; set; } = string.Empty;
}

/// <summary>Provides cached short-lived service access tokens.</summary>
public interface IServiceAccessTokenProvider
{
    /// <summary>Gets a usable service token or returns null when authentication is unavailable.</summary>
    ValueTask<string?> GetAccessTokenAsync(CancellationToken cancellationToken);
    /// <summary>Invalidates a rejected cached token.</summary>
    void Invalidate(string token);
}

/// <summary>Exchanges the runtime credential for a cached least-privilege service token.</summary>
public sealed class ServiceAccessTokenProvider(
    IHttpClientFactory clientFactory,
    IOptions<ServiceAuthenticationOptions> options,
    TimeProvider timeProvider,
    ILogger<ServiceAccessTokenProvider> logger) : IServiceAccessTokenProvider, IDisposable
{
    private static readonly TimeSpan RefreshSkew = TimeSpan.FromMinutes(2);
    private readonly SemaphoreSlim tokenLock = new(1, 1);
    private CachedToken? cachedToken;

    /// <inheritdoc />
    public async ValueTask<string?> GetAccessTokenAsync(CancellationToken cancellationToken)
    {
        var current = Volatile.Read(ref cachedToken);
        if (IsUsable(current)) return current!.Token;

        var settings = options.Value;
        if (string.IsNullOrWhiteSpace(settings.ClientId) || string.IsNullOrWhiteSpace(settings.ClientSecret))
        {
            logger.LogWarning("Intranet service authentication is not configured.");
            return null;
        }

        await tokenLock.WaitAsync(cancellationToken);
        try
        {
            current = cachedToken;
            if (IsUsable(current)) return current!.Token;

            using var response = await clientFactory.CreateClient("service-auth").PostAsJsonAsync(
                "/auth/v1/service/login",
                new ServiceLoginRequest(settings.ClientId, settings.ClientSecret),
                cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("AuthService rejected the Intranet service identity with status {StatusCode}.", response.StatusCode);
                return null;
            }

            var login = await response.Content.ReadFromJsonAsync<ServiceLoginResponse>(cancellationToken);
            if (login is null || string.IsNullOrWhiteSpace(login.AccessToken) || login.ExpiresIn <= 0)
            {
                logger.LogWarning("AuthService returned an invalid Intranet service-login response.");
                return null;
            }

            current = new(login.AccessToken, timeProvider.GetUtcNow().AddSeconds(login.ExpiresIn));
            Volatile.Write(ref cachedToken, current);
            return current.Token;
        }
        catch (Exception exception) when (exception is HttpRequestException ||
            (exception is TaskCanceledException && !cancellationToken.IsCancellationRequested))
        {
            logger.LogWarning(exception, "AuthService was unavailable while obtaining the Intranet service token.");
            return null;
        }
        finally
        {
            tokenLock.Release();
        }
    }

    /// <inheritdoc />
    public void Invalidate(string token)
    {
        var current = Volatile.Read(ref cachedToken);
        if (current is not null && string.Equals(current.Token, token, StringComparison.Ordinal))
            Interlocked.CompareExchange(ref cachedToken, null, current);
    }

    /// <inheritdoc />
    public void Dispose() => tokenLock.Dispose();

    private bool IsUsable(CachedToken? token) => token is not null && token.ExpiresAt - RefreshSkew > timeProvider.GetUtcNow();
    private sealed record CachedToken(string Token, DateTimeOffset ExpiresAt);
    private sealed record ServiceLoginRequest([property: JsonPropertyName("clientId")] string ClientId, [property: JsonPropertyName("clientSecret")] string ClientSecret);
    private sealed record ServiceLoginResponse([property: JsonPropertyName("accessToken")] string AccessToken, [property: JsonPropertyName("expiresIn")] int ExpiresIn);
}
