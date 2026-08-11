using System.Xml.Linq;

namespace Legacy.Maliev.Intranet.Tests;

public sealed class LegacyLoginExperienceContractTests
{
    [Fact]
    public void LoginClient_ExposesStablePreflightAndHonestGoogleAvailabilityStates()
    {
        var root = FindRepositoryRoot();
        var login = File.ReadAllText(Path.Combine(root, "Legacy.Maliev.Intranet.Client", "Pages", "Login.razor"));
        var google = File.ReadAllText(Path.Combine(root, "Legacy.Maliev.Intranet.Client", "wwwroot", "js", "google-identity-signin.js"));

        Assert.Contains("legacy-login-loading", login, StringComparison.Ordinal);
        Assert.Contains("role=\"status\" aria-live=\"polite\"", login, StringComparison.Ordinal);
        Assert.Contains("data-google-signin-state", login, StringComparison.Ordinal);
        Assert.Contains("host.hidden = state === \"unavailable\"", google, StringComparison.Ordinal);
        Assert.Contains("section.dataset.googleSigninState = state", google, StringComparison.Ordinal);
        Assert.Contains("setAvailability(host, \"ready\")", google, StringComparison.Ordinal);
        Assert.Contains("setAvailability(host, \"unavailable\")", google, StringComparison.Ordinal);
    }

    [Fact]
    public void LoginClient_LocalizesPreflightAndRecoveryGuidanceInEnglishAndThai()
    {
        var root = FindRepositoryRoot();
        var expectedKeys = new[]
        {
            "Employee gateway",
            "MALIEV employee workspace",
            "Secure access to your work",
            "Sign in with your MALIEV work account to continue.",
            "Checking your employee session...",
            "Need help accessing your employee account?",
            "Contact your MALIEV administrator if you cannot access your employee account.",
        };

        foreach (var resourcePath in new[]
                 {
                     Path.Combine(root, "Legacy.Maliev.Intranet.Client", "Pages", "Login.resx"),
                     Path.Combine(root, "Legacy.Maliev.Intranet.Client", "Pages", "Login.th.resx"),
                 })
        {
            var keys = XDocument.Load(resourcePath).Root!.Elements("data")
                .Select(data => data.Attribute("name")!.Value)
                .ToHashSet(StringComparer.Ordinal);

            foreach (var expectedKey in expectedKeys)
            {
                Assert.Contains(expectedKey, keys);
            }
        }
    }

    [Fact]
    public void LoginClient_UsesFourPixelSpacingCadenceAtAuthenticationBoundaries()
    {
        var root = FindRepositoryRoot();
        var css = File.ReadAllText(Path.Combine(root, "Legacy.Maliev.Intranet.Client", "wwwroot", "css", "app.css"));

        Assert.Contains(".legacy-login-card { width: min(100%, 26.25rem); min-width: 0; padding: 2rem;", css, StringComparison.Ordinal);
        Assert.Contains(".legacy-login-error { margin-bottom: 1rem; padding: .75rem 1rem;", css, StringComparison.Ordinal);
        Assert.Contains(".legacy-login-divider { display: grid; grid-template-columns: 1fr auto 1fr; align-items: center; gap: .75rem; margin: 1rem 0;", css, StringComparison.Ordinal);
        Assert.Contains(".legacy-login-email-summary { display: flex; min-height: 2.5rem;", css, StringComparison.Ordinal);
        Assert.Contains(".legacy-login-title { display: flex; align-items: center; flex-wrap: wrap; gap: .5rem;", css, StringComparison.Ordinal);
        Assert.Contains(".legacy-login-email-summary { display: flex; min-height: 2.5rem; align-items: center; justify-content: space-between; gap: .75rem; padding-inline: .75rem;", css, StringComparison.Ordinal);
        Assert.Contains(".legacy-login-main { padding: 1rem; }", css, StringComparison.Ordinal);
        Assert.Contains(".legacy-login-card { padding: 1.5rem; }", css, StringComparison.Ordinal);
        Assert.DoesNotContain("padding: 1.875rem", css, StringComparison.Ordinal);
        Assert.DoesNotContain("padding: .625rem", css, StringComparison.Ordinal);
        Assert.DoesNotContain("margin: 1.125rem 0", css, StringComparison.Ordinal);
        Assert.DoesNotContain("min-height: 2.375rem", css, StringComparison.Ordinal);
        Assert.DoesNotContain("padding: 1.125rem 1rem", css, StringComparison.Ordinal);
        Assert.DoesNotContain("padding: 1.375rem", css, StringComparison.Ordinal);
    }

