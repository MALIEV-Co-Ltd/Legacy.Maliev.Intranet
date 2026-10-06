namespace Legacy.Maliev.Intranet.Tests;

public sealed class ValidationEvidenceCallerContractTests
{
    [Fact]
    public void Preservation_IsFinalAlwaysStepWithAcceptedPinAndExactProductionInputs()
    {
        var workflow = Read(".github", "workflows", "_build-and-test.yml");
        var expected = string.Join('\n', new[]
        {
            "      - name: Preserve actual runner validation evidence",
            "        if: always()",
            "        uses: MALIEV-Co-Ltd/Legacy.Maliev.Workflows/actions/preserve-validation-evidence@45ced0919a604459c7bb5baf7f38d7bc53d708ba",
            "        with:",
            "          results-directory: TestResults",
            "          production-projects: |",
            "            Legacy.Maliev.Intranet.Bff",
            "            Legacy.Maliev.Intranet.Server",
            "            Legacy.Maliev.Intranet.Contracts",
            string.Empty,
        });

        Assert.EndsWith(expected, workflow, StringComparison.Ordinal);
        Assert.Equal(1, workflow.Split("actions/preserve-validation-evidence@", StringSplitOptions.None).Length - 1);
        var collector = Step(workflow, "Collect and gate Intranet coverage");
        Assert.EndsWith("          pwsh -NoProfile -File scripts/verify-test-coverage.ps1 -CoverageFile \"$coverage_file\"\n",
            collector, StringComparison.Ordinal);
        Assert.True(workflow.IndexOf(collector, StringComparison.Ordinal) < workflow.IndexOf(expected, StringComparison.Ordinal));
    }

    [Fact]
    public void Preservation_RetainsSameCoverageRunWithoutWeakeningCoverageFloors()
    {
        var workflow = Read(".github", "workflows", "_build-and-test.yml");
        var collector = Step(workflow, "Collect and gate Intranet coverage");
        Assert.Contains("dotnet test Legacy.Maliev.Intranet.Tests/Legacy.Maliev.Intranet.Tests.csproj", collector, StringComparison.Ordinal);
        Assert.Contains("--collect:\"XPlat Code Coverage\"", collector, StringComparison.Ordinal);
        Assert.Contains("--settings coverage.runsettings", collector, StringComparison.Ordinal);
        Assert.Contains("--results-directory TestResults/coverage-native", collector, StringComparison.Ordinal);
        Assert.Contains("--logger \"trx;LogFileName=coverage.trx\"", collector, StringComparison.Ordinal);
        Assert.DoesNotContain("--filter", collector, StringComparison.Ordinal);
        Assert.DoesNotContain("continue-on-error", workflow, StringComparison.Ordinal);
        Assert.DoesNotContain("<ExcludeByFile>", Read("coverage.runsettings"), StringComparison.Ordinal);
        Assert.Contains("<SkipAutoProps>false</SkipAutoProps>", Read("coverage.runsettings"), StringComparison.Ordinal);
        var guard = Read("scripts", "verify-test-coverage.ps1");
        Assert.Contains("'Legacy.Maliev.Intranet.Bff' = 0.80", guard, StringComparison.Ordinal);
        Assert.Contains("'Legacy.Maliev.Intranet.Server' = 0.85", guard, StringComparison.Ordinal);
        Assert.Contains("'Legacy.Maliev.Intranet.Contracts' = 0.95", guard, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("name: Focused proof")]
    [InlineData("id: focused_proof")]
    [InlineData("uses: reviewed/action@pin")]
    public void CollectorBoundary_StopsAtAnyFollowingYamlStep(string nextStep)
    {
        var collector = "      - name: Collect and gate Intranet coverage\n        run: |\n          echo '      - name: shell text'\n          dotnet test --collect:coverage\n";
        var workflow = collector + "      - " + nextStep + "\n        run: dotnet test --filter Focused\n";

        Assert.Equal(collector, Step(workflow, "Collect and gate Intranet coverage"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CollectorBoundary_RejectsMissingOrDuplicateCollector(bool duplicate)
    {
        var collector = "      - name: Collect and gate Intranet coverage\n        run: dotnet test\n";
        var workflow = duplicate ? collector + collector : "      - name: Other step\n";

        Assert.ThrowsAny<Xunit.Sdk.XunitException>(() => Step(workflow, "Collect and gate Intranet coverage"));
    }

    private static string Step(string workflow, string name)
    {
        var marker = "      - name: " + name + "\n";
        var start = workflow.IndexOf(marker, StringComparison.Ordinal);
        Assert.True(start >= 0, "The required coverage step must exist.");
        Assert.Equal(-1, workflow.IndexOf(marker, start + marker.Length, StringComparison.Ordinal));
        var end = workflow.IndexOf("\n      - ", start + marker.Length, StringComparison.Ordinal);
        return end < 0 ? workflow[start..] : workflow[start..(end + 1)];
    }

    private static string Read(params string[] parts)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Legacy.Maliev.Intranet.slnx"))) directory = directory.Parent;
        var root = directory?.FullName ?? throw new DirectoryNotFoundException("Could not find repository root.");
        return File.ReadAllText(Path.Combine(new[] { root }.Concat(parts).ToArray())).Replace("\r\n", "\n", StringComparison.Ordinal);
    }
}
