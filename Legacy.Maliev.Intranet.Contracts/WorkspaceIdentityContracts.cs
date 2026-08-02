using System.Globalization;

namespace Legacy.Maliev.Intranet.Contracts;

/// <summary>Shared employee identity rules used by both the browser and the BFF.</summary>
public static class WorkspaceIdentityRules
{
    /// <summary>The only email domain allowed for employee workspace sign-in.</summary>
    public const string AllowedEmailDomain = "maliev.com";

    /// <summary>The non-production email domain reserved for local Aspire fixtures.</summary>
    public const string LocalTestEmailDomain = "maliev.test";

    /// <summary>Returns whether an email is a well-formed MALIEV workspace address.</summary>
    public static bool IsAllowedEmployeeEmail(string? email)
        => IsAllowedEmployeeEmail(email, allowLocalTestDomain: false);

    /// <summary>
    /// Returns whether an email is an allowed workspace address for the specified runtime.
    /// The local fixture domain is opt-in so production callers remain restricted to MALIEV's
    /// corporate domain.
    /// </summary>
    public static bool IsAllowedEmployeeEmail(string? email, bool allowLocalTestDomain)
    {
        var normalized = email?.Trim();
        if (string.IsNullOrWhiteSpace(normalized) || normalized.Any(char.IsWhiteSpace))
        {
            return false;
        }

        var atIndex = normalized.IndexOf('@', StringComparison.Ordinal);
        if (atIndex <= 0 || atIndex != normalized.LastIndexOf('@') || atIndex == normalized.Length - 1)
        {
            return false;
        }

        var domain = normalized[(atIndex + 1)..];
        return string.Equals(domain, AllowedEmailDomain, StringComparison.OrdinalIgnoreCase) ||
               (allowLocalTestDomain &&
                string.Equals(domain, LocalTestEmailDomain, StringComparison.OrdinalIgnoreCase));
    }
}

/// <summary>Supported employee workspace cultures and safe normalization helpers.</summary>
public static class WorkspaceCulture
{
    /// <summary>The default workspace language used before a saved preference is applied.</summary>
    public const string DefaultCultureName = "en-TH";

    /// <summary>The exact language values accepted by the current workspace.</summary>
    public static IReadOnlyList<string> SupportedCultureNames { get; } =
        [DefaultCultureName, "th-TH", "en-US"];

    /// <summary>Normalizes an untrusted language value to one of the supported values.</summary>
    public static string Normalize(string? cultureName)
    {
        var candidate = cultureName?.Trim();
        return SupportedCultureNames.FirstOrDefault(
                   value => string.Equals(value, candidate, StringComparison.OrdinalIgnoreCase))
               ?? DefaultCultureName;
    }

    /// <summary>Creates the normalized .NET culture used for UI resources and formatting.</summary>
    public static CultureInfo GetCulture(string? cultureName) =>
        CultureInfo.GetCultureInfo(Normalize(cultureName));

    /// <summary>Applies a normalized culture to the current WebAssembly execution context.</summary>
    public static void Apply(string? cultureName)
    {
        var culture = GetCulture(cultureName);
        CultureInfo.DefaultThreadCurrentCulture = culture;
        CultureInfo.DefaultThreadCurrentUICulture = culture;
        CultureInfo.CurrentCulture = culture;
        CultureInfo.CurrentUICulture = culture;
    }
}
