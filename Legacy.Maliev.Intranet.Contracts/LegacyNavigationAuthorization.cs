namespace Legacy.Maliev.Intranet.Contracts;

/// <summary>
/// Applies the same fail-closed permission semantics to legacy workspace navigation
/// that the current Intranet shell uses for its navigation items.
/// </summary>
public static class LegacyNavigationAuthorization
{
    /// <summary>
    /// Determines whether a navigation item can be activated for the projected session.
    /// </summary>
    /// <param name="isAuthenticated">Whether the server-side session is authenticated.</param>
    /// <param name="requiredPermission">The permission required by the destination, if any.</param>
    /// <param name="permissions">Server-issued permission grants.</param>
    /// <param name="roles">Server-issued employee roles.</param>
    /// <returns><see langword="true"/> only when the session can activate the item.</returns>
    public static bool IsEnabled(
        bool isAuthenticated,
        string? requiredPermission,
        IEnumerable<string>? permissions,
        IEnumerable<string>? roles)
    {
        if (!isAuthenticated)
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(requiredPermission))
        {
            return true;
        }

        var grants = (permissions ?? [])
            .Concat(roles ?? [])
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (grants.Any(static grant =>
                string.Equals(grant, "platform.owner", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(grant, "*", StringComparison.Ordinal)))
        {
            return true;
        }

        return grants.Any(grant => Matches(requiredPermission, grant));
    }

    private static bool Matches(string requiredPermission, string grant)
    {
        if (string.Equals(requiredPermission, grant, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var requiredParts = requiredPermission.Split('.', StringSplitOptions.RemoveEmptyEntries);
        var grantParts = grant.Split('.', StringSplitOptions.RemoveEmptyEntries);
        if (requiredParts.Length == 0 || grantParts.Length == 0)
        {
            return false;
        }

        for (var index = 0; index < grantParts.Length; index++)
        {
            if (grantParts[index] == "*")
            {
                return index == grantParts.Length - 1;
            }

            if (index >= requiredParts.Length ||
                !string.Equals(requiredParts[index], grantParts[index], StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        return grantParts.Length == requiredParts.Length;
    }
}
