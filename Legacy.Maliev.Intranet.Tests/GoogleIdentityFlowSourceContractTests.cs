namespace Legacy.Maliev.Intranet.Tests;

public sealed class GoogleIdentityFlowSourceContractTests
{
    [Fact]
    public void BffAndClient_ExposeOnlyTheNonceBoundSameOriginContract()
    {
        var root = FindRepositoryRoot();
        var bff = File.ReadAllText(Path.Combine(root, "Legacy.Maliev.Intranet.Bff", "Program.cs"));
        var flow = File.ReadAllText(Path.Combine(root, "Legacy.Maliev.Intranet.Bff", "GoogleIdentityBffFlow.cs"));
        var client = File.ReadAllText(Path.Combine(root, "Legacy.Maliev.Intranet.Client", "wwwroot", "js", "google-identity-signin.js"));
        var login = File.ReadAllText(Path.Combine(root, "Legacy.Maliev.Intranet.Client", "Pages", "Login.razor"));
        var index = File.ReadAllText(Path.Combine(root, "Legacy.Maliev.Intranet.Client", "wwwroot", "index.html"));

        Assert.Contains("/bff/google/nonce", bff, StringComparison.Ordinal);
        Assert.Contains("/bff/google", bff, StringComparison.Ordinal);
        Assert.Contains("AllowAnonymous()", bff, StringComparison.Ordinal);
        Assert.Contains("RequireRateLimiting(\"employee-login\")", bff, StringComparison.Ordinal);
        Assert.Contains("Authentication:Google:ClientId", bff, StringComparison.Ordinal);
        Assert.Contains("FixedTimeEquals", flow, StringComparison.Ordinal);
        Assert.Contains("CreateProtector", flow, StringComparison.Ordinal);
        Assert.Contains("/bff/google/nonce", client, StringComparison.Ordinal);
        Assert.Contains("/bff/google\"", client, StringComparison.Ordinal);
        Assert.Contains("google.accounts.id.initialize", client, StringComparison.Ordinal);
        Assert.Contains("google.accounts.id.renderButton", client, StringComparison.Ordinal);
        Assert.Contains("data-google-signin-host", login, StringComparison.Ordinal);
        Assert.Contains("data-status-unavailable", login, StringComparison.Ordinal);
        Assert.Contains("localizedStatus(host, \"statusUnavailable\"", client, StringComparison.Ordinal);
        Assert.Contains("https://accounts.google.com/gsi/client", index, StringComparison.Ordinal);
        Assert.Contains("google-identity-signin.js", index, StringComparison.Ordinal);
        Assert.DoesNotContain("client_secret", client, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("access_token", client, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("refresh_token", client, StringComparison.OrdinalIgnoreCase);
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
