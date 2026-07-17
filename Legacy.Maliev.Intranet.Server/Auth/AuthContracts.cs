using System.ComponentModel.DataAnnotations;

namespace Legacy.Maliev.Intranet.Auth;

/// <summary>Stable permission names used by the employee BFF authorization policies.</summary>
public static class LegacyEmployeePermissions
{
    /// <summary>Allows reading one complete legacy customer profile.</summary>
    public const string CustomersRead = "legacy-customer.customers.read";
    /// <summary>Allows creating a legacy customer profile and its authentication identity.</summary>
    public const string CustomersCreate = "legacy-customer.customers.create";
    /// <summary>Allows listing and searching legacy customer profiles.</summary>
    public const string CustomersList = "legacy-customer.customers.list";
    /// <summary>Allows reading the legacy materials catalog.</summary>
    public const string CatalogMaterialsRead = "legacy-catalog.materials.read";
    /// <summary>Allows creating records in the legacy materials catalog.</summary>
    public const string CatalogMaterialsCreate = "legacy-catalog.materials.create";
}

/// <summary>Explicit removable grants that preserve employee-wide authorization during legacy rollout.</summary>
public sealed class LegacyEmployeeCompatibilityOptions
{
    /// <summary>Gets the configuration section for temporary employee compatibility grants.</summary>
    public const string SectionName = "LegacyEmployeeCompatibility";

    /// <summary>Gets or sets whether validated employees receive the legacy-wide materials read grant.</summary>
    public bool GrantCatalogMaterialsRead { get; set; }
}

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
public sealed record EmployeeIdentity(
    string Id,
    string UserName,
    string? Email,
    IReadOnlyList<string>? Permissions = null);

/// <summary>Result of a generic employee authentication attempt.</summary>
public sealed record EmployeeLoginResult(bool Succeeded, AuthTokenResponse? Tokens, EmployeeIdentity? Identity);

/// <summary>Validated rotated employee tokens and the identity bound to the new access token.</summary>
public sealed record EmployeeRefreshResult(AuthTokenResponse Tokens, EmployeeIdentity Identity);

/// <summary>Indicates that AuthService throttled an employee authentication request.</summary>
public sealed class LegacyAuthRateLimitedException(int? retryAfterSeconds) : Exception("Employee authentication was rate limited.")
{
    /// <summary>Gets the bounded Retry-After delay supplied by AuthService, when present.</summary>
    public int? RetryAfterSeconds { get; } = retryAfterSeconds;
}

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

/// <summary>Creates an employee identity with the password carried only in JSON.</summary>
public sealed record CreateEmployeeIdentityRequest(
    string UserName,
    string Email,
    string Password,
    bool EmailConfirmed,
    string? PhoneNumber);

/// <summary>Safe employee identity response with all security material excluded.</summary>
public sealed record EmployeeIdentityResponse(
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
    int DatabaseID);

/// <summary>Calls the independently deployed legacy Auth service.</summary>
public interface ILegacyAuthClient
{
    /// <summary>Authenticates an employee without exposing account enumeration detail.</summary>
    Task<EmployeeLoginResult> LoginAsync(string email, string password, CancellationToken cancellationToken);

    /// <summary>Rotates a single-use refresh token.</summary>
    Task<EmployeeRefreshResult?> RefreshAsync(string refreshToken, CancellationToken cancellationToken);

    /// <summary>Revokes the complete refresh-token family.</summary>
    Task RevokeAsync(string refreshToken, CancellationToken cancellationToken);

    /// <summary>Creates an AuthService-owned customer identity as an employee.</summary>
    Task<CustomerIdentityResponse?> CreateCustomerIdentityAsync(
        int databaseId,
        CreateCustomerIdentityRequest request,
        string accessToken,
        CancellationToken cancellationToken);

    /// <summary>Creates an AuthService-owned employee identity as an employee.</summary>
    Task<EmployeeIdentityResponse?> CreateEmployeeIdentityAsync(
        int databaseId,
        CreateEmployeeIdentityRequest request,
        string accessToken,
        CancellationToken cancellationToken);
}
