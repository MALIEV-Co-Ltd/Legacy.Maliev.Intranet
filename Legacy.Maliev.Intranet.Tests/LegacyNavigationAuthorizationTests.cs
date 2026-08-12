using Legacy.Maliev.Intranet.Contracts;
using System.Security.Claims;

namespace Legacy.Maliev.Intranet.Tests;

public sealed class LegacyNavigationAuthorizationTests
{
    [Fact]
    public void AnonymousSession_CannotActivateEvenUnscopedNavigation()
    {
        Assert.False(LegacyNavigationAuthorization.IsEnabled(false, null, [], []));
    }

    [Fact]
    public void AuthenticatedSession_CanActivateUnscopedNavigation()
    {
        Assert.True(LegacyNavigationAuthorization.IsEnabled(true, null, [], []));
    }

    [Fact]
    public void PermissionMustBeGrantedBeforeNavigationIsEnabled()
    {
        Assert.True(LegacyNavigationAuthorization.IsEnabled(
            true,
            "legacy.orders.read",
            ["legacy.orders.read"],
            []));
        Assert.False(LegacyNavigationAuthorization.IsEnabled(
            true,
            "legacy.orders.create",
            ["legacy.orders.read"],
            []));
    }

    [Fact]
    public void OwnerAndWildcardGrantsMatchCurrentWorkspaceSemantics()
    {
        Assert.True(LegacyNavigationAuthorization.IsEnabled(
            true,
            "legacy.accounting.read",
            [],
            ["platform.owner"]));
        Assert.True(LegacyNavigationAuthorization.IsEnabled(
            true,
            "legacy.accounting.read",
            ["legacy.accounting.*"],
            []));
        Assert.True(LegacyNavigationAuthorization.IsEnabled(
            true,
            "legacy.accounting.read",
            ["*"],
            []));
    }

    [Fact]
    public void PrincipalOverload_CombinesCaseInsensitivePermissionsAndRolesWithoutBroadeningMissingGrants()
    {
        var wildcard = Principal(
            [new Claim("permissions", "LEGACY.ORDERS.*")]);
        var ownerRole = Principal(
            [new Claim(ClaimTypes.Role, "PLATFORM.OWNER")]);
        var unrelated = Principal(
            [new Claim("permissions", "legacy.quotations.read")]);

        Assert.True(LegacyNavigationAuthorization.IsEnabled(wildcard, "legacy.orders.read"));
        Assert.True(LegacyNavigationAuthorization.IsEnabled(ownerRole, "legacy.accounting.read"));
        Assert.False(LegacyNavigationAuthorization.IsEnabled(unrelated, "legacy.orders.read"));
    }

    private static ClaimsPrincipal Principal(IEnumerable<Claim> claims) =>
        new(new ClaimsIdentity(claims, "test"));
}
