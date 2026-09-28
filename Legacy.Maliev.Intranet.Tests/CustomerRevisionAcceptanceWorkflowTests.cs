namespace Legacy.Maliev.Intranet.Tests;

public sealed class CustomerRevisionAcceptanceWorkflowTests
{
    [Fact]
    public void PullRequestValidation_RunsJoinedContractAgainstPinnedProducer()
    {
        var root = FindRoot();
        var workflow = File.ReadAllText(Path.Combine(root, ".github", "workflows", "_build-and-test.yml"));
        const string project = "acceptance/CustomerRevision/CustomerRevision.AcceptanceTests.csproj";
        const string producerCommit = "cebf45e8e1eeb600d760a565f8b0970c7f434148";

        Assert.Contains("repository: MALIEV-Co-Ltd/Legacy.Maliev.CustomerService", workflow, StringComparison.Ordinal);
        Assert.Contains($"ref: {producerCommit}", workflow, StringComparison.Ordinal);
        Assert.Contains("path: .dependencies/Legacy.Maliev.CustomerService", workflow, StringComparison.Ordinal);
        Assert.Contains("persist-credentials: false", workflow, StringComparison.Ordinal);
        Assert.Contains("Validate joined customer revision contract", workflow, StringComparison.Ordinal);
        Assert.Contains($"dotnet build {project}", workflow, StringComparison.Ordinal);
        Assert.Contains($"dotnet test {project}", workflow, StringComparison.Ordinal);
        Assert.Contains("export GITHUB_ACTIONS=false", workflow, StringComparison.Ordinal);
        Assert.Contains("-p:UseLocalMalievDependencies=true", workflow, StringComparison.Ordinal);
        Assert.True(File.Exists(Path.Combine(root, "acceptance", "CustomerRevision", "CustomerRevision.AcceptanceTests.csproj")));
    }

    private static string FindRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Legacy.Maliev.Intranet.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new DirectoryNotFoundException("Intranet solution root not found.");
    }
}
