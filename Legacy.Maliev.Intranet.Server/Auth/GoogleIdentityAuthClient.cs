using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Legacy.Maliev.Intranet.Contracts;
using Microsoft.Extensions.Logging;

namespace Legacy.Maliev.Intranet.Auth;

/// <summary>Uses the Intranet service identity for the AuthService Google exchange.</summary>
public sealed class GoogleIdentityAuthClient(
    HttpClient httpClient,
    IServiceAccessTokenProvider serviceTokenProvider,
    ILegacyAccessTokenValidator accessTokenValidator,
    TimeProvider timeProvider,
    ILogger<GoogleIdentityAuthClient> logger) : IGoogleIdentityAuthClient
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    /// <inheritdoc />
    public async Task<GoogleIdentityNonceResponse?> IssueNonceAsync(CancellationToken cancellationToken)
    {
        var token = await serviceTokenProvider.GetAccessTokenAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(token))
        {
            return null;
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, "/auth/v1/exchange/google/nonce")
        {
            Content = JsonContent.Create(new { application = "intranet" }),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        try
        {
            using var response = await httpClient.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                InvalidateOnAuthFailure(response.StatusCode, token);
                logger.LogWarning("AuthService rejected the Intranet Google nonce request with {StatusCode}.", response.StatusCode);
                return null;
            }

            var result = await response.Content.ReadFromJsonAsync<GoogleIdentityNonceResponse>(JsonOptions, cancellationToken);
            return result is not null &&
                !string.IsNullOrWhiteSpace(result.Nonce) &&
                result.ExpiresAtUtc > timeProvider.GetUtcNow()
                ? result
                : null;
        }
        catch (Exception exception) when (exception is HttpRequestException or JsonException ||
            (exception is TaskCanceledException && !cancellationToken.IsCancellationRequested))
        {
            logger.LogWarning(exception, "AuthService was unavailable while issuing an Intranet Google nonce.");
            return null;
        }
    }

    /// <inheritdoc />
    public async Task<EmployeeLoginResult> ExchangeAsync(
        string credential,
        string nonce,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(credential) || string.IsNullOrWhiteSpace(nonce))
        {
            return new(false, null, null);
        }

        var token = await serviceTokenProvider.GetAccessTokenAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(token))
        {
            return new(false, null, null);
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, "/auth/v1/exchange/google")
        {
            Content = JsonContent.Create(new
            {
                credential,
                application = "intranet",
                nonce,
            }),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        try
        {
            using var response = await httpClient.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                InvalidateOnAuthFailure(response.StatusCode, token);
                logger.LogWarning("AuthService rejected the Intranet Google exchange with {StatusCode}.", response.StatusCode);
                return new(false, null, null);
            }

            var tokens = await response.Content.ReadFromJsonAsync<AuthTokenResponse>(JsonOptions, cancellationToken);
            if (tokens is null || !IsTokenEnvelopeValid(tokens) ||
                !accessTokenValidator.TryValidateEmployee(tokens.AccessToken, out var identity) ||
                identity is null || !WorkspaceIdentityRules.IsAllowedEmployeeEmail(identity.Email))
            {
                logger.LogError("AuthService returned an invalid Google employee token contract.");
                return new(false, null, null);
            }

            return new(true, tokens, identity);
        }
        catch (Exception exception) when (exception is HttpRequestException or JsonException ||
            (exception is TaskCanceledException && !cancellationToken.IsCancellationRequested))
        {
            logger.LogWarning(exception, "AuthService was unavailable during the Intranet Google exchange.");
            return new(false, null, null);
        }
    }

    private bool IsTokenEnvelopeValid(AuthTokenResponse tokens) =>
        !string.IsNullOrWhiteSpace(tokens.AccessToken) &&
        !string.IsNullOrWhiteSpace(tokens.RefreshToken) &&
        string.Equals(tokens.TokenType, "Bearer", StringComparison.OrdinalIgnoreCase) &&
        tokens.ExpiresIn is > 0 and <= 1800 &&
        tokens.RefreshExpiresAt > timeProvider.GetUtcNow();

    private void InvalidateOnAuthFailure(HttpStatusCode statusCode, string token)
    {
        if (statusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
        {
            serviceTokenProvider.Invalidate(token);
        }
    }
}
