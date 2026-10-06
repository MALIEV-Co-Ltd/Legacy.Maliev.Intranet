using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace Legacy.Maliev.Intranet.Contracts;

/// <summary>Safe administrative identity projection; no hashes, stamps or credentials.</summary>
public sealed record EmployeeAdministrationIdentity(
    string Id, string? UserName, string? Email, bool EmailConfirmed,
    string? PhoneNumber, bool PhoneNumberConfirmed,
    [property: JsonRequired] bool TwoFactorEnabled,
    [property: JsonRequired] DateTimeOffset? LockoutEnd,
    bool LockoutEnabled, int AccessFailedCount,
    int DatabaseID, string Version);

/// <summary>Employee-owned edit projection, separate from identity and address versions.</summary>
public sealed record EmployeeAdministrationProfile(
    int Id, int? RoleId, string FirstName, string LastName, string FullName,
    string? PhoneNumber, string Email, DateTime? DateOfBirth, int? HomeAddressId,
    DateTime? CreatedDate, DateTime? ModifiedDate);

/// <summary>Allowlisted administrative identity fields; security material is never accepted.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record EmployeeAdministrationIdentityUpdate(
    [property: Required, EmailAddress, StringLength(320)] string UserName,
    [property: Required, EmailAddress, StringLength(320)] string Email,
    bool EmailConfirmed,
    [property: Phone, StringLength(64)] string? PhoneNumber,
    bool PhoneNumberConfirmed, bool TwoFactorEnabled,
    DateTimeOffset? LockoutEnd, bool LockoutEnabled);

/// <summary>Employee scalar write. The coordinator supplies the verified address relation.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record EmployeeAdministrationProfileUpdate(
    int? RoleId, string FirstName, string LastName, string? PhoneNumber,
    string Email, DateTime? DateOfBirth, int? HomeAddressId);

/// <summary>Address scalar fields without a browser-selectable owner or identifier.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record EmployeeAdministrationAddressUpdate(
    [property: StringLength(256)] string? Building,
    [property: StringLength(256)] string? AddressLine1,
    [property: StringLength(256)] string? AddressLine2,
    [property: StringLength(256)] string? City,
    [property: StringLength(256)] string? State,
    [property: StringLength(256)] string? PostalCode, int CountryId);

/// <summary>Internal producer request binding the verified address to its employee, not browser input.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record EmployeeAdministrationBoundAddressUpdate(
    int AddressId, string? Building, string? AddressLine1, string? AddressLine2,
    string? City, string? State, string? PostalCode, int CountryId);

/// <summary>Safe edit document with independently captured resource versions.</summary>
public sealed record EmployeeAdministrationEdit(
    EmployeeAdministrationProfile Profile, EmployeeAdministrationIdentity Identity,
    EmployeeAddressDetail? Address, IReadOnlyList<EmployeeRoleDetail> Roles,
    string ProfileVersion, string IdentityVersion, string? AddressVersion,
    IReadOnlyList<EmployeeAdministrationCountry> Countries);

/// <summary>Public country choices without unrelated catalog metadata or mutation capability.</summary>
public sealed record EmployeeAdministrationCountry(int Id, string Name);

/// <summary>Original employee form fields, without arbitrary identity or address identifiers.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record EmployeeAdministrationSaveRequest(
    [property: Required, StringLength(256)] string FirstName,
    [property: Required, StringLength(256)] string LastName,
    [property: Required, EmailAddress, StringLength(256)] string Email,
    [property: Phone, StringLength(64)] string? PhoneNumber,
    DateTime? DateOfBirth, int? RoleId,
    bool EmailConfirmed, bool PhoneNumberConfirmed, bool LockoutEnabled,
    int? CapturedHomeAddressId,
    [property: Required] string ProfileVersion,
    [property: Required] string IdentityVersion,
    string? AddressVersion, EmployeeAdministrationAddressUpdate? Address);

/// <summary>PII-free ordered-write result. Unknown outcomes require manual readback, never replay.</summary>
public sealed record EmployeeAdministrationSaveResult(
    string Stage, IReadOnlyList<string> CompletedStages, bool OutcomeUnknown,
    int StatusCode, int? CreatedAddressId = null);

/// <summary>Validates quoted producer versions without recomputing or replacing them.</summary>
public static class EmployeeAdministrationVersion
{
    /// <summary>Accepts exactly one uppercase Auth identity ETag.</summary>
    public static bool IsIdentity(string? version) => IsValid(version, "0123456789ABCDEF");

    /// <summary>Accepts exactly one lowercase Employee profile or address ETag.</summary>
    public static bool IsEmployee(string? version) => IsValid(version, "0123456789abcdef");

    private static bool IsValid(string? version, string alphabet) =>
        version is { Length: 66 } && version[0] == '"' && version[^1] == '"' &&
        version.AsSpan(1, 64).IndexOfAnyExcept(alphabet.AsSpan()) < 0;
}
