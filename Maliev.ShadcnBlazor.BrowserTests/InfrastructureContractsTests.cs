using Maliev.ShadcnBlazor.BrowserTests.Infrastructure;

namespace Maliev.ShadcnBlazor.BrowserTests;

public sealed class InfrastructureContractsTests
{
    [Fact]
    public async Task FailedIntranetStartupStopsTheExactStartedHostBeforeRethrowing()
    {
        var host = new object();
        object? stoppedHost = null;
        var stopCompleted = false;

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            IntranetClientServerFixture.StartAndWaitForReadinessAsync(
                () => host,
                _ => throw new InvalidOperationException("readiness failed"),
                startedHost =>
                {
                    stoppedHost = startedHost;
                    stopCompleted = true;
                    return Task.CompletedTask;
                }));

        Assert.Equal("readiness failed", exception.Message);
        Assert.Same(host, stoppedHost);
        Assert.True(stopCompleted);
    }

    [Theory]
    [InlineData("Debug")]
    [InlineData("Release")]
    public void IntranetHostUsesTheRequestedBuildConfiguration(string configuration)
    {
        var startInfo = IntranetClientServerFixture.CreateStartInfo(
            @"B:\repo",
            @"B:\repo\Client\Client.csproj",
            new Uri("http://127.0.0.1:54321"),
            configuration);

        Assert.Equal("dotnet", startInfo.FileName);
        Assert.Equal(@"B:\repo", startInfo.WorkingDirectory);
        Assert.Equal(
            ["run", "--project", @"B:\repo\Client\Client.csproj", "-c", configuration, "--no-build", "--urls", "http://127.0.0.1:54321/"],
            startInfo.ArgumentList);
    }

    [Fact]
    public void IntranetHostConfigurationMatchesTheBrowserTestBuild()
    {
#if DEBUG
        Assert.Equal("Debug", IntranetClientServerFixture.BuildConfiguration);
#else
        Assert.Equal("Release", IntranetClientServerFixture.BuildConfiguration);
#endif
    }

    [Fact]
    public void BoundedDiagnosticsRetainsTheMostRecentOutputWithinItsLimit()
    {
        var diagnostics = new BoundedDiagnostics(8);

        diagnostics.Append("0123".AsSpan());
        diagnostics.Append("456789".AsSpan());

        Assert.Equal("23456789", diagnostics.ToString());
    }

    [Fact]
    public void AddressInUseClassifierRejectsUnrelatedHostFailures()
    {
        Assert.True(ShowcaseServerFixture.IsAddressInUse("Failed to bind to address: Address already in use."));
        Assert.True(ShowcaseServerFixture.IsAddressInUse("Only one usage of each socket address is normally permitted."));
        Assert.False(ShowcaseServerFixture.IsAddressInUse("The showcase host exited because a dependency is unavailable."));
    }
}
