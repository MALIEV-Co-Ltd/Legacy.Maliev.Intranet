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
        Assert.Contains("          pwsh -NoProfile -File scripts/verify-test-coverage.ps1 -CoverageFile \"$coverage_file\"\n" + expected,
            workflow, StringComparison.Ordinal);
    }

    [Fact]
    public void Preservation_DoesNotChangeExistingCollectorExclusionOrCoverageFloors()
    {
        var workflow = Read(".github", "workflows", "_build-and-test.yml");
        var collector = workflow[workflow.IndexOf("      - name: Collect and gate Intranet coverage", StringComparison.Ordinal)..workflow.IndexOf("      - name: Preserve actual runner validation evidence", StringComparison.Ordinal)];
        Assert.Contains("dotnet test Legacy.Maliev.Intranet.Tests/Legacy.Maliev.Intranet.Tests.csproj", collector, StringComparison.Ordinal);
        Assert.Contains("--collect:\"XPlat Code Coverage\"", collector, StringComparison.Ordinal);
        Assert.Contains("--settings coverage.runsettings", collector, StringComparison.Ordinal);
        Assert.Contains("--results-directory TestResults", collector, StringComparison.Ordinal);
        Assert.DoesNotContain("--logger", collector, StringComparison.Ordinal);
        Assert.DoesNotContain("--filter", collector, StringComparison.Ordinal);
        Assert.DoesNotContain("continue-on-error", workflow, StringComparison.Ordinal);
        Assert.Contains("<ExcludeByFile>**/obj/**</ExcludeByFile>", Read("coverage.runsettings"), StringComparison.Ordinal);
        var guard = Read("scripts", "verify-test-coverage.ps1");
        Assert.Contains("'Legacy.Maliev.Intranet.Bff' = 0.80", guard, StringComparison.Ordinal);
        Assert.Contains("'Legacy.Maliev.Intranet.Server' = 0.85", guard, StringComparison.Ordinal);
        Assert.Contains("'Legacy.Maliev.Intranet.Contracts' = 0.95", guard, StringComparison.Ordinal);
    }

    private static string Read(params string[] parts)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Legacy.Maliev.Intranet.slnx"))) directory = directory.Parent;
        var root = directory?.FullName ?? throw new DirectoryNotFoundException("Could not find repository root.");
        return File.ReadAllText(Path.Combine(new[] { root }.Concat(parts).ToArray())).Replace("\r\n", "\n", StringComparison.Ordinal);
    }
}
