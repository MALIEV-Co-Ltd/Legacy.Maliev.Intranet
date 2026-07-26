using Legacy.Maliev.Intranet.Contracts;

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
}
