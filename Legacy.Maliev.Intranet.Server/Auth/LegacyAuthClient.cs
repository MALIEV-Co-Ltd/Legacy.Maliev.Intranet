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
        using var response = await SendIdentityAsync(request, cancellationToken);
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
        using var response = await SendIdentityAsync(request, cancellationToken);
        if (response.StatusCode == HttpStatusCode.Conflict)
        {
            return null;
        }

        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<EmployeeIdentityResponse>(cancellationToken);
    }

    private async Task<HttpResponseMessage> SendIdentityAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        // Only the two retained profile identity sends replace the caller-owned source helper.
        // Login, refresh and revoke above keep their existing admission and response contracts.
        request.Options.Set(new HttpRequestOptionsKey<bool>("Maliev.DependencyFailureOwnedByCaller"), logger.IsEnabled(LogLevel.Error));
        try
        {
            var response = await httpClient.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode) RecordIdentityFailure((int)response.StatusCode, null);
            return response;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception exception)
        {
            var type = exception.GetType();
            var name = type == typeof(HttpRequestException) ? nameof(HttpRequestException)
                : type == typeof(TaskCanceledException) ? nameof(TaskCanceledException)
                : type == typeof(OperationCanceledException) ? nameof(OperationCanceledException)
                : type == typeof(TimeoutException) ? nameof(TimeoutException)
                : type == typeof(IOException) ? nameof(IOException)
                : type == typeof(InvalidOperationException) ? nameof(InvalidOperationException)
                : type == typeof(ArgumentException) ? nameof(ArgumentException)
                : type == typeof(NotSupportedException) ? nameof(NotSupportedException)
                : type == typeof(Polly.Timeout.TimeoutRejectedException) ? nameof(Polly.Timeout.TimeoutRejectedException) : "UnknownException";
            RecordIdentityFailure(null, name);
            throw;
        }
    }

    private void RecordIdentityFailure(int? status, string? exceptionType)
    {
        try
        {
            if (status == 404)
            {
                logger.LogDebug(new EventId(5101, "DependencyResourceNotFound"),
                    "{EventName} Dependency={Dependency} Operation={Operation} StatusCode={StatusCode} Method={Method}",
                    "DependencyResourceNotFound", "AuthService", "Auth.Post", 404, "POST");
            }
            else if (status is { } number)
            {
                logger.LogError(new EventId(5101, "DependencyRequestFailure"),
                    "{EventName} Dependency={Dependency} Operation={Operation} StatusCode={StatusCode} Method={Method} ExceptionType={ExceptionType}",
                    "DependencyRequestFailure", "AuthService", "Auth.Post", number, "POST", exceptionType);
            }
            else
            {
                logger.LogError(new EventId(5101, "DependencyRequestFailure"),
                    "{EventName} Dependency={Dependency} Operation={Operation} Method={Method} ExceptionType={ExceptionType}",
                    "DependencyRequestFailure", "AuthService", "Auth.Post", "POST", exceptionType);
            }
        }
        catch (Exception)
        {
            // A diagnostic sink cannot replace the original identity response or exception.
        }
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
