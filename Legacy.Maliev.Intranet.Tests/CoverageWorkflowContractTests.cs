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
        Assert.Equal(2, workflow.Split("-ResultsDirectory TestResults/coverage-native", StringSplitOptions.None).Length - 1);
        Assert.Contains("--results-directory TestResults/coverage-native", workflow, StringComparison.Ordinal);
        Assert.Contains("find TestResults/coverage-native -name coverage.cobertura.xml", workflow, StringComparison.Ordinal);
        Assert.Contains("'$(EnableCoverageSymbols)' != 'true'", contractsProject, StringComparison.Ordinal);
    }

    [Fact]
    public void Ci_ExecutesRecoveryFormComponentsAndNativeBrowserSubmissionBeforeFullValidation()
    {
        var root = FindRepositoryRoot();
        var workflow = File.ReadAllText(Path.Combine(root, ".github", "workflows", "_build-and-test.yml"));
        var full = workflow.IndexOf("      - name: Validate .NET solution", StringComparison.Ordinal);
        var unit = workflow.IndexOf("      - name: Execute employee creation email boundaries before full validation", StringComparison.Ordinal);
        var browser = workflow.IndexOf("      - name: Execute recovery form native email admission before full validation", StringComparison.Ordinal);
        Assert.True(unit >= 0 && browser > unit && full > browser);
        var unitStep = workflow[unit..browser];
        Assert.Contains("FullyQualifiedName~EmployeeCreationEmailValidationTests", unitStep, StringComparison.Ordinal);
        Assert.Contains("FullyQualifiedName~EmployeeCreateEmailBoundaryComponentTests", unitStep, StringComparison.Ordinal);
        Assert.Contains("FullyQualifiedName~BffEmployeesCreateContractTests.ProfileEmailStorageBoundary_PreservesAcceptedInputAndRejectsOversizeBeforeDownstreamWrites", unitStep, StringComparison.Ordinal);
        Assert.Contains("FullyQualifiedName~EmployeeRecoveryEmailValidationComponentTests", unitStep, StringComparison.Ordinal);
        Assert.Contains("FullyQualifiedName~CoverageWorkflowContractTests", unitStep, StringComparison.Ordinal);
        var browserStep = workflow[browser..full];
        Assert.Contains("Legacy.Maliev.Intranet.BrowserTests/Legacy.Maliev.Intranet.BrowserTests.csproj", browserStep, StringComparison.Ordinal);
        Assert.Contains("FullyQualifiedName=Legacy.Maliev.Intranet.BrowserTests.EmployeeRecoveryEmailSyntaxBrowserTests.NativeSubmit_UsesHistoricalPredicateInsteadOfHtmlEmailSyntax", browserStep, StringComparison.Ordinal);
        Assert.Contains("--configuration Release --no-build --no-restore", browserStep, StringComparison.Ordinal);
        Assert.Contains("test ! -e TestResults/recovery-email-browser", browserStep, StringComparison.Ordinal);
        Assert.Contains("trx;LogFileName=recovery-email-browser.trx", browserStep, StringComparison.Ordinal);
        Assert.Contains("\"total\":\"4\",\"executed\":\"4\",\"passed\":\"4\",\"failed\":\"0\",\"notExecuted\":\"0\"", browserStep, StringComparison.Ordinal);
        foreach (var prerequisite in new[]
        {
            "      - name: Build BrowserTests Release with zero warnings",
            "      - name: Require valid browser executables before browser regressions",
            "      - name: Publish isolated Documents WASM fixture for focused and full browser validation",
        })
        {
            var setup = workflow.IndexOf(prerequisite, StringComparison.Ordinal);
            Assert.True(setup >= 0 && setup < browser);
        }
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
