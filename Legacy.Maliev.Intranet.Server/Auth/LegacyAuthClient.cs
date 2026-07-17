using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.Extensions.Logging;

namespace Legacy.Maliev.Intranet.Auth;

/// <summary>Strict JSON client for Legacy.Maliev.AuthService.</summary>
public sealed class LegacyAuthClient(
    HttpClient httpClient,
    ILegacyAccessTokenValidator accessTokenValidator,
    TimeProvider timeProvider,
    ILogger<LegacyAuthClient> logger) : ILegacyAuthClient
{
    /// <inheritdoc />
    public async Task<EmployeeLoginResult> LoginAsync(
        string email,
        string password,
        CancellationToken cancellationToken)
    {
        using var response = await httpClient.PostAsJsonAsync(
            "/auth/v1/login", new EmployeeLoginRequest(email, password), cancellationToken);
        ThrowIfRateLimited(response);
        if (response.StatusCode == HttpStatusCode.Unauthorized)
        {
            return new(false, null, null);
        }

        response.EnsureSuccessStatusCode();
        var tokens = await response.Content.ReadFromJsonAsync<AuthTokenResponse>(cancellationToken);
        if (tokens is null ||
            !IsTokenEnvelopeValid(tokens) ||
            !accessTokenValidator.TryValidateEmployee(tokens.AccessToken, out var identity))
        {
            logger.LogError("AuthService returned an invalid employee token contract");
            return new(false, null, null);
        }

        return new(true, tokens, identity);
    }

    /// <inheritdoc />
    public async Task<EmployeeRefreshResult?> RefreshAsync(string refreshToken, CancellationToken cancellationToken)
    {
        using var response = await httpClient.PostAsJsonAsync(
            "/auth/v1/refresh", new { refreshToken }, cancellationToken);
        ThrowIfRateLimited(response);
        if (response.StatusCode == HttpStatusCode.Unauthorized)
        {
            return null;
        }

        response.EnsureSuccessStatusCode();
        var tokens = await response.Content.ReadFromJsonAsync<AuthTokenResponse>(cancellationToken);
        if (tokens is null ||
            !IsTokenEnvelopeValid(tokens) ||
            !accessTokenValidator.TryValidateEmployee(tokens.AccessToken, out var identity) ||
            identity is null)
        {
            logger.LogError("AuthService returned an invalid refreshed employee token contract");
            return null;
        }

        return new EmployeeRefreshResult(tokens, identity);
    }

    /// <inheritdoc />
    public async Task RevokeAsync(string refreshToken, CancellationToken cancellationToken)
    {
        using var response = await httpClient.PostAsJsonAsync(
            "/auth/v1/revoke", new { refreshToken }, cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    /// <inheritdoc />
    public async Task<CustomerIdentityResponse?> CreateCustomerIdentityAsync(
        int databaseId,
        CreateCustomerIdentityRequest identity,
        string accessToken,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/auth/v1/customer-identities/{databaseId}")
        {
            Content = JsonContent.Create(identity),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        using var response = await httpClient.SendAsync(request, cancellationToken);
        if (response.StatusCode == HttpStatusCode.Conflict)
        {
            return null;
        }

        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<CustomerIdentityResponse>(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<EmployeeIdentityResponse?> CreateEmployeeIdentityAsync(
        int databaseId,
        CreateEmployeeIdentityRequest identity,
        string accessToken,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/auth/v1/employee-identities/{databaseId}")
        {
            Content = JsonContent.Create(identity),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        using var response = await httpClient.SendAsync(request, cancellationToken);
        if (response.StatusCode == HttpStatusCode.Conflict)
        {
            return null;
        }

        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<EmployeeIdentityResponse>(cancellationToken);
    }

    private bool IsTokenEnvelopeValid(AuthTokenResponse tokens) =>
        !string.IsNullOrWhiteSpace(tokens.AccessToken) &&
        !string.IsNullOrWhiteSpace(tokens.RefreshToken) &&
        string.Equals(tokens.TokenType, "Bearer", StringComparison.OrdinalIgnoreCase) &&
        tokens.ExpiresIn is > 0 and <= 1800 &&
        tokens.RefreshExpiresAt > timeProvider.GetUtcNow();

    private void ThrowIfRateLimited(HttpResponseMessage response)
    {
        if (response.StatusCode != HttpStatusCode.TooManyRequests)
        {
            return;
        }

        throw new LegacyAuthRateLimitedException(GetRetryAfterSeconds(response.Headers.RetryAfter));
    }

    private int? GetRetryAfterSeconds(RetryConditionHeaderValue? retryAfter)
    {
        double? seconds = retryAfter?.Delta?.TotalSeconds;
        if (seconds is null && retryAfter?.Date is { } retryDate)
        {
            seconds = (retryDate - timeProvider.GetUtcNow()).TotalSeconds;
        }

        return seconds is > 0 and <= 3600 ? (int)Math.Ceiling(seconds.Value) : null;
    }

}
