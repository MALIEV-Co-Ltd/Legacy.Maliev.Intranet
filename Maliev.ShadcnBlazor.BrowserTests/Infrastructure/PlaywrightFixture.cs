using Microsoft.Playwright;

namespace Maliev.ShadcnBlazor.BrowserTests.Infrastructure;

public sealed class PlaywrightFixture : IAsyncLifetime
{
    private IPlaywright? _playwright;
    public IBrowser Browser { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        _playwright = await Playwright.CreateAsync();
        Browser = await _playwright.Chromium.LaunchAsync(new() { Headless = true });
    }

    public async Task DisposeAsync()
    {
        if (Browser is not null)
            await Browser.DisposeAsync();
        _playwright?.Dispose();
    }
}
