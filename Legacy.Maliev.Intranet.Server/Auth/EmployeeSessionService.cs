using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.DependencyInjection;
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
    public async Task<string?> GetAccessTokenAsync(HttpContext context, CancellationToken cancellationToken) =>
        (await AcquireAccessTokenAsync(context, cancellationToken)).AccessToken;

    /// <summary>Acquires a credential without conflating invalid sessions with temporary refresh failures.</summary>
    public async Task<EmployeeAccessTokenAcquisition> AcquireAccessTokenAsync(HttpContext context, CancellationToken cancellationToken)
    {
        var result = await context.AuthenticateAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        if (!result.Succeeded || result.Properties is null)
        {
            return new(EmployeeAccessTokenStatus.Unauthenticated, null, []);
        }

        var accessToken = result.Properties.GetTokenValue(AccessToken);
        var expiresText = result.Properties.GetTokenValue(AccessExpiresAt);
        if (DateTimeOffset.TryParse(expiresText, out var expiresAt) &&
            expiresAt > timeProvider.GetUtcNow().AddMinutes(2))
        {
            return Available(accessToken, result.Principal);
        }

        var refreshToken = result.Properties.GetTokenValue(RefreshToken);
        if (string.IsNullOrWhiteSpace(refreshToken))
        {
            await context.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return new(EmployeeAccessTokenStatus.Unauthenticated, null, []);
        }

        var expectedEmployeeId = result.Principal?.FindFirstValue(ClaimTypes.NameIdentifier);
        EmployeeRefreshResult? refreshed = null;
        var transientRefreshFailure = false;
        var refreshThrottled = false;
        int? retryAfterSeconds = null;
        try
        {
            refreshed = await authClient.RefreshAsync(refreshToken, cancellationToken);
        }
        catch (LegacyAuthRateLimitedException exception)
        {
            transientRefreshFailure = true;
            refreshThrottled = true;
            retryAfterSeconds = exception.RetryAfterSeconds;
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
            var reconciliation = await TryReadRenewedAccessTokenAsync(context, refreshToken, expectedEmployeeId, cancellationToken);
            if (reconciliation.Result is not null)
            {
                return reconciliation.Result;
            }

            if (transientRefreshFailure)
            {
                return new(refreshThrottled ? EmployeeAccessTokenStatus.Throttled : EmployeeAccessTokenStatus.Unavailable,
                    null, [], retryAfterSeconds);
            }

            if (reconciliation.MaySignOut)
                await context.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return new(EmployeeAccessTokenStatus.Unauthenticated, null, []);
        }

        if (string.IsNullOrWhiteSpace(expectedEmployeeId) ||
            !string.Equals(refreshed.Identity.Id, expectedEmployeeId, StringComparison.Ordinal))
        {
            var reconciliation = await TryReadRenewedAccessTokenAsync(context, refreshToken, expectedEmployeeId, cancellationToken);
            if (reconciliation.Result is not null) return reconciliation.Result;
            if (reconciliation.MaySignOut)
                await context.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return new(EmployeeAccessTokenStatus.Unauthenticated, null, []);
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
        return Available(refreshed.Tokens.AccessToken, refreshedPrincipal);
    }

    private static EmployeeAccessTokenAcquisition Available(string? token, ClaimsPrincipal? principal) =>
        new(EmployeeAccessTokenStatus.Available, token,
            principal?.FindAll("permissions").Select(claim => claim.Value).Distinct(StringComparer.Ordinal).ToArray() ?? []);

    private async Task<(EmployeeAccessTokenAcquisition? Result, bool MaySignOut)> TryReadRenewedAccessTokenAsync(
        HttpContext context,
        string previousRefreshToken,
        string? expectedEmployeeId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(expectedEmployeeId))
        {
            return (new(EmployeeAccessTokenStatus.Unauthenticated, null, []), false);
        }

        AuthenticationTicket? current;
        try
        {
            var tickets = context.RequestServices.GetService<DistributedTicketStore>();
            // Non-store legacy/direct callers retain their existing facade semantics.
            // Both actual hosts register the same concrete store used by cookie auth;
            // cached AuthenticateAsync is not fresh-generation proof in those hosts.
            current = tickets is not null
                ? await tickets.RetrieveCurrentAsync(context, cancellationToken)
                : (await context.AuthenticateAsync(CookieAuthenticationDefaults.AuthenticationScheme)).Ticket;
            cancellationToken.ThrowIfCancellationRequested();
        }
        catch (Exception exception) when (exception is not OperationCanceledException && !cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning(exception, "Employee session reconciliation was unavailable; preserving the opaque session for retry.");
            return (new(EmployeeAccessTokenStatus.Unavailable, null, []), false);
        }
        if (current is null || current.Properties.ExpiresUtc is not { } ticketExpiresAt ||
            ticketExpiresAt <= timeProvider.GetUtcNow() ||
            !string.Equals(
                current.Principal?.FindFirstValue(ClaimTypes.NameIdentifier),
                expectedEmployeeId,
                StringComparison.Ordinal))
        {
            return (new(EmployeeAccessTokenStatus.Unauthenticated, null, []), false);
        }

        var currentRefreshToken = current.Properties.GetTokenValue(RefreshToken);
        var currentAccessToken = current.Properties.GetTokenValue(AccessToken);
        var expiresText = current.Properties.GetTokenValue(AccessExpiresAt);
        // Only the freshly observed original generation may retain existing signout.
        // Read -> remove is not atomic with a later peer write through IDistributedCache.
        if (string.Equals(currentRefreshToken, previousRefreshToken, StringComparison.Ordinal))
            return (null, true);
        if (string.IsNullOrWhiteSpace(currentRefreshToken) ||
            string.IsNullOrWhiteSpace(currentAccessToken) ||
            !DateTimeOffset.TryParse(expiresText, out var expiresAt) ||
            expiresAt <= timeProvider.GetUtcNow().AddMinutes(2))
        {
            return (new(EmployeeAccessTokenStatus.Unauthenticated, null, []), false);
        }

        return (Available(currentAccessToken, current.Principal), false);
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
