namespace Legacy.Maliev.Intranet.BrowserTests.Infrastructure;

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class BrowserCollection : ICollectionFixture<PlaywrightFixture>
{
    public const string Name = "Intranet browser";
}
