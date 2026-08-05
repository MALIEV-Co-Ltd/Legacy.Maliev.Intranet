namespace Legacy.Maliev.Intranet.Tests;

public sealed class LoginFormContractTests
{
    [Fact]
    public void LoginEmailPattern_RendersAValidHtmlRegularExpression()
    {
        var root = FindRoot();
        var source = File.ReadAllText(Path.Combine(
            root,
            "Legacy.Maliev.Intranet.Client",
            "Pages",
            "Login.razor"));

        Assert.Contains(
            "? \"^[^@\\\\s]+@(maliev\\\\.com|maliev\\\\.test)$\"",
            source,
            StringComparison.Ordinal);
        Assert.Contains(
            ": \"^[^@\\\\s]+@maliev\\\\.com$\";",
            source,
            StringComparison.Ordinal);
        Assert.DoesNotContain("[^@@\\\\s]", source, StringComparison.Ordinal);
        Assert.DoesNotContain("@@(maliev", source, StringComparison.Ordinal);
    }

    private static string FindRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Legacy.Maliev.Intranet.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new DirectoryNotFoundException("Could not find repository root.");
    }
}
