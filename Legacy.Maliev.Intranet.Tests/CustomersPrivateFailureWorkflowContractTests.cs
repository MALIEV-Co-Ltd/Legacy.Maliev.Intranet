namespace Legacy.Maliev.Intranet.Tests;

public sealed class CustomersPrivateFailureWorkflowContractTests
{
    [Fact]
    public void HostedGate_PreservesFullValidationBeforeActualEightCaseProof()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Legacy.Maliev.Intranet.slnx")))
        {
            directory = directory.Parent;
        }

        var root = directory?.FullName ?? throw new InvalidOperationException("Could not locate repository root.");
        var workflow = File.ReadAllText(Path.Combine(root, ".github", "workflows", "_build-and-test.yml"));
        var shared = workflow.IndexOf("actions/dotnet-validate@d7efac266bc66273bc45eab583618871292ecbd6", StringComparison.Ordinal);
        var strictBuild = workflow.IndexOf("--no-incremental", StringComparison.Ordinal);
        var coverageVerify = workflow.IndexOf("-Mode Verify", StringComparison.Ordinal);
        var focus = workflow.IndexOf("- name: Execute actual normal Program Customers", StringComparison.Ordinal);
        var proof = workflow.IndexOf("- name: Verify exact eight actual Customers", StringComparison.Ordinal);
        var preserve = workflow.IndexOf("- name: Preserve actual runner validation evidence", StringComparison.Ordinal);
        Assert.True(shared >= 0 && shared < strictBuild && strictBuild < coverageVerify && coverageVerify < focus && focus < proof && proof < preserve);
        Assert.Contains("timeout-minutes: 120", workflow, StringComparison.Ordinal);
        Assert.Contains("ref: 4517cf16f5f1159318e184969732d46eae4a8308", workflow, StringComparison.Ordinal);
        Assert.Contains("ref: 78e48ffc4ee000df0510cba5e7c7a3c4c4d539d7", workflow, StringComparison.Ordinal);
        Assert.Contains("test \"$(git -C .dependencies/Legacy.Maliev.ServiceDefaults rev-parse HEAD)\" = 4517cf16f5f1159318e184969732d46eae4a8308", workflow[..shared], StringComparison.Ordinal);
        Assert.Contains("test \"$(git -C .dependencies/Legacy.Maliev.CompatibilityContracts rev-parse HEAD)\" = 78e48ffc4ee000df0510cba5e7c7a3c4c4d539d7", workflow[..shared], StringComparison.Ordinal);
        Assert.Contains("-warnaserror", workflow[..focus], StringComparison.Ordinal);
        Assert.Contains("--no-build --no-restore", workflow[focus..proof], StringComparison.Ordinal);
        Assert.Contains("FullyQualifiedName~Legacy.Maliev.Intranet.Tests.BffCustomersPrivateFailureObservationTests", workflow[focus..proof], StringComparison.Ordinal);
        Assert.Contains("trx;LogFileName=customers-private-failure.trx", workflow[focus..proof], StringComparison.Ordinal);
        Assert.Contains("verify-customers-private-failure-focus.py", workflow[proof..preserve], StringComparison.Ordinal);
        Assert.Contains("actions/preserve-validation-evidence@45ced0919a604459c7bb5baf7f38d7bc53d708ba", workflow[preserve..], StringComparison.Ordinal);
        Assert.DoesNotContain("--filter", workflow[shared..strictBuild], StringComparison.Ordinal);
    }
}
