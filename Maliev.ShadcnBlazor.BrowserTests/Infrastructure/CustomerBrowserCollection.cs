namespace Maliev.ShadcnBlazor.BrowserTests.Infrastructure;

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class CustomerBrowserCollection
    : ICollectionFixture<IntranetClientServerFixture>, ICollectionFixture<PlaywrightFixture>
{
    public const string Name = "Production customer browser collection";
}