    [Fact]
    public void LoginClient_OwnsTheViewportWithResponsiveBrandAndAuthenticationRegions()
    {
        var root = FindRepositoryRoot();
        var login = File.ReadAllText(Path.Combine(root, "Legacy.Maliev.Intranet.Client", "Pages", "Login.razor"));
        var css = File.ReadAllText(Path.Combine(root, "Legacy.Maliev.Intranet.Client", "wwwroot", "css", "app.css"));

        Assert.Contains("legacy-login-shell", login, StringComparison.Ordinal);
        Assert.Contains("legacy-login-brand-panel", login, StringComparison.Ordinal);
        Assert.Contains("legacy-login-auth-panel", login, StringComparison.Ordinal);
        Assert.Contains("<h1>@Text[\"Secure access to your work\"]</h1>", login, StringComparison.Ordinal);
        Assert.DoesNotContain("legacy-login-eyebrow", login, StringComparison.Ordinal);
        Assert.True(login.IndexOf("legacy-login-brand-panel", StringComparison.Ordinal) < login.IndexOf("legacy-login-auth-panel", StringComparison.Ordinal));
        Assert.Equal(1, login.Split("href=\"https://www.maliev.com\"", StringSplitOptions.None).Length - 1);
        Assert.Contains("place-items: stretch", css, StringComparison.Ordinal);
        Assert.Contains(".legacy-login-page { width: 100%;", css, StringComparison.Ordinal);
        Assert.Contains("grid-template-columns: minmax(0, 44%) minmax(0, 56%)", css, StringComparison.Ordinal);
        Assert.Contains("@media (max-width: 959px)", css, StringComparison.Ordinal);
        Assert.Contains("@media (max-width: 640px)", css, StringComparison.Ordinal);
        Assert.Contains(".legacy-login-main { padding: 1rem; }", css, StringComparison.Ordinal);
    }

    [Fact]
    public void LoginClient_PreservesCurrentGatewayFlowAndSameOriginAuthBoundary()
    {
        var root = FindRepositoryRoot();
        var login = File.ReadAllText(Path.Combine(
            root,
            "Legacy.Maliev.Intranet.Client",
            "Pages",
            "Login.razor"));

        Assert.Contains("ContinueWithEmail", login, StringComparison.Ordinal);
        Assert.Contains("BackToEmailStep", login, StringComparison.Ordinal);
        Assert.Contains("data-google-signin-host", login, StringComparison.Ordinal);
        Assert.Contains("EmployeeAuthenticationClient", login, StringComparison.Ordinal);
        Assert.Contains("Remember me", login, StringComparison.Ordinal);
        Assert.Contains("WorkspaceIdentityRules.IsAllowedEmployeeEmail", login, StringComparison.Ordinal);
        Assert.Contains("IsLocalAspireHost", login, StringComparison.Ordinal);
        Assert.Contains("maliev\\\\.test", login, StringComparison.Ordinal);
        Assert.Contains("<MudForm Class=\"legacy-login-form\">", login, StringComparison.Ordinal);
        Assert.Contains("ButtonType=\"ButtonType.Button\"", login, StringComparison.Ordinal);
        Assert.Contains("OnKeyDown=\"HandleEmailKeyDown\"", login, StringComparison.Ordinal);
        Assert.Contains("OnKeyDown=\"HandlePasswordKeyDown\"", login, StringComparison.Ordinal);
        Assert.DoesNotContain("access_token", login, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("refresh_token", login, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("client_secret", login, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void LoginClient_DoesNotInvokeGoogleInteropBeforeTheHostIsRendered()
    {
        var root = FindRepositoryRoot();
        var login = File.ReadAllText(Path.Combine(
            root,
            "Legacy.Maliev.Intranet.Client",
            "Pages",
            "Login.razor"));

        Assert.Contains("private bool _googleIdentityInitialized", login, StringComparison.Ordinal);
        Assert.Contains("if (_isCheckingAuth || _googleIdentityInitialized)", login, StringComparison.Ordinal);
        Assert.Contains("_googleIdentityInitialized = true", login, StringComparison.Ordinal);
        Assert.Contains("_googleIdentityInitialized = false", login, StringComparison.Ordinal);
    }

    [Fact]
    public void LoginClient_PreservesThemeBootstrapResponsiveAndAccessibilityContracts()
    {
        var root = FindRepositoryRoot();
        var index = File.ReadAllText(Path.Combine(
            root,
            "Legacy.Maliev.Intranet.Client",
            "wwwroot",
            "index.html"));
        var css = File.ReadAllText(Path.Combine(
            root,
            "Legacy.Maliev.Intranet.Client",
            "wwwroot",
            "css",
            "app.css"));

        Assert.Contains("window.malievTheme", index, StringComparison.Ordinal);
        Assert.Contains("localStorage", index, StringComparison.Ordinal);
        Assert.Contains("dataset.malievTheme", index, StringComparison.Ordinal);
        Assert.Contains("prefers-color-scheme: dark", index, StringComparison.Ordinal);
        Assert.Contains("data-google-signin-host", File.ReadAllText(Path.Combine(
            root,
            "Legacy.Maliev.Intranet.Client",
            "Pages",
            "Login.razor")), StringComparison.Ordinal);
        Assert.Contains("@media (max-width: 640px)", css, StringComparison.Ordinal);
        Assert.Contains("@media (prefers-reduced-motion: reduce)", css, StringComparison.Ordinal);
        Assert.Contains("@media (forced-colors: active)", css, StringComparison.Ordinal);
        Assert.Contains("role=\"alert\"", File.ReadAllText(Path.Combine(
            root,
            "Legacy.Maliev.Intranet.Client",
            "Pages",
            "Login.razor")), StringComparison.Ordinal);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null &&
            !File.Exists(Path.Combine(directory.FullName, "Legacy.Maliev.Intranet.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("Repository root was not found.");
    }
}
