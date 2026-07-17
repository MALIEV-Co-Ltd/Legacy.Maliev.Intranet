using Microsoft.AspNetCore.Http;

namespace Legacy.Maliev.Intranet.Auth;

/// <summary>Defines the compatibility cookie transport policy without requiring a configured production host.</summary>
public static class LegacyCookieSecurity
{
    /// <summary>Returns the secure-cookie policy for the named host environment.</summary>
    public static CookieSecurePolicy ResolveSecurePolicy(string environmentName) =>
        string.Equals(environmentName, "Development", StringComparison.OrdinalIgnoreCase)
        || string.Equals(environmentName, "Testing", StringComparison.OrdinalIgnoreCase)
            ? CookieSecurePolicy.SameAsRequest
            : CookieSecurePolicy.Always;
}
