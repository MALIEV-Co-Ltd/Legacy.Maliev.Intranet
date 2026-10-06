using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace Legacy.Maliev.Intranet.Contracts;

/// <summary>Safe Auth customer projection, independently versioned and bound by DatabaseID.</summary>
public sealed record CustomerAdministrationIdentity(
    string Id, string? UserName, string? Email,
    [property: JsonRequired] bool EmailConfirmed,
    string? PhoneNumber,
    [property: JsonRequired] bool PhoneNumberConfirmed,
    [property: JsonRequired] bool TwoFactorEnabled,
    [property: JsonRequired] DateTimeOffset? LockoutEnd,
    [property: JsonRequired] bool LockoutEnabled,
    int AccessFailedCount, int DatabaseID, string? FaxNumber, string? MobileNumber, string Version);

/// <summary>Server-constructed Auth update; protected settings come from the captured identity, not the browser.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record CustomerAdministrationIdentityUpdate(
    [property: Required, EmailAddress, StringLength(320)] string UserName,
    [property: Required, EmailAddress, StringLength(320)] string Email,
    bool EmailConfirmed,
    [property: Phone, StringLength(64)] string? PhoneNumber,
    bool PhoneNumberConfirmed, bool TwoFactorEnabled, DateTimeOffset? LockoutEnd,
    bool LockoutEnabled, string? FaxNumber, string? MobileNumber);

/// <summary>Joined read document with the two original, resource-specific captured versions.</summary>
public sealed record CustomerAdministrationEdit(
    CustomerDetail Profile, CustomerAdministrationIdentity Identity,
    string ProfileVersion, string IdentityVersion);

/// <summary>Customer profile scalars and source flags only; no arbitrary identity or relation identifiers.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record CustomerAdministrationSaveRequest(
    [property: JsonRequired, Required, StringLength(256)] string FirstName,
    [property: JsonRequired, Required, StringLength(256)] string LastName,
    [property: JsonRequired, Required, EmailAddress, StringLength(256),
        RegularExpression(@"\A[a-zA-Z0-9._@+-]+\z")] string Email,
    [property: Phone, StringLength(64)] string? Telephone,
    [property: StringLength(64)] string? Mobile,
    [property: StringLength(64)] string? Fax,
    DateTime? DateOfBirth,
    [property: JsonRequired] bool EmailConfirmed,
    [property: JsonRequired] bool PhoneNumberConfirmed,
    [property: JsonRequired] bool LockoutEnabled,
    [property: JsonRequired, Required] string ProfileVersion,
    [property: JsonRequired, Required] string IdentityVersion)
{
    /// <summary>Maps only existing allowlisted profile fields; the producer/coordinator owns all relations.</summary>
    public CustomerUpdateRequest ToProfile() => new()
    {
        FirstName = FirstName,
        LastName = LastName,
        Email = Email,
        Telephone = Telephone,
        Mobile = Mobile,
        Fax = Fax,
        DateOfBirth = DateOfBirth,
    };

    /// <summary>Preserves protected settings from the identity bound to this customer and captured version.</summary>
    public CustomerAdministrationIdentityUpdate ToIdentity(int customerId, CustomerAdministrationIdentity identity)
    {
        if (!CustomerAdministrationVersion.IsBound(customerId, identity, IdentityVersion))
            throw new ArgumentException("A bound captured customer identity is required.", nameof(identity));
        return new(Email, Email, EmailConfirmed, Telephone, PhoneNumberConfirmed,
            identity.TwoFactorEnabled, identity.LockoutEnd, LockoutEnabled, Fax, Mobile);
    }
}

/// <summary>PII-free write progression; partial or unknown outcomes require explicit readback, never automatic replay.</summary>
public sealed record CustomerAdministrationSaveResult(
    string Stage, IReadOnlyList<string> CompletedStages, bool OutcomeUnknown, int StatusCode);

/// <summary>Validates captured producer ETags without replacing them with a newer read.</summary>
public static class CustomerAdministrationVersion
{
    /// <summary>Accepts the Customer producer's single quoted eight-digit lowercase xmin version.</summary>
    public static bool IsProfile(string? version) => IsValid(version, 8, "0123456789abcdef");

    /// <summary>Accepts the Auth producer's single quoted uppercase 64-digit identity version.</summary>
    public static bool IsIdentity(string? version) => IsValid(version, 64, "0123456789ABCDEF");

    /// <summary>Checks resource ownership and exact agreement between the captured strong ETag and body version.</summary>
    public static bool IsBound(int customerId, CustomerAdministrationIdentity? identity, string? version) =>
        customerId > 0 && identity is not null && identity.DatabaseID == customerId &&
        !string.IsNullOrWhiteSpace(identity.Id) && IsIdentity(version) &&
        string.Equals(version, '"' + identity.Version + '"', StringComparison.Ordinal);

    private static bool IsValid(string? version, int digits, string alphabet) =>
        version is not null && version.Length == digits + 2 && version[0] == '"' && version[^1] == '"' &&
        version.AsSpan(1, digits).IndexOfAnyExcept(alphabet.AsSpan()) < 0;
}
