using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;

namespace Legacy.Maliev.Intranet.Auth;

/// <summary>Strict JSON client for Legacy.Maliev.AuthService.</summary>
public sealed class LegacyAuthClient(HttpClient httpClient, ILogger<LegacyAuthClient> logger) : ILegacyAuthClient
{
    /// <inheritdoc />
    public async Task<EmployeeLoginResult> LoginAsync(
        string email,
        string password,
        CancellationToken cancellationToken)
    {
        using var response = await httpClient.PostAsJsonAsync(
            "/auth/v1/login", new EmployeeLoginRequest(email, password), cancellationToken);
        if (response.StatusCode == HttpStatusCode.Unauthorized)
        {
            return new(false, null, null);
        }

        response.EnsureSuccessStatusCode();
        var tokens = await response.Content.ReadFromJsonAsync<AuthTokenResponse>(cancellationToken);
        if (tokens is null || !TryReadEmployee(tokens.AccessToken, out var identity))
        {
            logger.LogError("AuthService returned an invalid employee token contract");
            return new(false, null, null);
        }

        return new(true, tokens, identity);
    }

    /// <inheritdoc />
    public async Task<AuthTokenResponse?> RefreshAsync(string refreshToken, CancellationToken cancellationToken)
    {
        using var response = await httpClient.PostAsJsonAsync(
            "/auth/v1/refresh", new { refreshToken }, cancellationToken);
        if (response.StatusCode == HttpStatusCode.Unauthorized)
        {
            return null;
        }

        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<AuthTokenResponse>(cancellationToken);
    }

    /// <inheritdoc />
    public async Task RevokeAsync(string refreshToken, CancellationToken cancellationToken)
    {
        using var response = await httpClient.PostAsJsonAsync(
            "/auth/v1/revoke", new { refreshToken }, cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    private static bool TryReadEmployee(string accessToken, out EmployeeIdentity? identity)
    {
        identity = null;
        var handler = new JwtSecurityTokenHandler { MapInboundClaims = false };
        if (!handler.CanReadToken(accessToken))
        {
            return false;
        }

        var token = handler.ReadJwtToken(accessToken);
        if (!string.Equals(token.Claims.FirstOrDefault(x => x.Type == "identity_kind")?.Value, "employee", StringComparison.Ordinal))
        {
            return false;
        }

        var id = token.Claims.FirstOrDefault(x => x.Type == JwtRegisteredClaimNames.Sub)?.Value;
        var name = token.Claims.FirstOrDefault(x => x.Type is JwtRegisteredClaimNames.Name or ClaimTypes.Name)?.Value;
        var email = token.Claims.FirstOrDefault(x => x.Type is JwtRegisteredClaimNames.Email or ClaimTypes.Email)?.Value;
        if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(name))
        {
            return false;
        }

        identity = new(id, name, email);
        return true;
    }
}