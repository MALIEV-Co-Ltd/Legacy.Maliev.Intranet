using System.ComponentModel.DataAnnotations;

namespace Legacy.Maliev.Intranet.Auth;

/// <summary>Employee login credentials sent only from the BFF to AuthService.</summary>
public sealed record EmployeeLoginRequest(
    [property: Required, EmailAddress, StringLength(320)] string UserName,
    [property: Required, StringLength(1024, MinimumLength = 1)] string Password,
    int IdentityKind = 1);

/// <summary>AuthService response retained only in the server-side ticket store.</summary>
public sealed record AuthTokenResponse(
    string AccessToken,
    string RefreshToken,
    string TokenType,
    int ExpiresIn,
    DateTimeOffset RefreshExpiresAt);

/// <summary>Authenticated employee claims decoded from the validated access token.</summary>
public sealed record EmployeeIdentity(string Id, string UserName, string? Email);

/// <summary>Result of a generic employee authentication attempt.</summary>
public sealed record EmployeeLoginResult(bool Succeeded, AuthTokenResponse? Tokens, EmployeeIdentity? Identity);

/// <summary>Calls the independently deployed legacy Auth service.</summary>
public interface ILegacyAuthClient
{
    /// <summary>Authenticates an employee without exposing account enumeration detail.</summary>
    Task<EmployeeLoginResult> LoginAsync(string email, string password, CancellationToken cancellationToken);

    /// <summary>Rotates a single-use refresh token.</summary>
    Task<AuthTokenResponse?> RefreshAsync(string refreshToken, CancellationToken cancellationToken);

    /// <summary>Revokes the complete refresh-token family.</summary>
    Task RevokeAsync(string refreshToken, CancellationToken cancellationToken);
}