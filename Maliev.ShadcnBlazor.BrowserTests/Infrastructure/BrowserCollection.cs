namespace Maliev.ShadcnBlazor.BrowserTests.Infrastructure;

[CollectionDefinition(Name)]
public sealed class BrowserCollection : ICollectionFixture<ShowcaseServerFixture>, ICollectionFixture<PlaywrightFixture>
{
    public const string Name = "Shadcn browser";
}
