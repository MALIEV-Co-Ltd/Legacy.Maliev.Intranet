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

/// <summary>Creates a customer identity with the password carried only in JSON.</summary>
public sealed record CreateCustomerIdentityRequest(
    string UserName,
    string Email,
    string Password,
    bool EmailConfirmed,
    string? PhoneNumber,
    string? FaxNumber,
    string? MobileNumber);

/// <summary>Safe customer identity response with all security material excluded.</summary>
public sealed record CustomerIdentityResponse(
    string Id,
    string? UserName,
    string? Email,
    bool EmailConfirmed,
    string? PhoneNumber,
    bool PhoneNumberConfirmed,
    bool TwoFactorEnabled,
    DateTimeOffset? LockoutEnd,
    bool LockoutEnabled,
    int AccessFailedCount,
    int DatabaseID,
    string? FaxNumber,
    string? MobileNumber);

/// <summary>Calls the independently deployed legacy Auth service.</summary>
public interface ILegacyAuthClient
{
    /// <summary>Authenticates an employee without exposing account enumeration detail.</summary>
    Task<EmployeeLoginResult> LoginAsync(string email, string password, CancellationToken cancellationToken);

    /// <summary>Rotates a single-use refresh token.</summary>
    Task<AuthTokenResponse?> RefreshAsync(string refreshToken, CancellationToken cancellationToken);

    /// <summary>Revokes the complete refresh-token family.</summary>
    Task RevokeAsync(string refreshToken, CancellationToken cancellationToken);

    /// <summary>Creates an AuthService-owned customer identity as an employee.</summary>
    Task<CustomerIdentityResponse?> CreateCustomerIdentityAsync(
        int databaseId,
        CreateCustomerIdentityRequest request,
        string accessToken,
        CancellationToken cancellationToken);
}
