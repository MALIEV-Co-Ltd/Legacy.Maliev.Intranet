using Microsoft.AspNetCore.Http;

namespace Legacy.Maliev.Intranet.Auth;

/// <summary>Defines the compatibility cookie transport policy. All employee session and antiforgery
/// cookies are HTTPS-only in every environment, including local Aspire — there is no HTTP fallback.</summary>
public static class LegacyCookieSecurity
{
    /// <summary>Returns the secure-cookie policy. Always <see cref="CookieSecurePolicy.Always"/>;
    /// the <paramref name="environmentName"/> parameter is retained for call-site compatibility and
    /// diagnostic clarity only. Hosts must be served over HTTPS in every environment.</summary>
    public static CookieSecurePolicy ResolveSecurePolicy(string environmentName) => CookieSecurePolicy.Always;
}
