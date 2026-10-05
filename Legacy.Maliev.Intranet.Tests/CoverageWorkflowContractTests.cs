namespace Legacy.Maliev.Intranet.Tests;

public sealed class CoverageWorkflowContractTests
{
    [Fact]
    public void Ci_CollectsAndGatesCoverageForMigratedRuntimeAssemblies()
    {
        var root = FindRepositoryRoot();
        var testProject = File.ReadAllText(Path.Combine(
            root,
            "Legacy.Maliev.Intranet.Tests",
            "Legacy.Maliev.Intranet.Tests.csproj"));
        var contractsProject = File.ReadAllText(Path.Combine(
            root,
            "Legacy.Maliev.Intranet.Contracts",
            "Legacy.Maliev.Intranet.Contracts.csproj"));
        var workflow = File.ReadAllText(Path.Combine(root, ".github", "workflows", "_build-and-test.yml"));
        var gate = File.ReadAllText(Path.Combine(root, "scripts", "verify-test-coverage.ps1"));
        var settings = File.ReadAllText(Path.Combine(root, "coverage.runsettings"));

        Assert.Contains("coverlet.collector", testProject, StringComparison.Ordinal);
        Assert.Contains("--collect:\"XPlat Code Coverage\"", workflow, StringComparison.Ordinal);
        Assert.Contains("-p:EnableCoverageSymbols=true", workflow, StringComparison.Ordinal);
        Assert.Contains("--settings coverage.runsettings", workflow, StringComparison.Ordinal);
        Assert.Contains("coverage.cobertura.xml", workflow, StringComparison.Ordinal);
        Assert.Contains("verify-test-coverage.ps1", workflow, StringComparison.Ordinal);
        Assert.Contains("'Legacy.Maliev.Intranet.Bff' = 0.80", gate, StringComparison.Ordinal);
        Assert.Contains("'Legacy.Maliev.Intranet.Server' = 0.85", gate, StringComparison.Ordinal);
        Assert.Contains("'Legacy.Maliev.Intranet.Contracts' = 0.95", gate, StringComparison.Ordinal);
        Assert.Contains("line coverage is", gate, StringComparison.Ordinal);
        Assert.DoesNotContain("<ExcludeByFile>", settings, StringComparison.Ordinal);
        Assert.Contains("<SkipAutoProps>false</SkipAutoProps>", settings, StringComparison.Ordinal);
        Assert.Contains("--no-incremental", workflow, StringComparison.Ordinal);
        Assert.Contains("-warnaserror", workflow, StringComparison.Ordinal);
        Assert.Contains("-p:EmitCompilerGeneratedFiles=true", workflow, StringComparison.Ordinal);
        Assert.Contains("--logger \"trx;LogFileName=coverage.trx\"", workflow, StringComparison.Ordinal);
        Assert.Contains("-Mode Capture", workflow, StringComparison.Ordinal);
        Assert.Contains("-Mode Verify", workflow, StringComparison.Ordinal);
        var capture = workflow.IndexOf("-Mode Capture", StringComparison.Ordinal);
        var test = workflow.IndexOf("dotnet test Legacy.Maliev.Intranet.Tests/", capture, StringComparison.Ordinal);
        var verify = workflow.IndexOf("-Mode Verify", test, StringComparison.Ordinal);
        Assert.True(capture < test && test < verify);
        Assert.Contains("--no-build", workflow[capture..verify], StringComparison.Ordinal);
        Assert.Contains("'$(EnableCoverageSymbols)' != 'true'", contractsProject, StringComparison.Ordinal);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Legacy.Maliev.Intranet.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new InvalidOperationException("Could not locate the Legacy.Maliev.Intranet repository root.");
    }
}
