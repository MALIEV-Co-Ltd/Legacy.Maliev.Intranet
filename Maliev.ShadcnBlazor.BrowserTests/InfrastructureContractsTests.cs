using Maliev.ShadcnBlazor.BrowserTests.Infrastructure;

namespace Maliev.ShadcnBlazor.BrowserTests;

public sealed class InfrastructureContractsTests
{
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
