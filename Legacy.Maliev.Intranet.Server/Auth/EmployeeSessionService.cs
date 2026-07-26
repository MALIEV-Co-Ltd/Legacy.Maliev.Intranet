using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Security.Claims;

namespace Legacy.Maliev.Intranet.Auth;

/// <summary>Creates, refreshes and revokes server-side employee sessions.</summary>
public sealed class EmployeeSessionService(
    ILegacyAuthClient authClient,
    TimeProvider timeProvider,
    ILogger<EmployeeSessionService> logger,
    IOptions<LegacyEmployeeCompatibilityOptions> compatibilityOptions)
{
    /// <summary>Stable legacy employee database identifier carried only after AuthService validation.</summary>
    public const string LegacyDatabaseIdClaim = "legacy_database_id";

    private const string AccessToken = "legacy_access_token";
    private const string RefreshToken = "legacy_refresh_token";
    private const string AccessExpiresAt = "legacy_access_expires_at";

    /// <summary>Signs in after AuthService has validated the employee.</summary>
    public async Task SignInAsync(
        HttpContext context,
        EmployeeLoginResult login,
        bool rememberMe = false)
    {
        if (!login.Succeeded || login.Tokens is null || login.Identity is null)
        {
            throw new InvalidOperationException("A validated employee login is required.");
        }

        var claims = CreateIdentityClaims(login.Identity).ToList();
        var principal = new System.Security.Claims.ClaimsPrincipal(
            new System.Security.Claims.ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme));
        var properties = new AuthenticationProperties
        {
            IsPersistent = rememberMe,
            IssuedUtc = timeProvider.GetUtcNow(),
            ExpiresUtc = timeProvider.GetUtcNow().AddHours(8),
        };
        StoreTokens(properties, login.Tokens);

        await context.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, principal, properties);
    }

    /// <summary>Returns a fresh downstream access token, rotating the refresh token when required.</summary>
    public async Task<string?> GetAccessTokenAsync(HttpContext context, CancellationToken cancellationToken)
    {
        var result = await context.AuthenticateAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        if (!result.Succeeded || result.Properties is null)
        {
            return null;
        }

        var accessToken = result.Properties.GetTokenValue(AccessToken);
        var expiresText = result.Properties.GetTokenValue(AccessExpiresAt);
        if (DateTimeOffset.TryParse(expiresText, out var expiresAt) &&
            expiresAt > timeProvider.GetUtcNow().AddMinutes(2))
        {
            return accessToken;
        }

        var refreshToken = result.Properties.GetTokenValue(RefreshToken);
        if (string.IsNullOrWhiteSpace(refreshToken))
        {
            await context.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return null;
        }

        var expectedEmployeeId = result.Principal?.FindFirstValue(ClaimTypes.NameIdentifier);
        EmployeeRefreshResult? refreshed = null;
        var transientRefreshFailure = false;
        try
        {
            refreshed = await authClient.RefreshAsync(refreshToken, cancellationToken);
        }
        catch (LegacyAuthRateLimitedException exception)
        {
            transientRefreshFailure = true;
            logger.LogWarning(
                exception,
                "Employee session refresh was rate limited; preserving the opaque session for retry after {RetryAfterSeconds} seconds.",
                exception.RetryAfterSeconds);
        }
        catch (Exception exception) when (
            exception is HttpRequestException or System.Text.Json.JsonException ||
            exception is TaskCanceledException && !cancellationToken.IsCancellationRequested)
        {
            transientRefreshFailure = true;
            logger.LogWarning(exception, "Employee session refresh was temporarily unavailable; preserving the opaque session for retry.");
        }

        if (refreshed is null)
        {
            // AuthService refresh tokens are single-use. Another request may have won the
            // rotation between this request's ticket read and refresh attempt. Re-read the
            // distributed ticket before treating a null result as revocation.
            var peerAccessToken = await TryReadRenewedAccessTokenAsync(context, refreshToken, expectedEmployeeId);
            if (peerAccessToken is not null)
            {
                return peerAccessToken;
            }

            if (transientRefreshFailure)
            {
                return null;
            }

            await context.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return null;
        }

        if (string.IsNullOrWhiteSpace(expectedEmployeeId) ||
            !string.Equals(refreshed.Identity.Id, expectedEmployeeId, StringComparison.Ordinal))
        {
            await context.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return null;
        }

        StoreTokens(result.Properties, refreshed.Tokens);
        var refreshedClaims = CreateIdentityClaims(refreshed.Identity);
        var refreshedPrincipal = new ClaimsPrincipal(new ClaimsIdentity(
            refreshedClaims,
            CookieAuthenticationDefaults.AuthenticationScheme,
            ClaimTypes.Name,
            ClaimTypes.Role));
        await context.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme,
            refreshedPrincipal,
            result.Properties);
        return refreshed.Tokens.AccessToken;
    }

    private async Task<string?> TryReadRenewedAccessTokenAsync(
        HttpContext context,
        string previousRefreshToken,
        string? expectedEmployeeId)
    {
        if (string.IsNullOrWhiteSpace(expectedEmployeeId))
        {
            return null;
        }

        var current = await context.AuthenticateAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        if (!current.Succeeded || current.Properties is null ||
            !string.Equals(
                current.Principal?.FindFirstValue(ClaimTypes.NameIdentifier),
                expectedEmployeeId,
                StringComparison.Ordinal))
        {
            return null;
        }

        var currentRefreshToken = current.Properties.GetTokenValue(RefreshToken);
        var currentAccessToken = current.Properties.GetTokenValue(AccessToken);
        var expiresText = current.Properties.GetTokenValue(AccessExpiresAt);
        if (string.IsNullOrWhiteSpace(currentRefreshToken) ||
            string.Equals(currentRefreshToken, previousRefreshToken, StringComparison.Ordinal) ||
            string.IsNullOrWhiteSpace(currentAccessToken) ||
            !DateTimeOffset.TryParse(expiresText, out var expiresAt) ||
            expiresAt <= timeProvider.GetUtcNow().AddMinutes(2))
        {
            return null;
        }

        return currentAccessToken;
    }

    /// <summary>Revokes the refresh family and always clears the local session.</summary>
    public async Task SignOutAsync(HttpContext context, CancellationToken cancellationToken)
    {
        var result = await context.AuthenticateAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        var refreshToken = result.Properties?.GetTokenValue(RefreshToken);
        if (!string.IsNullOrWhiteSpace(refreshToken))
        {
            try
            {
                await authClient.RevokeAsync(refreshToken, cancellationToken);
            }
            catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
            {
                logger.LogWarning(exception, "Refresh-token revocation was unavailable during employee sign-out.");
            }
        }

        await context.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
    }

    private void StoreTokens(AuthenticationProperties properties, AuthTokenResponse tokens)
    {
        properties.StoreTokens(
        [
            new AuthenticationToken { Name = AccessToken, Value = tokens.AccessToken },
            new AuthenticationToken { Name = RefreshToken, Value = tokens.RefreshToken },
            new AuthenticationToken
            {
                Name = AccessExpiresAt,
                Value = timeProvider.GetUtcNow().AddSeconds(tokens.ExpiresIn).ToString("O"),
            },
        ]);
    }

    private IEnumerable<Claim> CreatePermissionClaims(IReadOnlyList<string>? validatedPermissions)
    {
        var permissions = validatedPermissions ?? [];
        if (compatibilityOptions.Value.GrantCatalogMaterialsRead)
        {
            permissions = [.. permissions, LegacyEmployeePermissions.CatalogMaterialsRead];
        }

        return permissions
            .Where(permission => !string.IsNullOrWhiteSpace(permission))
            .Distinct(StringComparer.Ordinal)
            .Select(permission => new Claim("permissions", permission));
    }

    private IEnumerable<Claim> CreateIdentityClaims(EmployeeIdentity identity)
    {
        yield return new Claim(ClaimTypes.NameIdentifier, identity.Id);
        yield return new Claim(ClaimTypes.Name, identity.UserName);
        yield return new Claim(ClaimTypes.Email, identity.Email ?? identity.UserName);
        yield return new Claim("identity_kind", "employee");
        if (identity.LegacyDatabaseId is int legacyDatabaseId and > 0)
        {
            yield return new Claim(
                LegacyDatabaseIdClaim,
                legacyDatabaseId.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }

        foreach (var permission in CreatePermissionClaims(identity.Permissions))
        {
            yield return permission;
        }
    }
}
